using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using CryptoBlade.Configuration;
using CryptoBlade.Helpers;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Policies;
using CryptoExchange.Net.CommonObjects;
using Microsoft.Extensions.Options;
using Order = CryptoBlade.Models.Order;
using OrderSide = Bybit.Net.Enums.OrderSide;
using Position = CryptoBlade.Models.Position;
using PositionMode = CryptoBlade.Models.PositionMode;
using Ticker = CryptoBlade.Models.Ticker;

namespace CryptoBlade.Exchanges
{
    /// <summary>
    /// Bybit V5 (Linear Perpetual) REST client dopasowany do CryptoBlade.
    /// Zasady:
    /// - Metody akcyjne zwracają obiekty wyników z Success/OrderId/Error.
    /// - Place/Edit/Cancel zwracają OrderId, jeśli operacja się powiodła.
    /// - SetTradingStop zwraca bool w OperationResult.
    /// - CancelAll... zwraca best-effort liczbę anulowanych + Success.
    /// </summary>
    public class BybitCBRestClient : IBybitCBRestClient
    {
        private readonly IBybitRestClient m_bybitRestClient;
        private readonly Category m_category;
        private readonly ILogger<BybitCbFuturesRestClient> m_logger;
        private readonly IOptions<BybitCbFuturesRestClientOptions> m_options;
        private readonly IOptions<TradingBotOptions> m_trading_bot_options;

        public BybitCBRestClient(
            IOptions<BybitCbFuturesRestClientOptions> options,
            IOptions<TradingBotOptions> tradingBotOptions,
            IBybitRestClient bybitRestClient,
            ILogger<BybitCbFuturesRestClient> logger)
        {
            m_options = options;
            m_trading_bot_options = tradingBotOptions;
            m_category = Category.Linear;
            m_bybitRestClient = bybitRestClient;
            m_logger = logger;
        }

        #region Result types

        public sealed class OrderActionResult
        {
            public bool Success { get; init; }
            public string? OrderId { get; init; }
            public string? ClientOrderId { get; init; }
            public string? ErrorCode { get; init; }
            public string? ErrorMessage { get; init; }

            public static OrderActionResult Ok(string? orderId, string? linkId = null) =>
                new() { Success = true, OrderId = orderId, ClientOrderId = linkId };

            public static OrderActionResult Fail(string? code, string? message, string? linkId = null) =>
                new() { Success = false, OrderId = null, ClientOrderId = linkId, ErrorCode = code, ErrorMessage = message };
        }

        public sealed class OperationResult
        {
            public bool Success { get; init; }
            public string? ErrorCode { get; init; }
            public string? ErrorMessage { get; init; }

            public static OperationResult Ok() => new() { Success = true };
            public static OperationResult Fail(string? code, string? message) =>
                new() { Success = false, ErrorCode = code, ErrorMessage = message };
        }

        public sealed class CancelAllResult
        {
            public bool Success { get; init; }
            public int Canceled { get; init; }
            public string? ErrorCode { get; init; }
            public string? ErrorMessage { get; init; }

            public static CancelAllResult Ok(int canceled) => new() { Success = true, Canceled = canceled };
            public static CancelAllResult Fail(int canceled, string? code, string? message) =>
                new() { Success = false, Canceled = canceled, ErrorCode = code, ErrorMessage = message };
        }

        #endregion

        #region Helpers

        /// <summary>Zaokrągla cenę do tickSize (nie po miejscach dziesiętnych!).</summary>
        public static decimal RoundPriceToTickSize(decimal price, decimal tickSize)
        {
            if (tickSize <= 0m) return price;
            var mul = Math.Round(price / tickSize, MidpointRounding.AwayFromZero);
            return mul * tickSize;
        }

        /// <summary>Zaokrągla ilość do stepSize (qtyStep).</summary>
        public static decimal RoundQtyToStepSize(decimal qty, decimal stepSize)
        {
            if (stepSize <= 0m) return qty;
            var mul = Math.Floor(qty / stepSize);
            return mul * stepSize;
        }

        #endregion

        #region Risk / Account / Mode

        public async Task<bool> SetLeverageAsync(SymbolInfo symbol, CancellationToken cancel = default)
        {
            if (!symbol.MaxLeverage.HasValue)
            {
                m_logger.LogError("Failed to setup leverage. Max leverage is not set for {Symbol}", symbol.Name);
                return false;
            }

            var leverageRes = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryTooManyVisits.ExecuteAsync(async () =>
                await m_bybitRestClient.V5Api.Account.SetLeverageAsync(
                    m_category, symbol.Name, symbol.MaxLeverage.Value, symbol.MaxLeverage.Value, cancel));

            bool ok = leverageRes.Success || (leverageRes.Error != null &&
                                              leverageRes.Error.Code == (int)BybitErrorCodes.LeverageNotChanged);

            if (!ok)
                m_logger.LogError("Failed to setup leverage. {Err}", leverageRes.Error?.Message);

            return ok;
        }

        public async Task<bool> SwitchPositionModeAsync(PositionMode mode, string symbol, CancellationToken cancel = default)
        {
            var modeChange = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryTooManyVisits.ExecuteAsync(async () =>
                await m_bybitRestClient.V5Api.Account.SwitchPositionModeAsync(
                    m_category, mode.ToBybitPositionMode(), symbol, null, cancel));

            bool ok = modeChange.Success || (modeChange.Error != null &&
                                             modeChange.Error.Code == (int)BybitErrorCodes.PositionModeNotChanged);

            if (!ok)
                m_logger.LogError("Failed to setup position mode. {Err}", modeChange.Error?.Message);

            return ok;
        }

        #endregion

        #region Orders: Create / Edit / Cancel

        /// <summary>
        /// Tworzy zlecenie (market/limit/conditional) + opcjonalne TP/SL. Zwraca OrderId lub błąd.
        /// Market ⇒ TIF IOC. reduceOnly ≠ TP/SL. Full ⇒ TP/SL Market. Partial+Limit ⇒ wymagane limit price.
        /// Hedge-mode: wymagany positionIdx (1/2). One-way: positionIdx=null.
        /// </summary>
        public async Task<OrderActionResult> PlaceOrderAsync(
            string symbol,
            OrderSide side,
            NewOrderType orderType,
            decimal qty,
            decimal? price = null,
            PositionIdx? positionIdx = null,
            TimeInForce? timeInForce = null,
            bool reduceOnly = false,
            bool closeOnTrigger = false,
            // Conditional
            decimal? triggerPrice = null,
            TriggerType? triggerBy = null,
            TriggerDirection? triggerDirection = null,
            // TP/SL
            decimal? takeProfit = null,
            decimal? stopLoss = null,
            TriggerType? tpTriggerBy = TriggerType.MarkPrice,
            TriggerType? slTriggerBy = TriggerType.MarkPrice,
            StopLossTakeProfitMode? tpslMode = null,
            OrderType? tpOrderType = null,
            OrderType? slOrderType = null,
            decimal? tpLimitPrice = null,
            decimal? slLimitPrice = null,
            string? clientOrderId = null,
            CancellationToken cancel = default)
        {
            if (orderType == NewOrderType.Limit && price is null)
                return OrderActionResult.Fail(null, "Limit order requires price.", clientOrderId);

            if (orderType == NewOrderType.Market)
                timeInForce = TimeInForce.ImmediateOrCancel;

            if (reduceOnly && (takeProfit.HasValue || stopLoss.HasValue))
            {
                // API odrzuca takie połączenie – czyścimy parametry TP/SL, ale raportujemy ostrzeżenie
                m_logger.LogWarning("{Symbol}: reduceOnly=true → dropping TP/SL in create-order.", symbol);
                takeProfit = null; stopLoss = null;
                tpOrderType = null; slOrderType = null;
                tpLimitPrice = null; slLimitPrice = null;
                tpslMode = null;
            }

            if (tpslMode == StopLossTakeProfitMode.Full)
            {
                if (tpOrderType == OrderType.Limit || slOrderType == OrderType.Limit)
                {
                    m_logger.LogWarning("{Symbol}: tpslMode=Full ⇒ forcing TP/SL to Market.", symbol);
                    tpOrderType = OrderType.Market;
                    slOrderType = OrderType.Market;
                }
                tpLimitPrice = null; slLimitPrice = null;
            }
            else if (tpslMode == StopLossTakeProfitMode.Partial)
            {
                if (tpOrderType == OrderType.Limit && tpLimitPrice is null)
                    return OrderActionResult.Fail(null, "Partial+TP Limit requires TpLimitPrice.", clientOrderId);
                if (slOrderType == OrderType.Limit && slLimitPrice is null)
                    return OrderActionResult.Fail(null, "Partial+SL Limit requires SlLimitPrice.", clientOrderId);
            }

            var placeRes = await CryptoBlade.Strategies.Policies.ExchangePolicies<BybitOrderId>.RetryTooManyVisits
                .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                    category: m_category,
                    symbol: symbol,
                    side: side,
                    type: orderType,
                    quantity: qty,
                    price: price,
                    timeInForce: timeInForce,
                    positionIdx: positionIdx,
                    reduceOnly: reduceOnly,
                    closeOnTrigger: closeOnTrigger,
                    triggerPrice: triggerPrice,
                    triggerBy: triggerBy,
                    triggerDirection: triggerDirection,
                    takeProfit: takeProfit,
                    stopLoss: stopLoss,
                    takeProfitTriggerBy: tpTriggerBy,
                    stopLossTriggerBy: slTriggerBy,
                    stopLossTakeProfitMode: tpslMode,
                    takeProfitOrderType: tpOrderType,
                    stopLossOrderType: slOrderType,
                    takeProfitLimitPrice: tpLimitPrice,
                    stopLossLimitPrice: slLimitPrice,
                    ct: cancel));

            if (!placeRes.GetResultOrError(out var bybitOrderId, out var error))
                return OrderActionResult.Fail(error?.Code?.ToString(), error?.Message, clientOrderId);

            return OrderActionResult.Ok(bybitOrderId.OrderId, clientOrderId);
        }

        /// <summary>
        /// Edytuje istniejące zlecenie (cena/qty/TP/SL na ZLECENIU). Zwraca OrderId lub błąd.
        /// Brak tp/sl order type – w tpslMode=Full limit price są ignorowane.
        /// </summary>
        public async Task<OrderActionResult> AmendOrderAsync(
            string symbol,
            string? orderId = null,
            string? clientOrderId = null,
            decimal? price = null,
            decimal? qty = null,
            decimal? takeProfit = null,
            decimal? stopLoss = null,
            TriggerType? tpTriggerBy = null,
            TriggerType? slTriggerBy = null,
            StopLossTakeProfitMode? tpslMode = null,
            decimal? tpLimitPrice = null,
            decimal? slLimitPrice = null,
            CancellationToken cancel = default)
        {
            if (string.IsNullOrEmpty(orderId) && string.IsNullOrEmpty(clientOrderId))
                return OrderActionResult.Fail(null, "Amend requires orderId or clientOrderId.");

            if (tpslMode == StopLossTakeProfitMode.Full)
            {
                if (tpLimitPrice.HasValue || slLimitPrice.HasValue)
                    m_logger.LogWarning("{Symbol}: tpslMode=Full ⇒ ignoring tp/slLimitPrice.", symbol);
                tpLimitPrice = null; slLimitPrice = null;
            }

            var amendRes = await CryptoBlade.Strategies.Policies.ExchangePolicies<BybitOrderId>.RetryTooManyVisits.ExecuteAsync(async () =>
                await m_bybitRestClient.V5Api.Trading.EditOrderAsync(
                    category: m_category,
                    symbol: symbol,
                    orderId: orderId,
                    clientOrderId: clientOrderId,
                    price: price,
                    quantity: qty,
                    takeProfit: takeProfit,
                    stopLoss: stopLoss,
                    takeProfitTriggerBy: tpTriggerBy,
                    stopLossTriggerBy: slTriggerBy,
                    stopLossTakeProfitMode: tpslMode,
                    takeProfitLimitPrice: tpLimitPrice,
                    stopLossLimitPrice: slLimitPrice,
                    ct: cancel));

            if (!amendRes.GetResultOrError(out var data, out var error))
                return OrderActionResult.Fail(error?.Code?.ToString(), error?.Message, clientOrderId);

            // API zwykle zwraca OrderId, ale gdyby nie – przekaż to, co mamy
            return OrderActionResult.Ok(data?.OrderId ?? orderId ?? clientOrderId, clientOrderId);
        }

        /// <summary>
        /// Anuluje pojedyncze zlecenie. Zwraca OrderId lub błąd.
        /// </summary>
        public async Task<OrderActionResult> CancelOrderAsync(string symbol, string orderId, CancellationToken cancel = default)
        {
            var cancelOrder = await CryptoBlade.Strategies.Policies.ExchangePolicies<BybitOrderId>.RetryTooManyVisits
                .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading
                    .CancelOrderAsync(m_category, symbol, orderId, null, null, cancel));

            if (!cancelOrder.GetResultOrError(out var result, out var error))
                return OrderActionResult.Fail(error?.Code?.ToString(), error?.Message);

            return OrderActionResult.Ok(result?.OrderId ?? orderId);
        }

        /// <summary>
        /// Anuluje wszystkie zlecenia (opcjonalnie tylko wybranego symbolu). 
        /// Zwraca best-effort liczbę anulowanych oraz Success, jeśli po sprzątaniu nie wisi nic otwartego.
        /// </summary>
        public async Task<CancelAllResult> CancelAllOrdersAndCountAsync(string? symbol = null, CancellationToken cancel = default)
        {
            var before = await GetOrdersAsync(cancel);
            var openBefore = before.Where(o => string.IsNullOrEmpty(symbol) || o.Symbol == symbol)
                                   .Count(o => o.Status is Models.OrderStatus.New
                                                         or Models.OrderStatus.PartiallyFilled
                                                         or Models.OrderStatus.Pending);

            int lastSeenOpen = openBefore;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                var res = await CryptoBlade.Strategies.Policies.ExchangePolicies<BybitResponse<BybitOrderId>>.RetryTooManyVisits.ExecuteAsync(async () =>
                    await m_bybitRestClient.V5Api.Trading.CancelAllOrderAsync(
                        category: m_category,
                        symbol: symbol,
                        baseAsset: null,
                        settleAsset: m_trading_bot_options.Value.QuoteAsset,
                        orderFilter: null,
                        stopOrderType: null,
                        ct: cancel));

                if (!res.Success)
                {
                    var canceledSoFar = Math.Max(0, openBefore - lastSeenOpen);
                    return CancelAllResult.Fail(canceledSoFar, res.Error?.Code?.ToString(), res.Error?.Message);
                }

                await Task.Delay(300, cancel);

                var after = await GetOrdersAsync(cancel);
                var openAfter = after.Where(o => string.IsNullOrEmpty(symbol) || o.Symbol == symbol)
                                     .Count(o => o.Status is Models.OrderStatus.New
                                                           or Models.OrderStatus.PartiallyFilled
                                                           or Models.OrderStatus.Pending);

                if (openAfter == 0)
                    return CancelAllResult.Ok(openBefore);

                if (openAfter < lastSeenOpen)
                    lastSeenOpen = openAfter;
            }

            var canceled = Math.Max(0, openBefore - lastSeenOpen);
            return new CancelAllResult { Success = lastSeenOpen == 0, Canceled = canceled };
        }

        /// <summary>
        /// TP (reduce-only) dla longa. Zwraca OrderId lub błąd.
        /// </summary>
        public async Task<OrderActionResult> PlaceLongTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force, CancellationToken cancel = default)
        {
            var sellOrderRes = await CryptoBlade.Strategies.Policies.ExchangePolicies<BybitOrderId>.RetryTooManyVisits.ExecuteAsync(
                async () => await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                    category: m_category,
                    symbol: symbol,
                    side: OrderSide.Sell,
                    type: force ? NewOrderType.Market : NewOrderType.Limit,
                    quantity: qty,
                    price: price,
                    positionIdx: PositionIdx.BuyHedgeMode,
                    reduceOnly: true,
                    timeInForce: force ? TimeInForce.ImmediateOrCancel : TimeInForce.PostOnly,
                    ct: cancel));

            if (!sellOrderRes.GetResultOrError(out var data, out var error))
                return OrderActionResult.Fail(error?.Code?.ToString(), error?.Message);

            return OrderActionResult.Ok(data?.OrderId);
        }

        /// <summary>
        /// TP (reduce-only) dla shorta. Zwraca OrderId lub błąd.
        /// </summary>
        public async Task<OrderActionResult> PlaceShortTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force, CancellationToken cancel = default)
        {
            var buyOrderRes = await CryptoBlade.Strategies.Policies.ExchangePolicies<BybitOrderId>.RetryTooManyVisits.ExecuteAsync(
                async () => await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                    category: m_category,
                    symbol: symbol,
                    side: OrderSide.Buy,
                    type: force ? NewOrderType.Market : NewOrderType.Limit,
                    quantity: qty,
                    price: price,
                    positionIdx: PositionIdx.SellHedgeMode,
                    reduceOnly: true,
                    timeInForce: force ? TimeInForce.ImmediateOrCancel : TimeInForce.PostOnly,
                    ct: cancel));

            if (!buyOrderRes.GetResultOrError(out var data, out var error))
                return OrderActionResult.Fail(error?.Code?.ToString(), error?.Message);

            return OrderActionResult.Ok(data?.OrderId);
        }

        /// <summary>
        /// Ustawia/zmienia TP/SL/Trailing Stop NA POZYCJI. Full ⇒ Market; Partial ⇒ Limit dopuszczalny (z limitem).
        /// </summary>
        public async Task<OperationResult> SetTradingStopAsync(
            string symbol,
            decimal priceScale,
            decimal stopLoss,
            decimal? takeProfit,
            decimal? trailingStop,
            PositionIdx positionIdx,
            decimal? activePrice = null,
            decimal? takeProfitQuantity = null,
            decimal? stopLossQuantity = null,
            StopLossTakeProfitMode? stopLossTakeProfitMode = StopLossTakeProfitMode.Full,
            OrderType? tpOrderType = null,
            OrderType? slOrderType = null,
            decimal? tpLimitPrice = null,
            decimal? slLimitPrice = null,
            CancellationToken cancel = default)
        {
            if (stopLossTakeProfitMode == StopLossTakeProfitMode.Full)
            {
                if (tpOrderType == OrderType.Limit || slOrderType == OrderType.Limit)
                {
                    m_logger.LogWarning("{Symbol}: TradingStop Full ⇒ forcing TP/SL Market.", symbol);
                    tpOrderType = OrderType.Market;
                    slOrderType = OrderType.Market;
                }
                tpLimitPrice = null; slLimitPrice = null;
            }
            else if (stopLossTakeProfitMode == StopLossTakeProfitMode.Partial)
            {
                if (tpOrderType == OrderType.Limit && tpLimitPrice is null)
                    return OperationResult.Fail(null, "Partial+TP Limit requires TpLimitPrice.");
                if (slOrderType == OrderType.Limit && slLimitPrice is null)
                    return OperationResult.Fail(null, "Partial+SL Limit requires SlLimitPrice.");
            }

            var stopRes = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryTooManyVisits.ExecuteAsync(
                async () => await m_bybitRestClient.V5Api.Trading.SetTradingStopAsync(
                    category: m_category,
                    symbol: symbol,
                    positionIdx: positionIdx,
                    takeProfit: takeProfit,
                    stopLoss: stopLoss,
                    trailingStop: trailingStop,
                    takeProfitTrigger: TriggerType.MarkPrice,
                    stopLossTrigger: TriggerType.MarkPrice,
                    activePrice: activePrice,
                    takeProfitOrderType: tpOrderType ?? OrderType.Market,
                    stopLossOrderType: slOrderType ?? OrderType.Market,
                    takeProfitQuantity: takeProfitQuantity,
                    stopLossQuantity: stopLossQuantity,
                    takeProfitLimitPrice: tpLimitPrice,
                    stopLossLimitPrice: slLimitPrice,
                    stopLossTakeProfitMode: stopLossTakeProfitMode,
                    ct: cancel));

            if (!stopRes.Success)
            {
                m_logger.LogInformation("{Symbol}: SetTradingStop failed: {Err}", symbol, stopRes.Error?.Message);

                // Fallback: samo Trailing Stop (bez TP/SL)
                var ticker = await GetTickerAsync(symbol, cancel);
                if (ticker == null)
                    return OperationResult.Fail(stopRes.Error?.Code?.ToString(), stopRes.Error?.Message);

                const decimal pct = 0.003m; // 0.3 %
                decimal dist = Math.Round(ticker.LastPrice * pct, (int)priceScale);

                var fallback = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryTooManyVisits.ExecuteAsync(
                    async () => await m_bybitRestClient.V5Api.Trading.SetTradingStopAsync(
                        category: m_category,
                        symbol: symbol,
                        positionIdx: positionIdx,
                        takeProfit: 0,
                        stopLoss: 0,
                        trailingStop: dist,
                        takeProfitTrigger: TriggerType.MarkPrice,
                        stopLossTrigger: TriggerType.MarkPrice,
                        ct: cancel));

                return fallback.Success
                    ? OperationResult.Ok()
                    : OperationResult.Fail(fallback.Error?.Code?.ToString(), fallback.Error?.Message);
            }

            return OperationResult.Ok();
        }

        #endregion

        #region Reads

        public async Task<Strategies.Wallet.Balance> GetBalancesAsync(CancellationToken cancel = default)
        {
            var balance = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var balanceResult = await m_bybitRestClient.V5Api.Account.GetBalancesAsync(
                    AccountType.Unified, null, cancel);

                if (balanceResult.GetResultOrError(out var data, out var error))
                    return data;

                throw new InvalidOperationException(error.Message);
            });

            foreach (var b in balance.List)
            {
                if (b.AccountType == AccountType.Unified)
                {
                    var asset = b.Assets.FirstOrDefault(x =>
                        string.Equals(x.Asset, m_trading_bot_options.Value.QuoteAsset,
                            StringComparison.OrdinalIgnoreCase));
                    if (asset != null)
                        return asset.ToBalance();
                }
            }

            return new Strategies.Wallet.Balance();
        }

        public async Task<SymbolInfo[]> GetSymbolInfoAsync(CancellationToken cancel = default)
        {
            var symbolData = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                List<SymbolInfo> symbolInfo = new();
                string? cursor = null;

                using var sem = new SemaphoreSlim(4, 4);
                var bag = new ConcurrentBag<SymbolInfo>();

                while (true)
                {
                    var symbolsResult = await m_bybitRestClient.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(
                        m_category, null, null, null, null, cursor, cancel);

                    if (!symbolsResult.GetResultOrError(out var data, out var error))
                        throw new InvalidOperationException(error.Message);

                    var tasks = data.List
                        .Where(x => string.Equals(m_trading_bot_options.Value.QuoteAsset, x.QuoteAsset))
                        .Select(async x =>
                        {
                            await sem.WaitAsync(cancel);
                            try
                            {
                                var symbol = x.ToSymbolInfo();
                                symbol.Volume = await GetSymbolVolumeAsync(symbol.Name, cancel);
                                symbol.Volatility = await GetSymbolVolatility(symbol.Name, cancel);
                                bag.Add(symbol);
                            }
                            finally { sem.Release(); }
                        });

                    await Task.WhenAll(tasks);

                    if (string.IsNullOrWhiteSpace(data.NextPageCursor))
                        break;

                    cursor = data.NextPageCursor;
                }

                symbolInfo.AddRange(bag);
                return symbolInfo.ToArray();
            });

            return symbolData;
        }

        public async Task<decimal?> GetSymbolVolumeAsync(string symbol, CancellationToken cancel = default)
        {
            var ticker = await GetTickerAsync(symbol, cancel);
            return ticker?.Volume24H;
        }

        public async Task<decimal?> GetSymbolVolatility(string symbol, CancellationToken cancel = default)
        {
            var candles = await GetKlinesClosedAsync(symbol, TimeFrame.OneDay, 31, cancel);
            return TradingHelpers.CalculateVolatility(candles);
        }

        public async Task<Candle[]> GetKlinesClosedAsync(string symbol, TimeFrame interval, int limit, CancellationToken cancel = default)
        {
            var tf = interval.ToTimeSpan();
            var end = DateTime.UtcNow - tf;
            var start = end - tf * limit;

            return await GetKlinesAsync(symbol, interval, start, end, cancel);
        }

        public async Task<Candle[]> GetKlinesAsync(string symbol, TimeFrame interval, int limit, CancellationToken cancel = default)
        {
            var candles = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var dataResponse = await m_bybitRestClient.V5Api.ExchangeData.GetKlinesAsync(
                    m_category, symbol, interval.ToKlineInterval(), null, null, limit, cancel);

                if (!dataResponse.GetResultOrError(out var data, out var error))
                    throw new InvalidOperationException(error.Message);

                return data.List.Skip(1).Reverse().Select(x => x.ToCandle(interval)).ToArray();
            });

            return candles;
        }

        public async Task<Candle[]> GetKlinesAsync(string symbol, TimeFrame interval, DateTime start, DateTime end, CancellationToken cancel = default)
        {
            var candles = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var dataResponse = await m_bybitRestClient.V5Api.ExchangeData.GetKlinesAsync(
                    m_category, symbol, interval.ToKlineInterval(), start, end, 1000, cancel);

                if (!dataResponse.GetResultOrError(out var data, out var error))
                    throw new InvalidOperationException(error.Message);

                return data.List.Reverse().Select(x => x.ToCandle(interval)).ToArray();
            });

            return candles;
        }

        public async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancel = default)
        {
            var list = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var priceDataRes = await m_bybitRestClient.V5Api.ExchangeData.GetLinearInverseTickersAsync(
                    m_category, symbol, null, null, cancel);

                if (priceDataRes.GetResultOrError(out var data, out var error))
                    return data.List;

                throw new InvalidOperationException(error.Message);
            });

            var first = list.FirstOrDefault();
            if (first == null)
                throw new InvalidOperationException($"No ticker data for {symbol}");

            return first.ToTicker();
        }

        public async Task<Order[]> GetOrdersAsync(CancellationToken cancel = default)
        {
            var orders = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                List<Order> orders = new();
                string? cursor = null;

                while (true)
                {
                    var ordersResult = await m_bybitRestClient.V5Api.Trading.GetOrdersAsync(
                        m_category,
                        settleAsset: m_trading_bot_options.Value.QuoteAsset,
                        cursor: cursor,
                        ct: cancel);

                    if (!ordersResult.GetResultOrError(out var data, out var error))
                        throw new InvalidOperationException(error.Message);

                    orders.AddRange(data.List.Select(x => x.ToOrder()));

                    if (string.IsNullOrWhiteSpace(data.NextPageCursor))
                        break;

                    cursor = data.NextPageCursor;
                }

                return orders.ToArray();
            });

            return orders;
        }

        public async Task<Position[]> GetPositionsAsync(CancellationToken cancel = default)
        {
            var positions = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                List<Position> positions = new();
                string? cursor = null;

                while (true)
                {
                    var positionResult = await m_bybitRestClient.V5Api.Trading.GetPositionsAsync(
                        m_category,
                        settleAsset: m_trading_bot_options.Value.QuoteAsset,
                        cursor: cursor,
                        ct: cancel);

                    if (!positionResult.GetResultOrError(out var data, out var error))
                        throw new InvalidOperationException(error.Message);

                    foreach (var bybitPosition in data.List)
                    {
                        var position = bybitPosition.ToPosition();
                        if (position == null)
                            m_logger.LogWarning("Could not convert position for symbol: {Symbol}", bybitPosition.Symbol);
                        else
                            positions.Add(position);
                    }

                    if (string.IsNullOrWhiteSpace(data.NextPageCursor))
                        break;

                    cursor = data.NextPageCursor;
                }

                return positions.ToArray();
            });

            return positions;
        }

        public async Task<FundingRate[]> GetFundingRatesAsync(string symbol, DateTime start, DateTime end, CancellationToken cancel = default)
        {
            var rates = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var fundingRates = await m_bybitRestClient.V5Api.ExchangeData.GetFundingRateHistoryAsync(
                    m_category, symbol, start, end, 200, cancel);

                if (!fundingRates.GetResultOrError(out var data, out var error))
                    throw new InvalidOperationException(error.Message);

                return data.List.Select(x => x.ToFundingRate()).ToArray();
            });

            return rates;
        }

        #endregion

        #region Extra (RAW)

        public async Task<object?> GetFeeRatesRawAsync(string? symbol = null, CancellationToken cancel = default)
        {
            var res = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
                await m_bybitRestClient.V5Api.Account.GetFeeRateAsync(
                    category: m_category,
                    symbol: symbol,
                    baseAsset: null,
                    ct: cancel));

            if (!res.Success)
            {
                m_logger.LogError("GetFeeRates failed: {Err}", res.Error?.Message);
                return null;
            }
            return res.Data;
        }

        public async Task<object?> GetTransactionLogRawAsync(
            AccountType accountType = AccountType.Unified,
            DateTime? start = null,
            DateTime? end = null,
            string? cursor = null,
            CancellationToken cancel = default)
        {
            var res = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
                await m_bybitRestClient.V5Api.Account.GetTransactionHistoryAsync(
                    accountType: accountType,
                    startTime: start,
                    endTime: end,
                    limit: 1000,
                    cursor: cursor,
                    ct: cancel));

            if (!res.Success)
            {
                m_logger.LogError("GetTransactionLog failed: {Err}", res.Error?.Message);
                return null;
            }
            return res.Data;
        }

        public async Task<object?> GetOrderHistoryRawAsync(string? symbol = null, string? cursor = null, CancellationToken cancel = default)
        {
            var res = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
                await m_bybitRestClient.V5Api.Trading.GetOrderHistoryAsync(
                    category: m_category,
                    symbol: symbol,
                    baseAsset: null,
                    cursor: cursor,
                    ct: cancel));

            if (!res.Success)
            {
                m_logger.LogError("GetOrderHistory failed: {Err}", res.Error?.Message);
                return null;
            }
            return res.Data;
        }

        public async Task<object?> GetClosedPnlRawAsync(string? symbol = null, DateTime? start = null, DateTime? end = null, string? cursor = null, CancellationToken cancel = default)
        {
            var res = await CryptoBlade.Strategies.Policies.ExchangePolicies.RetryForever.ExecuteAsync(async () =>
                await m_bybitRestClient.V5Api.Trading.GetClosedProfitLossAsync(
                    category: m_category,
                    symbol: symbol,
                    startTime: start,
                    endTime: end,
                    cursor: cursor,
                    ct: cancel));

            if (!res.Success)
            {
                m_logger.LogError("GetClosedPnl failed: {Err}", res.Error?.Message);
                return null;
            }
            return res.Data;
        }

        #endregion
    }
}

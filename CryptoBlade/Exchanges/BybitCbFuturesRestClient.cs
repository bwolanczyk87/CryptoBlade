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
using OrderStatus = Bybit.Net.Enums.OrderStatus;
using Position = CryptoBlade.Models.Position;
using PositionMode = CryptoBlade.Models.PositionMode;
using Ticker = CryptoBlade.Models.Ticker;

namespace CryptoBlade.Exchanges
{
    public class BybitCbFuturesRestClient : ICbFuturesRestClient
    {
        private readonly IBybitRestClient m_bybitRestClient;
        private readonly Category m_category;
        private readonly ILogger<BybitCbFuturesRestClient> m_logger;
        private readonly IOptions<BybitCbFuturesRestClientOptions> m_options;
        private readonly IOptions<TradingBotOptions> m_trading_bot_options;

        public BybitCbFuturesRestClient(IOptions<BybitCbFuturesRestClientOptions> options,
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

        public async Task<bool> SetLeverageAsync(SymbolInfo symbol,
            CancellationToken cancel = default)
        {
            if (!symbol.MaxLeverage.HasValue)
            {
                m_logger.LogError($"Failed to setup leverage. Max leverage is not set for {symbol.Name}");
                return false;
            }

            var leverageRes = await ExchangePolicies.RetryTooManyVisits.ExecuteAsync(async () =>
            {
                var leverageRes = await m_bybitRestClient.V5Api.Account
                    .SetLeverageAsync(
                        m_category,
                        symbol.Name,
                        symbol.MaxLeverage.Value,
                        symbol.MaxLeverage.Value,
                        cancel);
                return leverageRes;
            });
            bool leverageOk = leverageRes.Success || leverageRes.Error != null &&
                leverageRes.Error.Code == (int)BybitErrorCodes.LeverageNotChanged;
            if (!leverageOk)
                m_logger.LogError($"Failed to setup leverage. {leverageRes.Error?.Message}");

            return leverageOk;
        }

        public async Task<bool> SwitchPositionModeAsync(PositionMode mode, string symbol,
            CancellationToken cancel = default)
        {
            var modeChange = await ExchangePolicies.RetryTooManyVisits.ExecuteAsync(async () =>
            {
                var modeChange = await m_bybitRestClient.V5Api.Account.SwitchPositionModeAsync(
                    m_category,
                    mode.ToBybitPositionMode(),
                    symbol,
                    null,
                    cancel);
                return modeChange;
            });

            bool modeOk = modeChange.Success || modeChange.Error != null &&
                modeChange.Error.Code == (int)BybitErrorCodes.PositionModeNotChanged;
            if (!modeOk)
                m_logger.LogError($"Failed to setup position mode. {modeChange.Error?.Message}");

            return modeOk;
        }

        public async Task<bool> CancelOrderAsync(string symbol, string orderId, CancellationToken cancel = default)
        {
            var cancelOrder = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>.RetryTooManyVisits
                .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading
                    .CancelOrderAsync(m_category, symbol, orderId, null, null, cancel));
            if (cancelOrder.GetResultOrError(out _, out var error))
                return true;
            m_logger.LogError($"{symbol}: Error canceling order: {error}");

            return false;
        }

        public async Task<bool> PlaceLimitOrderWithAttachedTpSlAsync(
            string symbol,
            OrderSide side,
            decimal quantity,
            decimal price,
            decimal takeProfitTriggerPrice,
            decimal takeProfitLimitPrice,
            decimal stopLossTriggerPrice,
            decimal stopLossLimitPrice,
            CancellationToken cancel = default)
        {
            var positionIdx = side == OrderSide.Buy
                ? PositionIdx.BuyHedgeMode
                : PositionIdx.SellHedgeMode;

            for (int attempt = 0; attempt < m_options.Value.PlaceOrderAttempts; attempt++)
            {
                m_logger.LogInformation(
                    $"{symbol} Placing {side} limit order qty '{quantity}' @ '{price}' " +
                    $"with TP(trig/limit): '{takeProfitTriggerPrice}' / '{takeProfitLimitPrice}', " +
                    $"SL(trig/limit): '{stopLossTriggerPrice}' / '{stopLossLimitPrice}', attempt: {attempt}");

                var orderRes =
                    await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>
                        .RetryTooManyVisits
                        .ExecuteAsync(async () =>
                            await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                                category: m_category,
                                symbol: symbol,
                                side: side,
                                type: NewOrderType.Limit,
                                quantity: quantity,
                                price: price,
                                positionIdx: positionIdx,
                                reduceOnly: false,
                                timeInForce: TimeInForce.PostOnly,
                                stopLossTakeProfitMode: StopLossTakeProfitMode.Partial,
                                takeProfit: takeProfitTriggerPrice,
                                stopLoss: stopLossTriggerPrice,
                                takeProfitTriggerBy: TriggerType.MarkPrice,
                                stopLossTriggerBy: TriggerType.MarkPrice,
                                takeProfitOrderType: OrderType.Limit,
                                stopLossOrderType: OrderType.Limit,
                                takeProfitLimitPrice: takeProfitLimitPrice,
                                stopLossLimitPrice: stopLossLimitPrice,

                                ct: cancel
                            ));

                if (!orderRes.GetResultOrError(out var orderIdResult, out _))
                    continue;

                var orderStatusRes =
                    await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrder>
                        .RetryTooManyVisitsBybitResponse
                        .ExecuteAsync(async () =>
                            await m_bybitRestClient.V5Api.Trading.GetOrdersAsync(
                                category: m_category,
                                symbol: symbol,
                                orderId: orderIdResult.OrderId,
                                ct: cancel));

                if (orderStatusRes.GetResultOrError(out var orderStatus, out _))
                {
                    var order = orderStatus.List
                        .FirstOrDefault(x => string.Equals(x.OrderId, orderIdResult.OrderId, StringComparison.Ordinal));

                    if (order != null && order.Status == OrderStatus.Cancelled)
                    {
                        m_logger.LogDebug($"{symbol}: {side} order was cancelled. Adjusting price.");

                        var orderBook =
                            await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderbook>
                                .RetryTooManyVisits
                                .ExecuteAsync(async () =>
                                    await m_bybitRestClient.V5Api.ExchangeData.GetOrderbookAsync(
                                        m_category,
                                        symbol,
                                        limit: 1,
                                        cancel));

                        if (orderBook.GetResultOrError(out var orderBookData, out _))
                        {
                            if (side == OrderSide.Buy)
                            {
                                var bestBid = orderBookData.Bids.FirstOrDefault();
                                if (bestBid != null)
                                    price = bestBid.Price;
                            }
                            else
                            {
                                var bestAsk = orderBookData.Asks.FirstOrDefault();
                                if (bestAsk != null)
                                    price = bestAsk.Price;
                            }
                        }

                        // pętla: jeszcze raz spróbuj z nową ceną
                        continue;
                    }

                    m_logger.LogInformation(
                        $"{symbol} {side} limit order placed qty '{quantity}' @ '{price}' " +
                        $"with attached TP/SL (LIMIT). OrderId={orderIdResult.OrderId}");

                    return true;
                }

                m_logger.LogInformation(
                    $"{symbol} Error getting order status for {side} order: {orderStatusRes.Error}");

                return false;
            }

            m_logger.LogInformation($"{symbol} could not place {side} limit order with TP/SL.");
            return false;
        }

        public async Task<bool> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price,
            CancellationToken cancel = default)
        {
            for (int attempt = 0; attempt < m_options.Value.PlaceOrderAttempts; attempt++)
            {
                m_logger.LogDebug($"{symbol} Placing limit buy order for '{quantity}' @ '{price}'");
                var buyOrderRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>.RetryTooManyVisits
                    .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                        category: m_category,
                        symbol: symbol,
                        side: OrderSide.Buy,
                        type: NewOrderType.Limit,
                        quantity: quantity,
                        price: price,
                        positionIdx: PositionIdx.BuyHedgeMode,
                        reduceOnly: false,
                        timeInForce: TimeInForce.PostOnly,
                        ct: cancel));
                if (!buyOrderRes.GetResultOrError(out var buyOrder, out _))
                    continue;
                var orderStatusRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrder>
                    .RetryTooManyVisitsBybitResponse
                    .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.GetOrdersAsync(
                        category: m_category,
                        symbol: symbol,
                        orderId: buyOrder.OrderId,
                        ct: cancel));
                if (orderStatusRes.GetResultOrError(out var orderStatus, out _))
                {
                    var order = orderStatus.List
                        .FirstOrDefault(x => string.Equals(x.OrderId, buyOrder.OrderId, StringComparison.Ordinal));
                    if (order != null && order.Status == OrderStatus.Cancelled)
                    {
                        m_logger.LogDebug($"{symbol}: Buy order was cancelled. Adjusting price.");
                        var orderBook = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderbook>
                            .RetryTooManyVisits
                            .ExecuteAsync(async () =>
                                await m_bybitRestClient.V5Api.ExchangeData.GetOrderbookAsync(m_category,
                                    symbol,
                                    limit: 1, cancel));
                        if (orderBook.GetResultOrError(out var orderBookData, out _))
                        {
                            var bestBid = orderBookData.Bids.FirstOrDefault();
                            if (bestBid != null)
                            {
                                price = bestBid.Price;
                            }
                        }

                        continue;
                    }

                    m_logger.LogInformation($"{symbol} Buy order placed for '{quantity}' @ '{price}'");
                    return true;
                }

                m_logger.LogInformation($"{symbol} Error getting order status: {orderStatusRes.Error}");
                return false;
            }

            m_logger.LogInformation($"{symbol} could not place buy order.");

            return false;
        }

        public async Task<bool> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price,
            CancellationToken cancel = default)
        {
            for (int attempt = 0; attempt < m_options.Value.PlaceOrderAttempts; attempt++)
            {
                m_logger.LogInformation(
                    $"{symbol} Placing limit sell order for '{quantity}' @ '{price}' attempt: {attempt}");
                var sellOrderRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>.RetryTooManyVisits
                    .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                        category: m_category,
                        symbol: symbol,
                        side: OrderSide.Sell,
                        type: NewOrderType.Limit,
                        quantity: quantity,
                        price: price,
                        positionIdx: PositionIdx.SellHedgeMode,
                        reduceOnly: false,
                        timeInForce: TimeInForce.PostOnly,
                        ct: cancel));
                if (!sellOrderRes.GetResultOrError(out var sellOrder, out _))
                    continue;
                var orderStatusRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrder>
                    .RetryTooManyVisitsBybitResponse
                    .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.GetOrdersAsync(
                        category: m_category,
                        symbol: symbol,
                        orderId: sellOrder.OrderId,
                        ct: cancel));
                if (orderStatusRes.GetResultOrError(out var orderStatus, out _))
                {
                    var order = orderStatus.List
                        .FirstOrDefault(x => string.Equals(x.OrderId, sellOrder.OrderId, StringComparison.Ordinal));
                    if (order != null && order.Status == OrderStatus.Cancelled)
                    {
                        m_logger.LogDebug($"{symbol} Sell order was cancelled. Adjusting price.");
                        var orderBook = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderbook>
                            .RetryTooManyVisits
                            .ExecuteAsync(async () =>
                                await m_bybitRestClient.V5Api.ExchangeData.GetOrderbookAsync(m_category,
                                    symbol,
                                    limit: 1, cancel));
                        if (orderBook.GetResultOrError(out var orderBookData, out _))
                        {
                            var bestAsk = orderBookData.Asks.FirstOrDefault();
                            if (bestAsk != null)
                            {
                                price = bestAsk.Price;
                            }
                        }

                        continue;
                    }

                    m_logger.LogInformation($"{symbol} Sell order placed for '{quantity}' @ '{price}'");
                    return true;
                }

                m_logger.LogInformation($"{symbol} Error getting order status: {orderStatusRes.Error}");
                return false;
            }

            m_logger.LogInformation($"{symbol} could not place sell order.");
            return false;
        }

        public async Task<bool> PlaceMarketBuyOrderAsync(string symbol, decimal quantity, decimal price,
            CancellationToken cancel = default)
        {
            var buyOrderRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>.RetryTooManyVisits
                .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                    category: m_category,
                    symbol: symbol,
                    side: OrderSide.Buy,
                    type: NewOrderType.Market,
                    quantity: quantity,
                    positionIdx: PositionIdx.BuyHedgeMode,
                    reduceOnly: false,
                    ct: cancel));
            if (!buyOrderRes.GetResultOrError(out _, out var error))
            {
                m_logger.LogInformation(
                    "Cannot place MARKET BUY for {Symbol}. {Error}",
                    symbol,
                    error?.Message ?? "No error details");

                return false;
            }

            return true;
        }

        public async Task<bool> PlaceMarketSellOrderAsync(string symbol, decimal quantity, decimal price,
            CancellationToken cancel = default)
        {
            var sellOrderRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>.RetryTooManyVisits
                .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                    category: m_category,
                    symbol: symbol,
                    side: OrderSide.Sell,
                    type: NewOrderType.Market,
                    quantity: quantity,
                    price: price,
                    positionIdx: PositionIdx.SellHedgeMode,
                    reduceOnly: false,
                    ct: cancel));

            if (!sellOrderRes.GetResultOrError(out _, out var error))
            {
                m_logger.LogInformation(
                    "Cannot place MARKET SELL for {Symbol}. {Error}",
                    symbol,
                    error?.Message ?? "No error details");

                return false;
            }

            return true;
        }

        public async Task<bool> PlaceLongTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force,
            CancellationToken cancel = default)
        {
            var sellOrderRes =
                await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>.RetryTooManyVisits.ExecuteAsync(
                    async () =>
                        await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                            category: m_category,
                            symbol: symbol,
                            side: OrderSide.Sell,
                            type: force ? NewOrderType.Market : NewOrderType.Limit,
                            quantity: qty,
                            price: price,
                            positionIdx: PositionIdx.BuyHedgeMode,
                            reduceOnly: true,
                            timeInForce: force ? TimeInForce.GoodTillCanceled : TimeInForce.PostOnly,
                            ct: cancel));
            if (!sellOrderRes.GetResultOrError(out var sellOrder, out var error))
            {
                m_logger.LogInformation($"{symbol} Failed to place long take profit order: {error}");
                return false;
            }

            var orderStatusRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrder>
                .RetryTooManyVisitsBybitResponse
                .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.GetOrdersAsync(
                    category: m_category,
                    symbol: symbol,
                    orderId: sellOrder.OrderId,
                    ct: cancel));
            if (orderStatusRes.GetResultOrError(out var orderStatus, out _))
            {
                var order = orderStatus.List
                    .FirstOrDefault(x => string.Equals(x.OrderId, sellOrder.OrderId, StringComparison.Ordinal));
                if (order != null && order.Status == OrderStatus.Cancelled)
                {
                    m_logger.LogDebug($"{symbol} long take profit order was cancelled.");
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> PlaceShortTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force,
            CancellationToken cancel = default)
        {
            var buyOrderRes =
                await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrderId>.RetryTooManyVisits.ExecuteAsync(
                    async () =>
                        await m_bybitRestClient.V5Api.Trading.PlaceOrderAsync(
                            category: m_category,
                            symbol: symbol,
                            side: OrderSide.Buy,
                            type: force ? NewOrderType.Market : NewOrderType.Limit,
                            quantity: qty,
                            price: price,
                            positionIdx: PositionIdx.SellHedgeMode,
                            reduceOnly: true,
                            timeInForce: force ? TimeInForce.GoodTillCanceled : TimeInForce.PostOnly,
                            ct: cancel));
            if (!buyOrderRes.GetResultOrError(out var buyOrder, out var error))
            {
                m_logger.LogInformation($"{symbol} Failed to place short take profit order: {error}");
                return false;
            }

            var orderStatusRes = await ExchangePolicies<Bybit.Net.Objects.Models.V5.BybitOrder>
                .RetryTooManyVisitsBybitResponse
                .ExecuteAsync(async () => await m_bybitRestClient.V5Api.Trading.GetOrdersAsync(
                    category: m_category,
                    symbol: symbol,
                    orderId: buyOrder.OrderId,
                    ct: cancel));
            if (orderStatusRes.GetResultOrError(out var orderStatus, out _))
            {
                var order = orderStatus.List
                    .FirstOrDefault(x => string.Equals(x.OrderId, buyOrder.OrderId, StringComparison.Ordinal));
                if (order != null && order.Status == OrderStatus.Cancelled)
                {
                    m_logger.LogError($"{symbol} short take profit order was cancelled.");
                    return false;
                }
            }

            return true;
        }

        public async Task<bool> SetTradingStopAsync(string symbol, decimal priceScale, decimal stopLoss, decimal? takeProfit, decimal? trailingStop,
            PositionIdx positionIdx, decimal? activePrice = null, decimal? takeProfitQuantity = null, decimal? stopLossQuantity = null,
            StopLossTakeProfitMode? stopLossTakeProfitMode = StopLossTakeProfitMode.Full, CancellationToken cancel = default)
        {
            var stopRes = await ExchangePolicies.RetryTooManyVisits.ExecuteAsync(
                async () =>
                    await m_bybitRestClient.V5Api.Trading.SetTradingStopAsync(
                        category: m_category,
                        symbol: symbol,
                        positionIdx: positionIdx,
                        takeProfit: takeProfit,
                        stopLoss: stopLoss,
                        trailingStop: trailingStop,
                        takeProfitTrigger: TriggerType.MarkPrice,
                        stopLossTrigger: TriggerType.MarkPrice,
                        activePrice: activePrice,
                        takeProfitOrderType: OrderType.Market,
                        stopLossOrderType: OrderType.Market,
                        takeProfitQuantity: takeProfitQuantity,
                        stopLossQuantity: stopLossQuantity,
                        stopLossTakeProfitMode: stopLossTakeProfitMode,
                        ct: cancel
                    )
            );

            if (!stopRes.Success)
            {
                m_logger.LogInformation($"{symbol}: Bybit SetTradingStop responded with success=false. Error: {stopRes.Error?.Message}");

                var ticker = await GetTickerAsync(symbol, cancel);
                if (ticker == null) return false;

                const decimal pct = 0.003m;                    // 0.3 %
                decimal dist = Math.Round(ticker.LastPrice * pct, (int)priceScale); // pomocnicze rozszerzenie Scale()

                m_logger.LogWarning(
                    "{Symbol}: SL/TP odrzucone, ustawiam trailing stop {Dist} ({Pct:P})",
                    symbol, dist, pct);

                return await SetTradingStopAsync(
                    symbol,
                    priceScale,
                    stopLoss: 0,               // 0 ⇒ brak klasycznego SL
                    takeProfit: 0,             // 0 ⇒ brak TP
                    trailingStop: dist,        // tylko TS
                    positionIdx: positionIdx,
                    cancel: cancel);
            }

            return true;
        }

        public async Task<Strategies.Wallet.Balance> GetBalancesAsync(CancellationToken cancel = default)
        {
            var balance = await ExchangePolicies.RetryForever
                .ExecuteAsync(async () =>
                {
                    var balanceResult = await m_bybitRestClient.V5Api.Account.GetBalancesAsync(AccountType.Unified,
                        null,
                        cancel);
                    if (balanceResult.GetResultOrError(out var data, out var error))
                        return data;
                    throw new InvalidOperationException(error.Message);
                });
            foreach (var b in balance.List)
            {
                if (b.AccountType == AccountType.Unified)
                {
                    var asset = b.Assets.FirstOrDefault(x =>
                        string.Equals(x.Asset, m_trading_bot_options.Value.QuoteAsset, StringComparison.OrdinalIgnoreCase));
                    if (asset != null)
                    {
                        var contract = asset.ToBalance();
                        return contract;
                    }
                }
            }

            return new Strategies.Wallet.Balance();
        }

        public async Task<SymbolInfo[]> GetSymbolInfoAsync(CancellationToken cancel = default)
        {
            var symbolData = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                List<SymbolInfo> symbolInfo = [];
                string? cursor = null;
                while (true)
                {
                    var symbolsResult = await m_bybitRestClient.V5Api.ExchangeData.GetLinearInverseSymbolsAsync(
                        m_category,
                        null,
                        null,
                        null,
                        null,
                        cursor,
                        cancel);
                    if (!symbolsResult.GetResultOrError(out var data, out var error))
                        throw new InvalidOperationException(error.Message);
                    var s = data.List
                        .Where(x => string.Equals(m_trading_bot_options.Value.QuoteAsset, x.QuoteAsset))
                        .Select(x =>
                        {
                            var symbol = x.ToSymbolInfo();
                            //symbol.Volume = await GetSymbolVolumeAsync(symbol.Name, cancel);
                            //symbol.Volatility = await GetSymbolVolatility(symbol.Name, cancel);
                            return symbol;
                        });
                    symbolInfo.AddRange(s);
                    if (string.IsNullOrWhiteSpace(data.NextPageCursor))
                        break;
                    cursor = data.NextPageCursor;
                }
                return symbolInfo.ToArray();
            });

            return symbolData;
        }

        public async Task<decimal?> GetSymbolVolumeAsync(string symbol, CancellationToken cancel = default)
        {
            var ticker = await GetTickerAsync(symbol, cancel);
            if (ticker == null)
                return null;

            return ticker.Volume24H;
        }

        public async Task<decimal?> GetSymbolVolatility(string symbol, CancellationToken cancel = default)
        {
            var candles = await GetKlinesAsync(symbol, TimeFrame.OneDay, 31, cancel);
            return TradingHelpers.CalculateVolatility(candles);
        }

        public async Task<Candle[]> GetKlinesAsync(
            string symbol,
            TimeFrame interval,
            int limit,
            CancellationToken cancel = default)
        {
            var candles = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var dataResponse = await m_bybitRestClient.V5Api.ExchangeData.GetKlinesAsync(
                    m_category,
                    symbol,
                    interval.ToKlineInterval(),
                    null,
                    null,
                    limit,
                    cancel);
                if (!dataResponse.GetResultOrError(out var data, out var error))
                {
                    throw new InvalidOperationException(error.Message);
                }

                // we don't want the last candle, because it's not closed yet
                var candleData = data.List.Skip(1).Reverse().Select(x => x.ToCandle(interval))
                    .ToArray();
                return candleData;
            });

            return candles;
        }

        public async Task<Candle[]> GetKlinesAsync(string symbol, TimeFrame interval, DateTime start, DateTime end,
            CancellationToken cancel = default)
        {
            var candles = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var dataResponse = await m_bybitRestClient.V5Api.ExchangeData.GetKlinesAsync(
                    m_category,
                    symbol,
                    interval.ToKlineInterval(),
                    start,
                    end,
                    1000,
                    cancel);
                if (!dataResponse.GetResultOrError(out var data, out var error))
                {
                    throw new InvalidOperationException(error.Message);
                }

                // we don't want the last candle, because it's not closed yet
                var candleData = data.List.Reverse().Select(x => x.ToCandle(interval))
                    .ToArray();
                return candleData;
            });

            return candles;
        }

        public async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancel = default)
        {
            var priceData = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var priceDataRes = await m_bybitRestClient.V5Api.ExchangeData.GetLinearInverseTickersAsync(
                    m_category,
                    symbol,
                    null,
                    null,
                    cancel);
                if (priceDataRes.GetResultOrError(out var data, out var error))
                {
                    return data.List;
                }

                throw new InvalidOperationException(error.Message);
            });

            var ticker = priceData.Select(x => x.ToTicker()).First();

            return ticker;
        }

        public async Task<Order[]> GetOrdersAsync(CancellationToken cancel = default)
        {
            var orders = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                List<Order> orders = new List<Order>();
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
            var positions = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                List<Position> positions = new List<Position>();
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
                            m_logger.LogWarning($"Could not convert position for symbol: {bybitPosition.Symbol}");
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

        public async Task<FundingRate[]> GetFundingRatesAsync(string symbol, DateTime start, DateTime end,
            CancellationToken cancel = default)
        {
            var rates = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var fundingRates = await m_bybitRestClient.V5Api.ExchangeData.GetFundingRateHistoryAsync(
                    m_category,
                    symbol,
                    start,
                    end,
                    200,
                    cancel);
                if (!fundingRates.GetResultOrError(out var data, out var error))
                    throw new InvalidOperationException(error.Message);
                return data.List.Select(x => x.ToFundingRate()).ToArray();
            });

            return rates;
        }

        public async Task<OpenInterestPoint[]> GetOpenInterestAsync(
            string symbol,
            TimeFrame interval,
            int limit = 2,
            CancellationToken cancel = default)
        {
            var res = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var r = await m_bybitRestClient.V5Api.ExchangeData.GetOpenInterestAsync(
                    category: m_category,
                    symbol: symbol,
                    interestInterval: interval.ToOpenInterestInterval(),
                    limit: limit,
                    ct: cancel);
                if (r.GetResultOrError(out var data, out var error))
                    return data;
                throw new InvalidOperationException(error.Message);
            });

            var points = res.List
                .OrderBy(x => x.Timestamp)
                .Select(x => new OpenInterestPoint
                {
                    Timestamp = x .Timestamp,
                    OpenInterest = x.OpenInterest
                })
                .ToArray();

            return points;
        }

        public async Task<PublicTrade[]> GetRecentTradesAsync(
            string symbol,
            int limit = 1000,
            CancellationToken cancel = default)
        {
            limit = Math.Clamp(limit, 1, 1000);

            var data = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var r = await m_bybitRestClient.V5Api.ExchangeData.GetTradeHistoryAsync(
                    category: m_category,           // "linear" dla USDT perpów / "inverse" dla inverse
                    symbol: symbol,
                    limit: limit,
                    ct: cancel);

                if (r.GetResultOrError(out var res, out var error))
                    return res;
                throw new InvalidOperationException(error.Message);
            });

            // Mapowanie na nasz model
            var trades = data.List.Select(x => new PublicTrade
            {
                Timestamp = x.Timestamp,
                Price = x.Price,
                Quantity = x.Quantity,
                Side = x.Side.ToOrderSide()
            });

            return trades.OrderBy(t => t.Timestamp).ToArray();
        }
    }
}
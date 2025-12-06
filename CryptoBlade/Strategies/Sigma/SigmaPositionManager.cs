using Bybit.Net.Enums;
using CryptoBlade.Exchanges;
using CryptoBlade.Exchanges.Interfaces;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Wallet;
using OrderSide = CryptoBlade.Models.OrderSide;
using OrderStatus = CryptoBlade.Models.OrderStatus;

namespace CryptoBlade.Strategies.Sigma
{
    public sealed class SigmaPositionManager(SigmaStrategyOptions options, IFuturesRestClient restClient)
    {
        private readonly SigmaStrategyOptions _options = options;
        private readonly IFuturesRestClient _restClient = restClient;
        private IMode? _lastMode;
        private decimal _lastRiskPerUnit = 0m;

        public async Task OnSignalAsync(
            DateTime nowUtc,
            SymbolInfo symbolInfo,
            SigmaData data,
            IMode? mode,
            ModeSignal signal,
            IWalletManager walletManager,
            ILogger logger,
            CancellationToken cancel)
        {
            logger.LogInformation(
                "sym={Symbol} | mode={Mode} | hasBuy={HasBuy} | hasSell={HasSell}",
                symbolInfo.Name, mode?.Kind.ToString() ?? "None",  signal.HasBuy,signal.HasSell
            );

            if (mode is null)
                return;

            _lastMode = mode;

            bool hasBuy = signal.HasBuy;
            bool hasSell = signal.HasSell;
            if ((!hasBuy && !hasSell) || (hasBuy && hasSell))
                return;

            var side = hasBuy ? OrderSide.Buy : OrderSide.Sell;

            var entryPriceOpt = mode.ComputeEntryPrice(data, symbolInfo, side);
            if (!entryPriceOpt.HasValue || entryPriceOpt.Value <= 0m)
                return;

            var entryPrice = entryPriceOpt.Value;

            var slPriceOpt = mode.ComputeStopLossPrice(data, symbolInfo, side, entryPrice, signal.Tier);
            if (!slPriceOpt.HasValue || slPriceOpt.Value <= 0m)
                return;

            var slPrice = slPriceOpt.Value;
            var refPrice = SessionHelpers.GetRefPrice(data) ?? data.LastPrice ?? entryPrice;
            decimal minMove = ComputeMinSlMove(symbolInfo, refPrice, _options);

            if (side == OrderSide.Buy)
            {
                if (entryPrice - slPrice < minMove)
                    slPrice = entryPrice - minMove;
            }
            else
            {
                if (slPrice - entryPrice < minMove)
                    slPrice = entryPrice + minMove;
            }

            if (slPrice <= 0m)
                return;

            var riskPerUnit = Math.Abs(entryPrice - slPrice);
            if (riskPerUnit <= 0m)
                return;

            _lastRiskPerUnit = riskPerUnit;

            decimal riskUsd = walletManager.Contract.Equity.HasValue 
                ? walletManager.Contract.Equity.Value * _options.RiskPerTradePct
                : _options.MinRiskPerTradeUsd;

            riskUsd = Math.Max(riskUsd, _options.MinRiskPerTradeUsd);
            riskUsd = Math.Min(riskUsd, _options.MaxRiskPerTradeUsd);

            if (riskUsd <= 0m)
                return;

            decimal distance = side == OrderSide.Buy
                ? entryPrice - slPrice
                : slPrice - entryPrice;

            if (distance <= 0m)
                return;

            decimal notionalUsd = riskUsd * entryPrice / distance;
            decimal quantity = CalculateQuantityInContracts(symbolInfo, entryPrice, notionalUsd);
            if (quantity <= 0m)
                return;

            string clientOrderId = SigmaClientOrderId.Build(symbolInfo.Name, mode.Kind, side, SigmaOrderKind.Entry, nowUtc);

            var request = new BybitFuturesRestClient.OrderRequest(
                Symbol: symbolInfo.Name,
                Category: Category.Linear,
                Side: side.ToOrderSide(),
                Type: NewOrderType.Limit,
                Quantity: quantity,
                Price: entryPrice,
                StopLossPrice: slPrice,
                SlTriggerBy: TriggerType.LastPrice,
                StopLossTakeProfitMode: StopLossTakeProfitMode.Partial,
                StopLossOrderType: OrderType.Market,
                ReduceOnly: false,
                CloseOnTrigger: false,
                TimeInForce: TimeInForce.PostOnly,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: clientOrderId);

            logger.LogInformation(
                "sym={Symbol} | side={Side} | mode={Mode} | qty={Qty} | entry={Entry} | sl={Sl} | riskUsd={RiskUsd}",
                symbolInfo.Name, side, mode.Kind, quantity, entryPrice, slPrice, riskUsd
            );

            _ = await _restClient.PlaceOrderAsync(request, cancel);
        }

        public async Task CancelStaleEntryOrdersAsync(Order[] orders, string symbol, ILogger logger, CancellationToken cancel)
        {
            if (_options.PendingEntryTimeoutMinutes <= 0)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            var maxAge = TimeSpan.FromMinutes(_options.PendingEntryTimeoutMinutes);

            foreach (var order in orders)
            {
                if (order is null)
                    continue;

                if (order.Status is OrderStatus.Filled or OrderStatus.Cancelled)
                    continue;

                if (!SigmaClientOrderId.IsSigmaOrderId(order.ClientOrderId))
                    continue;

                var kind = SigmaClientOrderId.TryParseKind(order.ClientOrderId);
                if (kind != SigmaOrderKind.Entry)
                    continue;

                var age = nowUtc - order.CreateTime;
                if (age < maxAge)
                    continue;

                logger.LogInformation(
                    "Sigma time-stop ENTRY: cancel stale orderId={OrderId} clientOrderId={ClientOrderId} age={Age} sym={Symbol}",
                    order.OrderId,
                    order.ClientOrderId,
                    age,
                    symbol);

                await _restClient.CancelOrderAsync(symbol, order.OrderId, cancel);
            }
        }

        public async Task OnOrderUpdateAsync(
            string symbol,
            SymbolInfo symbolInfo,
            SigmaData data,
            OrderUpdate update,
            CancellationToken cancel)
        {
            var clientOrderId = update.ClientOrderId;
            if (!SigmaClientOrderId.IsSigmaOrderId(clientOrderId))
                return;

            if (_lastMode is null)
                return;

            var status = update.Status;
            var modeKind = SigmaClientOrderId.TryParseMode(clientOrderId);
            var side = SigmaClientOrderId.TryGetSide(clientOrderId);
            var kind = SigmaClientOrderId.TryParseKind(clientOrderId);

            if (status == OrderStatus.Filled &&
                kind == SigmaOrderKind.Entry &&
                side.HasValue &&
                update.AverageFillPrice.HasValue &&
                update.Quantity.HasValue)
            {
                var entrySide = side.Value;
                var tpSide = entrySide == OrderSide.Buy
                    ? OrderSide.Sell
                    : OrderSide.Buy;

                var positionIdx = entrySide == OrderSide.Buy
                    ? PositionIdx.BuyHedgeMode
                    : PositionIdx.SellHedgeMode;

                var (tp1Price, _) = _lastMode.ComputeTakeProfits(
                    data,
                    symbolInfo,
                    entrySide,
                    update.AverageFillPrice.Value,
                    _lastRiskPerUnit);

                if (!tp1Price.HasValue || tp1Price.Value <= 0m)
                    return;

                var tp1ClientOrderId = SigmaClientOrderId.Build(
                    symbol,
                    modeKind,
                    tpSide,
                    SigmaOrderKind.TakeProfit1,
                    DateTime.UtcNow);

                var tp1Req = new BybitFuturesRestClient.OrderRequest(
                    Symbol: symbol,
                    Category: Category.Linear,
                    Side: tpSide.ToOrderSide(),
                    Type: NewOrderType.Limit,
                    Quantity: update.Quantity.Value,
                    Price: tp1Price.Value,
                    TriggerPrice: null,
                    TriggerBy: null,
                    TriggerDirection: null,
                    ReduceOnly: true,
                    CloseOnTrigger: false,
                    TimeInForce: TimeInForce.PostOnly,
                    PositionIdx: positionIdx,
                    ClientOrderId: tp1ClientOrderId);

                await _restClient.PlaceOrderAsync(tp1Req, cancel);
            }
        }


        private static decimal CalculateQuantityInContracts(SymbolInfo symbolInfo, decimal entryPrice, decimal usdNotional)
        {
            if (entryPrice <= 0m || usdNotional <= 0m)
                return 0m;

            var rawQty = usdNotional / entryPrice;

            if (symbolInfo.QtyStep.HasValue && symbolInfo.QtyStep.Value > 0m)
            {
                var step = symbolInfo.QtyStep.Value;
                rawQty -= rawQty % step;
            }

            return MathHelpers.RoundQuantity(symbolInfo.QtyStep ?? 0m, rawQty);
        }

        private static decimal ComputeMinSlMove(SymbolInfo symbolInfo, decimal refPrice, SigmaStrategyOptions options)
        {
            var scale = (int)symbolInfo.PriceScale;
            if (scale is < 0 or > 18)
                scale = 4;

            decimal tick = (decimal)Math.Pow(10, -scale);
            decimal tickFloor = tick * 2m;

            if (refPrice <= 0m || options is null)
                return tickFloor;

            // Minimalny ruch z fee (ułamek ceny, np. 0.003 = 0.3%)
            decimal minMoveFraction = SessionHelpers.ComputeMinMoveFraction(options);
            if (minMoveFraction <= 0m)
                return tickFloor;

            decimal pctFloor = refPrice * minMoveFraction;

            // SL nie może być bliżej niż "2 ticki" ani bliżej niż wynika z fee
            return Math.Max(tickFloor, pctFloor);
        }
    }
}

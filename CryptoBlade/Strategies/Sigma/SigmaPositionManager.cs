using Bybit.Net.Enums;
using CryptoBlade.Exchanges;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Wallet;
using CryptoExchange.Net.CommonObjects;
using SharpToken;
using System.Drawing;
using OrderSide = CryptoBlade.Models.OrderSide;

namespace CryptoBlade.Strategies.Sigma
{
    public enum TargetKind
    {
        None = 0,
        RMultiple = 1,
        Donchian = 2,
        OpeningRange = 3,
        Dvwap = 4,
        Swing = 5,
        LiqCluster = 6
    }

    public sealed class SigmaPositionManager(SigmaStrategyOptions options, ICbFuturesRestClient restClient)
    {
        private readonly SigmaTradeSession _session = new();
        private static readonly TimeSpan EntryTimeout = TimeSpan.FromMinutes(10);

        public async Task BeforeSingalExecutionAsync(DateTime nowUtc, SymbolInfo symbolInfo, SigmaData data, CancellationToken cancel)
        {
            if(_session.HasPendingEntry && _session.EntryCreatedUtc.HasValue)
            {
                var age = nowUtc - _session.EntryCreatedUtc.Value;
                if (age >= EntryTimeout)
                {
                    if (!string.IsNullOrEmpty(_session.EntryOrderId))
                    {
                        await restClient.CancelOrderAsync(symbolInfo.Name, _session.EntryOrderId!, cancel);
                        _session.Reset();
                        return;
                    }
                }
            }

            if (_session.IsActive && _session.Tp1Hit)
                await ApplyTrailingStopAsync(nowUtc, symbolInfo, data, restClient, cancel);
        }

        public async Task OnSignalAsync(
            DateTime nowUtc,
            SymbolInfo symbolInfo,
            SigmaData data,
            IMode? mode,
            ModeSignal signal,
            IWalletManager walletManager,
            CancellationToken cancel)
        {
            if (_session.IsActive)
                return;

            if (mode is null || mode.Kind == ModeKind.None)
                return;

            bool buy = signal.HasBuy && !signal.HasSell;
            bool sell = signal.HasSell && !signal.HasBuy;
            if (!buy && !sell)
                return;

            OrderSide side = buy ? OrderSide.Buy : OrderSide.Sell;
            decimal entryPrice, slPrice, tp1Price, tp2Price;

            // 1) ENTRY / SL – tryb-specyficzne
            var entryMaybe = mode.ComputeEntryPrice(data, symbolInfo, side);
            if (!entryMaybe.HasValue || entryMaybe.Value <= 0m)
                return;

            var slMaybe = mode.ComputeStopLossPrice(data, symbolInfo, side, entryMaybe.Value);
            if (!slMaybe.HasValue || slMaybe.Value <= 0m)
                return;

            entryPrice = entryMaybe.Value;
            slPrice = slMaybe.Value;

            if (side == OrderSide.Buy && slPrice >= entryPrice)
                return;
            if (side == OrderSide.Sell && slPrice <= entryPrice)
                return;

            // 2) Risk R = |ENTRY − SL|
            decimal risk = Math.Abs(entryPrice - slPrice);
            if (risk <= 0m)
                return;

            // 3) TP1 / TP2 – tryb-specyficzne
            (decimal? rawTp1, decimal? rawTp2) = mode.ComputeTakeProfits(data, symbolInfo, side, entryPrice, risk);

            if (!rawTp1.HasValue || rawTp1.Value <= 0m)
                return;

            tp1Price = MathHelpers.RoundPrice(symbolInfo.PriceScale, rawTp1.Value);
            if (tp1Price <= 0m)
                return;

            if (rawTp2.HasValue && rawTp2.Value > 0m)
                tp2Price = MathHelpers.RoundPrice(symbolInfo.PriceScale, rawTp2.Value);
            else
                tp2Price = tp1Price; // drugi target = ten sam poziom, gdy brak TP2

            if (tp2Price <= 0m)
                return;

            // 4) Risk sizing – NIESZKANY, dalej zostawiasz tak jak jest
            decimal distance = side == OrderSide.Buy
                ? entryPrice - slPrice
                : slPrice - entryPrice;

            if (distance <= 0m)
                return;

            decimal riskPerTradeUsd = ComputeRiskPerTradeUsd(data, signal, walletManager.Contract.Equity);
            decimal notionalUsd = riskPerTradeUsd * entryPrice / distance;
            decimal quantity = CalculateQuantityInContracts(symbolInfo, entryPrice, notionalUsd);

            if (quantity <= 0m)
                return;

            string entryClientOrderId = SigmaClientOrderId.Build(symbolInfo.Name, mode.Kind, side, SigmaOrderKind.Entry, nowUtc);

            var request = new BybitCbFuturesRestClient.OrderRequest(
                Symbol: symbolInfo.Name,
                Category: Category.Linear,
                Side: side.ToOrderSide(),
                Type: NewOrderType.Limit,
                Quantity: quantity,
                Price: entryPrice,
                TriggerPrice: null,
                TriggerBy: null,
                TriggerDirection: null,
                ReduceOnly: false,
                CloseOnTrigger: false,
                TimeInForce: TimeInForce.PostOnly,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: entryClientOrderId);

            var orderId = await restClient.PlaceOrderAsync(request, cancel);
            if (orderId is null)
                return;
        }

        public async Task OnOrderUpdateAsync(string symbol, SymbolInfo symbolInfo, SigmaData data, OrderUpdate update, ICbFuturesRestClient restClient, CancellationToken cancel)
        {
            var clientOrderId = update.ClientOrderId;
            if (string.IsNullOrWhiteSpace(clientOrderId))
                return;

            if (!SigmaClientOrderId.IsSigmaOrderId(clientOrderId))
                return;

            if (update.Status == Models.OrderStatus.Cancelled)
            {
                _session.Reset();
                return;
            }

            if(update.Status == Models.OrderStatus.New)
            {
                _session.InitPendingEntry(side, SigmaOrderKind.Entry, quantity, entryClientOrderId, orderId.OrderId, nowUtc,entryPrice, mode.Kind, slPrice, tp1Price, tp2Price);
            }
                return;

            if (update.Status != Models.OrderStatus.Filled)
                return;

            var kind = SigmaClientOrderId.TryParseKind(clientOrderId);

            switch (kind)
            {
                case SigmaOrderKind.Entry:
                    await HandleEntryFilledAsync(symbol, symbolInfo, update, restClient, cancel);
                    break;
                case SigmaOrderKind.TakeProfit1:
                    await HandleTp1FilledAsync(symbol, update, restClient, cancel);
                    break;
                case SigmaOrderKind.TakeProfit2:
                    await ApplyTrailingStopAsync(DateTime.UtcNow, symbolInfo, data, restClient, cancel);
                    break;
                case SigmaOrderKind.StopLoss:
                    await HandleFinalExitAsync(symbol, restClient, cancel);
                    break;
                case SigmaOrderKind.TrailingStopLoss:
                    await HandleFinalExitAsync(symbol, restClient, cancel);
                    break;
            }
        }

        public decimal ComputeRiskPerTradeUsd(
            SigmaData sigmaData,
            ModeSignal modeSignal,
            decimal? equity)
        {
            // 1) Equity strategii w USDT.
            if (equity.HasValue && equity <= 0m)
                return options.MinRiskPerTradeUsd;

            if (!equity.HasValue)
                return options.MinRiskPerTradeUsd;

            // 2) Base risk z equity (przed tierami i środowiskiem)
            decimal baseRisk = equity.Value * options.RiskPerTradePct;

            // clamp do widełek globalnych
            if (baseRisk < options.MinRiskPerTradeUsd) baseRisk = options.MinRiskPerTradeUsd;
            if (baseRisk > options.MaxRiskPerTradeUsd) baseRisk = options.MaxRiskPerTradeUsd;

            // 3) Mnożnik tieru (Hard/Medium/Soft/None)
            decimal tierMultiplier = modeSignal.Tier switch
            {
                ModeTier.Hard => options.TierHardRiskMultiplier,
                ModeTier.Medium => options.TierMediumRiskMultiplier,
                ModeTier.Soft => options.TierSoftRiskMultiplier,
                _ => options.TierNoneRiskMultiplier
            };

            decimal risk = baseRisk * tierMultiplier;

            // 4) Modulatory środowiska – korelacja / BTC shock
            decimal envMultiplier = 1.0m;

            if (double.IsFinite(sigmaData.CorrToBtc15m) &&
                Math.Abs(sigmaData.CorrToBtc15m) >= (double)options.CorrHighReduceSizeThreshold &&
                Math.Abs(sigmaData.CorrToBtc15m) < (double)options.CorrOppositeBlock)
            {
                envMultiplier *= options.CorrHighSizeMultiplier;
            }

            // BTC shock – do uzupełnienia po dodaniu BtcAtr do SigmaData.
            risk *= envMultiplier;

            // 5) Ostateczny clamp
            if (risk < options.MinRiskPerTradeUsd) risk = options.MinRiskPerTradeUsd;
            if (risk > options.MaxRiskPerTradeUsd) risk = options.MaxRiskPerTradeUsd;

            return risk;
        }

        private async Task HandleEntryFilledAsync(
            string symbol,
            SymbolInfo symbolInfo,
            OrderUpdate update,
            ICbFuturesRestClient restClient,
            CancellationToken cancel)
        {
            if (!_session.HasPendingEntry ||
                !string.Equals(update.ClientOrderId, _session.EntryClientOrderId, StringComparison.Ordinal))
                return;

            _session.State = SigmaTradeState.Active;
            _session.EntryFilledUtc = update.UpdateTime;

            if (update.AverageFillPrice.HasValue && update.AverageFillPrice.Value > 0m)
            {
                _session.EntryPrice = update.AverageFillPrice.Value;
            }

            if (_session.EntryPrice is null ||
                _session.SlPrice is null ||
                _session.Tp1Price is null ||
                _session.Tp2Price is null ||
                _session.Quantity is null ||
                _session.Side is null)
            {
                await HandleFinalExitAsync(symbol, restClient, cancel);
                return;
            }

            var side = _session.Side.Value;
            var qty = _session.Quantity.Value;
            var slPrice = _session.SlPrice.Value;
            var tp1Price = _session.Tp1Price.Value;
            var tp2Price = _session.Tp2Price.Value;

            ModeKind mode = SigmaClientOrderId.TryParseMode(update.ClientOrderId);

            string slClientOrderId = SigmaClientOrderId.Build(
                symbol,
                mode,
                side,
                SigmaOrderKind.StopLoss,
                _session.EntryFilledUtc!.Value);

            var slReq = new BybitCbFuturesRestClient.OrderRequest(
                Symbol: symbol,
                Category: Category.Linear,
                Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                Type: NewOrderType.Market,
                Quantity: qty,
                Price: null,
                TriggerPrice: slPrice,
                TriggerBy: TriggerType.MarkPrice,
                TriggerDirection: side == OrderSide.Buy ? TriggerDirection.Fall : TriggerDirection.Rise,
                ReduceOnly: true,
                CloseOnTrigger: true,
                TimeInForce: TimeInForce.GoodTillCanceled,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: slClientOrderId);

            var slOrderId = await restClient.PlaceOrderAsync(slReq, cancel);
            if (slOrderId is null)
            {
                await HandleFinalExitAsync(symbol, restClient, cancel);
                return;
            }

            _session.SlClientOrderId = slClientOrderId;
            _session.SlOrderId = slOrderId.OrderId;

            // TP1 / TP2 – 50% / 25% / 25%
            decimal tp1QtyRaw = qty * 0.5m;
            decimal tp2QtyRaw = qty * 0.25m;

            decimal tp1Qty = MathHelpers.RoundQuantity(symbolInfo.QtyStep.Value, tp1QtyRaw);
            decimal tp2Qty = MathHelpers.RoundQuantity(symbolInfo.QtyStep.Value, tp2QtyRaw);

            decimal runnerQty = qty - tp1Qty - tp2Qty;

            if (tp1Qty <= 0m)
            {
                tp1Qty = qty;
                tp2Qty = 0m;
                runnerQty = 0m;
            }
            else
            {
                if (tp1Qty + tp2Qty > qty)
                {
                    tp2Qty = MathHelpers.RoundQuantity(symbolInfo.QtyStep.Value, qty - tp1Qty);
                    if (tp2Qty < 0m) tp2Qty = 0m;
                }

                runnerQty = qty - tp1Qty - tp2Qty;
                if (runnerQty < 0m)
                    runnerQty = 0m;
            }

            if (tp1Qty > 0m)
            {
                string tp1ClientOrderId = SigmaClientOrderId.Build(
                    symbol,
                    mode,
                    side,
                    SigmaOrderKind.TakeProfit1,
                    _session.EntryFilledUtc.Value);

                var tp1Req = new BybitCbFuturesRestClient.OrderRequest(
                    Symbol: symbol,
                    Category: Category.Linear,
                    Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                    Type: NewOrderType.Limit,
                    Quantity: tp1Qty,
                    Price: tp1Price,
                    TriggerPrice: null,
                    TriggerBy: null,
                    TriggerDirection: null,
                    ReduceOnly: true,
                    CloseOnTrigger: false,
                    TimeInForce: TimeInForce.GoodTillCanceled,
                    PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                    ClientOrderId: tp1ClientOrderId);

                var tp1OrderId = await restClient.PlaceOrderAsync(tp1Req, cancel);
                if (tp1OrderId is not null)
                {
                    _session.Tp1ClientOrderId = tp1ClientOrderId;
                    _session.Tp1OrderId = tp1OrderId.OrderId;
                }
            }

            if (tp2Qty > 0m)
            {
                string tp2ClientOrderId = SigmaClientOrderId.Build(
                    symbol,
                    mode,
                    side,
                    SigmaOrderKind.TakeProfit2,
                    _session.EntryFilledUtc.Value);

                var tp2Req = new BybitCbFuturesRestClient.OrderRequest(
                    Symbol: symbol,
                    Category: Category.Linear,
                    Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                    Type: NewOrderType.Limit,
                    Quantity: tp2Qty,
                    Price: tp2Price,
                    TriggerPrice: null,
                    TriggerBy: null,
                    TriggerDirection: null,
                    ReduceOnly: true,
                    CloseOnTrigger: false,
                    TimeInForce: TimeInForce.GoodTillCanceled,
                    PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                    ClientOrderId: tp2ClientOrderId);

                var tp2OrderId = await restClient.PlaceOrderAsync(tp2Req, cancel);
                if (tp2OrderId is not null)
                {
                    _session.Tp2ClientOrderId = tp2ClientOrderId;
                    _session.Tp2OrderId = tp2OrderId.OrderId;
                }
            }
        }

        private async Task HandleTp1FilledAsync(
            string symbol,
            OrderUpdate update,
            ICbFuturesRestClient restClient,
            CancellationToken cancel)
        {
            if (!_session.IsActive ||
                _session.Tp1ClientOrderId is null ||
                !string.Equals(update.ClientOrderId, _session.Tp1ClientOrderId, StringComparison.Ordinal))
                return;

            _session.Tp1Hit = true;

            if (_session.EntryPrice is null || _session.SlPrice is null)
                return;

            var entry = _session.EntryPrice.Value;
            var bePrice = entry;

            // kasujemy stary SL i stawiamy nowy na BE
            if (!string.IsNullOrEmpty(_session.SlOrderId))
                await restClient.CancelOrderAsync(symbol, _session.SlOrderId!, cancel);

            ModeKind mode = SigmaClientOrderId.TryParseMode(update.ClientOrderId);
            var side = _session.Side ?? OrderSide.Buy;

            string slClientOrderId = SigmaClientOrderId.Build(
                symbol,
                mode,
                side,
                SigmaOrderKind.StopLossBreakEven,
                update.UpdateTime!.Value);

            var slReq = new BybitCbFuturesRestClient.OrderRequest(
                Symbol: symbol,
                Category: Category.Linear,
                Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                Type: NewOrderType.Market,
                Quantity: _session.Quantity ?? 0m,
                Price: null,
                TriggerPrice: bePrice,
                TriggerBy: TriggerType.MarkPrice,
                TriggerDirection: side == OrderSide.Buy ? TriggerDirection.Fall : TriggerDirection.Rise,
                ReduceOnly: true,
                CloseOnTrigger: true,
                TimeInForce: TimeInForce.GoodTillCanceled,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: slClientOrderId);

            var slOrderId = await restClient.PlaceOrderAsync(slReq, cancel);
            if (slOrderId is not null)
            {
                _session.SlClientOrderId = slClientOrderId;
                _session.SlOrderId = slOrderId.OrderId;
                _session.SlPrice = bePrice;
            }
        }

        private async Task ApplyTrailingStopAsync(DateTime nowUtc, SymbolInfo symbolInfo, SigmaData data, ICbFuturesRestClient restClient, CancellationToken cancel)
        {
            if (_session.Side is null || _session.SlPrice is null || _session.EntryPrice is null)
                return;

            double atr5 = double.IsNaN(data.Atr5mAbs) ? double.NaN : data.Atr5mAbs;
            if (!double.IsFinite(atr5) || atr5 <= 0.0)
                return;

            decimal atr = (decimal)atr5;

            decimal? lastClose = data.Last1mClose ?? data.LastPrice;
            if (lastClose is null || lastClose <= 0m)
                return;

            decimal currentSl = _session.SlPrice.Value;
            decimal entry = _session.EntryPrice.Value;
            var side = _session.Side.Value;

            decimal factor = _session.EntryMode switch
            {
                ModeKind.MM => 1.0m,
                ModeKind.BO => 1.0m,
                ModeKind.MR => 1.5m,
                _ => 1.2m
            };

            decimal rawNewSl = side == OrderSide.Buy
                ? lastClose.Value - factor * atr
                : lastClose.Value + factor * atr;

            bool improves = side == OrderSide.Buy
                ? rawNewSl > currentSl
                : rawNewSl < currentSl;

            if (!improves)
                return;

            decimal be = entry;

            if (side == OrderSide.Buy && rawNewSl < be) rawNewSl = be;
            if (side == OrderSide.Sell && rawNewSl > be) rawNewSl = be;

            decimal newSl = MathHelpers.RoundPrice(symbolInfo.PriceScale, rawNewSl);

            if (Math.Abs(newSl - currentSl) < ComputeMinSlMove(symbolInfo, entry))
                return;

            if (!string.IsNullOrEmpty(_session.SlOrderId))
                await restClient.CancelOrderAsync(symbolInfo.Name, _session.SlOrderId!, cancel);

            string slClientOrderId = SigmaClientOrderId.Build(symbolInfo.Name, _session.EntryMode.Value,
                side,
                SigmaOrderKind.TrailingStopLoss,
                nowUtc);

            var slReq = new BybitCbFuturesRestClient.OrderRequest(
                Symbol: symbolInfo.Name,
                Category: Category.Linear,
                Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                Type: NewOrderType.Market,
                Quantity: _session.Quantity ?? 0m,
                Price: null,
                TriggerPrice: newSl,
                TriggerBy: TriggerType.MarkPrice,
                TriggerDirection: side == OrderSide.Buy ? TriggerDirection.Fall : TriggerDirection.Rise,
                ReduceOnly: true,
                CloseOnTrigger: true,
                TimeInForce: TimeInForce.GoodTillCanceled,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: slClientOrderId);

            var slOrderId = await restClient.PlaceOrderAsync(slReq, cancel);
            if (slOrderId is null)
                return;

            _session.SlClientOrderId = slClientOrderId;
            _session.SlOrderId = slOrderId.OrderId;
            _session.SlPrice = newSl;
        }

        private async Task HandleFinalExitAsync(
            string symbol,
            ICbFuturesRestClient restClient,
            CancellationToken cancel)
        {
            var ids = new[]
                { _session.EntryOrderId, _session.SlOrderId, _session.Tp1OrderId, _session.Tp2OrderId }
                .Where(id => !string.IsNullOrEmpty(id));

            foreach (var id in ids)
                await restClient.CancelOrderAsync(symbol, id!, cancel);

            _session.Reset();
        }

        private static decimal CalculateQuantityInContracts(
            SymbolInfo symbolInfo,
            decimal entryPrice,
            decimal usdNotional)
        {
            if (entryPrice <= 0m || usdNotional <= 0m)
                return 0m;

            var rawQty = usdNotional / entryPrice;

            if (symbolInfo.QtyStep.HasValue && symbolInfo.QtyStep.Value > 0m)
            {
                var step = symbolInfo.QtyStep.Value;
                rawQty -= rawQty % step;
            }

            return MathHelpers.RoundQuantity(symbolInfo.QtyStep.Value, rawQty);
        }

        private static decimal ComputeMinSlMove(SymbolInfo symbolInfo, decimal refPrice)
        {
            var scale = (int)symbolInfo.PriceScale;
            if (scale is < 0 or > 18)
                scale = 4;

            decimal tick = (decimal)Math.Pow(10, -scale);
            return tick * 2m;
        }
    }
}

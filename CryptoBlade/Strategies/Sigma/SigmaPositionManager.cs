using Bybit.Net.Enums;
using CryptoBlade.Exchanges;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Wallet;
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

    /// <summary>
    /// SigmaPositionManager – zarządza cyklem życia pojedynczej pozycji:
    /// - beforeSignal: timeout pending entry + trailing po TP1/TP2,
    /// - onSignal: wyznaczanie ENTRY/SL/TP1/TP2 + size i złożenie zlecenia ENTRY,
    /// - onOrderUpdate: reakcja na fill/cancel ENTRY/SL/TP1/TP2.
    /// </summary>
    public sealed class SigmaPositionManager
    {
        private readonly SigmaStrategyOptions _options;
        private readonly ICbFuturesRestClient _restClient;
        private readonly SigmaTradeSession _session = new();

        private static readonly TimeSpan EntryTimeout = TimeSpan.FromMinutes(10);

        public SigmaPositionManager(SigmaStrategyOptions options, ICbFuturesRestClient restClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        }

        // =====================================================================
        // 1. BEFORE SIGNAL (per-iteration housekeeping)
        // =====================================================================

        public async Task BeforeSignalExecutionAsync(
            DateTime nowUtc,
            SymbolInfo symbolInfo,
            SigmaData data,
            CancellationToken cancel)
        {
            // 1) Timeout pending ENTRY
            if (_session.HasPendingEntry && _session.EntryCreatedUtc.HasValue)
            {
                var age = nowUtc - _session.EntryCreatedUtc.Value;
                if (age >= EntryTimeout)
                {
                    if (!string.IsNullOrEmpty(_session.EntryOrderId))
                    {
                        await SafeCancelAsync(symbolInfo.Name, _session.EntryOrderId!, cancel);
                    }

                    // Na wszelki wypadek próbujemy skasować ew. SL/TP (nie powinno ich jeszcze być)
                    await CancelProtectiveOrdersAsync(symbolInfo.Name, cancel);

                    _session.Reset();
                    return;
                }
            }

            // 2) Trailing stop – po TP1 / TP2, gdy pozycja nadal jest aktywna
            if (_session.HasActivePosition && _session.Tp1Hit)
            {
                await UpdateTrailingStopAsync(nowUtc, symbolInfo, data, cancel);
            }
        }

        // =====================================================================
        // 2. ON SIGNAL (wejście w pozycję)
        // =====================================================================

        public async Task OnSignalAsync(
            DateTime nowUtc,
            SymbolInfo symbolInfo,
            SigmaData data,
            IMode? mode,
            ModeSignal signal,
            IWalletManager walletManager,
            CancellationToken cancel)
        {
            // Nie otwieramy nowej pozycji, jeśli coś już zarządzamy.
            if (_session.State != SigmaTradeState.Flat)
                return;

            if (mode is null || mode.Kind == ModeKind.None)
                return;

            bool buy = signal.HasBuy && !signal.HasSell;
            bool sell = signal.HasSell && !signal.HasBuy;
            if (!buy && !sell)
                return;

            var side = buy ? OrderSide.Buy : OrderSide.Sell;

            // 1) ENTRY / SL – tryb-specyficzne
            var entryMaybe = mode.ComputeEntryPrice(data, symbolInfo, side);
            if (!entryMaybe.HasValue || entryMaybe.Value <= 0m)
                return;

            var slMaybe = mode.ComputeStopLossPrice(data, symbolInfo, side, entryMaybe.Value);
            if (!slMaybe.HasValue || slMaybe.Value <= 0m)
                return;

            var entryPrice = entryMaybe.Value;
            var slPrice = slMaybe.Value;

            if (side == OrderSide.Buy && slPrice >= entryPrice)
                return;
            if (side == OrderSide.Sell && slPrice <= entryPrice)
                return;

            // 2) Jednostkowe ryzyko w punktach
            decimal riskPerUnit = Math.Abs(entryPrice - slPrice);
            if (riskPerUnit <= 0m)
                return;

            // 3) TP1 / TP2 – tryb-specyficzne
            (decimal? rawTp1, decimal? rawTp2) =
                mode.ComputeTakeProfits(data, symbolInfo, side, entryPrice, riskPerUnit);

            if (!rawTp1.HasValue || rawTp1.Value <= 0m)
                return;

            var tp1Price = MathHelpers.RoundPrice(symbolInfo.PriceScale, rawTp1.Value);
            if (tp1Price <= 0m)
                return;

            decimal tp2Price;
            if (rawTp2.HasValue && rawTp2.Value > 0m)
            {
                tp2Price = MathHelpers.RoundPrice(symbolInfo.PriceScale, rawTp2.Value);
                if (tp2Price <= 0m)
                    return;
            }
            else
            {
                tp2Price = tp1Price; // fallback – oba targety na tym samym poziomie
            }

            // 4) Risk sizing – ile USDT chcemy zaryzykować
            decimal riskPerTradeUsd = ComputeRiskPerTradeUsd(data, signal, walletManager.Contract.Equity);
            if (riskPerTradeUsd <= 0m)
                return;

            // distance = |ENTRY − SL| w cenie
            decimal distance = side == OrderSide.Buy
                ? entryPrice - slPrice
                : slPrice - entryPrice;

            if (distance <= 0m)
                return;

            // Dla kontraktów linear: risk ≈ (distance / entryPrice) * notional
            decimal notionalUsd = riskPerTradeUsd * entryPrice / distance;

            decimal quantity = CalculateQuantityInContracts(symbolInfo, entryPrice, notionalUsd);
            if (quantity <= 0m)
                return;

            // 5) Limit ENTRY (PostOnly)
            string entryClientOrderId = SigmaClientOrderId.Build(
                symbolInfo.Name,
                mode.Kind,
                side,
                SigmaOrderKind.Entry,
                nowUtc);

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

            var orderId = await _restClient.PlaceOrderAsync(request, cancel);
            if (orderId is null)
                return;

            _session.InitPendingEntry(
                side,
                SigmaOrderKind.Entry,
                quantity,
                entryClientOrderId,
                orderId.OrderId,
                nowUtc,
                entryPrice,
                mode.Kind,
                slPrice,
                tp1Price,
                tp2Price);
        }

        // =====================================================================
        // 3. ON ORDER UPDATE (reakcje na fill/cancel)
        // =====================================================================

        public async Task OnOrderUpdateAsync(
            string symbol,
            SymbolInfo symbolInfo,
            SigmaData data,
            OrderUpdate update,
            CancellationToken cancel)
        {
            var clientOrderId = update.ClientOrderId;
            if (string.IsNullOrWhiteSpace(clientOrderId))
                return;

            if (!SigmaClientOrderId.IsSigmaOrderId(clientOrderId))
                return;

            var kind = SigmaClientOrderId.TryParseKind(clientOrderId);

            // 1) Cancel – nie resetujemy ślepo całej sesji, tylko aktualizujemy odpowiedni slot
            if (update.Status == Models.OrderStatus.Cancelled)
            {
                await HandleOrderCancelledAsync(symbol, kind, clientOrderId, cancel);
                return;
            }

            // Interesują nas tylko pełne filly
            if (update.Status != Models.OrderStatus.Filled)
                return;

            switch (kind)
            {
                case SigmaOrderKind.Entry:
                    await HandleEntryFilledAsync(symbol, symbolInfo, update, cancel);
                    break;

                case SigmaOrderKind.TakeProfit1:
                    await HandleTp1FilledAsync(symbol, symbolInfo, update, data, cancel);
                    break;

                case SigmaOrderKind.TakeProfit2:
                    await HandleTp2FilledAsync(symbol, symbolInfo, update, data, cancel);
                    break;

                case SigmaOrderKind.StopLoss:
                case SigmaOrderKind.StopLossBreakEven:
                case SigmaOrderKind.TrailingStopLoss:
                case SigmaOrderKind.MRTimeStop:
                    await HandleStopLossFilledAsync(symbol, clientOrderId, cancel);
                    break;
            }
        }

        // =====================================================================
        // 3.1. Cancel handling
        // =====================================================================

        private async Task HandleOrderCancelledAsync(
            string symbol,
            SigmaOrderKind kind,
            string clientOrderId,
            CancellationToken cancel)
        {
            switch (kind)
            {
                case SigmaOrderKind.Entry:
                    if (_session.HasPendingEntry &&
                        string.Equals(clientOrderId, _session.EntryClientOrderId, StringComparison.Ordinal))
                    {
                        // ENTRY został anulowany -> wracamy do FLAT
                        await CancelProtectiveOrdersAsync(symbol, cancel);
                        _session.Reset();
                    }
                    break;

                case SigmaOrderKind.TakeProfit1:
                    if (_session.IsActive &&
                        string.Equals(clientOrderId, _session.Tp1ClientOrderId, StringComparison.Ordinal))
                    {
                        _session.Tp1ClientOrderId = null;
                        _session.Tp1OrderId = null;
                        // Tp1Hit zostaje false – to był cancel, nie TP.
                    }
                    break;

                case SigmaOrderKind.TakeProfit2:
                    if (_session.IsActive &&
                        string.Equals(clientOrderId, _session.Tp2ClientOrderId, StringComparison.Ordinal))
                    {
                        _session.Tp2ClientOrderId = null;
                        _session.Tp2OrderId = null;
                    }
                    break;

                case SigmaOrderKind.StopLoss:
                case SigmaOrderKind.StopLossBreakEven:
                case SigmaOrderKind.TrailingStopLoss:
                case SigmaOrderKind.MRTimeStop:
                    if (_session.IsActive &&
                        string.Equals(clientOrderId, _session.SlClientOrderId, StringComparison.Ordinal))
                    {
                        // SL został skasowany (np. do podniesienia BE/trailing).
                        // Zostawiamy sesję aktywną, ale czyścimy referencję do SL.
                        _session.SlClientOrderId = null;
                        _session.SlOrderId = null;
                        // SL zostanie odtworzony w BeforeSignal/TP logic.
                    }
                    break;
            }
        }

        // =====================================================================
        // 3.2. Entry fill -> zakładanie SL/TP
        // =====================================================================

        private async Task HandleEntryFilledAsync(
            string symbol,
            SymbolInfo symbolInfo,
            OrderUpdate update,
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
                _session.Side is null ||
                _session.EntryMode is null)
            {
                await ForceFlatAsync(symbol, cancel);
                return;
            }

            var side = _session.Side.Value;
            var totalQty = _session.Quantity.Value;
            var slPrice = _session.SlPrice.Value;
            var tp1Price = _session.Tp1Price.Value;
            var tp2Price = _session.Tp2Price.Value;
            var mode = _session.EntryMode.Value;

            _session.ActiveQuantity = totalQty;

            // --- SL (pełna ilość, ReduceOnly) ---

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
                Quantity: totalQty,
                Price: null,
                TriggerPrice: slPrice,
                TriggerBy: TriggerType.MarkPrice,
                TriggerDirection: side == OrderSide.Buy ? TriggerDirection.Fall : TriggerDirection.Rise,
                ReduceOnly: true,
                CloseOnTrigger: true,
                TimeInForce: TimeInForce.GoodTillCanceled,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: slClientOrderId);

            var slOrderId = await _restClient.PlaceOrderAsync(slReq, cancel);
            if (slOrderId is null)
            {
                await ForceFlatAsync(symbol, cancel);
                return;
            }

            _session.SlClientOrderId = slClientOrderId;
            _session.SlOrderId = slOrderId.OrderId;

            // --- TP1 / TP2 – 50% / 25% / 25% (reszta runner) ---

            decimal tp1QtyRaw = totalQty * 0.5m;
            decimal tp2QtyRaw = totalQty * 0.25m;

            var step = symbolInfo.QtyStep ?? 0m;

            decimal tp1Qty = MathHelpers.RoundQuantity(step, tp1QtyRaw);
            decimal tp2Qty = MathHelpers.RoundQuantity(step, tp2QtyRaw);

            decimal runnerQty = totalQty - tp1Qty - tp2Qty;

            if (tp1Qty <= 0m)
            {
                // fallback – wszystko na jednym TP1
                tp1Qty = totalQty;
                tp2Qty = 0m;
                runnerQty = 0m;
            }
            else
            {
                if (tp1Qty + tp2Qty > totalQty)
                {
                    tp2Qty = MathHelpers.RoundQuantity(step, totalQty - tp1Qty);
                    if (tp2Qty < 0m) tp2Qty = 0m;
                }

                runnerQty = totalQty - tp1Qty - tp2Qty;
                if (runnerQty < 0m)
                    runnerQty = 0m;
            }

            _session.Tp1Quantity = tp1Qty;
            _session.Tp2Quantity = tp2Qty;
            _session.RunnerQuantity = runnerQty;

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

                var tp1OrderId = await _restClient.PlaceOrderAsync(tp1Req, cancel);
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

                var tp2OrderId = await _restClient.PlaceOrderAsync(tp2Req, cancel);
                if (tp2OrderId is not null)
                {
                    _session.Tp2ClientOrderId = tp2ClientOrderId;
                    _session.Tp2OrderId = tp2OrderId.OrderId;
                }
            }
        }

        // =====================================================================
        // 3.3. TP1 fill -> SL na BE
        // =====================================================================

        private async Task HandleTp1FilledAsync(
            string symbol,
            SymbolInfo symbolInfo,
            OrderUpdate update,
            SigmaData data,
            CancellationToken cancel)
        {
            if (!_session.IsActive ||
                _session.Tp1ClientOrderId is null ||
                !string.Equals(update.ClientOrderId, _session.Tp1ClientOrderId, StringComparison.Ordinal))
                return;

            if (_session.Tp1Hit)
                return; // już przetworzone

            _session.Tp1Hit = true;

            // Zmniejszamy ActiveQuantity o ilość TP1
            var tp1Qty = _session.Tp1Quantity ?? 0m;
            if (_session.ActiveQuantity.HasValue && tp1Qty > 0m)
            {
                _session.ActiveQuantity = Math.Max(0m, _session.ActiveQuantity.Value - tp1Qty);
            }

            if (_session.EntryPrice is null || _session.Side is null)
                return;

            var remainingQty = _session.ActiveQuantity ?? 0m;
            if (remainingQty <= 0m)
            {
                await ForceFlatAsync(symbol, cancel);
                return;
            }

            var entry = _session.EntryPrice.Value;
            var side = _session.Side.Value;
            var bePrice = entry;
            var mode = _session.EntryMode ?? ModeKind.None;

            string slClientOrderId = SigmaClientOrderId.Build(
                symbol,
                mode,
                side,
                SigmaOrderKind.StopLossBreakEven,
                update.UpdateTime ?? DateTime.UtcNow);

            var slReq = new BybitCbFuturesRestClient.OrderRequest(
                OrderId: _session.SlOrderId,
                Symbol: symbol,
                Category: Category.Linear,
                Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                Type: NewOrderType.Market,
                Quantity: remainingQty,
                Price: null,
                TriggerPrice: bePrice,
                TriggerBy: TriggerType.MarkPrice,
                ClientOrderId: slClientOrderId);

            var slOrderId = await _restClient.PlaceOrderAsync(slReq, cancel);
            if (slOrderId is not null)
            {
                _session.SlClientOrderId = slClientOrderId;
                _session.SlOrderId = slOrderId.OrderId;
                _session.SlPrice = bePrice;
            }
        }

        // =====================================================================
        // 3.4. TP2 fill -> runner + trailing / full exit
        // =====================================================================

        private async Task HandleTp2FilledAsync(
            string symbol,
            SymbolInfo symbolInfo,
            OrderUpdate update,
            SigmaData data,
            CancellationToken cancel)
        {
            if (!_session.IsActive ||
                _session.Tp2ClientOrderId is null ||
                !string.Equals(update.ClientOrderId, _session.Tp2ClientOrderId, StringComparison.Ordinal))
                return;

            var tp2Qty = _session.Tp2Quantity ?? 0m;
            if (_session.ActiveQuantity.HasValue && tp2Qty > 0m)
            {
                _session.ActiveQuantity = Math.Max(0m, _session.ActiveQuantity.Value - tp2Qty);
            }

            var remainingQty = _session.ActiveQuantity ?? 0m;
            if (remainingQty <= 0m)
            {
                await ForceFlatAsync(symbol, cancel);
                return;
            }

            // Dla pozostałego runnera – od razu aktualizujemy trailing SL
            var now = update.UpdateTime ?? DateTime.UtcNow;
            await UpdateTrailingStopAsync(now, symbolInfo, data, cancel);
        }

        // =====================================================================
        // 3.5. SL / SLBE / TSL / MRTimeStop fill -> pełny exit
        // =====================================================================

        private async Task HandleStopLossFilledAsync(
            string symbol,
            string clientOrderId,
            CancellationToken cancel)
        {
            if (!_session.IsActive)
                return;

            // Jeśli to nasz aktualny SL (dowolnego rodzaju), kończymy sesję.
            if (_session.SlClientOrderId is not null &&
                !string.Equals(clientOrderId, _session.SlClientOrderId, StringComparison.Ordinal))
                return;

            await ForceFlatAsync(symbol, cancel);
        }

        // =====================================================================
        // TRAILING
        // =====================================================================

        private async Task UpdateTrailingStopAsync(
            DateTime nowUtc,
            SymbolInfo symbolInfo,
            SigmaData data,
            CancellationToken cancel)
        {
            if (!_session.HasActivePosition ||
                _session.Side is null ||
                _session.SlPrice is null ||
                _session.EntryPrice is null ||
                _session.ActiveQuantity is null ||
                _session.EntryMode is null)
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

            // Nigdy nie cofamy SL poniżej BE
            decimal be = entry;
            if (side == OrderSide.Buy && rawNewSl < be) rawNewSl = be;
            if (side == OrderSide.Sell && rawNewSl > be) rawNewSl = be;

            decimal newSl = MathHelpers.RoundPrice(symbolInfo.PriceScale, rawNewSl);

            if (Math.Abs(newSl - currentSl) < ComputeMinSlMove(symbolInfo, entry))
                return;

            var qty = _session.ActiveQuantity.Value;
            if (qty <= 0m)
                return;

            string slClientOrderId = SigmaClientOrderId.Build(
                symbolInfo.Name,
                _session.EntryMode.Value,
                side,
                SigmaOrderKind.TrailingStopLoss,
                nowUtc);

            var slReq = new BybitCbFuturesRestClient.OrderRequest(
                OrderId: _session.SlOrderId,
                Symbol: symbolInfo.Name,
                Category: Category.Linear,
                Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                Type: NewOrderType.Market,
                Quantity: qty,
                Price: null,
                TriggerPrice: newSl,
                TriggerBy: TriggerType.MarkPrice,
                ClientOrderId: slClientOrderId);

            var slOrderId = await _restClient.AmendOrderAsync(slReq, cancel);
            if (slOrderId is null)
                return;

            _session.SlClientOrderId = slClientOrderId;
            _session.SlOrderId = slOrderId.OrderId;
            _session.SlPrice = newSl;
        }

        // =====================================================================
        // RISK
        // =====================================================================

        public decimal ComputeRiskPerTradeUsd(
            SigmaData sigmaData,
            ModeSignal modeSignal,
            decimal? equity)
        {
            // 1) Equity strategii w USDT.
            if (equity.HasValue && equity <= 0m)
                return _options.MinRiskPerTradeUsd;

            if (!equity.HasValue)
                return _options.MinRiskPerTradeUsd;

            // 2) Base risk z equity (przed tierami i środowiskiem)
            decimal baseRisk = equity.Value * _options.RiskPerTradePct;

            // clamp do widełek globalnych
            if (baseRisk < _options.MinRiskPerTradeUsd) baseRisk = _options.MinRiskPerTradeUsd;
            if (baseRisk > _options.MaxRiskPerTradeUsd) baseRisk = _options.MaxRiskPerTradeUsd;

            // 3) Mnożnik tieru (Hard/Medium/Soft/None)
            decimal tierMultiplier = modeSignal.Tier switch
            {
                ModeTier.Hard => _options.TierHardRiskMultiplier,
                ModeTier.Medium => _options.TierMediumRiskMultiplier,
                ModeTier.Soft => _options.TierSoftRiskMultiplier,
                _ => _options.TierNoneRiskMultiplier
            };

            decimal risk = baseRisk * tierMultiplier;

            // 4) Modulatory środowiska – korelacja / BTC shock
            decimal envMultiplier = 1.0m;

            if (double.IsFinite(sigmaData.CorrToBtc15m) &&
                Math.Abs(sigmaData.CorrToBtc15m) >= (double)_options.CorrHighReduceSizeThreshold &&
                Math.Abs(sigmaData.CorrToBtc15m) < (double)_options.CorrOppositeBlock)
            {
                envMultiplier *= _options.CorrHighSizeMultiplier;
            }

            // BTC shock – do uzupełnienia po dodaniu BtcAtr do SigmaData.
            risk *= envMultiplier;

            // 5) Ostateczny clamp
            if (risk < _options.MinRiskPerTradeUsd) risk = _options.MinRiskPerTradeUsd;
            if (risk > _options.MaxRiskPerTradeUsd) risk = _options.MaxRiskPerTradeUsd;

            return risk;
        }

        // =====================================================================
        // HELPERY
        // =====================================================================

        private async Task ForceFlatAsync(
            string symbol,
            CancellationToken cancel)
        {
            var ids = new[]
                {
                    _session.EntryOrderId,
                    _session.SlOrderId,
                    _session.Tp1OrderId,
                    _session.Tp2OrderId
                }
                .Where(id => !string.IsNullOrEmpty(id));

            foreach (var id in ids)
            {
                await SafeCancelAsync(symbol, id!, cancel);
            }

            _session.Reset();
        }

        private async Task CancelProtectiveOrdersAsync(string symbol, CancellationToken cancel)
        {
            var ids = new[]
                {
                    _session.SlOrderId,
                    _session.Tp1OrderId,
                    _session.Tp2OrderId
                }
                .Where(id => !string.IsNullOrEmpty(id));

            foreach (var id in ids)
            {
                await SafeCancelAsync(symbol, id!, cancel);
            }
        }

        private async Task SafeCancelAsync(string symbol, string orderId, CancellationToken cancel)
        {
            try
            {
                await _restClient.CancelOrderAsync(symbol, orderId, cancel);
            }
            catch
            {
                // best-effort – nie chcemy wysadzić strategii przez błąd cancel
            }
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

            return MathHelpers.RoundQuantity(symbolInfo.QtyStep ?? 0m, rawQty);
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

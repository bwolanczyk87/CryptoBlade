using Bybit.Net.Enums;
using CryptoBlade.Exchanges;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Modes;
using System.Globalization;
using OrderSide = CryptoBlade.Models.OrderSide;

namespace CryptoBlade.Strategies.Sigma
{
    public enum SigmaTradeState
    {
        Flat = 0,
        WaitingForEntryFill = 1,
        Active = 2
    }

    public sealed class SigmaTradeSession
    {
        public SigmaTradeState State { get; set; } = SigmaTradeState.Flat;

        public OrderSide? Side { get; set; }
        public string? DirectionTag { get; set; }     // "LONG"/"SHORT"

        public decimal? Quantity { get; set; }

        public string? EntryClientOrderId { get; set; }
        public string? EntryOrderId { get; set; }
        public DateTime? EntryCreatedUtc { get; set; }
        public DateTime? EntryFilledUtc { get; set; }
        public decimal? EntryPrice { get; set; }
        public Mode? EntryMode { get; set; }

        public string? SlClientOrderId { get; set; }
        public string? SlOrderId { get; set; }
        public decimal? SlPrice { get; set; }

        public string? Tp1ClientOrderId { get; set; }
        public string? Tp1OrderId { get; set; }
        public decimal? Tp1Price { get; set; }
        public bool Tp1Hit { get; set; }

        public string? Tp2ClientOrderId { get; set; }
        public string? Tp2OrderId { get; set; }
        public decimal? Tp2Price { get; set; }

        public void Reset()
        {
            State = SigmaTradeState.Flat;
            Side = null;
            DirectionTag = null;
            Quantity = null;

            EntryClientOrderId = null;
            EntryOrderId = null;
            EntryCreatedUtc = null;
            EntryFilledUtc = null;
            EntryPrice = null;
            EntryMode = null;

            SlClientOrderId = null;
            SlOrderId = null;
            SlPrice = null;

            Tp1ClientOrderId = null;
            Tp1OrderId = null;
            Tp1Price = null;
            Tp1Hit = false;

            Tp2ClientOrderId = null;
            Tp2OrderId = null;
            Tp2Price = null;
        }

        public bool HasPendingEntry =>
            State == SigmaTradeState.WaitingForEntryFill && EntryClientOrderId is not null;

        public bool IsActive => State == SigmaTradeState.Active;

        /// <summary>Ustawia sesję w stan oczekiwania na fill nowego ENTRY.</summary>
        public void InitPendingEntry(
            OrderSide side,
            string directionTag,
            decimal quantity,
            string entryClientOrderId,
            string entryOrderId,
            DateTime entryCreatedUtc,
            decimal entryPrice,
            Mode entryMode,
            decimal slPrice,
            decimal tp1Price,
            decimal tp2Price)
        {
            Reset();
            State = SigmaTradeState.WaitingForEntryFill;
            Side = side;
            DirectionTag = directionTag;
            Quantity = quantity;

            EntryClientOrderId = entryClientOrderId;
            EntryOrderId = entryOrderId;
            EntryCreatedUtc = entryCreatedUtc;
            EntryPrice = entryPrice;
            EntryMode = entryMode;

            SlPrice = slPrice;
            Tp1Price = tp1Price;
            Tp2Price = tp2Price;
        }

        /// <summary>Odtwarza stan oczekującego ENTRY z istniejącego zlecenia.</summary>
        public void InitPendingEntryFromRecovery(Order entry)
        {
            Reset();
            State = SigmaTradeState.WaitingForEntryFill;
            Side = entry.Side;
            DirectionTag = entry.Side == OrderSide.Buy ? "LONG" : "SHORT";
            EntryMode = null;
            Quantity = entry.Quantity;
            EntryClientOrderId = entry.ClientOrderId;
            EntryOrderId = entry.OrderId;
            EntryCreatedUtc = entry.CreateTime;
            EntryPrice = entry.Price > 0m ? entry.Price : null;
        }

        /// <summary>Odtwarza stan aktywnego trade'u z istniejącego SL i opcjonalnie TP1/TP2.</summary>
        public void InitActiveFromRecovery(Order sl, Order? tp1, Order? tp2)
        {
            Reset();

            var side = sl.Side == OrderSide.Sell ? OrderSide.Buy : OrderSide.Sell;
            State = SigmaTradeState.Active;
            Side = side;
            DirectionTag = side == OrderSide.Buy ? "LONG" : "SHORT";
            EntryMode = null;
            Quantity = sl.Quantity;

            SlClientOrderId = sl.ClientOrderId;
            SlOrderId = sl.OrderId;
            SlPrice = sl.Price;

            if (tp1 is not null)
            {
                Tp1ClientOrderId = tp1.ClientOrderId;
                Tp1OrderId = tp1.OrderId;
                Tp1Price = tp1.Price;
            }

            if (tp2 is not null)
            {
                Tp2ClientOrderId = tp2.ClientOrderId;
                Tp2OrderId = tp2.OrderId;
                Tp2Price = tp2.Price;
            }

            decimal? guessEntry = null;
            if (Tp1Price.HasValue && SlPrice.HasValue)
                guessEntry = (Tp1Price.Value + SlPrice.Value) / 2m;
            else if (Tp2Price.HasValue && SlPrice.HasValue)
                guessEntry = (Tp2Price.Value + SlPrice.Value) / 2m;

            EntryPrice = guessEntry;
            EntryFilledUtc = sl.CreateTime;
        }
    }

    /// <summary>
    /// Manager pojedynczego trade'u Sigmy (ENTRY + SL/TP1/TP2 + BE + trailing + MR time-stop).
    /// </summary>
    public sealed class SigmaPositionManager
    {
        private readonly SigmaTradeSession _session = new();

        private static readonly TimeSpan EntryTimeout = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MrTimeStop = TimeSpan.FromMinutes(40);

        public async Task OnSignalAsync(
            string symbol,
            SymbolInfo symbolInfo,
            SigmaData sigmaData,
            Mode activeMode,
            ModeSignal modeSignal,
            bool tradable,
            DateTime nowUtc,
            ICbFuturesRestClient restClient,
            CancellationToken cancel)
        {
            // 0) timeout dla pending ENTRY
            await CheckAndCancelStaleEntryAsync(symbol, restClient, nowUtc, cancel);

            // 0.5) trailing SL dla aktywnej pozycji
            if (_session.IsActive)
                await ApplyTrailingStopAsync(symbol, symbolInfo, sigmaData, activeMode, restClient, nowUtc, cancel);

            // 0.6) time-stop dla MR
            await ApplyMrTimeStopAsync(symbol, restClient, nowUtc, cancel);

            // 1) jeśli globalne gate'y blokują, nie otwieramy nowego trade'u
            if (!tradable)
                return;

            // 2) nie otwieramy nowego, jeśli już mamy trade
            if (_session.IsActive || _session.HasPendingEntry)
                return;

            bool buy = modeSignal.HasBuy && !modeSignal.HasSell;
            bool sell = modeSignal.HasSell && !modeSignal.HasBuy;
            if (!buy && !sell)
                return;

            var side = buy ? OrderSide.Buy : OrderSide.Sell;
            var directionTag = buy ? "LONG" : "SHORT";

            // 3) wyliczamy entry/SL/TP w sposób zależny od reżimu:
            if (!TryComputeEntrySetup(
                    sigmaData,
                    symbolInfo,
                    activeMode,
                    side,
                    out var entryPrice,
                    out var slPrice,
                    out var tp1Price,
                    out var tp2Price))
            {
                return;
            }

            // 4) sizing – na razie na sztywno 100 USD
            decimal qty = CalculateQuantityInContracts(symbolInfo, entryPrice, 100m);
            if (qty <= 0)
                return;

            string entryClientOrderId = BuildClientOrderId(symbol, directionTag, "ENTRY", nowUtc);

            var request = new BybitCbFuturesRestClient.CbOrderRequest(
                Symbol: symbol,
                Category: Category.Linear,
                Side: side.ToOrderSide(),
                Type: NewOrderType.Limit,
                Quantity: qty,
                Price: entryPrice,
                TriggerPrice: null,
                TriggerBy: null,
                ReduceOnly: false,
                CloseOnTrigger: false,
                TimeInForce: TimeInForce.PostOnly,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: entryClientOrderId);

            var orderId = await restClient.PlaceOrderAsync(request, cancel);
            if (orderId is null)
                return;

            // 5) zapis stanu sesji (w jednej linijce na call-site)
            _session.InitPendingEntry(
                side,
                directionTag,
                qty,
                entryClientOrderId,
                orderId.OrderId,
                nowUtc,
                entryPrice,
                activeMode,
                slPrice,
                tp1Price,
                tp2Price);
        }

        /// <summary>
        /// Reakcja na OrderUpdate – pośrednio wołana z TradingStrategyBase.
        /// </summary>
        public async Task OnOrderUpdateAsync(
            string symbol,
            OrderUpdate update,
            ICbFuturesRestClient restClient,
            CancellationToken cancel)
        {
            var clientOrderId = update.ClientOrderId;
            if (string.IsNullOrWhiteSpace(clientOrderId))
                return;

            if (!clientOrderId.StartsWith("SIGMA|", StringComparison.Ordinal))
                return;

            if (update.Status != CryptoBlade.Models.OrderStatus.Filled)
                return;

            var kind = TryParseKind(clientOrderId);

            switch (kind)
            {
                case SigmaOrderKind.Entry:
                    await HandleEntryFilledAsync(symbol, update, restClient, cancel);
                    break;
                case SigmaOrderKind.TakeProfit1:
                    await HandleTp1FilledAsync(symbol, update, restClient, cancel);
                    break;
                case SigmaOrderKind.TakeProfit2:
                case SigmaOrderKind.StopLoss:
                    await HandleFinalExitAsync(symbol, restClient, cancel);
                    break;
            }
        }

        #region ENTRY timeout / entry setup / SL/TP / BE / trailing / cleanup

        private async Task CheckAndCancelStaleEntryAsync(
            string symbol,
            ICbFuturesRestClient restClient,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            if (!_session.HasPendingEntry || !_session.EntryCreatedUtc.HasValue)
                return;

            var age = nowUtc - _session.EntryCreatedUtc.Value;
            if (age <= EntryTimeout)
                return;

            if (!string.IsNullOrEmpty(_session.EntryOrderId))
                await restClient.CancelOrderAsync(symbol, _session.EntryOrderId!, cancel);

            _session.Reset();
        }

        private bool TryComputeEntrySetup(
            SigmaData d,
            SymbolInfo symbolInfo,
            Mode mode,
            OrderSide side,
            out decimal entryPrice,
            out decimal slPrice,
            out decimal tp1Price,
            out decimal tp2Price)
        {
            entryPrice = slPrice = tp1Price = tp2Price = 0m;

            decimal? lastClose = d.Last1mClose ?? d.LastPrice;
            if (lastClose is null || lastClose <= 0m)
                return false;

            decimal refPrice = lastClose.Value;

            decimal atr1h = double.IsNaN(d.Atr1hAbs) ? 0m : (decimal)d.Atr1hAbs;
            decimal atr5m = double.IsNaN(d.Atr5mAbs) ? 0m : (decimal)d.Atr5mAbs;

            // bazowa jednostka ryzyka – preferujemy ATR 5m, fallback do ATR 1h / 0.5% ceny
            decimal riskUnit =
                atr5m > 0m ? atr5m :
                atr1h > 0m ? atr1h * 0.5m :
                refPrice * 0.005m;

            if (riskUnit <= 0m)
                return false;

            decimal dir = side == OrderSide.Buy ? 1m : -1m;

            decimal? last5High = d.Last5mHigh ?? d.Last1mHigh ?? refPrice;
            decimal? last5Low = d.Last5mLow ?? d.Last1mLow ?? refPrice;

            decimal range5 =
                (last5High.HasValue && last5Low.HasValue)
                    ? (last5High.Value - last5Low.Value)
                    : refPrice * 0.002m;

            if (range5 <= 0m)
                range5 = refPrice * 0.002m;

            decimal? dvwap = d.LastDvwap;

            switch (mode)
            {
                case Mode.MM:
                    {
                        if (side == OrderSide.Buy)
                        {
                            decimal lo = last5Low ?? refPrice;
                            entryPrice = lo + 0.5m * range5;
                            slPrice = lo - riskUnit;
                        }
                        else
                        {
                            decimal hi = last5High ?? refPrice;
                            entryPrice = hi - 0.5m * range5;
                            slPrice = hi + riskUnit;
                        }
                        break;
                    }

                case Mode.MR:
                    {
                        decimal baseEntry = dvwap.HasValue && dvwap.Value > 0m
                            ? dvwap.Value
                            : refPrice;

                        entryPrice = baseEntry;
                        decimal mrRisk = riskUnit * 0.8m;

                        slPrice = side == OrderSide.Buy
                            ? baseEntry - mrRisk
                            : baseEntry + mrRisk;

                        break;
                    }

                case Mode.BO:
                    {
                        if (d.OpeningRangeHigh.HasValue && d.OpeningRangeLow.HasValue)
                        {
                            decimal orHigh = d.OpeningRangeHigh.Value;
                            decimal orLow = d.OpeningRangeLow.Value;
                            decimal margin = riskUnit * 0.5m;

                            if (side == OrderSide.Buy)
                            {
                                entryPrice = orHigh;
                                slPrice = orLow - margin;
                            }
                            else
                            {
                                entryPrice = orLow;
                                slPrice = orHigh + margin;
                            }
                        }
                        else
                        {
                            if (side == OrderSide.Buy)
                            {
                                decimal lo = last5Low ?? refPrice;
                                entryPrice = lo + 0.5m * range5;
                                slPrice = lo - riskUnit;
                            }
                            else
                            {
                                decimal hi = last5High ?? refPrice;
                                entryPrice = hi - 0.5m * range5;
                                slPrice = hi + riskUnit;
                            }
                        }
                        break;
                    }

                default:
                    {
                        entryPrice = refPrice;
                        slPrice = side == OrderSide.Buy
                            ? refPrice - riskUnit
                            : refPrice + riskUnit;
                        break;
                    }
            }

            // R i TP1/TP2
            decimal r = Math.Abs(entryPrice - slPrice);
            if (r <= 0m)
                return false;

            tp1Price = entryPrice + dir * 1.0m * r;
            tp2Price = entryPrice + dir * 1.8m * r;

            // zaokrąglenia
            entryPrice = RoundPrice(symbolInfo, entryPrice);
            slPrice = RoundPrice(symbolInfo, slPrice);
            tp1Price = RoundPrice(symbolInfo, tp1Price);
            tp2Price = RoundPrice(symbolInfo, tp2Price);

            if (entryPrice <= 0m || slPrice <= 0m || tp1Price <= 0m || tp2Price <= 0m)
                return false;

            return true;
        }

        private async Task HandleEntryFilledAsync(
            string symbol,
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

            // SL stop-market RO
            string slClientOrderId = BuildClientOrderId(symbol, _session.DirectionTag!, "SL",
                _session.EntryFilledUtc!.Value);

            var slReq = new BybitCbFuturesRestClient.CbOrderRequest(
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

            // TP1 / TP2
            decimal tp1Qty = qty * 0.5m;
            decimal tp2Qty = qty - tp1Qty;

            string tp1ClientOrderId = BuildClientOrderId(symbol, _session.DirectionTag!, "TP1",
                _session.EntryFilledUtc.Value);

            var tp1Req = new BybitCbFuturesRestClient.CbOrderRequest(
                Symbol: symbol,
                Category: Category.Linear,
                Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                Type: NewOrderType.Limit,
                Quantity: tp1Qty,
                Price: tp1Price,
                TriggerPrice: null,
                TriggerBy: null,
                ReduceOnly: true,
                CloseOnTrigger: true,
                TimeInForce: TimeInForce.GoodTillCanceled,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: tp1ClientOrderId);

            var tp1OrderId = await restClient.PlaceOrderAsync(tp1Req, cancel);
            if (tp1OrderId is not null)
            {
                _session.Tp1ClientOrderId = tp1ClientOrderId;
                _session.Tp1OrderId = tp1OrderId.OrderId;
            }

            string tp2ClientOrderId = BuildClientOrderId(symbol, _session.DirectionTag!, "TP2",
                _session.EntryFilledUtc.Value);

            var tp2Req = new BybitCbFuturesRestClient.CbOrderRequest(
                Symbol: symbol,
                Category: Category.Linear,
                Side: (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide(),
                Type: NewOrderType.Limit,
                Quantity: tp2Qty,
                Price: tp2Price,
                TriggerPrice: null,
                TriggerBy: null,
                ReduceOnly: true,
                CloseOnTrigger: true,
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
            var bePrice = entry; // na start czysty BE

            // kasujemy stary SL i stawiamy nowy na BE
            if (!string.IsNullOrEmpty(_session.SlOrderId))
                await restClient.CancelOrderAsync(symbol, _session.SlOrderId!, cancel);

            var side = _session.Side ?? OrderSide.Buy;
            string slClientOrderId = BuildClientOrderId(symbol, _session.DirectionTag!, "SLBE",
                update.UpdateTime!.Value);

            var slReq = new BybitCbFuturesRestClient.CbOrderRequest(
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

        private async Task ApplyTrailingStopAsync(
            string symbol,
            SymbolInfo symbolInfo,
            SigmaData d,
            Mode mode,
            ICbFuturesRestClient restClient,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            if (!_session.IsActive ||
                _session.Side is null ||
                _session.SlPrice is null ||
                _session.EntryPrice is null)
                return;

            // trailing dopiero po TP1 (logika MM/BO z założeń Sigmy)
            if (!_session.Tp1Hit)
                return;

            double atr5 = d.Atr5mAbs;
            if (double.IsNaN(atr5) || !(atr5 > 0.0))
                return;

            decimal atr = (decimal)atr5;

            decimal? lastClose = d.Last1mClose ?? d.LastPrice;
            if (lastClose is null || lastClose <= 0m)
                return;

            decimal currentSl = _session.SlPrice.Value;
            decimal entry = _session.EntryPrice.Value;
            var side = _session.Side.Value;

            decimal factor = mode switch
            {
                Mode.MM => 1.0m,
                Mode.BO => 1.0m,
                Mode.MR => 1.5m,
                _ => 1.2m
            };

            decimal rawNewSl = side == OrderSide.Buy
                ? lastClose.Value - factor * atr
                : lastClose.Value + factor * atr;

            // tylko w stronę zysku
            bool improves = side == OrderSide.Buy
                ? rawNewSl > currentSl
                : rawNewSl < currentSl;

            if (!improves)
                return;

            // nie schodzimy poniżej BE (long) / powyżej BE (short)
            decimal be = entry;
            if (side == OrderSide.Buy && rawNewSl < be) rawNewSl = be;
            if (side == OrderSide.Sell && rawNewSl > be) rawNewSl = be;

            decimal newSl = RoundPrice(symbolInfo, rawNewSl);

            if (Math.Abs(newSl - currentSl) < ComputeMinSlMove(symbolInfo, entry))
                return;

            // kasujemy stary SL
            if (!string.IsNullOrEmpty(_session.SlOrderId))
                await restClient.CancelOrderAsync(symbol, _session.SlOrderId!, cancel);

            string slClientOrderId = BuildClientOrderId(symbol, _session.DirectionTag!, "SLTRAIL", nowUtc);
            var slReq = new BybitCbFuturesRestClient.CbOrderRequest(
                Symbol: symbol,
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

        private async Task ApplyMrTimeStopAsync(
            string symbol,
            ICbFuturesRestClient restClient,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            // Trade musi być aktywny i pochodzić z MR
            if (!_session.IsActive || _session.EntryMode != Mode.MR)
                return;

            if (_session.EntryFilledUtc is null)
                return;

            // Time-stop tylko jeśli TP1 jeszcze nie strzelony
            if (_session.Tp1Hit)
                return;

            var age = nowUtc - _session.EntryFilledUtc.Value;
            if (age <= MrTimeStop)
                return;

            if (_session.Side is null || _session.Quantity is null)
            {
                await HandleFinalExitAsync(symbol, restClient, cancel);
                return;
            }

            var side = _session.Side.Value;
            decimal qty = _session.Quantity.Value;
            if (qty <= 0)
            {
                await HandleFinalExitAsync(symbol, restClient, cancel);
                return;
            }

            // Zamykamy pozycję marketem reduce-only
            var closeSide = (side == OrderSide.Buy ? OrderSide.Sell : OrderSide.Buy).ToOrderSide();
            string clientOrderId = BuildClientOrderId(symbol, _session.DirectionTag ?? "UNK", "MRTSTOP", nowUtc);

            var closeReq = new BybitCbFuturesRestClient.CbOrderRequest(
                Symbol: symbol,
                Category: Category.Linear,
                Side: closeSide,
                Type: NewOrderType.Market,
                Quantity: qty,
                Price: null,
                TriggerPrice: null,
                TriggerBy: null,
                ReduceOnly: true,
                CloseOnTrigger: false,
                TimeInForce: TimeInForce.GoodTillCanceled,
                PositionIdx: side == OrderSide.Buy ? PositionIdx.BuyHedgeMode : PositionIdx.SellHedgeMode,
                ClientOrderId: clientOrderId);

            await restClient.PlaceOrderAsync(closeReq, cancel);

            // Sprzątamy wszystkie nasze SIGMA-order’y i resetujemy stan
            await HandleFinalExitAsync(symbol, restClient, cancel);
        }

        private async Task HandleFinalExitAsync(
            string symbol,
            ICbFuturesRestClient restClient,
            CancellationToken cancel)
        {
            var ids = new[] { _session.EntryOrderId, _session.SlOrderId, _session.Tp1OrderId, _session.Tp2OrderId }
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

            return rawQty;
        }

        public async Task RecoverFromOpenOrdersAsync(
            Order[] openOrders,
            string symbol,
            SymbolInfo symbolInfo,
            ICbFuturesRestClient restClient,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            _session.Reset();
            if (openOrders is null || openOrders.Length == 0)
                return;

            // Tylko nasze SIGMA-order’y
            var sigmaOrders = openOrders
                .Where(o => !string.IsNullOrWhiteSpace(o.ClientOrderId) &&
                            o.ClientOrderId!.StartsWith("SIGMA|", StringComparison.Ordinal))
                .ToArray();

            if (sigmaOrders.Length == 0)
                return;

            // Rozdzielamy według rodzaju
            var entries = sigmaOrders.Where(o => TryParseKind(o.ClientOrderId!) == SigmaOrderKind.Entry).ToList();
            var sls = sigmaOrders.Where(o => TryParseKind(o.ClientOrderId!) == SigmaOrderKind.StopLoss).ToList();
            var tp1s = sigmaOrders.Where(o => TryParseKind(o.ClientOrderId!) == SigmaOrderKind.TakeProfit1).ToList();
            var tp2s = sigmaOrders.Where(o => TryParseKind(o.ClientOrderId!) == SigmaOrderKind.TakeProfit2).ToList();

            // 1) TP bez SL => traktujemy jako osierocone – kasujemy wszystko i wracamy
            if (!sls.Any() && (tp1s.Any() || tp2s.Any()))
            {
                foreach (var o in sigmaOrders)
                    await restClient.CancelOrderAsync(symbol, o.OrderId, cancel);

                _session.Reset();
                return;
            }

            // 2) ENTRY bez SL => waiting for fill
            if (!sls.Any() && entries.Any())
            {
                var entry = entries.OrderByDescending(o => o.CreateTime).First();
                _session.InitPendingEntryFromRecovery(entry);
                return;
            }

            // 3) Mamy SL => trade jest aktywny
            if (sls.Any())
            {
                var sl = sls.OrderByDescending(o => o.CreateTime).First();
                var tp1 = tp1s.OrderByDescending(o => o.CreateTime).FirstOrDefault();
                var tp2 = tp2s.OrderByDescending(o => o.CreateTime).FirstOrDefault();

                _session.InitActiveFromRecovery(sl, tp1, tp2);
                return;
            }

            // fallback – jakby coś poszło nie tak, czyścimy
            _session.Reset();
        }

        private static decimal RoundPrice(SymbolInfo symbolInfo, decimal price)
        {
            var scale = (int)symbolInfo.PriceScale;
            if (scale is < 0 or > 18)
                scale = 4;

            return Math.Round(price, scale, MidpointRounding.AwayFromZero);
        }

        private static decimal ComputeMinSlMove(SymbolInfo symbolInfo, decimal refPrice)
        {
            var scale = (int)symbolInfo.PriceScale;
            if (scale is < 0 or > 18)
                scale = 4;

            decimal tick = (decimal)Math.Pow(10, -scale);
            return tick * 2m;
        }

        private static string BuildClientOrderId(string symbol, string directionTag, string kind, DateTime nowUtc)
        {
            var ts = nowUtc.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
            return $"SIGMA|{symbol}|{directionTag}|{kind}|{ts}";
        }

        private static SigmaOrderKind TryParseKind(string clientOrderId)
        {
            var parts = clientOrderId.Split('|');
            if (parts.Length < 4)
                return SigmaOrderKind.Unknown;

            return parts[3] switch
            {
                "ENTRY" => SigmaOrderKind.Entry,
                "SL" or "SLBE" or "SLTRAIL" => SigmaOrderKind.StopLoss,
                "TP1" => SigmaOrderKind.TakeProfit1,
                "TP2" => SigmaOrderKind.TakeProfit2,
                _ => SigmaOrderKind.Unknown
            };
        }

        private enum SigmaOrderKind
        {
            Unknown = 0,
            Entry = 1,
            StopLoss = 2,
            TakeProfit1 = 3,
            TakeProfit2 = 4
        }

        #endregion
    }
}

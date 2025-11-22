using Bybit.Net.Enums;
using CryptoBlade.Exchanges;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Wallet;
using System.Globalization;
using OrderSide = CryptoBlade.Models.OrderSide;

namespace CryptoBlade.Strategies.Sigma
{
    /// <summary>
    /// Manager pojedynczego trade'u Sigmy (ENTRY + SL/TP1/TP2 + BE + trailing + MR time-stop).
    /// </summary>
    public sealed class SigmaPositionManager(SigmaStrategyOptions options)
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
            IWalletManager walletManager, 
            CancellationToken cancel)
        {
            // 0) timeout dla pending ENTRY
            await CheckAndCancelStaleEntryAsync(symbol, restClient, nowUtc, cancel);

            // 0.5) trailing SL dla aktywnej pozycji
            if (_session.IsActive)
                await ApplyTrailingStopAsync(symbol, symbolInfo, sigmaData, restClient, nowUtc, cancel);

            // 0.6) time-stop dla MR
            await ApplyMrTimeStopAsync(symbol, restClient, nowUtc, cancel);

            // --- Obsługa istniejącego pending ENTRY ---

            if (_session.HasPendingEntry)
            {
                bool conflictingSignal = (modeSignal.HasBuy && _session.Side == OrderSide.Sell) || (modeSignal.HasSell && _session.Side == OrderSide.Buy);

                // Jeśli globalne gate'y blokują nowe wejścia lub sygnał odwraca kierunek,
                // kasujemy pending ENTRY zamiast bezmyślnie czekać na timeout.
                if (!tradable || conflictingSignal)
                {
                    await CancelPendingEntryAsync(symbol, restClient, nowUtc, cancel);
                }
                else
                {
                    // Gate'y są OK i brak konfliktu – szanujemy istniejący pending ENTRY.
                    return;
                }
            }

            // 1) jeśli globalne gate'y blokują, nie otwieramy nowego trade'u
            if (!tradable)
                return;

            // 2) nie otwieramy nowego, jeśli już mamy aktywny trade
            if (_session.IsActive)
                return;

            bool buy = modeSignal.HasBuy && !modeSignal.HasSell;
            bool sell = modeSignal.HasSell && !modeSignal.HasBuy;
            if (!buy && !sell)
                return; // brak jednoznacznego biasu

            var side = buy ? OrderSide.Buy : OrderSide.Sell;
            var directionTag = buy ? "LONG" : "SHORT";

            // 3) ENTRY/SL/TP z cech Sigmy
            if (!TryComputeEntrySetup(sigmaData, symbolInfo, activeMode, side, out var entryPrice, out var slPrice, out var tp1Price, out var tp2Price))
            {
                return;
            }

            // 4) Risk sizing – target R = riskPerTradeUsd, liczone z odległości ENTRY→SL
            decimal distance = side == OrderSide.Buy
                ? entryPrice - slPrice
                : slPrice - entryPrice;

            if (distance <= 0m)
                return;

            var riskPerTradeUsd = ComputeRiskPerTradeUsdAsync(
                sigmaData,
                modeSignal,
                equity: walletManager.Contract.Equity);

            if (riskPerTradeUsd <= 0m)
                return;

            // Dla kontraktów linear: risk ≈ (distance / entryPrice) * notional,
            // więc notional = Risk * entryPrice / distance.
            decimal notionalUsd = riskPerTradeUsd * entryPrice / distance;

            decimal qty = CalculateQuantityInContracts(symbolInfo, entryPrice, notionalUsd);
            if (qty <= 0m)
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

            // 5) zapis stanu sesji
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

            if (update.Status == CryptoBlade.Models.OrderStatus.Cancelled)
            {
                _session.Reset();
                return;
            }

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

        public decimal ComputeRiskPerTradeUsdAsync(
            SigmaData sigmaData,
            ModeSignal modeSignal,
            decimal? equity)
        {
            // 1) Equity strategii w USDT.
            // Dopasuj tę linijkę do swojego IWalletManager.
            // Przykład zakłada metodę GetStrategyEquityUsdAsync(symbol).

            if (equity.HasValue && equity <= 0m)
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

            // Wysoka korelacja z BTC → redukcja size (ale nie twarda blokada, ta jest w ModeEngine).
            if (double.IsFinite(sigmaData.CorrToBtc15m) &&
                Math.Abs(sigmaData.CorrToBtc15m) >= (double)options.CorrHighReduceSizeThreshold &&
                Math.Abs(sigmaData.CorrToBtc15m) < (double)options.CorrOppositeBlock)
            {
                envMultiplier *= options.CorrHighSizeMultiplier;
            }

            // BTC shock – placeholder; po dodaniu BtcAtr do SigmaData wstaw tu warunek.
            // if (sigmaData.BtcAtrPct1d >= (double)opt.BtcAtrShockThresholdPct)
            // {
            //     envMultiplier *= opt.BtcShockSizeMultiplier;
            // }

            risk *= envMultiplier;

            // 5) Ostateczny clamp
            if (risk < options.MinRiskPerTradeUsd) risk = options.MinRiskPerTradeUsd;
            if (risk > options.MaxRiskPerTradeUsd) risk = options.MaxRiskPerTradeUsd;

            return risk;
        }

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

        private async Task CancelPendingEntryAsync(
            string symbol,
            ICbFuturesRestClient restClient,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            if (!_session.HasPendingEntry)
                return;

            if (!string.IsNullOrEmpty(_session.EntryOrderId))
            {
                await restClient.CancelOrderAsync(symbol, _session.EntryOrderId!, cancel);
            }

            _session.Reset();
        }

        private bool TryComputeEntrySetup(SigmaData d, SymbolInfo symbolInfo, Mode mode,  OrderSide side, out decimal entryPrice, out decimal slPrice, out decimal tp1Price, out decimal tp2Price)
        {
            entryPrice = slPrice = tp1Price = tp2Price = 0m;
            decimal? lastClose = d.Last1mClose ?? d.LastPrice;
            if (lastClose is null || lastClose <= 0m)
                return false;

            decimal refPrice = lastClose.Value;

            decimal atr1h = double.IsNaN(d.Atr1hAbs) ? 0m : (decimal)d.Atr1hAbs;
            decimal atr5m = double.IsNaN(d.Atr5mAbs) ? 0m : (decimal)d.Atr5mAbs;

            // bazowa jednostka ryzyka – preferujemy ATR 5m, fallback do ATR 1h / 0.5% ceny
            decimal riskUnit = atr5m > 0m ? atr5m : atr1h > 0m ? atr1h * 0.5m : refPrice * 0.005m;

            if (riskUnit <= 0m)
                return false;

            decimal dir = side == OrderSide.Buy ? 1m : -1m;

            decimal? last5High = d.Last5mHigh ?? d.Last1mHigh ?? refPrice;
            decimal? last5Low = d.Last5mLow ?? d.Last1mLow ?? refPrice;

            decimal range5 = (last5High.HasValue && last5Low.HasValue)
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

        private async Task HandleEntryFilledAsync(string symbol, OrderUpdate update, ICbFuturesRestClient restClient, CancellationToken cancel)
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
            ICbFuturesRestClient restClient,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            if (!_session.IsActive ||
                _session.Side is null ||
                _session.SlPrice is null ||
                _session.EntryPrice is null)
                return;

            // Trailing dopiero po TP1 (logika MM/BO z założeń Sigmy)
            if (!_session.Tp1Hit)
                return;

            // ATR 5m jako podstawa
            double atr5 = double.IsNaN(d.Atr5mAbs) ? double.NaN : d.Atr5mAbs;
            if (!double.IsFinite(atr5) || atr5 <= 0.0)
                return;

            decimal atr = (decimal)atr5;

            decimal? lastClose = d.Last1mClose ?? d.LastPrice;
            if (lastClose is null || lastClose <= 0m)
                return;

            decimal currentSl = _session.SlPrice.Value;
            decimal entry = _session.EntryPrice.Value;
            var side = _session.Side.Value;

            // Używamy trybu z wejścia, nie bieżącego reżimu
            var entryMode = _session.EntryMode ?? Mode.MM;

            decimal factor = entryMode switch
            {
                Mode.MM => 1.0m,
                Mode.BO => 1.0m,
                Mode.MR => 1.5m,
                _ => 1.2m
            };

            decimal rawNewSl = side == OrderSide.Buy
                ? lastClose.Value - factor * atr
                : lastClose.Value + factor * atr;

            // Tylko w stronę zysku
            bool improves = side == OrderSide.Buy
                ? rawNewSl > currentSl
                : rawNewSl < currentSl;

            if (!improves)
                return;

            // BE po TP1 – SL nie może wrócić poniżej BE
            decimal be = entry; // uproszczone: po TP1 zakładamy BE na entry; 
                                // jeśli masz bardziej złożoną logikę, wstaw ją tutaj.

            if (side == OrderSide.Buy && rawNewSl < be) rawNewSl = be;
            if (side == OrderSide.Sell && rawNewSl > be) rawNewSl = be;

            decimal newSl = RoundPrice(symbolInfo, rawNewSl);

            if (Math.Abs(newSl - currentSl) < ComputeMinSlMove(symbolInfo, entry))
                return;

            // Kasujemy stary SL
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

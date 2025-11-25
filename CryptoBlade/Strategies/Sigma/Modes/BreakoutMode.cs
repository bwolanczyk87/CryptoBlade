using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public sealed class BreakoutMode(SigmaStrategyOptions options) : IMode
    {
        private readonly SigmaStrategyOptions _options = options ?? throw new ArgumentNullException(nameof(options));

        public ModeKind Kind => ModeKind.BO;

        public static double Score(SigmaData data, SigmaStrategyOptions options)
        {
            double bo = 0.0;
            double atr = data.AtrPct1h;
            bool mmAtrOk = double.IsFinite(atr) && atr > 0.0 &&
                           atr <= (double)options.BoAtrMaxPct;

            if (!mmAtrOk)
                return bo;

            double bbwP = data.Bbw15mPct;
            double zSlope = data.ZSlopeDvwap;
            double ac = data.AutoCorr5m;
            double oi = data.OiDelta1hPct;

            // 1) Szerokie pasma BB – breakout z kompresji → ekspansja (0..40)
            double bbwNorm = MathHelpers.Normalize01(
                bbwP,
                (double)options.BbWidthBreakoutPct,
                (double)options.BbWidthExitBreakoutPct);

            bbwNorm = MathHelpers.Clamp01(bbwNorm);
            bo += 40.0 * bbwNorm;                   // 0..40

            // 2) ATR – breakout lubi wyższe ATR, ale z limitem (0..15)
            if (double.IsFinite(atr))
            {
                // brak dolnego progu – rosnący score do BoAtrMaxPct
                double atrNorm = MathHelpers.Normalize01(
                    atr,
                    0.0,
                    (double)options.BoAtrMaxPct);

                atrNorm = MathHelpers.Clamp01(atrNorm);
                bo += 15.0 * atrNorm;               // 0..15
            }

            // 3) Absolutny slope DVWAP – siła jednokierunkowego ruchu (0..25)
            if (double.IsFinite(zSlope))
            {
                // 4 sigma nachylenia → pełna premia
                double slopeMag = Math.Min(Math.Abs(zSlope) / 4.0, 1.0);
                bo += 25.0 * slopeMag;              // 0..25
            }

            // 4) ΔOI>0 – napływ kapitału na wybiciu (0..10)
            if (double.IsFinite(oi) && oi > 0.0)
            {
                // saturacja przy ~10% zmiany OI
                double oiMag = Math.Min(oi / 10.0, 1.0);
                bo += 10.0 * oiMag;                 // 0..10
            }

            // 5) Dodatnia autokorelacja – kontynuacja po wybiciu (0..10)
            if (double.IsFinite(ac) && ac > 0.0)
            {
                double acClamped = Math.Min(ac, 1.0);
                bo += 10.0 * acClamped;             // 0..10
            }

            return Math.Min(Math.Max(bo, 0.0), 100.0);
        }

        public ModeSignal GenerateSignal(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(data);

            // Reset per-bar debug
            data.BreakoutEntryTier = 0;
            data.BreakoutLongCandidate = false;
            data.BreakoutShortCandidate = false;

            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h > (double)_options.BoAtrMaxPct)
            {
                // Zbyt duża zmienność dzienna – BO z retestem jest niebezpieczne.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 1. Hard – najbardziej selektywny tier:
            //    - BBW powyżej bazowego progu,
            //    - wymagamy BBW expanding,
            //    - NIE dopuszczamy Donchian bez OR+retest,
            //    - wymagamy silnego retestu (głębokość 3–50 bps),
            //    - wymagamy OI + CVD w stronę wybicia (trend lub flush).
            // -----------------------------------------------------------------

            var hard = CalculateSignal(
                data,
                tier: ModeTier.Hard,
                bbwMinOffset: +5.0,   // np. 35-percentyl jeśli baza to 30
                requireBbwExpanding: true,
                allowDonchianFallback: false,
                strongRetestDepthMinOffset: 0.0,   // 3 bps
                strongRetestDepthMaxOffset: 0.0,   // 50 bps
                oiThresholdOffset: +0.3,   // OI próg ~0.8
                requireCvd: true,
                allowFlush: true);

            if (hard.HasBuy || hard.HasSell)
                return hard;

            // -----------------------------------------------------------------
            // 2. Medium – bazowy tier BO:
            //    - BBW wg bazowego progu,
            //    - wymagamy BBW expanding,
            //    - dopuszczamy Donchian jako fallback,
            //    - nie wymagamy silnego retestu (wystarczy OR+retest lub Donchian),
            //    - wymagamy OI + CVD w stronę wybicia (trend lub flush).
            // -----------------------------------------------------------------

            var medium = CalculateSignal(
                data,
                tier: ModeTier.Medium,
                bbwMinOffset: 0.0,
                requireBbwExpanding: true,
                allowDonchianFallback: true,
                strongRetestDepthMinOffset: null,
                strongRetestDepthMaxOffset: null,
                oiThresholdOffset: 0.0,   // OI próg ~0.5
                requireCvd: true,
                allowFlush: true);

            if (medium.HasBuy || medium.HasSell)
                return medium;

            // -----------------------------------------------------------------
            // 3. Soft – najluźniejszy tier:
            //    - BBW lekko poniżej bazowego progu (ale nadal sensowne),
            //    - wymagamy BBW expanding (nie łapiemy totalnej flauty),
            //    - dopuszczamy Donchian jako fallback,
            //    - bez wymogu silnego retestu,
            //    - OI musi wspierać breakout, CVD tylko jako dodatkowy plus (nie blokuje wejść).
            // -----------------------------------------------------------------

            var soft = CalculateSignal(
                data,
                tier: ModeTier.Soft,
                bbwMinOffset: -5.0,   // np. 25-percentyl, ale clamped
                requireBbwExpanding: true,
                allowDonchianFallback: true,
                strongRetestDepthMinOffset: null,
                strongRetestDepthMaxOffset: null,
                oiThresholdOffset: -0.2,   // OI próg ~0.3
                requireCvd: false,
                allowFlush: true);

            if (soft.HasBuy || soft.HasSell)
                return soft;

            // -----------------------------------------------------------------
            // 4. None – testoswy sygnał bez żadnych wymagań (do testów i debugu)
            //var none = new ModeSignal(true, false, ModeTier.None);
            //return none;

            // Brak sygnału w którymkolwiek tierze
            return ModeSignal.None;
        }

        private ModeSignal CalculateSignal(
            SigmaData data,
            ModeTier tier,
            double bbwMinOffset,
            bool requireBbwExpanding,
            bool allowDonchianFallback,
            double? strongRetestDepthMinOffset,
            double? strongRetestDepthMaxOffset,
            double oiThresholdOffset,
            bool requireCvd,
            bool allowFlush)
        {
            // -----------------------------------------------------------------
            // 1. Środowisko breakoutowe: BBWidth + inside/NR7/Donchian
            // -----------------------------------------------------------------

            double bbwPct = data.Bbw15mPct;
            bool bbwFinite = double.IsFinite(bbwPct);

            double bbwMinBase = (double)_options.BbWidthBreakoutPct; // np. 30
            double bbwMin = bbwMinBase + bbwMinOffset;
            if (bbwMin < 5.0) bbwMin = 5.0;
            if (bbwMin > 90.0) bbwMin = 90.0;

            bool bbwEnough =
                bbwFinite &&
                bbwPct >= bbwMin;

            bool bbwExploding =
                !requireBbwExpanding ||
                (bbwFinite && data.Bbw15mExpanding);

            bool preCompression =
                data.HasInsideOrNr7 || data.DonchianBreak;

            bool envOk = bbwEnough && bbwExploding && preCompression;

            if (!envOk)
            {
                // Brak środowiska breakoutowego – nie szukamy BO.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 2. Struktura: OR breakout + retest / Donchian breakout
            // -----------------------------------------------------------------

            bool orRetestUp = data.OrBreakoutRetestUp5m;
            bool orRetestDown = data.OrBreakoutRetestDown5m;

            bool donchianUp = data.DonchianBreakUp;
            bool donchianDown = data.DonchianBreakDown;

            bool structureUp = orRetestUp;
            bool structureDown = orRetestDown;

            if (allowDonchianFallback)
            {
                structureUp |= (!orRetestDown && donchianUp);
                structureDown |= (!orRetestUp && donchianDown);
            }

            if (!structureUp && !structureDown)
            {
                // Ani OR breakout+retest, ani Donchian – brak triggera strukturalnego.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 3. Silny retest (opcjonalnie, dla A+ / Hard)
            // -----------------------------------------------------------------

            if (strongRetestDepthMinOffset is not null || strongRetestDepthMaxOffset is not null)
            {
                double depthUp = data.OrRetestDepthBpsUp5m;
                double depthDown = data.OrRetestDepthBpsDown5m;

                const double baseDepthMin = 3.0;
                const double baseDepthMax = 50.0;

                double depthMin = baseDepthMin + (strongRetestDepthMinOffset ?? 0.0);
                double depthMax = baseDepthMax + (strongRetestDepthMaxOffset ?? 0.0);

                if (depthMin < 1.0) depthMin = 1.0;
                if (depthMax < depthMin + 1.0) depthMax = depthMin + 1.0;
                if (depthMax > 80.0) depthMax = 80.0;

                if (structureUp)
                {
                    bool ok =
                        orRetestUp &&
                        double.IsFinite(depthUp) &&
                        depthUp >= depthMin &&
                        depthUp <= depthMax;

                    structureUp = ok;
                }

                if (structureDown)
                {
                    bool ok =
                        orRetestDown &&
                        double.IsFinite(depthDown) &&
                        depthDown >= depthMin &&
                        depthDown <= depthMax;

                    structureDown = ok;
                }

                if (!structureUp && !structureDown)
                {
                    // Zostaliśmy bez żadnej strony po narzuceniu wymogu strong retestu.
                    return ModeSignal.None;
                }
            }

            // -----------------------------------------------------------------
            // 4. Flow: ΔOI_1h, ΔCVD_5m
            // -----------------------------------------------------------------

            double oi = data.OiDelta1hPct;
            double cvd = data.DeltaCvd5m;

            const double oiThresholdBase = 0.5;
            double oiThreshold = oiThresholdBase + oiThresholdOffset;
            if (oiThreshold < 0.1) oiThreshold = 0.1;

            bool oiUp = double.IsFinite(oi) && oi > oiThreshold;
            bool oiDown = double.IsFinite(oi) && oi < -oiThreshold;

            bool cvdUp = double.IsFinite(cvd) && cvd > 0.0;
            bool cvdDown = double.IsFinite(cvd) && cvd < 0.0;

            bool longFlowOk;
            bool shortFlowOk;

            if (requireCvd)
            {
                // v2-style:
                // - trend: OI↑ + CVD w stronę wybicia,
                // - flush: OI↓ + CVD w stronę wybicia (kapitulacja)
                bool trendLongFlow = oiUp && cvdUp;
                bool trendShortFlow = oiUp && cvdDown;

                bool flushLongFlow = allowFlush && oiDown && cvdUp;
                bool flushShortFlow = allowFlush && oiDown && cvdDown;

                longFlowOk = trendLongFlow || flushLongFlow;
                shortFlowOk = trendShortFlow || flushShortFlow;
            }
            else
            {
                // Soft: CVD nie blokuje wejść – wymagamy tylko OI wspierającej breakout.
                longFlowOk = oiUp;
                shortFlowOk = oiDown;
            }

            if (!longFlowOk && !shortFlowOk)
            {
                // Flow nie wspiera wybicia w żadną stronę
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 5. Składanie triggerów long/short
            // -----------------------------------------------------------------

            bool longTrigger =
                structureUp &&
                longFlowOk;

            bool shortTrigger =
                structureDown &&
                shortFlowOk;

            // -----------------------------------------------------------------
            // 6. Sanity + debug
            // -----------------------------------------------------------------

            if (longTrigger && shortTrigger)
            {
                // Konflikt – nie otwieramy pozycji w żadną stronę dla tego tieru
                return ModeSignal.None;
            }

            if (!longTrigger && !shortTrigger)
            {
                // Brak sygnału dla tego tieru
                return ModeSignal.None;
            }

            data.BreakoutLongCandidate = longTrigger;
            data.BreakoutShortCandidate = shortTrigger;
            data.BreakoutEntryTier = (int)tier;

            return new ModeSignal(longTrigger, shortTrigger, tier);
        }

        public decimal? ComputeEntryPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side)
        {
            var refPrice = SessionHelpers.GetRefPrice(data);
            if (!refPrice.HasValue)
                return null;

            decimal entry;

            if (data.OpeningRangeHigh.HasValue && data.OpeningRangeLow.HasValue)
            {
                // klasyczny retest OR
                entry = side == OrderSide.Buy
                    ? data.OpeningRangeHigh.Value
                    : data.OpeningRangeLow.Value;
            }
            else
            {
                // fallback – ta sama geometria co momentum (pullback)
                decimal lo = data.Last5mLow ?? data.Last1mLow ?? refPrice.Value;
                decimal hi = data.Last5mHigh ?? data.Last1mHigh ?? refPrice.Value;

                if (side == OrderSide.Buy)
                {
                    var pullbackRange = refPrice.Value - lo;
                    if (pullbackRange <= 0m)
                        return null;

                    entry = refPrice.Value - 0.5m * pullbackRange;
                    if (entry >= refPrice.Value)
                        return null;
                }
                else
                {
                    var pullbackRange = hi - refPrice.Value;
                    if (pullbackRange <= 0m)
                        return null;

                    entry = refPrice.Value + 0.5m * pullbackRange;
                    if (entry <= refPrice.Value)
                        return null;
                }
            }

            entry = MathHelpers.RoundPrice(symbolInfo.PriceScale, entry);
            return entry > 0m ? entry : null;
        }

        public decimal? ComputeStopLossPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side, decimal entryPrice)
        {
            var refPrice = SessionHelpers.GetRefPrice(data);
            if (!refPrice.HasValue)
                return null;

            var riskUnit = SessionHelpers.ComputeRiskUnit(data, refPrice.Value, _options.MinAtr5mFloor);
            if (riskUnit <= 0m)
                return null;

            decimal sl;

            if (data.OpeningRangeHigh.HasValue && data.OpeningRangeLow.HasValue)
            {
                var orHigh = data.OpeningRangeHigh.Value;
                var orLow = data.OpeningRangeLow.Value;
                var orRange = orHigh - orLow;
                if (orRange <= 0m)
                    return null;

                var margin = Math.Max(riskUnit * 0.5m, orRange * 0.2m);

                if (side == OrderSide.Buy)
                    sl = orLow - margin;
                else
                    sl = orHigh + margin;
            }
            else
            {
                // fallback – jak momentum
                decimal lo = data.Last5mLow ?? data.Last1mLow ?? refPrice.Value;
                decimal hi = data.Last5mHigh ?? data.Last1mHigh ?? refPrice.Value;

                if (side == OrderSide.Buy)
                    sl = lo - riskUnit;
                else
                    sl = hi + riskUnit;
            }

            sl = MathHelpers.RoundPrice(symbolInfo.PriceScale, sl);

            if (sl <= 0m)
                return null;

            if (side == OrderSide.Buy && sl >= entryPrice)
                return null;
            if (side == OrderSide.Sell && sl <= entryPrice)
                return null;

            return sl;
        }

        public (decimal? Tp1, decimal? Tp2) ComputeTakeProfits(
            SigmaData data,
            SymbolInfo symbolInfo,
            OrderSide side,
            decimal entryPrice,
            decimal risk)
        {
            if (risk <= 0m)
                return (null, null);

            var targets = new List<(decimal Price, double RMultiple)>();

            // Opening range jako naturalny target wybicia
            if (data.OpeningRangeHigh.HasValue && data.OpeningRangeLow.HasValue)
            {
                var orHigh = data.OpeningRangeHigh.Value;
                var orLow = data.OpeningRangeLow.Value;

                if (side == OrderSide.Buy && orHigh > entryPrice)
                {
                    var r = (double)((orHigh - entryPrice) / risk);
                    targets.Add((orHigh, r));
                }
                else if (side == OrderSide.Sell && orLow < entryPrice)
                {
                    var r = (double)((entryPrice - orLow) / risk);
                    targets.Add((orLow, r));
                }
            }

            // Donchian jako extension target po wybiciu
            if (data.DonchianResult is { } don)
            {
                if (side == OrderSide.Buy && don.UpperBand > entryPrice)
                {
                    var r = (double)((don.UpperBand - entryPrice) / risk);
                    targets.Add((don.UpperBand.Value, r));
                }
                else if (side == OrderSide.Sell && don.LowerBand < entryPrice)
                {
                    var r = (double)((entryPrice - don.LowerBand) / risk);
                    targets.Add((don.LowerBand.Value, r));
                }
            }

            // bazowe 1R / 2R dla breakout
            if (side == OrderSide.Buy)
            {
                targets.Add((entryPrice + risk, 1.0));
                targets.Add((entryPrice + 2m * risk, 2.0));
            }
            else
            {
                targets.Add((entryPrice - risk, 1.0));
                targets.Add((entryPrice - 2m * risk, 2.0));
            }

            return SessionHelpers.ChooseTakeProfits(targets, side, entryPrice, symbolInfo.PriceScale);
        }
    }
}

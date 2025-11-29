using System;
using System.Collections.Generic;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// BreakoutMode v3 – lokalny breakout z kompresji, bez Opening Range.
    ///
    /// Założenia:
    /// - Reżim BO wybierany przez ModeEngine na podstawie ATR / spread.
    /// - Tutaj decydujemy tylko o TRIGGERZE wejścia (Hard / Medium / Soft)
    ///   oraz o ENTRY/SL/TP dla sygnałów breakoutowych.
    /// - Breakout jest zdefiniowany lokalnie:
    ///   * kompresja na 5m (inside/NR7),
    ///   * wybicie Donchianem (15m) w górę lub dół,
    ///   * wsparcie orderflow (ΔOI_1h, ΔCVD_5m).
    /// - ENTRY jest limitem na pullbacku względem ostatniej świecy 5m/1m,
    ///   SL oparty na lokalnym swing high/low ± ATR (ComputeRiskUnit),
    ///   TP dobierane z 1–2R + pasm Donchiana.
    /// </summary>
    public sealed class BreakoutMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public BreakoutMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ModeKind Kind => ModeKind.BO;

        /// <summary>
        /// Scoring reżimu BO – używany przez ModeEngine do wyboru trybu.
        /// </summary>
        public static double Score(SigmaData data, SigmaStrategyOptions options)
        {
            double bo = 0.0;
            double atr = data.AtrPct1h;

            bool atrOk = double.IsFinite(atr) && atr > 0.0 &&
                         atr <= (double)options.BoAtrMaxPct;

            if (!atrOk)
                return 0.0;

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
                double slopeMag = Math.Min(Math.Abs(zSlope) / 4.0, 1.0);
                bo += 25.0 * slopeMag;              // 0..25
            }

            // 4) ΔOI>0 – napływ kapitału na wybiciu (0..10)
            if (double.IsFinite(oi) && oi > 0.0)
            {
                double oiMag = Math.Min(oi / 10.0, 1.0); // ~10% OI -> full
                bo += 10.0 * oiMag;                     // 0..10
            }

            // 5) Dodatnia autokorelacja – kontynuacja po wybiciu (0..10)
            if (double.IsFinite(ac) && ac > 0.0)
            {
                double acClamped = Math.Min(ac, 1.0);
                bo += 10.0 * acClamped;             // 0..10
            }

            return Math.Min(Math.Max(bo, 0.0), 100.0);
        }

        public ModeSignal GenerateSignal(SigmaData data, bool enableTestSignal)
        {
            // Testowy sygnał bez wymagań
            if (enableTestSignal)
                return new ModeSignal(true, false, ModeTier.None);

            ArgumentNullException.ThrowIfNull(data);

            // Reset per-bar debug
            data.BreakoutEntryTier = 0;
            data.BreakoutLongCandidate = false;
            data.BreakoutShortCandidate = false;

            // Globalny gate ATR dla BO – bardzo wysokie ATR blokują tryb
            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h > (double)_options.BoAtrMaxPct)
            {
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 1. Hard – najbardziej selektywny tier:
            //    - BBW powyżej bazowego progu +5,
            //    - wymagamy BBW expanding,
            //    - wymagamy kompresji inside/NR7,
            //    - wymagamy OI + CVD w stronę wybicia (trend lub flush).
            // -----------------------------------------------------------------
            var hard = CalculateSignal(
                data,
                tier: ModeTier.Hard,
                bbwMinOffset: +5.0,
                requireBbwExpanding: true,
                oiThresholdOffset: +0.3,   // OI próg ~0.8
                requireCvd: true,
                allowFlush: true);

            if (hard.HasBuy || hard.HasSell)
                return hard;

            // -----------------------------------------------------------------
            // 2. Medium – bazowy tier BO:
            //    - BBW wg bazowego progu,
            //    - wymagamy BBW expanding,
            //    - wymagamy kompresji inside/NR7,
            //    - wymagamy OI + CVD w stronę wybicia (trend lub flush).
            // -----------------------------------------------------------------
            var medium = CalculateSignal(
                data,
                tier: ModeTier.Medium,
                bbwMinOffset: 0.0,
                requireBbwExpanding: true,
                oiThresholdOffset: 0.0,   // OI próg ~0.5
                requireCvd: true,
                allowFlush: true);

            if (medium.HasBuy || medium.HasSell)
                return medium;

            // -----------------------------------------------------------------
            // 3. Soft – najluźniejszy tier:
            //    - BBW lekko poniżej bazowego progu (ale nadal sensowne),
            //    - wymagamy BBW expanding,
            //    - kompresja optional (łapiemy też wybicia z luźniejszych range'y),
            //    - OI musi wspierać breakout, CVD tylko jako dodatkowy plus.
            // -----------------------------------------------------------------
            var soft = CalculateSignal(
                data,
                tier: ModeTier.Soft,
                bbwMinOffset: -5.0,
                requireBbwExpanding: true,
                oiThresholdOffset: -0.2,   // OI próg ~0.3
                requireCvd: false,
                allowFlush: true);

            if (soft.HasBuy || soft.HasSell)
                return soft;

            return ModeSignal.None;
        }

        private ModeSignal CalculateSignal(
            SigmaData data,
            ModeTier tier,
            double bbwMinOffset,
            bool requireBbwExpanding,
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

            bool bbwEnough = bbwFinite && bbwPct >= bbwMin;

            bool bbwExploding =
                !requireBbwExpanding ||
                (bbwFinite && data.Bbw15mExpanding);

            // Kompresja: ostatnia świeca 5m jako inside/NR7
            bool hasCompression = data.HasInsideOrNr7;

            // Dla Hard/Medium wymagamy kompresji, dla Soft traktujemy ją jako plus,
            // ale nie twardy warunek (tier soft ma łapać szersze range'y).
            bool compressionRequired = tier is ModeTier.Hard or ModeTier.Medium;

            bool preCompression =
                !compressionRequired ||
                hasCompression ||
                data.DonchianBreak; // fallback: breakout z lokalnego range'u nawet bez idealnej inside/NR7

            bool envOk = bbwEnough && bbwExploding && preCompression;

            if (!envOk)
            {
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 2. Struktura: Donchian breakout w górę / dół
            // -----------------------------------------------------------------
            bool donchianUp = data.DonchianBreakUp;
            bool donchianDown = data.DonchianBreakDown;

            // Hard/Medium: wymagamy jednocześnie kompresji 5m i wybicia Donchian
            // Soft: wystarczy sam Donchian breakout.
            bool structureUp =
                donchianUp &&
                (!compressionRequired || hasCompression);

            bool structureDown =
                donchianDown &&
                (!compressionRequired || hasCompression);

            if (!structureUp && !structureDown)
            {
                return ModeSignal.None;
            }

            // Dodatkowy sanity check: BB musi faktycznie się rozszerzać,
            // niezależnie od requireBbwExpanding (szczególnie istotne dla Soft).
            if (!data.Bbw15mExpanding)
                return ModeSignal.None;

            // -----------------------------------------------------------------
            // 3. Flow: ΔOI_1h, ΔCVD_5m
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
                // trend: OI↑ + CVD w stronę wybicia,
                // flush: OI↓ + CVD w stronę wybicia (kapitulacja)
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
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 4. Składanie triggerów long/short
            // -----------------------------------------------------------------
            bool longTrigger =
                structureUp &&
                longFlowOk;

            bool shortTrigger =
                structureDown &&
                shortFlowOk;

            // -----------------------------------------------------------------
            // 5. Sanity + debug
            // -----------------------------------------------------------------
            if (longTrigger && shortTrigger)
            {
                // Konflikt – nie otwieramy pozycji w żadną stronę dla tego tieru
                return ModeSignal.None;
            }

            if (!longTrigger && !shortTrigger)
            {
                return ModeSignal.None;
            }

            data.BreakoutLongCandidate = longTrigger;
            data.BreakoutShortCandidate = shortTrigger;
            data.BreakoutEntryTier = (int)tier;

            return new ModeSignal(longTrigger, shortTrigger, tier);
        }

        /// <summary>
        /// Entry price dla breakoutów: limit na pullbacku względem ostatniego
        /// ruchu 5m/1m, tak aby:
        /// - dla long wejść poniżej bieżącej ceny (po częściowym cofnięciu),
        /// - dla short wejść powyżej bieżącej ceny.
        /// </summary>
        public decimal? ComputeEntryPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side)
        {
            var refPrice = SessionHelpers.GetRefPrice(data);
            if (!refPrice.HasValue || refPrice.Value <= 0m)
                return null;

            decimal basis = refPrice.Value;

            decimal lo = data.Last5mLow
                         ?? data.Last1mLow
                         ?? basis;

            decimal hi = data.Last5mHigh
                         ?? data.Last1mHigh
                         ?? basis;

            decimal entry;

            if (side == OrderSide.Buy)
            {
                // Pullback z góry w dół: 50% cofnięcia od basis do lokalnego low.
                var pullbackRange = basis - lo;
                if (pullbackRange <= 0m)
                    return null;

                entry = basis - 0.5m * pullbackRange;
                if (entry >= basis)
                    return null;
            }
            else
            {
                // Pullback z dołu w górę: 50% cofnięcia od basis do lokalnego high.
                var pullbackRange = hi - basis;
                if (pullbackRange <= 0m)
                    return null;

                entry = basis + 0.5m * pullbackRange;
                if (entry <= basis)
                    return null;
            }

            entry = MathHelpers.RoundPrice(symbolInfo.PriceScale, entry);
            return entry > 0m ? entry : (decimal?)null;
        }

        public decimal? ComputeStopLossPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side, decimal entryPrice)
        {
            var refPrice = SessionHelpers.GetRefPrice(data);
            if (!refPrice.HasValue || refPrice.Value <= 0m)
                return null;

            var riskUnit = SessionHelpers.ComputeRiskUnit(
                data,
                refPrice.Value,
                _options.RiskFloorPct,
                _options.RiskCapPct,
                _options.RiskFallbackPct);

            if (riskUnit <= 0m)
                return null;

            decimal basis = refPrice.Value;

            decimal lo = data.Last5mLow
                         ?? data.Last1mLow
                         ?? basis;

            decimal hi = data.Last5mHigh
                         ?? data.Last1mHigh
                         ?? basis;

            // Domyślnie 1×ATR5m za lokalnym zakresem.
            // Dla twardszych tierów (Medium/Hard) zaciskamy bufor ATR.
            var tier = (ModeTier)data.BreakoutEntryTier;

            decimal atrMultiplier = 1.0m;
            if (tier == ModeTier.Hard)
                atrMultiplier = 0.5m;
            else if (tier == ModeTier.Medium)
                atrMultiplier = 0.75m;

            decimal sl;

            if (side == OrderSide.Buy)
            {
                sl = lo - atrMultiplier * riskUnit;
            }
            else if (side == OrderSide.Sell)
            {
                sl = hi + atrMultiplier * riskUnit;
            }
            else
            {
                return null;
            }

            sl = MathHelpers.RoundPrice(symbolInfo.PriceScale, sl);

            if (sl <= 0m)
                return null;

            // sanity: SL musi leżeć po właściwej stronie względem entry
            if (side == OrderSide.Buy && sl >= entryPrice)
                return null;
            if (side == OrderSide.Sell && sl <= entryPrice)
                return null;

            return sl;
        }


        /// <summary>
        /// Take-profit dla breakoutów:
        /// - kandydaci: 1R, 2R, oraz pasmo Donchiana po stronie wybicia,
        /// - finalny wybór: SessionHelpers.ChooseTakeProfits (filtr R i spacing).
        /// </summary>
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

            // Donchian jako naturalny extension target po wybiciu
            if (data.DonchianResult is { } don)
            {
                if (side == OrderSide.Buy && don.UpperBand.HasValue && don.UpperBand.Value > entryPrice)
                {
                    var r = (double)((don.UpperBand.Value - entryPrice) / risk);
                    targets.Add((don.UpperBand.Value, r));
                }
                else if (side == OrderSide.Sell && don.LowerBand.HasValue && don.LowerBand.Value < entryPrice)
                {
                    var r = (double)((entryPrice - don.LowerBand.Value) / risk);
                    targets.Add((don.LowerBand.Value, r));
                }
            }

            // Bazowe 1R / 2R dla breakout
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

            var (tp1, tp2) = SessionHelpers.ChooseTakeProfits(targets, side, entryPrice, symbolInfo.PriceScale);

            // TP1 – musi być >0 i po właściwej stronie względem entry
            if (tp1.HasValue)
            {
                var p = MathHelpers.RoundPrice(symbolInfo.PriceScale, tp1.Value);
                bool invalid =
                    p <= 0m ||
                    (side == OrderSide.Buy && p <= entryPrice) ||
                    (side == OrderSide.Sell && p >= entryPrice);

                tp1 = invalid ? null : p;
            }

            // TP2 – to samo
            if (tp2.HasValue)
            {
                var p = MathHelpers.RoundPrice(symbolInfo.PriceScale, tp2.Value);
                bool invalid =
                    p <= 0m ||
                    (side == OrderSide.Buy && p <= entryPrice) ||
                    (side == OrderSide.Sell && p >= entryPrice);

                tp2 = invalid ? null : p;
            }

            return (tp1, tp2);
        }
    }
}

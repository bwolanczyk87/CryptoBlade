using System;
using System.Collections.Generic;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// MomentumMode v3 – trigger-only z tierami:
    /// - reżim Momentum (MM) wybierany jest wcześniej przez ModeEngine
    ///   na podstawie ATR / spread / globalnych gate'ów,
    /// - tutaj decydujemy tylko o TRIGGERZE wejścia, w trzech tierach jakości:
    ///   * Soft   – luźniejsze progi trendu/pullbacku, bez wymogu sweepe'a ani CVD flipu,
    ///   * Medium – bazowe progi, wymagany flip CVD, bez twardego wymogu sweepe'a,
    ///   * Hard   – ostrzejsze progi, wymagany sweep + overshoot oraz CVD flip.
    /// - cała metodologia jest jedna (CalculateSignal), tiery jedynie modulują progi
    ///   i użyte komponenty patternu.
    /// </summary>
    public sealed class MomentumMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public MomentumMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ModeKind Kind => ModeKind.MM;

        /// <summary>
        /// Scoring reżimu Momentum – używany przez ModeEngine.
        /// </summary>
        public static double Score(SigmaData data, SigmaStrategyOptions options)
        {
            double mm = 0.0;
            double atr = data.AtrPct1h;

            bool mmAtrOk = double.IsFinite(atr) && atr > 0.0 &&
                           atr >= (double)options.MmAtrMinPct &&
                           atr <= (double)options.MmAtrMaxPct;

            if (!mmAtrOk)
                return 0.0;

            double adx = data.Adx1h;
            double zSlope = data.ZSlopeDvwap;
            double zDev = data.ZDvwap;
            double ac = data.AutoCorr5m;
            double oi = data.OiDelta1hPct;

            // 1) ADX – im bliżej AdxEnableMomentum, tym wyższy score (0..40)
            double adxNorm = MathHelpers.Normalize01(
                adx,
                (double)options.AdxDisableMomentum,
                (double)options.AdxEnableMomentum);

            adxNorm = MathHelpers.Clamp01(adxNorm);
            mm += 40.0 * adxNorm;                  // 0..40

            // 2) Absolutne nachylenie DVWAP – "siła trendu" w obie strony (0..25)
            if (double.IsFinite(zSlope))
            {
                // 4 sigma nachylenia → pełna premia
                double slopeMag = Math.Min(Math.Abs(zSlope) / 4.0, 1.0);
                mm += 25.0 * slopeMag;             // 0..25
            }

            // 3) Umiarkowane odchylenie od DVWAP (nie za blisko, nie ekstremalnie daleko) (0..15)
            if (double.IsFinite(zDev))
            {
                double absDev = Math.Abs(zDev);

                // pełne 1.0 w okolicach 1.0–2.0 sigma, 0 przy 0 i >=4
                double devScore = 0.0;
                if (absDev > 0.2 && absDev < 4.0)
                {
                    if (absDev <= 2.0)
                        devScore = (absDev - 0.2) / (2.0 - 0.2);   // rośnie 0→1
                    else
                        devScore = (4.0 - absDev) / (4.0 - 2.0);   // spada 1→0
                }

                devScore = MathHelpers.Clamp01(devScore);
                mm += 15.0 * devScore;              // 0..15
            }

            // 4) ΔOI wyrównany z kierunkiem trendu (0..10)
            double oiAligned = 0.0;
            if (double.IsFinite(oi) && double.IsFinite(zSlope) && Math.Abs(zSlope) > 0.1)
            {
                int trendSign = Math.Sign(zSlope);
                int oiSign = Math.Sign(oi);

                if (oiSign == trendSign)
                {
                    // saturacja przy ~10% zmiany OI
                    double oiMag = Math.Min(Math.Abs(oi) / 10.0, 1.0);
                    oiAligned = oiMag;
                }
            }
            mm += 10.0 * oiAligned;                 // 0..10

            // 5) Dodatnia autokorelacja – kontynuacja (0..10)
            if (double.IsFinite(ac) && ac > 0.0)
            {
                double acClamped = Math.Min(ac, 1.0);
                mm += 10.0 * acClamped;             // 0..10
            }

            return Math.Min(Math.Max(mm, 0.0), 100.0);
        }

        public IReadOnlyDictionary<ModeTier, ModeSignal> GenerateSignals(
            SigmaData data,
            ModeTier? requestedTier = null)
        {
            ArgumentNullException.ThrowIfNull(data);

            // Domyślna mapa – na starcie wszystko None.
            var result = new Dictionary<ModeTier, ModeSignal>
            {
                [ModeTier.Hard] = ModeSignal.None,
                [ModeTier.Medium] = ModeSignal.None,
                [ModeTier.Soft] = ModeSignal.None
            };

            // Testowy sygnał – poza normalną logiką Momentum.
            if (requestedTier == ModeTier.Test)
            {
                result[ModeTier.Test] = new ModeSignal(true, false, ModeTier.Test);
                return result;
            }

            // Globalny gate ATR dla Momentum
            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h < (double)_options.MmAtrMinPct ||
                data.AtrPct1h > (double)_options.MmAtrMaxPct)
            {
                return result;
            }

            // Jeśli jawnie podano tier (Hard/Medium/Soft) → licz tylko ten jeden.
            if (requestedTier is ModeTier.Hard or ModeTier.Medium or ModeTier.Soft)
            {
                result[requestedTier.Value] = GenerateForTier(data, requestedTier.Value);
                return result;
            }

            // requestedTier == null / None → licz wszystkie 3 tiery.
            result[ModeTier.Hard] = GenerateForTier(data, ModeTier.Hard);
            result[ModeTier.Medium] = GenerateForTier(data, ModeTier.Medium);
            result[ModeTier.Soft] = GenerateForTier(data, ModeTier.Soft);

            return result;
        }

        private ModeSignal GenerateForTier(SigmaData data, ModeTier tier)
        {
            return tier switch
            {
                // 1. Hard – najbardziej selektywny:
                //    - ostrzejsze wymagania na trend/pullback,
                //    - sweep + overshoot + CVD flip obowiązkowe.
                ModeTier.Hard => CalculateSignal(
                    data,
                    tier: ModeTier.Hard,
                    slopeThresholdOffset: +0.15,   // DVWAP mocniej nachylony
                    adxMinOffset: +2.0,            // ADX wyższy niż bazowy próg momentum
                    zDevMinMagOffset: +0.2,        // głębszy pullback
                    overshootOffset: +0.5,         // większy overshoot przy sweepie
                    useSweep: true,
                    useCvdFlip: true),

                // 2. Medium – bazowy tier Momentum:
                //    - progi trendu/pullbacku jak w v2,
                //    - wymagamy CVD flipu, ale nie wymuszamy sweepe'a.
                ModeTier.Medium => CalculateSignal(
                    data,
                    tier: ModeTier.Medium,
                    slopeThresholdOffset: 0.0,
                    adxMinOffset: 0.0,
                    zDevMinMagOffset: 0.0,
                    overshootOffset: null,   // brak wymogu overshootu
                    useSweep: false,         // nie wymagamy sweepa
                    useCvdFlip: true),       // ale wymagamy flipu takerów

                // 3. Soft – najluźniejszy tier:
                //    - lekko obniżone progi trendu/pullbacku,
                //    - brak wymogu sweepe'a i CVD flipu – proto-momentum.
                ModeTier.Soft => CalculateSignal(
                    data,
                    tier: ModeTier.Soft,
                    slopeThresholdOffset: -0.10,
                    adxMinOffset: -3.0,
                    zDevMinMagOffset: -0.10,
                    overshootOffset: null,
                    useSweep: false,
                    useCvdFlip: false),

                _ => ModeSignal.None
            };
        }


        private ModeSignal CalculateSignal(
            SigmaData data,
            ModeTier tier,
            double slopeThresholdOffset,
            double adxMinOffset,
            double zDevMinMagOffset,
            double? overshootOffset,
            bool useSweep,
            bool useCvdFlip)
        {
            // --- Dane wejściowe ---
            double adx = data.Adx1h;
            double zSlope = data.ZSlopeDvwap;
            double zDev = data.ZDvwap;
            double ac = data.AutoCorr5m;
            double oi = data.OiDelta1hPct;

            // -----------------------------------------------------------------
            // 1) Trend: nachylenie DVWAP + ADX
            // -----------------------------------------------------------------
            const double baseSlopeThreshold = 0.5; // bazowo: "wyraźny" trend D-VWAP
            double slopeThreshold = baseSlopeThreshold + slopeThresholdOffset;
            if (slopeThreshold < 0.2) slopeThreshold = 0.2;
            if (slopeThreshold > 1.0) slopeThreshold = 1.0;

            bool upTrend = zSlope > slopeThreshold;
            bool downTrend = zSlope < -slopeThreshold;

            double adxEnableBase = (double)_options.AdxEnableMomentum; // np. 22
            double adxMin = adxEnableBase + adxMinOffset;
            if (adxMin < 10.0) adxMin = 10.0;

            bool trendLongOk = !double.IsFinite(adx) || adx >= adxMin;
            bool trendShortOk = trendLongOk;

            // -----------------------------------------------------------------
            // 2) Pullback do DVWAP – jak daleko od "value" chcemy wejść
            // -----------------------------------------------------------------
            const double baseZDevMinMag = 0.5; // bazowo: 0.5 sigma od DVWAP
            double zDevMinMag = baseZDevMinMag + zDevMinMagOffset;
            if (zDevMinMag < 0.2) zDevMinMag = 0.2;
            if (zDevMinMag > 2.0) zDevMinMag = 2.0;

            bool pullbackLong = zDev < -zDevMinMag && zDev > -3.5;
            bool pullbackShort = zDev > zDevMinMag && zDev < 3.5;

            // -----------------------------------------------------------------
            // 3) Flow wspierający kontynuację (AC + OI)
            // -----------------------------------------------------------------
            const double acMin = -0.10;
            bool acSupportsTrend = !double.IsFinite(ac) || ac >= acMin;

            bool oiSupportsUp = oi > 0.0;
            bool oiSupportsDown = oi < 0.0;

            bool protoLong =
                upTrend &&
                trendLongOk &&
                pullbackLong &&
                acSupportsTrend &&
                oiSupportsUp;

            bool protoShort =
                downTrend &&
                trendShortOk &&
                pullbackShort &&
                acSupportsTrend &&
                oiSupportsDown;

            if (!protoLong && !protoShort)
                return ModeSignal.None;

            // -----------------------------------------------------------------
            // 4) Pattern: sweep -> reclaim + CVD flip (opcjonalnie)
            // -----------------------------------------------------------------
            bool patternLongOk = true;
            bool patternShortOk = true;

            if (useSweep)
            {
                bool sweepLong = data.SweepReclaimDown5m;
                bool sweepShort = data.SweepReclaimUp5m;

                if (overshootOffset is double o)
                {
                    const double baseOvershootBps = 1.0;
                    double overshootMin = baseOvershootBps + o;
                    if (overshootMin < 0.5) overshootMin = 0.5;
                    if (overshootMin > 5.0) overshootMin = 5.0;

                    sweepLong &= data.SweepDownOvershootBps5m >= overshootMin;
                    sweepShort &= data.SweepUpOvershootBps5m >= overshootMin;
                }

                patternLongOk &= sweepLong;
                patternShortOk &= sweepShort;
            }

            if (useCvdFlip)
            {
                bool cvdFlipLong = data.CvdFlipUp5m;
                bool cvdFlipShort = data.CvdFlipDown5m;

                patternLongOk &= cvdFlipLong;
                patternShortOk &= cvdFlipShort;
            }

            bool buy =
                protoLong &&
                patternLongOk;

            bool sell =
                protoShort &&
                patternShortOk;

            // -----------------------------------------------------------------
            // 5) Sanity + debug
            // -----------------------------------------------------------------
            if (buy && sell)
            {
                // konflikt – nie otwieramy w żadną stronę dla tego tieru
                return ModeSignal.None;
            }

            if (!buy && !sell)
            {
                // brak sygnału dla tego tieru
                return ModeSignal.None;
            }
            return new ModeSignal(buy, sell, tier);
        }

        /// <summary>
        /// ENTRY: 50% pullback 1/5m w kierunku trendu.
        /// - long: wejście poniżej refPrice (po częściowej korekcie w dół),
        /// - short: wejście powyżej refPrice (po częściowej korekcie w górę).
        /// </summary>
        public decimal? ComputeEntryPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side)
        {
            var refPrice = SessionHelpers.GetRefPrice(data);
            if (!refPrice.HasValue)
                return null;

            decimal lo = data.Last5mLow ?? data.Last1mLow ?? refPrice.Value;
            decimal hi = data.Last5mHigh ?? data.Last1mHigh ?? refPrice.Value;

            decimal entry;

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

            entry = MathHelpers.RoundPrice(symbolInfo.PriceScale, entry);
            return entry > 0m ? entry : null;
        }

        /// <summary>
        /// SL: wyjście za lokalny swing (5m/1m) plus riskUnit z ATR5m.
        /// </summary>
        public decimal? ComputeStopLossPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side, decimal entryPrice, ModeTier tier)
        {
            var refPrice = SessionHelpers.GetRefPrice(data);
            if (!refPrice.HasValue)
                return null;

            var riskUnit = SessionHelpers.ComputeRiskUnit(
                data,
                refPrice.Value,
                _options.RiskFloorPct,
                _options.RiskCapPct,
                _options.RiskFallbackPct);
            if (riskUnit <= 0m)
                return null;

            decimal lo = data.Last5mLow ?? data.Last1mLow ?? refPrice.Value;
            decimal hi = data.Last5mHigh ?? data.Last1mHigh ?? refPrice.Value;
            decimal atrMultiplier = 1.0m;

            if (tier == ModeTier.Hard)
            {
                bool hasSweepLong = data.SweepReclaimDown5m;
                bool hasSweepShort = data.SweepReclaimUp5m;

                if ((side == OrderSide.Buy && hasSweepLong) ||
                    (side == OrderSide.Sell && hasSweepShort))
                {
                    atrMultiplier = 0.4m;
                }
            }

            decimal sl;

            if (side == OrderSide.Buy)
            {
                sl = lo - atrMultiplier * riskUnit;
                if (sl >= entryPrice)
                    return null;
            }
            else if (side == OrderSide.Sell)
            {
                sl = hi + atrMultiplier * riskUnit;
                if (sl <= entryPrice)
                    return null;
            }
            else
            {
                return null;
            }

            sl = MathHelpers.RoundPrice(symbolInfo.PriceScale, sl);
            return sl > 0m ? sl : (decimal?)null;
        }

        public (decimal? Tp1, decimal? Tp2) ComputeTakeProfits(
            SigmaData data,
            SymbolInfo symbolInfo,
            OrderSide side,
            decimal entryPrice,
            decimal risk)
        {
            if (risk <= 0m || entryPrice <= 0m)
                return (null, null);

            var minMoveFraction = SessionHelpers.ComputeMinMoveFraction(_options);
            var targets = new List<(decimal Price, double RMultiple)>();

            void AddTarget(decimal price)
            {
                if (price <= 0m)
                    return;

                decimal move = side == OrderSide.Buy ? price - entryPrice : entryPrice - price;
                if (move <= 0m)
                    return;

                if (minMoveFraction > 0m)
                {
                    var movePct = move / entryPrice;
                    if (movePct < minMoveFraction)
                        return; // za blisko, fee zjadłyby zbyt duży % PnL
                }

                var r = (double)(move / risk);
                if (!double.IsFinite(r) || r <= 0.0)
                    return;

                targets.Add((price, r));
            }

            // Bazowe 1R / 2R
            if (side == OrderSide.Buy)
            {
                AddTarget(entryPrice + risk);        // 1R
                AddTarget(entryPrice + 2m * risk);   // 2R
            }
            else if (side == OrderSide.Sell)
            {
                AddTarget(entryPrice - risk);        // 1R
                AddTarget(entryPrice - 2m * risk);   // 2R
            }

            // Donchian jako naturalny extension target po stronie trendu
            if (data.DonchianResult is { } don)
            {
                if (side == OrderSide.Buy && don.UpperBand.HasValue && don.UpperBand.Value > entryPrice)
                {
                    AddTarget(don.UpperBand.Value);
                }
                else if (side == OrderSide.Sell && don.LowerBand.HasValue && don.LowerBand.Value < entryPrice)
                {
                    AddTarget(don.LowerBand.Value);
                }
            }

            var (tp1, tp2) = SessionHelpers.ChooseTakeProfits(
                targets,
                side,
                entryPrice,
                symbolInfo.PriceScale);

            // sanity + zaokrąglenie
            if (tp1.HasValue)
            {
                var p = MathHelpers.RoundPrice(symbolInfo.PriceScale, tp1.Value);
                bool invalid =
                    p <= 0m ||
                    (side == OrderSide.Buy && p <= entryPrice) ||
                    (side == OrderSide.Sell && p >= entryPrice);

                tp1 = invalid ? null : p;
            }

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

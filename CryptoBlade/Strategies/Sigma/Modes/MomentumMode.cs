using Bybit.Net.Enums;
using CryptoBlade.Strategies.Sigma.Helpers;
using System;
using System.Threading;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// MomentumMode v3 – trigger-only z tierami:
    /// - reżim Momentum (MM) wybierany jest wcześniej przez ModeEngine na podstawie ATR / spread / globalnych gate'ów,
    /// - tutaj decydujemy tylko o TRIGGERZE wejścia, w trzech tierach jakości:
    ///   * Soft   – luźniejsze progi trendu/pullbacku, bez wymogu sweepe'a ani CVD flipu,
    ///   * Medium – bazowe progi, wymagany flip CVD, bez twardego wymogu sweepe'a,
    ///   * Hard   – ostrzejsze progi, wymagany sweep + overshoot oraz CVD flip.
    /// - cała metodologia jest jedna (CalculateSignal), tiery jedynie modulują progi i użyte komponenty patternu.
    /// </summary>
    public sealed class MomentumMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public MomentumMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ModeKind Kind => ModeKind.MM;

        public static double Score(SigmaData data, SigmaStrategyOptions options)
        {
            double mm = 0.0;
            double atr = data.AtrPct1h;
            bool mmAtrOk = double.IsFinite(atr) && atr > 0.0 &&
                           atr >= (double)options.MmAtrMinPct &&
                           atr <= (double)options.MmAtrMaxPct;

            if (!mmAtrOk)
                return mm;

            double adx = data.Adx1h;
            double zSlope = data.ZSlopeDvwap;
            double zDev = data.ZDvwap;
            double ac = data.AutoCorr5m;
            double oi = data.OiDelta1hPct;

            // 1) ADX – im bliżej AdxEnableMomentum, tym wyższy score (0..40)
            double adxNorm = StatisticsHelpers.Normalize01(
                adx,
                (double)options.AdxDisableMomentum,
                (double)options.AdxEnableMomentum);

            adxNorm = StatisticsHelpers.Clamp01(adxNorm);
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

                devScore = StatisticsHelpers.Clamp01(devScore);
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

        public ModeSignal GenerateSignal(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(data);

            // Reset per-bar debug
            data.MomentumEntryTier = 0;
            data.MomentumLongCandidate = false;
            data.MomentumShortCandidate = false;

            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h < (double)_options.MmAtrMinPct ||
                data.AtrPct1h > (double)_options.MmAtrMaxPct)
            {
                // Zmienność poza "zdrowym" zakresem dla Momentum.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 1. Hard – najbardziej selektywny tier
            // -----------------------------------------------------------------
            // - ostrzejsze wymagania na trend/pullback,
            // - sweep + overshoot + CVD flip obowiązkowe.
            var hard = CalculateSignal(
                data,
                tier: ModeTier.Hard,
                slopeThresholdOffset: +0.15,   // DVWAP musi być wyraźniej nachylony
                adxMinOffset: +2.0,     // ADX wyższy niż bazowy próg momentum
                zDevMinMagOffset: +0.2,     // głębszy pullback od DVWAP
                overshootOffset: +0.5,     // większy overshoot przy sweepie
                useSweep: true,
                useCvdFlip: true);

            if (hard.HasBuy || hard.HasSell)
                return hard;

            // -----------------------------------------------------------------
            // 2. Medium – bazowy tier Momentum
            // -----------------------------------------------------------------
            // - progi trendu/pullbacku jak w v2,
            // - wymagamy CVD flipu, ale nie wymuszamy sweepe'a.
            var medium = CalculateSignal(
                data,
                tier: ModeTier.Medium,
                slopeThresholdOffset: 0.0,
                adxMinOffset: 0.0,
                zDevMinMagOffset: 0.0,
                overshootOffset: null,   // brak wymogu overshootu
                useSweep: false,  // nie wymagamy sweepe'a
                useCvdFlip: true);  // ale wymagamy flipu takerów

            if (medium.HasBuy || medium.HasSell)
                return medium;

            // -----------------------------------------------------------------
            // 3. Soft – najluźniejszy tier
            // -----------------------------------------------------------------
            // - lekko obniżone progi trendu/pullbacku,
            // - brak wymogu sweepe'a i CVD flipu – czyste proto-momentum.
            var soft = CalculateSignal(
                data,
                tier: ModeTier.Soft,
                slopeThresholdOffset: -0.10,
                adxMinOffset: -3.0,
                zDevMinMagOffset: -0.10,
                overshootOffset: null,
                useSweep: false,
                useCvdFlip: false);

            if (soft.HasBuy || soft.HasSell)
                return soft;

            // -----------------------------------------------------------------
            //4.None – testoswy sygnał bez żadnych wymagań(do testów i debugu)
            var none = new ModeSignal(true, false, ModeTier.None);
            return none;

            // Brak sygnału w którymkolwiek tierze
            return ModeSignal.None;
        }

        /// <summary>
        /// Generyczne liczenie sygnału Momentum dla danego tieru.
        /// Jeden algorytm, różne progi:
        /// - slopeThresholdOffset  – korekta bazowego progu nachylenia D-VWAP,
        /// - adxMinOffset          – korekta minimalnego ADX dla trendu,
        /// - zDevMinMagOffset      – korekta minimalnego |zDVWAP| dla pullbacku,
        /// - overshootOffset       – korekta minimalnego overshootu przy sweepie (jeśli useSweep == true),
        /// - useSweep              – czy wymagamy sweepe'a (Reclaim + ewentualny overshoot),
        /// - useCvdFlip            – czy wymagamy flipu CVD w stronę setupu.
        /// </summary>
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

            // jeśli wyłączyliśmy oba (useSweep == false && useCvdFlip == false),
            // patternLongOk / patternShortOk pozostają true i nie filtrują proto-momentum.

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

            data.MomentumLongCandidate = buy;
            data.MomentumShortCandidate = sell;
            data.MomentumEntryTier = (int)tier;

            return new ModeSignal(buy, sell, tier);
        }

        public List<(decimal Price, double RMultiple)> AddTakeProfitTargets(
            SigmaData data,
            OrderSide side,
            decimal entryPrice,
            decimal risk)
        {
            var targets = new List<(decimal Price, double RMultiple)>();

            if (risk <= 0m)
                return targets;

            var don = data.DonchianResult;
            if (don is null)
                return targets;

            decimal donHigh = don.UpperBand ?? 0m;
            decimal donLow = don.LowerBand ?? 0m;

            if (side == OrderSide.Buy && donHigh > entryPrice)
            {
                var rDon = (double)((donHigh - entryPrice) / risk);
                targets.Add((donHigh, rDon));
            }
            else if (side == OrderSide.Sell && donLow < entryPrice)
            {
                var rDon = (double)((entryPrice - donLow) / risk);
                targets.Add((donLow, rDon));
            }

            return targets;
        }
    }
}

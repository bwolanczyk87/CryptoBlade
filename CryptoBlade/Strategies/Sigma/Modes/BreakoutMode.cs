using System;
using System.Threading;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// BreakoutMode v3 – KO-mode na wybicia z kompresji:
    ///
    /// Założenia:
    /// - globalne gate'y (spread, ATR dla BO) są wspólne dla wszystkich tierów,
    /// - tiery różnicują:
    ///   * jak "mocna" musi być ekspansja zmienności (BBW),
    ///   * czy dopuszczamy Donchian jako fallback, czy wymagamy stricte OR+retest,
    ///   * czy wymagamy silnego retestu (głębokości) dla A+,
    ///   * jak duża musi być zmiana OI,
    ///   * czy wymagamy ΔCVD w stronę wybicia.
    ///
    /// Hard  – A+ wybicia: OR+retest, mocne BBW, silny flow (OI + CVD), retest z sensowną głębokością.
    /// Medium – bazowe BO: środowisko breakoutowe, OR/Donchian, OI + CVD w stronę wybicia.
    /// Soft   – luźniejsze BO: to samo środowisko, OR/Donchian, OI w stronę wybicia, CVD opcjonalne.
    /// </summary>
    public sealed class BreakoutMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public BreakoutMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Mode Kind => Mode.BO;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
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

        /// <summary>
        /// Generyczne liczenie sygnału Breakout dla danego tieru.
        ///
        /// Parametry:
        /// - bbwMinOffset:
        ///     bazowy próg BBW to options.BbWidthBreakoutPct (percentyl 0..100),
        ///     rzeczywisty próg środowiska BO: base + bbwMinOffset.
        ///
        /// - requireBbwExpanding:
        ///     jeśli true, wymagamy aby Bbw15mExpanding == true.
        ///
        /// - allowDonchianFallback:
        ///     jeśli true, dopuszczamy DonchianBreak jako fallback, gdy OR+retest nie jest aktywny
        ///     po danej stronie.
        ///
        /// - strongRetestDepthMinOffset / strongRetestDepthMaxOffset:
        ///     jeśli któryś != null, wymagamy "silnego retestu" dla OR breakout:
        ///       depth ∈ [baseMin + minOffset, baseMax + maxOffset],
        ///     gdzie baseMin = 3 bps, baseMax = 50 bps.
        ///
        /// - oiThresholdOffset:
        ///     bazowy próg dla |ΔOI_1h| to 0.5,
        ///     rzeczywisty próg = base + oiThresholdOffset.
        ///
        /// - requireCvd:
        ///     jeśli true, wymagamy aby ΔCVD wspierał breakout (tak jak w v2: trend/flush).
        ///     jeśli false, ΔCVD jest ignorowane przy filtracji – ważny jest tylko OI.
        ///
        /// - allowFlush:
        ///     jeśli true, dopuszczamy zarówno "trend flow" (OI↑ + CVD w stronę wybicia),
        ///     jak i "flush" (OI↓ + CVD w stronę wybicia).
        /// </summary>
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
    }
}

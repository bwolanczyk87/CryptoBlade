using System;
using System.Threading;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// BreakoutMode v2 – KO-mode:
    ///
    /// Wejścia tylko gdy:
    /// - globalne gate'y przeszły (spread, ATR dla BO),
    /// - środowisko breakoutowe:
    ///     * BBWidth_15m >= BbWidthBreakoutPct,
    ///     * Bbw15mExpanding == true,
    ///     * (HasInsideOrNr7 || DonchianBreak),
    /// - faktyczne wybicie + retest:
    ///     * OR breakout + retest (preferowane),
    ///     * lub Donchian breakout (fallback gdy OR już dawno za nami),
    /// - potwierdzenie flow:
    ///     * ΔOI_1h w kierunku wybicia,
    ///     * ΔCVD_5m zgodne z kierunkiem,
    /// - brak konfliktu z BTC biasem.
    ///
    /// buy/sell – standardowy breakout,
    /// buyExtra/sellExtra – silne setupy (głębszy retest + wyższa BBWidth).
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
            if (data is null)
                throw new ArgumentNullException(nameof(data));

            // -----------------------------------------------------------------
            // 1. Globalne gate'y dla BO: spread + ATR (tylko górny próg)
            // -----------------------------------------------------------------

            if (double.IsFinite(data.SpreadBps) &&
                data.SpreadBps > (double)_options.MaxSpreadBps)
            {
                // Spread za szeroki – brak wejść BO.
                return ModeSignal.None;
            }

            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h > (double)_options.BoAtrMaxPct)
            {
                // Zbyt duża zmienność dzienna – BO z retestem jest niebezpieczne.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 2. Środowisko breakoutowe: BBWidth + inside/NR7/Donchian
            // -----------------------------------------------------------------

            double bbwPct = data.Bbw15mPct;
            bool bbwFinite = double.IsFinite(bbwPct);

            bool bbwEnough =
                bbwFinite &&
                bbwPct >= (double)_options.BbWidthBreakoutPct;     // np. 30-percentyl

            bool bbwExploding =
                bbwFinite &&
                data.Bbw15mExpanding;

            bool volSupportsBreakout = bbwEnough && bbwExploding;

            bool preCompression =
                data.HasInsideOrNr7 || data.DonchianBreak;

            if (!volSupportsBreakout || !preCompression)
            {
                // Nie mamy środowiska: brak sensu szukać BO.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 3. Struktura: OR breakout + retest / Donchian breakout
            // -----------------------------------------------------------------

            bool orRetestUp = data.OrBreakoutRetestUp5m;
            bool orRetestDown = data.OrBreakoutRetestDown5m;

            bool donchianUp = data.DonchianBreakUp;
            bool donchianDown = data.DonchianBreakDown;

            // Preferujemy OR breakout + retest. Donchian treatujemy jako fallback.
            bool structureUp = orRetestUp || (!orRetestDown && donchianUp);
            bool structureDown = orRetestDown || (!orRetestUp && donchianDown);

            if (!structureUp && !structureDown)
            {
                // Ani OR breakout+retest, ani Donchian – brak triggera.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 4. Flow: ΔOI_1h, ΔCVD_5m
            // -----------------------------------------------------------------

            double oi = data.OiDelta1hPct;
            double cvd = data.DeltaCvd5m;

            bool oiUp = double.IsFinite(oi) && oi > 0.5;
            bool oiDown = double.IsFinite(oi) && oi < -0.5;

            bool cvdUp = double.IsFinite(cvd) && cvd > 0.0;
            bool cvdDown = double.IsFinite(cvd) && cvd < 0.0;

            // -----------------------------------------------------------------
            // 5. BTC bias gating
            // -----------------------------------------------------------------

            if (data.BtcBiasOpposite)
            {
                // Jeśli nasz setup idzie przeciwnie do silnego BTC biasu
                // przy wysokiej korelacji – w ogóle nie dotykamy BO.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 6. Składanie triggerów long/short
            // -----------------------------------------------------------------

            bool longTrigger =
                structureUp &&
                oiUp &&
                cvdUp &&
                volSupportsBreakout;

            bool shortTrigger =
                structureDown &&
                oiDown &&
                cvdDown &&
                volSupportsBreakout;

            // "Extra" – silniejsze setupy:
            // - retest był głębszy (ale nie za głęboki),
            // - BBWidth jeszcze wyższe (>= BbWidthExitBreakoutPct).
            double depthUp = data.OrRetestDepthBpsUp5m;
            double depthDown = data.OrRetestDepthBpsDown5m;

            bool strongVol =
                bbwFinite &&
                bbwPct >= (double)_options.BbWidthExitBreakoutPct;   // np. 45-percentyl

            bool strongLong =
                longTrigger &&
                orRetestUp &&
                depthUp >= 3.0 && depthUp <= 50.0 && // realny, ale nie "knife"
                strongVol;

            bool strongShort =
                shortTrigger &&
                orRetestDown &&
                depthDown >= 3.0 && depthDown <= 50.0 &&
                strongVol;

            // Safety: jeśli z jakiegoś powodu wyszły oba kierunki – ignorujemy.
            if (longTrigger && shortTrigger)
            {
                longTrigger = false;
                shortTrigger = false;
                strongLong = false;
                strongShort = false;
            }

            return new ModeSignal(
                buy: longTrigger,
                sell: shortTrigger,
                buyExtra: strongLong,
                sellExtra: strongShort);
        }
    }
}

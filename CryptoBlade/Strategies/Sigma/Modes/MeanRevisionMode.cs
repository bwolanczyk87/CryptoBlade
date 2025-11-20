using System;
using System.Threading;
using CryptoBlade.Strategies.Sigma.Helpers;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// MeanReversionMode v2 – trigger-only.
    ///
    /// Wejścia tylko gdy:
    /// - globalne gate’y przechodzą (spread, ATR dla MR),
    /// - ADX niski (brak silnego trendu),
    /// - poprzedni z-score był poza "outer" (ZVwapEnableMR),
    ///   bieżący wrócił do "inner" (ZVwapExitMR) – close-back-in do DVWAP,
    /// - flow (ΔCVD) i nachylenie DVWAP nie są przeciwne do mean-reversion,
    /// - ΔOI nie wskazuje na "parowy" trend, który może zgnieść fade.
    ///
    /// buy/sell – standardowy MR,
    /// buyExtra/sellExtra – silne setupy z dużym wcześniejszym odchyleniem.
    /// </summary>
    public sealed class MeanReversionMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public MeanReversionMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Mode Kind => Mode.MR;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            ArgumentNullException.ThrowIfNull(data);

            // Reset per-mode debug
            data.MeanReversionEntryTier = 0;
            data.MeanReversionLongCandidate = false;
            data.MeanReversionShortCandidate = false;

            // -----------------------------------------------------------------
            // 1. Globalne gate’y: spread + ATR dla MR
            // -----------------------------------------------------------------

            if (double.IsFinite(data.SpreadBps) &&
                data.SpreadBps > (double)_options.MaxSpreadBps)
            {
                // Spread za szeroki – nie handlujemy mean-reversion.
                return ModeSignal.None;
            }

            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h < (double)_options.MrAtrMinPct ||
                data.AtrPct1h > (double)_options.MrAtrMaxPct)
            {
                // Zmienność nie w "umiarkowanym" zakresie dla MR.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 2. Trend: ADX niski – nie łapiemy pociągu
            // -----------------------------------------------------------------

            if (double.IsFinite(data.Adx1h) &&
                data.Adx1h > (double)_options.AdxDisableMomentum)
            {
                // ADX zbyt wysoki – raczej momentum niż MR.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 3. Close-back-in do DVWAP – kluczowy pattern MR
            // -----------------------------------------------------------------

            double zPrev = data.ZDvwapPrev;
            double zCurr = data.ZDvwap;

            if (!double.IsFinite(zPrev) || !double.IsFinite(zCurr))
                return ModeSignal.None;

            var (strictBackInLong, strictBackInShort) = PatternDetectors.DetectCloseBackInToVwap(
                zPrev,
                zCurr,
                outerZ: (double)_options.ZVwapEnableMR,
                innerZ: (double)_options.ZVwapExitMR
            );

            // Tier 1: luźniejszy back-in (outerSoft -> innerLoose + poprawa |z|)
            bool relaxedBackInLong = strictBackInLong;
            bool relaxedBackInShort = strictBackInShort;

            if (!strictBackInLong && !strictBackInShort)
            {
                double outerZ = (double)_options.ZVwapEnableMR;
                double innerZ = (double)_options.ZVwapExitMR;

                // Miększe progi tylko w kodzie – bez nowych parametrów:
                double outerSoft = Math.Max(0.8, outerZ - 0.3);   // np. z 1.6 zrobi się 1.3
                double innerLoose = innerZ + 0.3;                  // np. z 0.8 zrobi się 1.1
                double minImprovement = 0.5;                        // wymagana poprawa |z|

                double absPrev = Math.Abs(zPrev);
                double absCurr = Math.Abs(zCurr);

                bool wasOuterEnough = absPrev > outerSoft;
                bool improved = absPrev - absCurr >= minImprovement;
                bool inLooseBand = absCurr <= innerLoose;

                if (wasOuterEnough && improved && inLooseBand)
                {
                    if (zPrev < 0 && zCurr > zPrev)
                        relaxedBackInLong = true;
                    else if (zPrev > 0 && zCurr < zPrev)
                        relaxedBackInShort = true;
                }
            }

            if (!relaxedBackInLong && !relaxedBackInShort)
            {
                // Wciąż brak sensownego powrotu do wartości
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 4. Momentum / nachylenie – upewniamy się, że to nie jest breakout
            // -----------------------------------------------------------------

            double ac = data.AutoCorr5m;
            bool weakMomentum = !double.IsFinite(ac) || ac <= 0.20;   // brak silnej autokorelacji w przód

            double slope = data.ZSlopeDvwap;
            bool slopeOkLong = !double.IsFinite(slope) || slope >= -0.5;  // DVWAP nie "pikuje" mocno w dół
            bool slopeOkShort = !double.IsFinite(slope) || slope <= 0.5;  // DVWAP nie "ciągnie" mocno w górę

            // -----------------------------------------------------------------
            // 5. Orderflow – CVD i OI
            // -----------------------------------------------------------------

            double dcvd = data.DeltaCvd5m;
            bool cvdSupportsLong = double.IsFinite(dcvd) && dcvd > 0.0;
            bool cvdSupportsShort = double.IsFinite(dcvd) && dcvd < 0.0;

            // Jeśli masz już pola CvdFlipUp5m / CvdFlipDown5m z Momentum,
            // możesz zamiast powyższego użyć:
            //
            // bool cvdSupportsLong = data.CvdFlipUp5m || (double.IsFinite(dcvd) && dcvd > 0.0);
            // bool cvdSupportsShort = data.CvdFlipDown5m || (double.IsFinite(dcvd) && dcvd < 0.0);

            double oi = data.OiDelta1hPct;
            bool oiNeutral = !double.IsFinite(oi) || Math.Abs(oi) <= 2.0;
            // MR nie lubi bardzo silnego trendu w OI – to sygnał, że "pociąg jedzie".

            // -----------------------------------------------------------------
            // 6. Składanie triggerów long/short
            // -----------------------------------------------------------------

            bool longTrigger =
                relaxedBackInLong &&
                weakMomentum &&
                slopeOkLong &&
                oiNeutral &&
                cvdSupportsLong;

            bool shortTrigger =
                relaxedBackInShort &&
                weakMomentum &&
                slopeOkShort &&
                oiNeutral &&
                cvdSupportsShort;

            // Tier 2 (A+): strict back-in + duże wcześniejsze odchylenie
            bool strongLong =
                longTrigger &&
                strictBackInLong &&
                Math.Abs(zPrev) >= (double)_options.ZVwapEnableMR + 0.5;

            bool strongShort =
                shortTrigger &&
                strictBackInShort &&
                Math.Abs(zPrev) >= (double)_options.ZVwapEnableMR + 0.5;

            // Debug: kandydaci + tier
            data.MeanReversionLongCandidate = longTrigger;
            data.MeanReversionShortCandidate = shortTrigger;

            if ((strongLong || strongShort) && (longTrigger || shortTrigger))
                data.MeanReversionEntryTier = 2;
            else if (longTrigger || shortTrigger)
                data.MeanReversionEntryTier = 1;
            else
                data.MeanReversionEntryTier = 0;

            // W razie konfliktu – reset
            if (longTrigger && shortTrigger)
            {
                longTrigger = false;
                shortTrigger = false;
                strongLong = false;
                strongShort = false;
                data.MeanReversionEntryTier = 0;
                data.MeanReversionLongCandidate = false;
                data.MeanReversionShortCandidate = false;
            }

            return new ModeSignal(longTrigger, shortTrigger, strongLong, strongShort);

        }
    }
}

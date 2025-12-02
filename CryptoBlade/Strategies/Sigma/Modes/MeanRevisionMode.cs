using System;
using System.Collections.Generic;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// MeanReversionMode v3 – trigger-only z tierami (Soft / Medium / Hard).
    ///
    /// Założenia:
    /// - globalne gate’y (spread, ATR dla MR) są stałe i wspólne dla wszystkich tierów;
    /// - tiery różnicują:
    ///   * jak niski musi być ADX (jak bardzo "nietrendowy" jest rynek),
    ///   * jak wymagający jest pattern close-back-in do DVWAP (outer/inner z-score + siła wcześniejszego odchylenia),
    ///   * czy wymagamy wsparcia ΔCVD.
    ///
    /// Wejście / SL / TP:
    /// - ENTRY: limit przy DVWAP z lekkim dyskontem/premią zależnym od riskUnit (ATR5m),
    /// - SL: ~1× riskUnit za entry (czyste 1R),
    /// - TP: DVWAP jako główny target mean-reversion + 1R/1.5R,
    ///        z finalnym wyborem przez SessionHelpers.ChooseTakeProfits.
    /// </summary>
    public sealed class MeanReversionMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public MeanReversionMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public ModeKind Kind => ModeKind.MR;

        /// <summary>
        /// Scoring reżimu MR – używany przez ModeEngine do wyboru trybu.
        /// </summary>
        public static double Score(SigmaData data, SigmaStrategyOptions options)
        {
            double mr = 0.0;
            double atr = data.AtrPct1h;
            bool mmAtrOk = double.IsFinite(atr) && atr > 0.0 &&
                           atr >= (double)options.MrAtrMinPct &&
                           atr <= (double)options.MrAtrMaxPct;

            if (!mmAtrOk)
                return 0.0;

            double adx = data.Adx1h;
            double zDev = data.ZDvwap;
            double zSlope = data.ZSlopeDvwap;
            double ac = data.AutoCorr5m;
            double bbwP = data.Bbw15mPct;

            // 1) Niski ADX – im niższy, tym lepiej dla MR (0..30)
            double adxLow = 1.0 - MathHelpers.Normalize01(
                adx,
                (double)options.AdxDisableMomentum,
                (double)options.AdxEnableMomentum);

            adxLow = MathHelpers.Clamp01(adxLow);
            mr += 30.0 * adxLow;                    // 0..30

            // 2) Bliskość DVWAP – preferujemy |zDev| blisko 0 (0..35)
            if (double.IsFinite(zDev))
            {
                double absDev = Math.Abs(zDev);
                double devScore = 0.0;

                // 1.0 przy zDev=0, 0 przy |zDev|>=3
                if (absDev <= 3.0)
                    devScore = 1.0 - (absDev / 3.0);

                devScore = MathHelpers.Clamp01(devScore);
                mr += 35.0 * devScore;              // 0..35
            }

            // 3) Kara za duże |slope| – MR nie lubi runaway-trendów (do -25)
            if (double.IsFinite(zSlope))
            {
                // 5 sigma nachylenia → pełna kara
                double slopeMag = Math.Min(Math.Abs(zSlope) / 5.0, 1.0);
                mr -= 25.0 * slopeMag;              // 0..-25
            }

            // 4) Autokorelacja: ujemna lub blisko zera sprzyja MR (0..20)
            if (double.IsFinite(ac))
            {
                if (ac < 0.0)
                {
                    double acMag = Math.Min(-ac, 1.0);
                    mr += 20.0 * acMag;             // 0..20 przy ac=-1
                }
                else
                {
                    // im bliżej 0, tym lepiej; ac->1 obniża score
                    double flatScore = 1.0 - Math.Min(ac, 1.0);
                    mr += 10.0 * flatScore;         // 0..10
                }
            }

            // 5) Wąskie BB – kompresja (0..15)
            if (double.IsFinite(bbwP))
            {
                double bbwNorm = MathHelpers.Normalize01(
                    bbwP,
                    0.0,
                    (double)options.BbWidthExitBreakoutPct);

                double bbwScore = 1.0 - MathHelpers.Clamp01(bbwNorm);
                mr += 15.0 * bbwScore;              // 0..15
            }

            if (mr < 0.0) mr = 0.0;
            if (mr > 100.0) mr = 100.0;

            return mr;
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

            // Tryb testowy – zero gate’ów, czysty sygnał.
            if (requestedTier == ModeTier.Test)
            {
                result[ModeTier.Test] = new ModeSignal(true, false, ModeTier.Test);
                return result;
            }

            // -----------------------------------------------------------------
            // 0. Globalne gate’y: ATR dla MR (spread gate jest globalnie w strategii)
            // -----------------------------------------------------------------
            if (!double.IsFinite(data.AtrPct1h) ||
                data.AtrPct1h < (double)_options.MrAtrMinPct ||
                data.AtrPct1h > (double)_options.MrAtrMaxPct)
            {
                // Zmienność nie w "umiarkowanym" zakresie dla MR.
                return result;
            }

            // Jeśli ktoś jawnie podał konkretny tier (Hard/Medium/Soft),
            // liczony jest tylko ten jeden – bez kaskady.
            if (requestedTier is ModeTier.Hard or ModeTier.Medium or ModeTier.Soft)
            {
                result[requestedTier.Value] = GenerateForTier(data, requestedTier.Value);
                return result;
            }

            // requestedTier == null albo ModeTier.None → licz wszystkie 3 tiery.
            result[ModeTier.Hard] = GenerateForTier(data, ModeTier.Hard);
            result[ModeTier.Medium] = GenerateForTier(data, ModeTier.Medium);
            result[ModeTier.Soft] = GenerateForTier(data, ModeTier.Soft);

            return result;
        }

        private ModeSignal GenerateForTier(SigmaData data, ModeTier tier)
        {
            return tier switch
            {
                ModeTier.Hard => CalculateSignal(
                    data,
                    tier: ModeTier.Hard,
                    adxMaxOffset: -2.0,   // ADX musi być niżej niż bazowy próg
                    outerZOffset: +0.2,   // wymagana większa wcześniejsza odchyłka od DVWAP
                    innerZOffset: -0.1,   // ciaśniejszy powrót do "value"
                    minStrongPrevZOffset: 0.0,   // próg A+ bazuje na ZVwapEnableMR + 0.5
                    enableRelaxedBackInFallback: false,
                    requireCvdSupport: true),

                ModeTier.Medium => CalculateSignal(
                    data,
                    tier: ModeTier.Medium,
                    adxMaxOffset: 0.0,
                    outerZOffset: 0.0,
                    innerZOffset: 0.0,
                    minStrongPrevZOffset: 0.0,
                    enableRelaxedBackInFallback: true,
                    requireCvdSupport: true),

                ModeTier.Soft => CalculateSignal(
                    data,
                    tier: ModeTier.Soft,
                    adxMaxOffset: +3.0,
                    outerZOffset: -0.2,
                    innerZOffset: +0.1,
                    minStrongPrevZOffset: 0.0,
                    enableRelaxedBackInFallback: true,
                    requireCvdSupport: false),

                _ => ModeSignal.None
            };
        }

        private ModeSignal CalculateSignal(
            SigmaData data,
            ModeTier tier,
            double adxMaxOffset,
            double outerZOffset,
            double innerZOffset,
            double minStrongPrevZOffset,
            bool enableRelaxedBackInFallback,
            bool requireCvdSupport)
        {
            // -----------------------------------------------------------------
            // 1. ADX – rynek nie może być w silnym trendzie
            // -----------------------------------------------------------------
            double adxMaxBase = (double)_options.AdxDisableMomentum;
            double adxMax = adxMaxBase + adxMaxOffset;
            if (adxMax < 10.0) adxMax = 10.0;

            if (double.IsFinite(data.Adx1h) &&
                data.Adx1h > adxMax)
            {
                // ADX zbyt wysoki dla tego tieru – raczej momentum niż MR.
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 2. Close-back-in do DVWAP – pattern mean-reversion
            // -----------------------------------------------------------------
            double zPrev = data.ZDvwapPrev;
            double zCurr = data.ZDvwap;

            if (!double.IsFinite(zPrev) || !double.IsFinite(zCurr))
                return ModeSignal.None;

            double outerZ = (double)_options.ZVwapEnableMR + outerZOffset;
            double innerZ = (double)_options.ZVwapExitMR + innerZOffset;

            if (outerZ < 0.8) outerZ = 0.8;
            if (innerZ < 0.1) innerZ = 0.1;

            // Strict close-back-in – bazuje na PatternDetectors
            var (strictBackInLong, strictBackInShort) = PatternDetectors.DetectCloseBackInToVwap(
                zPrev,
                zCurr,
                outerZ: outerZ,
                innerZ: innerZ
            );

            // Relaxed back-in – luźniejsze progi + wymagana poprawa |z|
            bool relaxedBackInLong = strictBackInLong;
            bool relaxedBackInShort = strictBackInShort;

            if (enableRelaxedBackInFallback &&
                !strictBackInLong && !strictBackInShort)
            {
                double outerSoft = Math.Max(0.8, outerZ - 0.3);
                double innerLoose = innerZ + 0.3;
                double minImprovement = 0.5;

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

            bool backInLong;
            bool backInShort;

            if (tier == ModeTier.Hard)
            {
                // Hard: wymagamy strict back-in
                backInLong = strictBackInLong;
                backInShort = strictBackInShort;
            }
            else
            {
                // Soft/Medium: wystarczy relaxed (strict też się łapie)
                backInLong = relaxedBackInLong;
                backInShort = relaxedBackInShort;
            }

            if (!backInLong && !backInShort)
            {
                // Brak sensownego powrotu do wartości
                return ModeSignal.None;
            }

            // -----------------------------------------------------------------
            // 3. Momentum / nachylenie – upewniamy się, że to nie jest breakout
            // -----------------------------------------------------------------
            double ac = data.AutoCorr5m;
            bool weakMomentum = !double.IsFinite(ac) || ac <= 0.20;   // brak silnej autokorelacji

            double slope = data.ZSlopeDvwap;
            bool slopeOkLong = !double.IsFinite(slope) || slope >= -0.5;  // DVWAP nie "pikuje" mocno w dół
            bool slopeOkShort = !double.IsFinite(slope) || slope <= 0.5;  // DVWAP nie "ciągnie" mocno w górę

            // -----------------------------------------------------------------
            // 4. Orderflow – CVD i OI
            // -----------------------------------------------------------------
            double dcvd = data.DeltaCvd5m;
            bool cvdSupportsLong = !requireCvdSupport || (double.IsFinite(dcvd) && dcvd > 0.0);
            bool cvdSupportsShort = !requireCvdSupport || (double.IsFinite(dcvd) && dcvd < 0.0);

            double oi = data.OiDelta1hPct;
            bool oiNeutral = !double.IsFinite(oi) || Math.Abs(oi) <= 2.0;
            // MR nie lubi bardzo silnego trendu w OI – to sygnał, że "pociąg jedzie".

            // -----------------------------------------------------------------
            // 5. Składanie triggerów long/short
            // -----------------------------------------------------------------
            bool longTrigger =
                backInLong &&
                weakMomentum &&
                slopeOkLong &&
                oiNeutral &&
                cvdSupportsLong;

            bool shortTrigger =
                backInShort &&
                weakMomentum &&
                slopeOkShort &&
                oiNeutral &&
                cvdSupportsShort;

            // Dodatkowe wymagania A+ dla tieru Hard – duża wcześniejsza odchyłka
            if (tier == ModeTier.Hard)
            {
                double strongPrevMin = (double)_options.ZVwapEnableMR + 0.5 + minStrongPrevZOffset;

                if (Math.Abs(zPrev) < strongPrevMin)
                {
                    longTrigger = false;
                    shortTrigger = false;
                }
            }

            // -----------------------------------------------------------------
            // 6. Sanity + debug
            // -----------------------------------------------------------------
            if (longTrigger && shortTrigger)
            {
                // Konflikt – nie otwieramy żadnej strony dla tego tieru
                return ModeSignal.None;
            }

            if (!longTrigger && !shortTrigger)
            {
                // Brak sygnału dla tego tieru
                return ModeSignal.None;
            }

            return new ModeSignal(longTrigger, shortTrigger, tier);
        }

        /// <summary>
        /// ENTRY dla mean-reversion:
        /// - bazą jest DVWAP (jeśli dostępny), w przeciwnym razie refPrice,
        /// - przesuwamy się o niewielki offset w stronę "value":
        ///   long: poniżej DVWAP, short: powyżej DVWAP,
        /// - offset zależy od riskUnit (ATR5m) z capem w bps.
        /// </summary>
        public decimal? ComputeEntryPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side)
        {
            var refPrice = SessionHelpers.GetRefPrice(data);
            if (!refPrice.HasValue || refPrice.Value <= 0m)
                return null;

            decimal basis = data.LastDvwap.HasValue && data.LastDvwap.Value > 0m
                ? data.LastDvwap.Value
                : refPrice.Value;

            if (basis <= 0m)
                return null;

            var riskUnit = SessionHelpers.ComputeRiskUnit(
                data,
                basis,
                _options.RiskFloorPct,
                _options.RiskCapPct,
                _options.RiskFallbackPct);

            // offset: min(0.5×riskUnit, 0.3% ceny)
            decimal offsetMaxPct = 0.3m / 100m; // 30 bps
            decimal offsetByAtr = riskUnit > 0m ? riskUnit * 0.5m : 0m;
            decimal offsetByPct = basis * offsetMaxPct;
            decimal offset = offsetByAtr > 0m ? Math.Min(offsetByAtr, offsetByPct) : offsetByPct;

            if (offset <= 0m)
                offset = basis * 0.001m; // minimalne 10 bps

            decimal entry = side == OrderSide.Buy
                ? basis - offset
                : basis + offset;

            entry = MathHelpers.RoundPrice(symbolInfo.PriceScale, entry);
            return entry > 0m ? entry : null;
        }

        /// <summary>
        /// SL dla MR:
        /// - ~1× riskUnit za entry:
        ///   long: entry - riskUnit, short: entry + riskUnit.
        /// </summary>
        public decimal? ComputeStopLossPrice(SigmaData data, SymbolInfo symbolInfo, OrderSide side, decimal entryPrice, ModeTier tier)
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

            decimal sl = side == OrderSide.Buy
                ? entryPrice - riskUnit
                : entryPrice + riskUnit;

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
                        return;
                }

                var r = (double)(move / risk);
                if (!double.IsFinite(r) || r <= 0.0)
                    return;

                targets.Add((price, r));
            }

            // DVWAP jako główny target mean-reversion
            if (data.LastDvwap.HasValue && data.LastDvwap.Value > 0.0m)
            {
                var dvwap = data.LastDvwap.Value;

                if (side == OrderSide.Buy && dvwap > entryPrice)
                    AddTarget(dvwap);
                else if (side == OrderSide.Sell && dvwap < entryPrice)
                    AddTarget(dvwap);
            }

            // Fallback – czyste R-multiples
            if (side == OrderSide.Buy)
            {
                AddTarget(entryPrice + risk);           // 1R
                AddTarget(entryPrice + 1.5m * risk);    // 1.5R
            }
            else if (side == OrderSide.Sell)
            {
                AddTarget(entryPrice - risk);           // 1R
                AddTarget(entryPrice - 1.5m * risk);    // 1.5R
            }

            var (tp1, tp2) = SessionHelpers.ChooseTakeProfits(targets, side, entryPrice, symbolInfo.PriceScale);

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

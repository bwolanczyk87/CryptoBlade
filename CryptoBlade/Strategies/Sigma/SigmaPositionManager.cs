using CryptoBlade.Strategies.Sigma.Modes;
using System;

namespace CryptoBlade.Strategies.Sigma
{
    /// <summary>
    /// Kierunek pozycji dla SigmaPositionManager.
    /// Celowo trzymamy własny enum, żeby nie być zależnym od modeli giełdy.
    /// </summary>
    public enum SigmaPositionSide
    {
        Long = 1,
        Short = -1
    }

    /// <summary>
    /// Plan zarządzania pozycją wyliczany w momencie wejścia.
    /// Jest w 100% niezależny od giełdy – strategia mapuje to dalej na:
    /// EntryPrice, StopLossPrice, TakeProfitPrice, TrailingStopActivePrice,
    /// TrailingStopPriceDistance, StopLossTakeProfitMode itd.
    /// </summary>
    public sealed class SigmaPositionPlan
    {
        public Mode Mode { get; init; }
        public SigmaPositionSide Side { get; init; }

        /// <summary>
        /// Cena wejścia (limit), którą strategia faktycznie zleci na giełdzie.
        /// </summary>
        public decimal EntryPrice { get; init; }

        /// <summary>
        /// Początkowy SL – za strukturą + bufor ATR.
        /// </summary>
        public decimal StopLossPrice { get; init; }

        /// <summary>
        /// TP1 – zwykle 1R dla MM/BO, ciaśniej dla MR.
        /// Może być wykorzystany jako poziom dla zlecenia reduce-only.
        /// </summary>
        public decimal? TakeProfit1Price { get; init; }

        /// <summary>
        /// TP2 – zwykle 1.6–2.2R dla MM/BO (agresywny target).
        /// </summary>
        public decimal? TakeProfit2Price { get; init; }

        /// <summary>
        /// Cena, przy której chcemy przesunąć SL na BE (break-even).
        /// </summary>
        public decimal? BreakEvenActivationPrice { get; init; }

        /// <summary>
        /// Cena, przy której chcemy aktywować trailing stop (SetTradingStop z trailingStop & activePrice).
        /// </summary>
        public decimal? TrailingStopActivationPrice { get; init; }

        /// <summary>
        /// Dystans trailing stop (w punktach ceny).
        /// </summary>
        public decimal? TrailingStopDistance { get; init; }

        /// <summary>
        /// Time-stop – po ilu minutach od wejścia MR ma zostać zamknięty, jeśli nie był TP1.
        /// Dla MM/BO zwykle null.
        /// </summary>
        public TimeSpan? TimeStop { get; init; }

        /// <summary>
        /// Odległość 1R w punktach (EntryPrice - StopLossPrice dla longa, odwrotnie dla shorta).
        /// Przydaje się w audycie.
        /// </summary>
        public decimal RDistanceAbs { get; init; }
    }

    /// <summary>
    /// Wspólne zarządzanie pozycją dla wszystkich trzech reżimów Sigmy.
    ///
    /// Wejście:
    /// - tryb (MM/MR/BO),
    /// - kierunek (long/short),
    /// - snapshot SigmaData,
    /// - EntryPrice (już policzony zgodnie z logiką moda – np. 50% świecy trigger),
    /// - InvalidationPrice (poziom strukturalny: low/ high sweepe'a, krawędź OR, itp.),
    /// - priceScale – liczba miejsc po przecinku dla ceny (do Math.Round).
    ///
    /// Wyjście:
    /// - plan z Entry/SL/TP1/TP2 + logiką BE/TS/time-stop.
    /// </summary>
    public sealed class SigmaPositionManager
    {
        private readonly SigmaStrategyOptions _options;

        // Wspólne parametry – można później przenieść do SigmaStrategyOptions.
        private const decimal MmTp1R = 1.0m;
        private const decimal MmTp2R = 1.8m;
        private const decimal BoTp1R = 1.0m;
        private const decimal BoTp2R = 2.0m;
        private const decimal MrTp1R = 0.8m;
        private const decimal MrTp2R = 1.2m;

        private const decimal MmBeR = 1.0m;     // MM/BO: BE po 1R
        private const decimal BoBeR = 1.0m;
        private const decimal MrBeR = 0.7m;     // MR: szybciej BE

        private const decimal MmTrailR = 1.2m;  // od kiedy trailing
        private const decimal BoTrailR = 1.2m;
        private const decimal MrTrailR = 1.0m;

        private const decimal AtrBufferMultiplier = 0.6m;      // max(0.5..0.6)*ATR5m
        private const decimal TrailingAtrMultiplier = 0.8m;    // ~0.8 ATR5m jako TS

        // Minimalny dystans SL jako % ceny (safety, gdy struktura blisko wejścia)
        private const decimal MinStopDistancePct = 0.0015m;    // 0.15%

        // Time-stop dla MR – 40 min domyślnie (pomiędzy 30 a 45).
        private static readonly TimeSpan DefaultMrTimeStop = TimeSpan.FromMinutes(40);

        public SigmaPositionManager(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Buduje plan wejścia dla nowej pozycji.
        /// </summary>
        /// <param name="mode">Aktywny reżim (MM/MR/BO).</param>
        /// <param name="side">Long/Short.</param>
        /// <param name="data">Snapshot SigmaData z bieżącej decyzji.</param>
        /// <param name="entryPrice">
        /// Cena wejścia (limit) wyliczona przez strategię,
        /// np. 50% świecy trigger / retest level, z uwzględnieniem tick size.
        /// </param>
        /// <param name="invalidationPrice">
        /// Poziom strukturalny, po którego wybiciu setup jest obalony:
        /// - MM: low/high sweepe'a + trochę miejsca,
        /// - BO: krawędź OR / Donchian,
        /// - MR: ekstremum odchylenia od wartości.
        /// </param>
        /// <param name="priceScale">Liczba miejsc po przecinku dla ceny (do Math.Round).</param>
        public SigmaPositionPlan BuildEntryPlan(
            Mode mode,
            SigmaPositionSide side,
            SigmaData data,
            decimal entryPrice,
            decimal invalidationPrice,
            int priceScale)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (entryPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(entryPrice));
            if (priceScale < 0 || priceScale > 10) throw new ArgumentOutOfRangeException(nameof(priceScale));

            decimal atr5mAbs = EstimateAtr5mAbs(data, entryPrice);
            if (atr5mAbs <= 0m)
            {
                // Jeśli nie mamy sensownego ATR, minimalna odległość SL = 0.2% ceny.
                atr5mAbs = Math.Round(entryPrice * 0.002m, priceScale);
                if (atr5mAbs <= 0m)
                    atr5mAbs = _options.MinAtr5mFloor;
            }

            decimal buffer = Math.Max(_options.MinAtr5mFloor, atr5mAbs * AtrBufferMultiplier);

            // ------------------------
            // 1. Wyznaczamy SL
            // ------------------------

            decimal stopLoss;

            if (side == SigmaPositionSide.Long)
            {
                // Invalidation poniżej ceny – SL jeszcze kawałek niżej.
                var rawSl = invalidationPrice - buffer;

                // Safety: jeśli wyszło za blisko / powyżej entry, narzucamy min dystans.
                var minDistance = entryPrice * MinStopDistancePct;
                if (rawSl >= entryPrice - minDistance)
                    rawSl = entryPrice - minDistance;

                stopLoss = RoundPrice(rawSl, priceScale);
            }
            else
            {
                // Short: invalidation powyżej ceny – SL wyżej.
                var rawSl = invalidationPrice + buffer;

                var minDistance = entryPrice * MinStopDistancePct;
                if (rawSl <= entryPrice + minDistance)
                    rawSl = entryPrice + minDistance;

                stopLoss = RoundPrice(rawSl, priceScale);
            }

            decimal rDist = ComputeRDistanceAbs(side, entryPrice, stopLoss);
            if (rDist <= 0m)
                throw new InvalidOperationException("Nieprawidłowy dystans R (Entry/SL są odwrócone).");

            // ------------------------
            // 2. TP1/TP2 + BE/TS
            // ------------------------

            decimal tp1R, tp2R, beR, trailR;
            TimeSpan? timeStop;

            switch (mode)
            {
                case Mode.MM:
                    tp1R = MmTp1R;
                    tp2R = MmTp2R;
                    beR = MmBeR;
                    trailR = MmTrailR;
                    timeStop = null;
                    break;

                case Mode.BO:
                    tp1R = BoTp1R;
                    tp2R = BoTp2R;
                    beR = BoBeR;
                    trailR = BoTrailR;
                    timeStop = null;
                    break;

                case Mode.MR:
                    // Mean reversion – ciaśniejsze targety i time-stop.
                    tp1R = MrTp1R;
                    tp2R = MrTp2R;
                    beR = MrBeR;
                    trailR = MrTrailR;
                    timeStop = DefaultMrTimeStop;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Nieobsługiwany tryb Sigmy.");
            }

            decimal sign = side == SigmaPositionSide.Long ? 1m : -1m;

            decimal? tp1 = RoundPrice(entryPrice + sign * rDist * tp1R, priceScale);
            decimal? tp2 = RoundPrice(entryPrice + sign * rDist * tp2R, priceScale);

            decimal? beActivation = RoundPrice(entryPrice + sign * rDist * beR, priceScale);
            decimal? tsActivation = RoundPrice(entryPrice + sign * rDist * trailR, priceScale);
            decimal? tsDistance = RoundPrice(atr5mAbs * TrailingAtrMultiplier, priceScale);

            return new SigmaPositionPlan
            {
                Mode = mode,
                Side = side,
                EntryPrice = RoundPrice(entryPrice, priceScale),
                StopLossPrice = stopLoss,
                TakeProfit1Price = tp1,
                TakeProfit2Price = tp2,
                BreakEvenActivationPrice = beActivation,
                TrailingStopActivationPrice = tsActivation,
                TrailingStopDistance = tsDistance,
                TimeStop = timeStop,
                RDistanceAbs = rDist
            };
        }

        /// <summary>
        /// Szacuje ATR na 5m na bazie ATR 1h z SigmaData.
        /// Jeśli brak danych, korzysta z MinAtr5mFloor.
        /// </summary>
        private decimal EstimateAtr5mAbs(SigmaData data, decimal refPrice)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (refPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(refPrice));

            double atr1hAbsDouble = data.Atr1hAbs;
            double atrPct1hDouble = data.AtrPct1h;

            decimal atrFromAbs = 0m;
            if (double.IsFinite(atr1hAbsDouble) && atr1hAbsDouble > 0.0)
                atrFromAbs = (decimal)atr1hAbsDouble;

            decimal atrFromPct = 0m;
            if (double.IsFinite(atrPct1hDouble) && atrPct1hDouble > 0.0)
                atrFromPct = (decimal)(atrPct1hDouble / 100.0) * refPrice;

            decimal atrBase;
            if (atrFromAbs > 0m && atrFromPct > 0m)
                atrBase = Math.Min(atrFromAbs, atrFromPct);    // konserwatywnie
            else
                atrBase = atrFromAbs > 0m ? atrFromAbs : atrFromPct;

            if (atrBase <= 0m)
                return _options.MinAtr5mFloor;

            // ATR(1h) -> ~ATR(5m). 1h = 12 * 5m. Uproszczenie liniowe jest OK,
            // bo i tak potem clampujemy przez MinAtr5mFloor.
            decimal atr5m = atrBase / 12m;

            if (atr5m <= 0m)
                atr5m = _options.MinAtr5mFloor;

            return atr5m;
        }

        private static decimal ComputeRDistanceAbs(SigmaPositionSide side, decimal entry, decimal stop)
        {
            return side == SigmaPositionSide.Long
                ? entry - stop
                : stop - entry;
        }

        private static decimal RoundPrice(decimal price, int priceScale)
        {
            return Math.Round(price, priceScale, MidpointRounding.AwayFromZero);
        }
    }
}

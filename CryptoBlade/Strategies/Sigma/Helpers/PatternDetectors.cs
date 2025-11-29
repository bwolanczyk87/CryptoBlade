using System;
using System.Collections.Generic;
using System.Linq;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    /// <summary>
    /// Lekkie, deterministyczne detektory struktur świec i zakresów.
    /// Zero stanu; wszystko jako pure functions.
    /// </summary>
    public static class PatternDetectors
    {
        /// <summary>
        /// Zwraca true, gdy bieżąca świeca jest w pełni wewnątrz poprzedniej.
        /// </summary>
        public static bool IsInsideBar(Quote previous, Quote current)
        {
            if (previous == null || current == null)
                return false;

            return current.High <= previous.High && current.Low >= previous.Low;
        }

        /// <summary>
        /// NR7: ostatni bar ma najmniejszy (ściśle) range (High-Low) wśród ostatnich 7 barów.
        /// Zwraca false, gdy brak wystarczającej liczby barów lub dane są niespójne.
        /// </summary>
        public static bool IsNr7Pattern(IReadOnlyList<Quote> bars)
        {
            if (bars == null || bars.Count < 7)
                return false;

            // Bierzemy ostatnie 7 barów
            var window = bars.Skip(bars.Count - 7).ToArray();

            decimal lastRange = window[^1].High - window[^1].Low;
            if (lastRange <= 0m)
                return false;

            for (int i = 0; i < window.Length - 1; i++)
            {
                decimal r = window[i].High - window[i].Low;
                if (r <= 0m)
                    return false;

                // Jeśli którykolwiek wcześniejszy range jest mniejszy lub równy,
                // to ostatni nie jest ściśle najmniejszy -> brak NR7.
                if (lastRange >= r)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Flaga: czy ostatni bar tworzy inside bar względem poprzedniego
        /// LUB spełnia warunek NR7 na ostatnich 7 barach.
        /// Wymaga co najmniej 2 barów (dla inside); NR7 sam sprawdza wymóg 7 barów.
        /// </summary>
        public static bool HasInsideBarOrNr7Pattern(IReadOnlyList<Quote> bars)
        {
            if (bars == null || bars.Count < 2)
                return false;

            var last = bars[^1];
            var prev = bars[^2];

            if (IsInsideBar(prev, last))
                return true;

            return IsNr7Pattern(bars);
        }

        /// <summary>
        /// Detekcja wybicia Donchian Channel na ostatnim barze.
        /// Zwraca:
        /// - DonchanResult (ostatni punkt) lub null przy braku danych,
        /// - up:  close &gt; UpperBand,
        /// - down: close &lt; LowerBand.
        /// </summary>
        public static (DonchianResult? donchian, bool up, bool down) DetectDonchianBreakout(
            IReadOnlyList<Quote> bars,
            int period)
        {
            if (bars == null || bars.Count < period + 1)
                return (null, false, false);

            var dcList = bars.GetDonchian(period).ToList();
            if (dcList.Count == 0)
                return (null, false, false);

            var dc = dcList[^1];
            if (!dc.UpperBand.HasValue || !dc.LowerBand.HasValue)
                return (null, false, false);

            decimal close = bars[^1].Close;
            bool up = close > dc.UpperBand.Value;
            bool down = close < dc.LowerBand.Value;

            return (dc, up, down);
        }

        /// <summary>
        /// Opening Range (max/min) z pierwszych N minut.
        /// Zakłada 1-minutowe bary w kolejności rosnącej po czasie.
        /// Zwraca (0,0) przy braku danych lub minutes &lt;= 0.
        /// </summary>
        public static (decimal high, decimal low) ComputeOpeningRange(
            IReadOnlyList<Quote> oneMinuteBars,
            int minutes = 30)
        {
            if (oneMinuteBars == null || oneMinuteBars.Count == 0 || minutes <= 0)
                return (0m, 0m);

            int take = Math.Min(minutes, oneMinuteBars.Count);
            var segment = oneMinuteBars.Take(take).ToArray();

            decimal high = segment.Max(b => b.High);
            decimal low = segment.Min(b => b.Low);

            return (high, low);
        }

        /// <summary>
        /// Detekcja prostego wzorca sweep -> reclaim na ostatnim barze.
        ///
        /// Idea:
        /// - patrzymy na ostatni bar w serii (np. 5m),
        /// - szukamy wstecz wśród poprzednich <paramref name="lookbackBars"/> barów
        ///   poziomu "swing high" / "swing low" (max High / min Low),
        /// - jeśli ostatni bar:
        ///     * wybija ten poziom knotem (High &gt; swingHigh lub Low &lt; swingLow)
        ///       o co najmniej <paramref name="minOvershootBps"/> bps
        ///     * i zamyka się po przeciwnej stronie poziomu (reclaim),
        ///   to sygnalizujemy sweepUpReclaim lub sweepDownReclaim.
        ///
        /// Zwraca też overshoot w bps – ile mniej więcej "poza" poziom poszedł knot.
        /// </summary>
        /// <param name="bars">Świece 5m, w kolejności rosnącej po czasie.</param>
        /// <param name="lookbackBars">
        /// Ile wcześniejszych barów wstecz brać do wyznaczenia poziomu high/low.
        /// Typowe wartości 8-20.
        /// </param>
        /// <param name="minOvershootBps">
        /// Minimalny overshoot w bps, żeby uznać ruch za realny sweep (domyślnie 1 bps).
        /// </param>
        public static (bool sweepUpReclaim,
                       bool sweepDownReclaim,
                       double sweepUpOvershootBps,
                       double sweepDownOvershootBps)
            DetectSweepReclaimOnLastBar(
                IReadOnlyList<Quote> bars,
                int lookbackBars = 12,
                double minOvershootBps = 1.0)
        {
            if (bars == null || bars.Count < 3 || lookbackBars <= 0)
                return (false, false, 0.0, 0.0);

            int lastIndex = bars.Count - 1;
            var last = bars[lastIndex];

            // Zakres do szukania wcześniejszego poziomu high/low – bez ostatniego bara
            int end = lastIndex - 1;
            int start = Math.Max(0, end - lookbackBars + 1);

            if (end <= start)
                return (false, false, 0.0, 0.0);

            decimal swingHigh = decimal.MinValue;
            decimal swingLow = decimal.MaxValue;

            for (int i = start; i <= end; i++)
            {
                var b = bars[i];
                if (b.High > swingHigh) swingHigh = b.High;
                if (b.Low < swingLow) swingLow = b.Low;
            }

            bool sweepUpReclaim = false;
            bool sweepDownReclaim = false;
            double sweepUpOvershootBps = 0.0;
            double sweepDownOvershootBps = 0.0;

            // Sweep w górę: High wybija poprzedni swingHigh, ale close jest <= swingHigh
            if (swingHigh > 0m && last.High > swingHigh && last.Close <= swingHigh)
            {
                var ratio = (double)(last.High / swingHigh);
                sweepUpOvershootBps = (ratio - 1.0) * 10_000.0;

                if (sweepUpOvershootBps >= minOvershootBps)
                    sweepUpReclaim = true;
                else
                    sweepUpOvershootBps = 0.0;
            }

            // Sweep w dół: Low wybija poprzedni swingLow, ale close jest >= swingLow
            if (swingLow > 0m && last.Low < swingLow && last.Close >= swingLow)
            {
                var ratio = (double)(swingLow / last.Low);
                sweepDownOvershootBps = (ratio - 1.0) * 10_000.0;

                if (sweepDownOvershootBps >= minOvershootBps)
                    sweepDownReclaim = true;
                else
                    sweepDownOvershootBps = 0.0;
            }

            return (sweepUpReclaim, sweepDownReclaim, sweepUpOvershootBps, sweepDownOvershootBps);
        }

        /// <summary>
        /// Detekcja "close-back-in do D-VWAP":
        /// poprzedni z-score (zPrev) poza strefą |z| >= outerZ,
        /// bieżący z-score (zCurr) wraca do "wnętrza" |z| <= innerZ.
        ///
        /// Long: zPrev mocno poniżej DVWAP, zCurr wraca w okolice DVWAP.
        /// Short: zPrev mocno powyżej DVWAP, zCurr wraca w okolice DVWAP.
        /// </summary>
        public static (bool backInLong, bool backInShort) DetectCloseBackInToVwap(
            double zPrev,
            double zCurr,
            double outerZ = 1.8,
            double innerZ = 1.0)
        {
            if (!double.IsFinite(zPrev) || !double.IsFinite(zCurr))
                return (false, false);

            double absOuter = Math.Abs(outerZ);
            double absInner = Math.Abs(innerZ);

            // Parametry muszą mieć sens: outer > inner > 0
            if (absOuter <= absInner || absInner <= 0.0)
                return (false, false);

            bool backInLong = zPrev <= -absOuter && Math.Abs(zCurr) <= absInner;
            bool backInShort = zPrev >= absOuter && Math.Abs(zCurr) <= absInner;

            return (backInLong, backInShort);
        }
    }
}

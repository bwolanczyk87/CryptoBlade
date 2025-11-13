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
        /// Donchian Breakout na podstawie kanału o zadanym okresie.
        /// Zwraca (up, down): 
        ///  up=true, gdy ostatnie zamknięcie jest powyżej górnego pasma,
        ///  down=true, gdy ostatnie zamknięcie jest poniżej dolnego pasma.
        /// Wymaga co najmniej (period + 1) barów (zgodnie z wymaganiami biblioteki).
        /// </summary>
        public static (bool up, bool down) DetectDonchianBreakout(
            IReadOnlyList<Quote> bars,
            int period = 20)
        {
            if (bars == null || bars.Count == 0)
                return (false, false);

            if (period < 2)
                throw new ArgumentOutOfRangeException(nameof(period), "Donchian period must be >= 2.");

            if (bars.Count < period + 1)
                return (false, false);

            // Skender zwróci kanał dla każdego baru od "period"
            var dc = bars.GetDonchian(period)
                         .LastOrDefault(r => r.UpperBand.HasValue && r.LowerBand.HasValue);

            if (dc == null)
                return (false, false);

            decimal close = bars[^1].Close;
            bool up = close > dc.UpperBand!.Value;
            bool down = close < dc.LowerBand!.Value;

            return (up, down);
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
    }
}

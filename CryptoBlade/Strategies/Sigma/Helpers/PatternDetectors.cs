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
        /// True gdy ostatnia świeca jest w pełni wewnątrz poprzedniej.
        /// </summary>
        public static bool IsInsideBar(Quote prev, Quote last)
        {
            if (prev == null || last == null) return false;
            return last.High <= prev.High && last.Low >= prev.Low;
        }

        /// <summary>
        /// NR7: ostatni bar ma najmniejszy range (High-Low) wśród ostatnich 7.
        /// </summary>
        public static bool IsNr7(IReadOnlyList<Quote> bars)
        {
            if (bars == null || bars.Count < 7) return false;
            var window = bars.Skip(bars.Count - 7).ToArray();
            double lastRange = (double)(window[^1].High - window[^1].Low);
            if (lastRange < 0) return false;
            for (int i = 0; i < window.Length - 1; i++)
            {
                double r = (double)(window[i].High - window[i].Low);
                if (r < 0) return false;
                if (lastRange > r) return false; // nie jest najmniejszy
            }
            return true;
        }

        /// <summary>
        /// Flaga inside lub NR7 na podstawie listy barów.
        /// </summary>
        public static bool HasInsideOrNr7(IReadOnlyList<Quote> bars)
        {
            if (bars == null || bars.Count < 8) return false;
            var last = bars[^1];
            var prev = bars[^2];
            return IsInsideBar(prev, last) || IsNr7(bars);
        }

        /// <summary>
        /// Donchian Breakout na podstawie kanału o zadanym okresie.
        /// Zwraca (up, down). Działa na dowolnym TF; sensowny okres np. 20.
        /// </summary>
        public static (bool up, bool down) DonchianBreak(IReadOnlyList<Quote> bars, int period = 20)
        {
            if (bars == null || bars.Count < period + 1) return (false, false);

            // Skender zwróci kanał dla każdego baru od "period"
            var dc = bars.GetDonchian(period).LastOrDefault();
            if (dc == null || !dc.UpperBand.HasValue || !dc.LowerBand.HasValue) return (false, false);

            decimal close = bars[^1].Close;
            bool up = close > dc.UpperBand!.Value;
            bool dn = close < dc.LowerBand!.Value;
            return (up, dn);
        }

        /// <summary>
        /// Opening Range (max/min) z pierwszych N minut. Zakłada 1-minutowe bary.
        /// Zwraca (0,0) przy braku danych.
        /// </summary>
        public static (decimal high, decimal low) OpeningRange(IReadOnlyList<Quote> oneMinuteBars, int minutes = 30)
        {
            if (oneMinuteBars == null || oneMinuteBars.Count == 0 || minutes <= 0)
                return (0m, 0m);

            int take = Math.Min(minutes, oneMinuteBars.Count);
            var seg = oneMinuteBars.Take(take).ToArray();
            decimal hi = seg.Max(b => b.High);
            decimal lo = seg.Min(b => b.Low);
            return (hi, lo);
        }
    }
}

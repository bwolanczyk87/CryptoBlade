using MathNet.Numerics.Statistics;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    /// <summary>
    /// Zbiór prostych funkcji statystycznych i narzędziowych.
    /// Zasada: nie rzucamy wyjątków na brak danych; zwracamy NaN gdy niepoliczalne.
    /// </summary>
    public static class Stats
    {
        // ----- ŚREDNIA -----
        public static double Mean(double[] v)
        {
            if (v == null || v.Length == 0) return double.NaN;
            return v.Mean();
        }

        public static double Mean(ReadOnlySpan<double> v)
        {
            if (v.Length == 0) return double.NaN;
            return v.ToArray().Mean();
        }

        // ----- ODCHYLENIE STANDARDOWE (próba; korekta Bessela) -----
        public static double StdDevSample(double[] v)
        {
            if (v == null || v.Length < 2) return double.NaN;
            return v.StandardDeviation();
        }

        public static double StdDevSample(ReadOnlySpan<double> v)
        {
            if (v.Length < 2) return double.NaN;
            return v.ToArray().StandardDeviation();
        }

        // ----- LOG-STOPY ZWROTU -----
        public static double[] ReturnsLog(double[] px)
        {
            if (px == null) return Array.Empty<double>();
            int n = px.Length;
            if (n < 2) return Array.Empty<double>();

            var r = new double[n - 1];
            for (int i = 1; i < n; i++)
            {
                double p0 = px[i - 1], p1 = px[i];
                if (p0 <= 0 || p1 <= 0 || !double.IsFinite(p0) || !double.IsFinite(p1))
                {
                    r[i - 1] = 0.0; // neutralny placeholder, nie NaN (ułatwia dalsze obliczenia)
                    continue;
                }
                r[i - 1] = Math.Log(p1 / p0);
            }
            return r;
        }

        // ----- KORELACJA PEARSONA -----
        public static double Corr(double[] a, double[] b)
        {
            if (a == null || b == null) return double.NaN;
            int n = Math.Min(a.Length, b.Length);
            if (n < 3) return double.NaN;

            // skróć do wspólnej długości
            var aa = a.AsSpan(0, n).ToArray();
            var bb = b.AsSpan(0, n).ToArray();

            // MathNet poradzi sobie ze stałymi szeregami zwracając NaN
            var r = Correlation.Pearson(aa, bb);
            return double.IsFinite(r) ? r : double.NaN;
        }

        // ----- Nasycenie wartości (symetryczny clamp) -----
        public static double Saturate(double v, double limit)
        {
            if (!double.IsFinite(v)) return double.NaN;
            if (v > limit) return limit;
            if (v < -limit) return -limit;
            return v;
        }

        /// <summary>
        /// Rolling VWAP na bazie (H+L+C)/3 i wolumenu. Zwraca serię tej samej długości
        /// z NaN dla próbek < window (warmup). lastVwap to ostatnia wartość serii.
        /// </summary>
        public static (double[] vwapSeries, double lastVwap) RollingVwap(Quote[] q, int window)
        {
            if (q == null || q.Length == 0 || window <= 1)
                return (Array.Empty<double>(), double.NaN);

            int n = q.Length;
            var vwap = new double[n];
            var tp = new double[n];
            var vol = new double[n];

            for (int i = 0; i < n; i++)
            {
                tp[i] = (double)((q[i].High + q[i].Low + q[i].Close) / 3m);
                vol[i] = (double)q[i].Volume;
            }

            double sumPv = 0.0, sumV = 0.0;
            var qPv = new Queue<double>(window);
            var qV = new Queue<double>(window);

            for (int i = 0; i < n; i++)
            {
                double pv = tp[i] * vol[i];
                sumPv += pv; sumV += vol[i];
                qPv.Enqueue(pv); qV.Enqueue(vol[i]);

                if (qPv.Count > window)
                {
                    sumPv -= qPv.Dequeue();
                    sumV -= qV.Dequeue();
                }

                vwap[i] = (qPv.Count == window && sumV > 0.0) ? (sumPv / sumV) : double.NaN;
            }

            var last = vwap.Length > 0 ? vwap[^1] : double.NaN;
            return (vwap, last);
        }

        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);

        // gładkie ścięcie outlierów (x ~ N(0,1), k=2-3)
        public static double TanhScaled(double z, double k = 2.0) => Math.Tanh(z / k);

        // zscore z guardami
        public static double Z(double x, double mean, double std)
            => (std > 1e-12) ? (x - mean) / std : 0.0;

        public static double Percentiles(double[] xs, double p01to99)
        {
            if (xs == null || xs.Length == 0) return double.NaN;
            var p = Clamp(p01to99, 0, 100) / 100.0;
            var arr = xs.OrderBy(v => v).ToArray();
            var idx = (arr.Length - 1) * p;
            var i = (int)Math.Floor(idx);
            var frac = idx - i;
            if (i + 1 < arr.Length) return arr[i] * (1 - frac) + arr[i + 1] * frac;
            return arr[^1];
        }

        public static int SignWithEps(double x, double eps) => x > eps ? 1 : x < -eps ? -1 : 0;
    }
}

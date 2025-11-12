using System;
using System.Linq;
using MathNet.Numerics.Statistics;

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
    }
}

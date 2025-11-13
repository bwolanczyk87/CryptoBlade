using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.Statistics;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    /// <summary>
    /// Zbiór prostych funkcji statystycznych i narzędziowych
    /// używanych w strategii Sigma.
    /// Konwencja: brak danych / niepoliczalne => double.NaN
    /// (z wyjątkiem log-stóp zwrotu, gdzie używamy 0.0 jako neutralny placeholder).
    /// </summary>
    public static class StatisticsHelpers
    {
        // ===== ŚREDNIA =====

        /// <summary>
        /// Średnia arytmetyczna. Zwraca NaN dla null / pustej tablicy.
        /// </summary>
        public static double ComputeMean(double[] values)
        {
            if (values == null || values.Length == 0)
                return double.NaN;

            return values.Mean();
        }

        /// <summary>
        /// Średnia arytmetyczna dla ReadOnlySpan. Zwraca NaN dla pustego.
        /// </summary>
        public static double ComputeMean(ReadOnlySpan<double> values)
        {
            if (values.Length == 0)
                return double.NaN;

            return values.ToArray().Mean();
        }

        // ===== ODCHYLENIE STANDARDOWE (próba; korekta Bessela) =====

        /// <summary>
        /// Odchylenie standardowe próbki (korekta Bessela).
        /// Zwraca NaN, gdy mniej niż 2 elementy.
        /// </summary>
        public static double ComputeSampleStdDev(double[] values)
        {
            if (values == null || values.Length < 2)
                return double.NaN;

            return values.StandardDeviation();
        }

        /// <summary>
        /// Odchylenie standardowe próbki (korekta Bessela) dla ReadOnlySpan.
        /// </summary>
        public static double ComputeSampleStdDev(ReadOnlySpan<double> values)
        {
            if (values.Length < 2)
                return double.NaN;

            return values.ToArray().StandardDeviation();
        }

        // ===== LOG-STOPY ZWROTU =====

        /// <summary>
        /// Logarytmiczne stopy zwrotu ln(p_t / p_{t-1}) dla ciągu cen.
        /// Dla niepoprawnych cen (<= 0 lub niefinity) wpisuje 0.0 zamiast NaN,
        /// żeby nie psuć dalszych obliczeń.
        /// </summary>
        public static double[] ComputeLogReturns(double[] prices)
        {
            if (prices == null)
                return Array.Empty<double>();

            int n = prices.Length;
            if (n < 2)
                return Array.Empty<double>();

            var r = new double[n - 1];

            for (int i = 1; i < n; i++)
            {
                double p0 = prices[i - 1];
                double p1 = prices[i];

                if (p0 <= 0 || p1 <= 0 || !double.IsFinite(p0) || !double.IsFinite(p1))
                {
                    // Neutralny placeholder – nie generujemy NaN.
                    r[i - 1] = 0.0;
                }
                else
                {
                    r[i - 1] = Math.Log(p1 / p0);
                }
            }

            return r;
        }

        // ===== KORELACJA PEARSONA =====

        /// <summary>
        /// Korelacja Pearsona dwóch wektorów (przyciętych do wspólnej długości).
        /// Zwraca NaN, gdy mniej niż 3 obserwacje lub wynik nie jest skończony.
        /// </summary>
        public static double ComputePearsonCorrelation(double[] a, double[] b)
        {
            if (a == null || b == null)
                return double.NaN;

            int n = Math.Min(a.Length, b.Length);
            if (n < 3)
                return double.NaN;

            var aa = a.AsSpan(0, n).ToArray();
            var bb = b.AsSpan(0, n).ToArray();

            double r = Correlation.Pearson(aa, bb);
            return double.IsFinite(r) ? r : double.NaN;
        }

        // ===== Nasycenie wartości (symetryczny clamp) =====

        /// <summary>
        /// Symetryczny clamp: ogranicza wartość do przedziału [-|limit|, +|limit|].
        /// Dla NaN/Infinity zwraca NaN.
        /// </summary>
        public static double ClampSymmetric(double value, double limit)
        {
            if (!double.IsFinite(value))
                return double.NaN;

            double l = Math.Abs(limit);
            if (value > l) return l;
            if (value < -l) return -l;
            return value;
        }

        // ===== Rolling VWAP =====

        /// <summary>
        /// Rolling VWAP z ruchomym oknem 'window' barów na bazie (H+L+C)/3 i wolumenu.
        /// Zwraca serię tej samej długości; NaN dla próbek przed wypełnieniem okna.
        /// lastVwap to ostatnia wartość serii.
        /// Zakłada, że quotes są posortowane rosnąco po Date.
        /// </summary>
        public static (double[] vwapSeries, double lastVwap) ComputeRollingVwap(
            Quote[] quotes,
            int window)
        {
            if (quotes == null || quotes.Length == 0 || window <= 1)
                return (Array.Empty<double>(), double.NaN);

            int n = quotes.Length;
            var vwap = new double[n];
            var tp = new double[n];
            var vol = new double[n];

            for (int i = 0; i < n; i++)
            {
                tp[i] = (double)((quotes[i].High + quotes[i].Low + quotes[i].Close) / 3m);
                vol[i] = (double)quotes[i].Volume;
            }

            double sumPv = 0.0;
            double sumV = 0.0;

            var qPv = new Queue<double>(window);
            var qV = new Queue<double>(window);

            for (int i = 0; i < n; i++)
            {
                double pv = tp[i] * vol[i];

                sumPv += pv;
                sumV += vol[i];

                qPv.Enqueue(pv);
                qV.Enqueue(vol[i]);

                if (qPv.Count > window)
                {
                    sumPv -= qPv.Dequeue();
                    sumV -= qV.Dequeue();
                }

                if (qPv.Count == window && sumV > 0.0)
                {
                    vwap[i] = sumPv / sumV;
                }
                else
                {
                    vwap[i] = double.NaN;
                }
            }

            double last = vwap.Length > 0 ? vwap[^1] : double.NaN;
            return (vwap, last);
        }

        // ===== Zwykły clamp =====

        /// <summary>
        /// Zwykły clamp do przedziału [min, max]. Dla wartości spoza zwraca min/max.
        /// Rzuca wyjątek, jeśli min &gt; max.
        /// </summary>
        public static double ClampToRange(double value, double min, double max)
        {
            if (min > max)
                throw new ArgumentException("min cannot be greater than max", nameof(min));

            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // ===== Gładkie obcinanie outlierów tanh =====

        /// <summary>
        /// Gładkie obcięcie outlierów przy użyciu funkcji tanh dla z-score'ów.
        /// Zakładamy, że 'zScore' ~ N(0,1); 'k' ~ 2–3 określa próg wygładzenia.
        /// </summary>
        public static double SmoothTanhClip(double zScore, double k = 2.0)
        {
            if (k <= 0.0)
                return Math.Tanh(zScore); // awaryjnie bez skalowania

            return Math.Tanh(zScore / k);
        }

        // ===== Percentyl =====

        /// <summary>
        /// Percentyl (0–100) z liniową interpolacją pomiędzy sąsiednimi wartościami.
        /// Zwraca NaN dla pustej tablicy.
        /// </summary>
        public static double ComputePercentile(double[] values, double percentile0To100)
        {
            if (values == null || values.Length == 0)
                return double.NaN;

            double pClamped = ClampToRange(percentile0To100, 0.0, 100.0) / 100.0;
            var arr = values.OrderBy(v => v).ToArray();

            double idx = (arr.Length - 1) * pClamped;
            int i = (int)Math.Floor(idx);
            double frac = idx - i;

            if (i + 1 < arr.Length)
            {
                // Liniowa interpolacja pomiędzy arr[i] i arr[i+1]
                return arr[i] * (1.0 - frac) + arr[i + 1] * frac;
            }

            return arr[^1];
        }

        // ===== Znak z martwą strefą =====

        /// <summary>
        /// Znak z martwą strefą eps:
        /// 1 gdy x &gt; eps, -1 gdy x &lt; -eps, 0 gdy |x| ≤ eps.
        /// </summary>
        public static int SignWithDeadZone(double x, double eps)
        {
            if (eps < 0)
                eps = -eps;

            if (x > eps) return 1;
            if (x < -eps) return -1;
            return 0;
        }
    }
}

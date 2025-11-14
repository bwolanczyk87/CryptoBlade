using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    /// <summary>
    /// Helpery VWAP/D-VWAP dla Sigmy:
    /// - dzienny anchored VWAP (D-VWAP) z kotwicą sesji,
    /// - fallback na rolling VWAP,
    /// - Z-score względem VWAP,
    /// - HAC/Newey–West t-stat nachylenia D-VWAP.
    /// Wszystkie funkcje są czyste (pure) i nie wykonują I/O.
    /// </summary>
    public static class VwapHelpers
    {
        /// <summary>
        /// Liczy serię D-VWAP na bazie 1-minutowych barów od kotwicy sesji (anchorUtc).
        ///
        /// Logika:
        /// 1) Próbuje policzyć anchored D-VWAP (od anchorUtc do końca dnia), jeśli
        ///    intraday.Length >= minIntradayBars.
        /// 2) Jeśli anchored jest nieużywalny (brak danych / niefinite last),
        ///    spada do fallbacku: rolling VWAP z oknem rollingWindow (min. 10 barów).
        ///
        /// Zwraca:
        ///  - vwapSeries  – cała seria VWAP (anchored albo rolling),
        ///  - lastVwap    – ostatnią wartość VWAP,
        ///  - tpStdDay    – odchylenie std typowej ceny (H+L+C)/3 dla sesji lub
        ///                  ostatniego okna (rolling), używane do Z-score,
        ///  - source      – źródło VWAP (D-VWAP vs rolling).
        ///
        /// Przy braku sensownych danych zwraca:
        ///  (Array.Empty, NaN, NaN, VwapSource.None).
        /// </summary>
        public static (double[] vwapSeries, double lastVwap, double tpStdDay, VwapSource source)
            ComputeAnchoredDailyVwapSeries(
                Quote[] q1m,
                DateTime anchorUtc,
                int minIntradayBars = 20,
                int rollingWindow = 60)
        {
            // 0) Brak danych
            if (q1m == null || q1m.Length == 0)
                return (Array.Empty<double>(), double.NaN, double.NaN, VwapSource.None);

            // 1) Intraday od kotwicy sesji
            var intraday = q1m.Where(q => q.Date >= anchorUtc).ToArray();

            // Helper: std dev typowej ceny dla podanego zbioru barów
            static double TpStdFromQuotes(Quote[] qs)
            {
                if (qs == null || qs.Length < 2)
                    return double.NaN;

                var tp = qs
                    .Select(z => (double)((z.High + z.Low + z.Close) / 3m))
                    .ToArray();

                return StatisticsHelpers.ComputeSampleStdDev(tp);
            }

            // --- 1) Anchored daily VWAP (priorytet)
            if (intraday.Length >= minIntradayBars)
            {
                int n = intraday.Length;
                var tp = new double[n];
                var vol = new double[n];

                for (int i = 0; i < n; i++)
                {
                    tp[i] = (double)((intraday[i].High + intraday[i].Low + intraday[i].Close) / 3m);
                    vol[i] = (double)intraday[i].Volume;
                }

                var vwap = new double[n];
                double accPv = 0.0;
                double accV = 0.0;

                for (int i = 0; i < n; i++)
                {
                    accPv += tp[i] * vol[i];
                    accV += vol[i];
                    vwap[i] = (accV > 0.0) ? (accPv / accV) : double.NaN;
                }

                double last = vwap[^1];

                // Jeśli ostatni VWAP jest sensowny – używamy D-VWAP
                if (double.IsFinite(last))
                {
                    double tpStdDay = TpStdFromQuotes(intraday);
                    return (vwap, last, tpStdDay, VwapSource.DailyAnchored);
                }
                // w przeciwnym razie lecimy do fallbacku (rolling VWAP)
            }

            // --- 2) Fallback: Rolling VWAP (np. 60×1m)
            var (rvwap, rlast) = StatisticsHelpers.ComputeRollingVwap(
                q1m,
                Math.Max(10, rollingWindow));

            if (rlast > 0.0 && rvwap.Length > 0)
            {
                // std TP liczymy z ostatniego okna rollingWindow (tak jak w oryginale),
                // żeby Z-score miał lokalne odniesienie.
                var tail = q1m
                    .TakeLast(Math.Min(rollingWindow, q1m.Length))
                    .ToArray();

                double tpStd = TpStdFromQuotes(tail);
                return (rvwap, rlast, tpStd, VwapSource.Rolling);
            }

            // --- 3) Nic sensownego się nie udało policzyć
            return (Array.Empty<double>(), double.NaN, double.NaN, VwapSource.None);
        }

        /// <summary>
        /// Z-score do VWAP:
        /// z = (LastPrice − lastVwap) / tpStdDay,
        /// następnie symetryczny clamp do [-10, +10].
        ///
        /// Zwraca NaN, jeśli:
        /// - lastPrice <= 0 lub null,
        /// - lastVwap nie jest skończony,
        /// - tpStdDay <= 0 lub NaN.
        /// </summary>
        public static double ComputeZDvwap(decimal? lastPrice, double lastVwap, double tpStdDay)
        {
            if (!(lastPrice > 0m) || !double.IsFinite(lastVwap) || !(tpStdDay > 0.0))
                return double.NaN;

            double z = ((double)lastPrice.Value - lastVwap) / tpStdDay;
            return StatisticsHelpers.ClampSymmetric(z, 10.0);
        }

        /// <summary>
        /// HAC/Newey–West t-statystyka nachylenia (slope) dla ostatnich lastK
        /// wartości serii (np. D-VWAP).
        ///
        /// Kroki:
        /// 1) Bierzemy ogon długości lastK (lub mniej, jeśli seria krótsza).
        /// 2) Filtrujemy NaN, budując punkty (x, y) z równym krokiem w czasie.
        /// 3) Estymujemy prostą y = β0 + β1 x metodą OLS.
        /// 4) Liczymy wariancję β1 z korekcją HAC (Bartlett kernel, bandwidth L ~ 4*(n/100)^(2/9)).
        /// 5) Zwracamy t = β1 / se(β1).
        ///
        /// Zwraca NaN, jeśli:
        /// - po filtracji mamy mniej niż 10 punktów,
        /// - występuje degeneracja numeryczna (det ≈ 0, varSlope ≤ 0).
        /// </summary>
        public static double ComputeSlopeTstatHAC(double[] series, int lastK)
        {
            if (series == null)
                return double.NaN;

            int nAll = series.Length;
            int k = Math.Min(lastK, nAll);
            if (k < 10)
                return double.NaN;

            var y = series[^k..];

            // Budujemy punkty (x, y) tylko dla skończonych wartości
            var xs = new List<double>(k);
            var ys = new List<double>(k);

            for (int i = 0; i < k; i++)
            {
                double yi = y[i];
                if (double.IsFinite(yi))
                {
                    xs.Add(i);            // równy krok czasowy
                    ys.Add(yi);
                }
            }

            int n = ys.Count;
            if (n < 10)
                return double.NaN;

            // OLS: beta = (X'X)^-1 X'y, gdzie X = [1, x]
            double sx = 0.0, sy = 0.0, sxx = 0.0, sxy = 0.0;
            for (int i = 0; i < n; i++)
            {
                double x = xs[i];
                double yy = ys[i];

                sx += x;
                sy += yy;
                sxx += x * x;
                sxy += x * yy;
            }

            double denom = n * sxx - sx * sx;
            if (Math.Abs(denom) < 1e-12)
                return double.NaN;

            double beta1 = (n * sxy - sx * sy) / denom;   // slope
            double beta0 = (sy - beta1 * sx) / n;         // intercept

            // Residua
            var e = new double[n];
            for (int i = 0; i < n; i++)
                e[i] = ys[i] - (beta0 + beta1 * xs[i]);

            // inv(X'X) dla 2x2
            double a = n, b = sx, c = sx, d = sxx;
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12)
                return double.NaN;

            double inv00 = d / det;
            double inv01 = -b / det;
            double inv10 = -c / det;
            double inv11 = a / det;

            // --- HAC / Newey–West: bandwidth L ~ 4 * (n / 100)^(2/9)
            int L = (int)Math.Floor(4.0 * Math.Pow(n / 100.0, 2.0 / 9.0));
            if (L < 1)
                L = 1;

            // Gamma0 (część bez opóźnień)
            double S00 = 0.0, S01 = 0.0, S11 = 0.0;
            for (int i = 0; i < n; i++)
            {
                double ei = e[i];
                double xi = xs[i];
                double e2 = ei * ei;

                S00 += e2;
                S01 += e2 * xi;
                S11 += e2 * xi * xi;
            }

            // Składniki z opóźnieniami (Bartlett kernel)
            for (int lag = 1; lag <= L; lag++)
            {
                double w = 1.0 - (double)lag / (L + 1.0);

                for (int i = lag; i < n; i++)
                {
                    int j = i - lag;

                    double ei = e[i];
                    double ej = e[j];
                    double xi = xs[i];
                    double xj = xs[j];

                    double cov = w * ei * ej;

                    // Symetria: +lag i -lag → mnożnik 2 dla elementów diagonalnych
                    S00 += 2.0 * cov;
                    S01 += cov * (xi + xj);
                    S11 += 2.0 * cov * (xi * xj);
                }
            }

            // Var(beta) ≈ (X'X)^-1 * S * (X'X)^-1; interesuje nas [1,1] (slope)
            double M00 = inv00 * S00 + inv01 * S01;
            double M01 = inv00 * S01 + inv01 * S11;
            double M10 = inv10 * S00 + inv11 * S01;
            double M11 = inv10 * S01 + inv11 * S11;

            double varSlope = M10 * inv01 + M11 * inv11;
            if (!(varSlope > 0.0))
                return double.NaN;

            double seSlope = Math.Sqrt(varSlope);
            if (!(seSlope > 0.0))
                return double.NaN;

            return beta1 / seSlope;
        }
    }

    /// <summary>
    /// Źródło VWAP użyte przy konstrukcji cechy (D-VWAP vs rolling).
    /// </summary>
    public enum VwapSource
    {
        None = 0,
        DailyAnchored = 1,
        Rolling = 2
    }
}

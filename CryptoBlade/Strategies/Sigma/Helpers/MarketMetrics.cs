using CryptoBlade.Models;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    /// <summary>
    /// Kalkulatory metryk rynkowych dla Sigmy:
    /// - ΔOI%,
    /// - funding snapshot,
    /// - basis%,
    /// - korelacje z log-stop zwrotu.
    /// Wszystkie funkcje są czyste (pure functions) i nie wykonują I/O.
    /// </summary>
    public static class MarketMetrics
    {
        // ===== OPEN INTEREST Δ% =====

        /// <summary>
        /// Liczy procentową zmianę open interest między dwiema wartościami
        /// (last - previous) / previous * 100.
        /// Zwraca NaN, jeśli previous &lt;= 0 lub wynik nie jest skończony.
        /// </summary>
        public static double ComputeOpenInterestDeltaPct(decimal previous, decimal last)
        {
            if (previous <= 0m)
                return double.NaN;

            var pct = (double)((last - previous) / previous * 100m);
            return double.IsFinite(pct) ? pct : double.NaN;
        }

        /// <summary>
        /// Liczy ΔOI% z serii punktów open interest (np. 1h),
        /// używając dwóch ostatnich próbek.
        /// </summary>
        public static double ComputeOpenInterestDeltaPctFromSeries(
            IReadOnlyList<decimal> openInterestSeries)
        {
            if (openInterestSeries == null || openInterestSeries.Count < 2)
                return double.NaN;

            var previous = openInterestSeries[^2];
            var last = openInterestSeries[^1];
            return ComputeOpenInterestDeltaPct(previous, last);
        }

        /// <summary>
        /// Liczy ΔOI% z serii punktów open interest (np. modeli OpenInterestPoint),
        /// przy użyciu selektora wybierającego wartość OI (np. p =&gt; p.OpenInterestUsd).
        /// </summary>
        public static double ComputeOpenInterestDeltaPctFromPoints<TPoint>(
            IReadOnlyList<TPoint> points,
            Func<TPoint, decimal> selector)
        {
            if (points == null || points.Count < 2 || selector == null)
                return double.NaN;

            var previous = selector(points[^2]);
            var last = selector(points[^1]);
            return ComputeOpenInterestDeltaPct(previous, last);
        }

        // ===== FUNDING SNAPSHOT =====

        /// <summary>
        /// Buduje snapshot fundingu:
        /// - Predicted: z tickera (FundingRate * 100, NextFundingTime jako Utc),
        /// - LastSettled: z historii fundingu (ostatnia próbka z ostatnich 24h, Rate * 100).
        /// Jeśli brakuje danych, odpowiednie pola będą null.
        /// </summary>
        public static (FundingRate? Predicted, FundingRate? LastSettled) ComputeFundingSnapshot(
            Ticker? ticker,
            IEnumerable<FundingRate>? recentFundingRatesUtc)
        {
            FundingRate? predicted = null;
            FundingRate? lastSettled = null;

            // 1) Predicted: UI „current funding rate” + czas kolejnego cyklu z tickera
            if (ticker != null && ticker.FundingRate.HasValue && ticker.NextFundingTime.HasValue)
            {
                // Normalizujemy do % (np. 0.0001 → 0.01)
                var pct = ticker.FundingRate.Value * 100m;

                predicted = new FundingRate
                {
                    Time = DateTime.SpecifyKind(ticker.NextFundingTime.Value, DateTimeKind.Utc),
                    Rate = pct
                };
            }

            // 2) Last settled z historii (przekazanej już jako recentFundingRatesUtc)
            if (recentFundingRatesUtc != null)
            {
                var arr = recentFundingRatesUtc
                    .Where(r => r != null)
                    .OrderBy(r => r.Time)
                    .ToArray();

                if (arr.Length > 0)
                {
                    var last = arr[^1];
                    lastSettled = new FundingRate
                    {
                        Time = DateTime.SpecifyKind(last.Time, DateTimeKind.Utc),
                        Rate = last.Rate * 100m
                    };
                }
            }

            return (predicted, lastSettled);
        }

        // ===== BASIS% =====

        /// <summary>
        /// Liczy basis w % jako (mark - index) / index * 100.
        /// Zwraca NaN, gdy indexPrice <= 0 lub markPrice <= 0 albo wynik nie jest skończony.
        /// </summary>
        public static double ComputeBasisPct(decimal markPrice, decimal indexPrice)
        {
            if (indexPrice <= 0m || markPrice <= 0m)
                return double.NaN;

            var pct = (double)((markPrice - indexPrice) / indexPrice * 100m);
            return double.IsFinite(pct) ? pct : double.NaN;
        }

        /// <summary>
        /// Wygodny wrapper: basis% z tickera (MarkPrice, IndexPrice).
        /// </summary>
        public static double ComputeBasisPctFromTicker(Ticker? ticker)
        {
            if (ticker == null)
                return double.NaN;

            return ComputeBasisPct(ticker.MarkPrice, ticker.IndexPrice);
        }

        // ===== KORELACJE (log-returny) =====

        /// <summary>
        /// Liczy korelację Pearsona między log-stopami zwrotu dwóch serii cen
        /// (np. symbol vs BTC) oraz zwraca ostatni log-return serii Y.
        ///
        /// Wejścia:
        /// - xPrices: ceny zamknięcia serii X,
        /// - yPrices: ceny zamknięcia serii Y,
        /// - window: maksymalna długość okna (liczba log-returnów) użyta do korelacji.
        ///
        /// Zwraca:
        /// - correlation: korelacja Pearsona log-returnów X i Y w ostatnim oknie,
        /// - lastYLogReturn: ostatni log-return serii Y.
        ///
        /// Zwraca (NaN, NaN), gdy danych jest zbyt mało lub wynik jest niepoliczalny.
        /// </summary>
        public static (double correlation, double lastYLogReturn) ComputeRollingLogReturnCorrelationAndLastY(
            double[] xPrices,
            double[] yPrices,
            int window)
        {
            if (xPrices == null || yPrices == null)
                return (double.NaN, double.NaN);

            if (window <= 0)
                return (double.NaN, double.NaN);

            // 1) log-returny
            var rX = StatisticsHelpers.ComputeLogReturns(xPrices);
            var rY = StatisticsHelpers.ComputeLogReturns(yPrices);

            int m = Math.Min(rX.Length, rY.Length);
            if (m < 3)
                return (double.NaN, double.NaN);

            int win = Math.Min(window, m);
            if (win < 3)
                return (double.NaN, double.NaN);

            // 2) wycinek z końca
            var segX = new double[win];
            var segY = new double[win];
            Array.Copy(rX, rX.Length - win, segX, 0, win);
            Array.Copy(rY, rY.Length - win, segY, 0, win);

            // 3) korelacja
            var corr = StatisticsHelpers.ComputePearsonCorrelation(segX, segY);
            var lastY = rY[^1];

            if (!double.IsFinite(corr) || !double.IsFinite(lastY))
                return (double.NaN, double.NaN);

            return (corr, lastY);
        }
    }
}

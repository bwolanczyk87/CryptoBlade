using CryptoBlade.Models;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    /// <summary>
    /// Kalkulatory metryk rynkowych dla Sigmy:
    /// - ΔOI,
    /// - funding snapshot,
    /// - basis%,
    /// - korelacje z log-stop zwrotu,
    /// - klasyczne wskaźniki (ADX, ATR, Bollinger bandwidth),
    /// - mikrostruktura (spread, ΔCVD, dystans do klastrów likwidacji),
    /// - prosta autokorelacja lag-1 z shrinkiem.
    ///
    /// Wszystkie funkcje są czyste (pure functions) i nie wykonują I/O.
    /// Zasada: brak danych / wynik niepoliczalny => double.NaN.
    /// </summary>
    public static class MarketMetrics
    {
        // =====================================================================
        //  OPEN INTEREST Δ%
        // =====================================================================

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

        public static double ComputeOpenInterestDeltaPctFromSeries<T>(
            IReadOnlyList<T> points,
            TimeSpan horizon,
            Func<T, DateTime> getTime,
            Func<T, decimal> getOi)
        {
            if (points == null || points.Count < 2)
                return double.NaN;

            // zakładam, że już są posortowane rosnąco po czasie;
            // jeśli nie, możesz tu dodać OrderBy.
            var last = points[^1];
            var lastTime = getTime(last);
            var lastOi = getOi(last);

            if (lastOi <= 0)
                return double.NaN;

            var targetTime = lastTime - horizon;

            // szukamy punktu jak najbliżej targetTime, ale <= targetTime
            T? prev = default;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                var pt = points[i];
                var t = getTime(pt);

                if (t <= targetTime)
                {
                    prev = pt;
                    break;
                }
            }

            // jeśli nie znaleźliśmy punktu sprzed 1h, to weź najstarszy,
            // żeby nie robić głupich delta z jakiegoś śmiesznego kawałka
            if (prev is null)
                prev = points[0];

            var prevOi = getOi(prev);

            if (prevOi <= 0)
                return double.NaN;

            var delta = (double)((lastOi - prevOi) / prevOi * 100m);

            return double.IsFinite(delta) ? delta : double.NaN;
        }


        /// <summary>
        /// Liczy ΔOI% z serii wartości (np. 1h open interest),
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

        // =====================================================================
        //  FUNDING SNAPSHOT
        // =====================================================================

        /// <summary>
        /// Buduje snapshot fundingu:
        /// - Predicted: z tickera (FundingRate * 100, NextFundingTime jako Utc),
        /// - LastSettled: z historii fundingu (ostatnia próbka, Rate * 100).
        /// Jeśli brakuje danych, odpowiednie pola będą null.
        /// </summary>
        public static (FundingRate? Predicted, FundingRate? LastSettled) ComputeFundingSnapshot(
            Ticker? ticker,
            IEnumerable<FundingRate>? recentFundingRatesUtc)
        {
            FundingRate? predicted = null;
            FundingRate? lastSettled = null;

            // 1) Predicted: aktualny funding + czas kolejnego cyklu z tickera
            if (ticker != null && ticker.FundingRate.HasValue && ticker.NextFundingTime.HasValue)
            {
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

        // =====================================================================
        //  BASIS %
        // =====================================================================

        /// <summary>
        /// Liczy basis w % jako (mark - index) / index * 100.
        /// Zwraca NaN, gdy indexPrice &lt;= 0 lub markPrice &lt;= 0 albo wynik nie jest skończony.
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

        // =====================================================================
        //  KORELACJA (log-returny)
        // =====================================================================

        /// <summary>
        /// Liczy korelację Pearsona pomiędzy log-stopami zwrotu dwóch serii cen
        /// (X i Y) oraz zwraca ostatni log-zwrot serii Y.
        /// 
        /// Wejście:
        /// - obie serie to zbiory świec z TEGO SAMEGO interwału czasowego
        ///   (np. 15m vs 15m), możliwie dobrze wyrównane w czasie,
        /// - do obliczeń używane są tylko ceny Close.
        /// 
        /// Kroki:
        /// 1) z obu serii wyznaczane są szeregi log-stop zwrotu,
        /// 2) wybierane jest ostatnie okno długości 'window' (lub krótsze,
        ///    jeśli danych jest mniej),
        /// 3) liczona jest korelacja Pearsona między log-zwrotami X i Y
        ///    w tym oknie,
        /// 4) zwracany jest również ostatni log-zwrot serii Y.
        /// 
        /// Zwraca (NaN, NaN), gdy:
        /// - którakolwiek z serii ma za mało punktów (mniej niż 3 log-zwroty),
        /// - korelacja nie jest policzalna (denominator ~= 0),
        /// - ostatni log-zwrot Y nie jest skończony.
        public static (double correlation, double lastYLogReturn)
            ComputeRollingLogReturnCorrelationAndLastYFromQuotes(
                IReadOnlyList<Quote> xQuotes,
                IReadOnlyList<Quote> yQuotes,
                int window)
        {
            if (xQuotes == null || yQuotes == null)
                return (double.NaN, double.NaN);

            int n = Math.Min(xQuotes.Count, yQuotes.Count);
            if (n < 2)
                return (double.NaN, double.NaN);

            var x = new double[n];
            var y = new double[n];

            for (int i = 0; i < n; i++)
            {
                x[i] = (double)xQuotes[i].Close;
                y[i] = (double)yQuotes[i].Close;
            }

            return ComputeRollingLogReturnCorrelationAndLastY(x, y, window);
        }

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
        public static (double correlation, double lastYLogReturn)
            ComputeRollingLogReturnCorrelationAndLastY(
                double[] xPrices,
                double[] yPrices,
                int window)
        {
            if (xPrices == null || yPrices == null)
                return (double.NaN, double.NaN);

            if (window <= 0)
                return (double.NaN, double.NaN);

            var rX = StatisticsHelpers.ComputeLogReturns(xPrices);
            var rY = StatisticsHelpers.ComputeLogReturns(yPrices);

            int m = Math.Min(rX.Length, rY.Length);
            if (m < 3)
                return (double.NaN, double.NaN);

            int win = Math.Min(window, m);
            if (win < 3)
                return (double.NaN, double.NaN);

            var segX = new double[win];
            var segY = new double[win];
            Array.Copy(rX, rX.Length - win, segX, 0, win);
            Array.Copy(rY, rY.Length - win, segY, 0, win);

            var corr = StatisticsHelpers.ComputePearsonCorrelation(segX, segY);
            var lastY = rY[^1];

            if (!double.IsFinite(corr) || !double.IsFinite(lastY))
                return (double.NaN, double.NaN);

            return (corr, lastY);
        }

        /// <summary>
        /// Heurystyka do wykrywania sytuacji, w której bias naszego instrumentu (X)
        /// jest przeciwny do biasu instrumentu referencyjnego (Y), przy uwzględnieniu:
        /// - siły korelacji (corr),
        /// - kierunku ostatniego log-zwrotu X i Y,
        /// - kierunku nachylenia DVWAP (zSlopeDvwap) jako fallbacku, gdy zwrot X ≈ 0.
        /// 
        /// Zasada:
        /// - gdy |corr| < corrThr → zwraca false (brak silnej zależności, nie blokujemy),
        /// - gdy corr ≥ corrThr (silna dodatnia korelacja) → opposite = różne znaki biasu X i Y,
        /// - gdy corr ≤ -corrThr (silna ujemna korelacja) → opposite = te same znaki biasu X i Y,
        /// - gdy któryś z kierunków jest nieokreślony (|ret| < epsRet i |zSlopeDvwap| < zMin) → false.
        /// 
        public static bool ComputeBiasOpposite(
            double corr,
            double lastXLogReturn,
            double lastYLogReturn,
            double zSlopeDvwap,
            double epsRet = 0.0005,  // 0.05%
            double zMin = 0.8,
            double corrThr = 0.85)
        {
            if (!double.IsFinite(corr))
                return false;

            // kierunek Y (BTC)
            int sY = StatisticsHelpers.SignWithDeadZone(lastYLogReturn, epsRet);

            // kierunek X (symbol, np. ETH)
            int sX = StatisticsHelpers.SignWithDeadZone(lastXLogReturn, epsRet);

            // Fallback: jeśli X ≈ 0, użyj znaku nachylenia DVWAP, ale tylko przy istotnym t-stat
            if (sX == 0 && double.IsFinite(zSlopeDvwap) && Math.Abs(zSlopeDvwap) >= zMin)
                sX = zSlopeDvwap > 0 ? 1 : -1;

            // brak kierunku → brak blokady
            if (sX == 0 || sY == 0)
                return false;

            // dodatnia silna korelacja: opposite = różne znaki
            if (corr >= corrThr)
                return sX != sY;

            // ujemna silna korelacja: opposite = te same znaki (dziwny współruch)
            if (corr <= -corrThr)
                return sX == sY;

            // słaba korelacja => nie ingerujemy
            return false;
        }

        // =====================================================================
        //  TREND / ZMIENNOŚĆ (ADX, ATR, BBW, AUTOKORELACJA)
        // =====================================================================

        /// <summary>
        /// Zwraca ostatnią dostępną wartość ADX dla podanej serii świec i okresu.
        /// 
        /// Wejście:
        /// - quotes: dowolny interwał (np. 1h, 4h),
        /// - lookback: okres ADX (domyślnie 14).
        ///
        /// Zwraca:
        /// - ostatnie ADX w [0,100] lub NaN, gdy niepoliczalne.
        /// </summary>
        public static double ComputeAdxLast(
            IReadOnlyList<Quote> quotes,
            int lookback = 14)
        {
            if (quotes == null || quotes.Count < 2 || lookback <= 1)
                return double.NaN;

            var adxSeries = quotes.GetAdx(lookback);
            var last = adxSeries.LastOrDefault(r => r != null && r.Adx.HasValue);

            return last?.Adx ?? double.NaN;
        }

        /// <summary>
        /// Liczy ATR w jednostkach ceny i jako procent względem ceny referencyjnej:
        /// atrPct = ATR / refPrice * 100.
        ///
        /// Wejście:
        /// - quotes: dowolny interwał świec (np. 1h),
        /// - refPrice: cena referencyjna (np. ostatni close lub last price),
        /// - lookback: okres ATR (domyślnie 14).
        ///
        /// Zwraca:
        /// - atrPct: ATR w % (NaN, jeśli brak danych / refPrice ≤ 0),
        /// - atrAbs: ATR w punktach (może być 0, jeśli biblioteka nie policzyła).
        /// </summary>
        public static (double atrPct, double atrAbs) ComputeAtrPercentAndAbs(
            IReadOnlyList<Quote> quotes,
            double refPrice,
            int lookback = 14)
        {
            double atrAbs = double.NaN;

            if (quotes != null && quotes.Count > 0 && lookback > 1)
            {
                var atrRow = quotes.GetAtr(lookback).LastOrDefault();
                if (atrRow != null && atrRow.Atr.HasValue)
                    atrAbs = (double)atrRow.Atr.Value;
                else
                    atrAbs = 0.0;
            }

            // refPrice musi być dodatni i skończony, ATR dodatni żeby liczyć %.
            if (!double.IsFinite(atrAbs) || atrAbs <= 0.0 ||
                !double.IsFinite(refPrice) || !(refPrice > 0.0))
            {
                return (double.NaN, atrAbs);
            }

            double atrPct = (atrAbs / refPrice) * 100.0;
            return (atrPct, atrAbs);
        }

        /// <summary>
        /// Liczy:
        /// - surową szerokość Bollinger Bandwidth w %: (Upper-Lower)/SMA * 100,
        /// - percentyl (mid-rank) ostatniej szerokości względem całej historii.
        ///
        /// Wejście:
        /// - quotes: dowolny interwał (np. 15m),
        /// - period: okres Bollinger Bands,
        /// - stdDevMultiplier: mnożnik odchylenia standardowego.
        ///
        /// Zwraca:
        /// - percentile: percentyl w [0,100] dla ostatniej szerokości,
        /// - lastBandwidthPct: ostatnia szerokość w %.
        /// </summary>
        public static (double percentile, double lastBandwidthPct)
            ComputeBollingerBandwidthPercentile(
                IReadOnlyList<Quote> quotes,
                int period,
                double stdDevMultiplier)
        {
            if (quotes == null || quotes.Count < period + 2)
                return (double.NaN, double.NaN);

            var bands = quotes
                .GetBollingerBands(period, stdDevMultiplier)
                .ToArray();

            var widths = bands
                .Where(b => b != null &&
                            b.Width.HasValue &&
                            b.Sma.HasValue &&
                            b.Sma.Value != 0)
                .Select(b => (double)((b.UpperBand!.Value - b.LowerBand!.Value) / b.Sma!.Value * 100))
                .ToArray();

            int n = widths.Length;
            if (n == 0)
                return (double.NaN, double.NaN);

            double last = widths[^1];

            // Percentyl mid-rank (lepszy niż czysty "ranking", mniej wrażliwy na duplikaty).
            int lt = 0, eq = 0;
            for (int i = 0; i < n; i++)
            {
                if (widths[i] < last) lt++;
                else if (Math.Abs(widths[i] - last) < 1e-12) eq++;
            }

            double percentile = (lt + 0.5 * eq) * 100.0 / n;
            return (percentile, last);
        }

        /// <summary>
        /// Sprawdza, czy Bollinger Bandwidth (w %) rośnie względem stanu sprzed 'back' barów.
        /// 
        /// Wejście:
        /// - quotes: dowolny interwał (np. 15m),
        /// - period: okres Bollinger Bands,
        /// - stdDevMultiplier: mnożnik odchylenia,
        /// - back: ile barów wstecz porównujemy.
        ///
        /// Zwraca true, gdy:
        /// - obie szerokości są policzalne i,
        /// - ostatnia szerokość &gt; szerokość sprzed 'back' barów.
        /// </summary>
        public static bool IsBollingerBandwidthExpanding(
            IReadOnlyList<Quote> quotes,
            int period,
            double stdDevMultiplier,
            int back = 2)
        {
            if (quotes == null || quotes.Count < period + back + 1)
                return false;

            var bands = quotes
                .GetBollingerBands(period, stdDevMultiplier)
                .ToArray();

            double Bw(int idx)
            {
                var b = bands[idx];
                if (b == null ||
                    !b.UpperBand.HasValue ||
                    !b.LowerBand.HasValue ||
                    !b.Sma.HasValue ||
                    b.Sma.Value == 0)
                {
                    return double.NaN;
                }

                return (double)((b.UpperBand.Value - b.LowerBand.Value) / b.Sma.Value * 100);
            }

            int lastIdx = bands.Length - 1;
            int prevIdx = lastIdx - back;

            var now = Bw(lastIdx);
            var was = Bw(prevIdx);

            if (!double.IsFinite(now) || !double.IsFinite(was))
                return false;

            return now > was;
        }

        /// <summary>
        /// Autokorelacja lag-1 log-stóp zwrotu ceny zamknięcia, ze shrinkiem
        /// lambda = m / (m + shrinkKappa) oraz clampem do [-1,1].
        ///
        /// Wejście:
        /// - quotes: seria świec (dowolny interwał),
        /// - shrinkKappa: parametr shrinku (domyślnie 8).
        ///
        /// Zwraca:
        /// - rho w [-1,1] lub NaN, gdy niepoliczalne.
        /// </summary>
        public static double ComputeLag1AutoCorrelationShrunkFromQuotes(
            IReadOnlyList<Quote> quotes,
            int shrinkKappa = 8)
        {
            if (quotes == null || quotes.Count < 3)
                return double.NaN;

            int n = quotes.Count;
            var px = new double[n];
            for (int i = 0; i < n; i++)
                px[i] = (double)quotes[i].Close;

            var r = StatisticsHelpers.ComputeLogReturns(px);
            int m = r.Length;
            if (m < 3)
                return double.NaN;

            double mean = StatisticsHelpers.ComputeMean(r);
            double num = 0.0, den = 0.0;

            for (int i = 1; i < m; i++)
                num += (r[i] - mean) * (r[i - 1] - mean);

            for (int i = 0; i < m; i++)
                den += (r[i] - mean) * (r[i] - mean);

            if (!(den > 0.0))
                return double.NaN;

            double rhoHat = num / den;

            double lambda = (double)m / (m + Math.Max(1, shrinkKappa));
            double rho = lambda * rhoHat;

            if (rho > 1.0) rho = 1.0;
            if (rho < -1.0) rho = -1.0;

            return rho;
        }

        // =====================================================================
        //  MIKROSTRUKTURA: SPREAD, ΔCVD, LIQUIDATION CLUSTERS
        // =====================================================================

        /// <summary>
        /// Liczy spread w bps z tickera (BestBid/BestAsk).
        ///
        /// Zwraca NaN, jeżeli nie da się policzyć sensownej wartości.
        /// </summary>
        public static double ComputeSpreadBps(Ticker? ticker)
        {
            if (ticker != null &&
                ticker.BestBidPrice > 0m &&
                ticker.BestAskPrice > 0m)
            {
                var spr = ticker.BestAskPrice - ticker.BestBidPrice;
                var mid = (ticker.BestAskPrice + ticker.BestBidPrice) / 2m;

                if (mid > 0m)
                {
                    var bps = (double)((spr / mid) * 10_000m);
                    return double.IsFinite(bps) ? bps : double.NaN;
                }
            }

            return double.NaN;
        }

        /// <summary>
        /// Liczy skumulowaną deltę wolumenu takerów w zadanym oknie czasu
        /// (np. 5 minut) na podstawie public trades:
        /// - BUY => +qty,
        /// - SELL => -qty.
        ///
        /// Wejście:
        /// - trades: kolekcja trade'ów,
        /// - nowUtc: "teraz" (kotwica okna),
        /// - lookback: długość okna (np. TimeSpan.FromMinutes(5)).
        ///
        /// Zwraca:
        /// - signed volume delta (double) lub NaN, gdy brak danych w oknie.
        /// </summary>
        public static double ComputeSignedVolumeDelta(
            IReadOnlyCollection<PublicTrade>? trades,
            DateTime fromUtc,
            DateTime toUtc)
        {
            if (trades == null || trades.Count == 0 || fromUtc >= toUtc)
                return double.NaN;

            decimal sum = 0m;

            foreach (var t in trades)
            {
                if (t.Timestamp < fromUtc || t.Timestamp > toUtc)
                    continue;

                var signed = t.Side == OrderSide.Buy
                    ? t.Quantity
                    : -t.Quantity;

                sum += signed;
            }

            var v = (double)sum;
            return double.IsFinite(v) ? v : double.NaN;
        }


        /// <summary>
        /// Heurystyczna odległość (w %) do najbliższego "silnego" klastra likwidacji
        /// w pobliżu bieżącej ceny.
        ///
        /// Idea:
        /// 1) Bierzemy likwidacje z ostatniego "lookbackMinutes" (np. 20 minut).
        /// 2) Binujemy je względem price, z krokiem "binStepPct" (np. 0.10%).
        /// 3) Liczymy próg siły klastra jako max(percentyl pctl z rozkładu wolumenów,
        ///    minSharePct całkowitego wolumenu).
        /// 4) Wybieramy biny o wolumenie ≥ próg i ≠ 0 (czyli poza bieżącą ceną),
        ///    a następnie najbliższy taki bin względem ceny.
        ///
        /// Zwraca:
        /// - dystans w % do najbliższego dużego klastra likwidacji,
        ///   lub NaN, jeśli nie udało się policzyć.
        /// </summary>
        public static double ComputeDistanceToLiqClusterPct(
            IReadOnlyCollection<LiquidationEvent>? liqs,
            decimal lastPrice,
            DateTime nowUtc,
            double lookbackMinutes = 20.0,
            double binStepPct = 0.10,   // 0.10%
            double pctl = 75.0,
            double minSharePct = 5.0)   // 5% całkowitego wolumenu
        {
            if (liqs == null || liqs.Count == 0)
                return double.NaN;

            if (lastPrice <= 0m)
                return double.NaN;

            var thresholdTs = nowUtc - TimeSpan.FromMinutes(lookbackMinutes);

            var window = liqs
                .Where(e => e.Timestamp >= thresholdTs)
                .ToArray();

            if (window.Length == 0)
                return double.NaN;

            decimal step = lastPrice * (decimal)(binStepPct / 100.0); // np. 0.001m dla 0.10%
            if (step <= 0m)
                return double.NaN;

            var bins = new Dictionary<long, decimal>();

            foreach (var e in window)
            {
                // indeks bina względem lastPrice
                decimal rel = (e.Price - lastPrice) / step;
                long bin = (long)decimal.Round(rel, 0, MidpointRounding.AwayFromZero);

                bins.TryGetValue(bin, out var v);
                bins[bin] = v + e.Quantity;
            }

            if (bins.Count == 0)
                return double.NaN;

            var vols = bins
                .Values
                .Select(v => (double)v)
                .OrderBy(v => v)
                .ToArray();

            double p = StatisticsHelpers.ComputePercentile(vols, pctl);

            decimal total = bins.Values.Aggregate(0m, (acc, v) => acc + v);
            decimal minShare = total * (decimal)(minSharePct / 100.0);

            decimal thrDec = (decimal)Math.Max(p, (double)minShare);

            var strong = bins
                .Where(kv => kv.Value >= thrDec && kv.Key != 0)
                .Select(kv => kv.Key)
                .ToArray();

            if (strong.Length == 0)
            {
                var best = bins
                    .Where(kv => kv.Key != 0)
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => (long?)kv.Key)
                    .FirstOrDefault();

                if (!best.HasValue)
                    return double.NaN;

                strong = new[] { best.Value };
            }

            long nearest = strong
                .OrderBy(b => Math.Abs(b))
                .First();

            decimal distAbs = Math.Abs(nearest) * step;
            double distPct = (double)(distAbs / lastPrice * 100m);

            return (distPct > 0.0 && double.IsFinite(distPct)) ? distPct : double.NaN;
        }
    }
}

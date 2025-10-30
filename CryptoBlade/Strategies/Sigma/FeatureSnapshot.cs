using CryptoBlade.Models;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma
{
    internal sealed class FeatureSnapshot
    {
        // Pierwotne cechy
        public string Symbol { get; private set; } = "";
        public double Adx1h { get; private set; }
        public double AtrPct1h { get; private set; }
        public double ZDvwap { get; private set; }          // z-score odległości od D-VWAP
        public double ZSlopeDvwap { get; private set; }     // z-score nachylenia D-VWAP
        public double AutoCorr5m { get; private set; }      // autokorelacja 5m
        public double Bbw15mPct { get; private set; }       // percentyl BB width na 15m
        public bool HasInsideOrNr7 { get; private set; }    // flaga inside/NR7 na 15m/1h

        // Derywaty/flow
        public double OiDelta1hPct { get; private set; }    // ΔOI$/h (procentowo)
        public double Funding8h { get; private set; }       // funding (ostatni)
        public double BasisPct { get; private set; }        // contango/backwardation
        public double DeltaCvd5m { get; private set; }      // zmiana CVD/taker imbalance 5m
        public double DistToLiqPct { get; private set; }    // dystans do klastra liq (procent ceny) – gdy dostępny

        public static async Task<FeatureSnapshot> BuildAsync(
            string symbol,
            Quote[] q1m,
            Quote[] q5m,
            Quote[] q15m,
            Quote[] q1h,
            Ticker ticker,
            ISigmaDataProvider data,
            CancellationToken cancel)
        {
            var f = new FeatureSnapshot { Symbol = symbol };

            // ADX(1H)
            f.Adx1h = q1h.GetAdx(14).LastOrDefault()?.Adx ?? 0;

            // ART(1H)
            var atrRes = q1h.GetAtr(14).LastOrDefault();
            if (atrRes?.Atr is > 0)
            {
                var refPx = ticker?.BestAskPrice > 0 ? ticker.BestAskPrice : q1h.Last().Close;
                if (refPx > 0)
                    f.AtrPct1h = (double)(atrRes.Atr / (double)refPx) * 100.0;
            }

            // VWAP dzienny – przybliżenie z 1m: liczymy od północy/UTC lub sesyjnego okna; tu szkic/placeholder
            // TODO: wprowadź precyzyjny VWAP_D z sum(p*q) / sum(q) na tickach/trades
            var vwap = q1h.GetVwap().LastOrDefault()?.Vwap ?? (double)ticker.LastPrice;
            var std = StdDev(q1m.Select(x => x.Close).ToArray());
            f.ZDvwap = std > 0 ? (double)(((double)ticker.LastPrice - vwap) / std) : 0;

            // Nachylenie D-VWAP (tu szkic: regresja po VWAP na 1h)
            var v1h = q1h.GetVwap().Select(x => x.Vwap.Value).ToArray();
            f.ZSlopeDvwap = ZScoreSlope(v1h);

            // Autokorelacja 5m (prosty AR(1) na stopach zwrotu)
            var r5 = Returns(q5m.Select(x => x.Close).ToArray());
            f.AutoCorr5m = AutoCorrLag1(r5);

            // BB width percentyl (15m)
            var bb = q15m.GetBollingerBands(20, 2).ToArray();
            double[] widths = bb.Where(x => x?.Width != null).Select(x => (double)x.Width!).ToArray();
            f.Bbw15mPct = PercentileRank(widths, widths.LastOrDefault());

            // Inside/NR7 (placeholder wykrycia prostymi heurystykami)
            f.HasInsideOrNr7 = DetectInsideOrNr7(q15m) || DetectInsideOrNr7(q1h);

            // Derywaty/flow (TODO: implementacje w providerze)
            f.OiDelta1hPct = await data.GetOpenInterestDelta1hPctAsync(symbol, cancel);
            f.Funding8h = await data.GetFundingRateAsync(symbol, cancel);
            f.BasisPct = await data.GetBasisPctAsync(symbol, cancel);
            f.DeltaCvd5m = await data.GetDeltaCvd5mAsync(symbol, cancel);
            f.DistToLiqPct = await data.GetDistToNearestLiquidationPctAsync(symbol, ticker.LastPrice, cancel);

            return f;
        }

        // ====== Pomocnicze proste statystyki ======
        private static double[] Returns(decimal[] px)
            => Returns(px.Select(p => (double)p).ToArray());

        private static double[] Returns(double[] px)
        {
            var r = new double[Math.Max(0, px.Length - 1)];
            for (int i = 1; i < px.Length; i++) r[i - 1] = Math.Log(px[i] / px[i - 1]);
            return r;
        }

        private static double AutoCorrLag1(double[] r)
        {
            if (r.Length < 3) return 0;
            double mean = r.Average();
            double num = 0, den = 0;
            for (int i = 1; i < r.Length; i++) num += (r[i] - mean) * (r[i - 1] - mean);
            for (int i = 0; i < r.Length; i++) den += (r[i] - mean) * (r[i] - mean);
            return den == 0 ? 0 : num / den;
        }

        private static double StdDev(decimal[] v) => StdDev(v.Select(x => (double)x).ToArray());
        private static double StdDev(double[] v)
        {
            if (v.Length == 0) return 0;
            double m = v.Average();
            return Math.Sqrt(v.Select(x => (x - m) * (x - m)).Sum() / v.Length);
        }

        // Z-score nachylenia: regresja liniowa po sekwencji (prosty szkic)
        private static double ZScoreSlope(decimal[] series) => ZScoreSlope(series.Select(x => (double)x).ToArray());
        private static double ZScoreSlope(double[] series)
        {
            if (series.Length < 10) return 0;
            double n = series.Length;
            double sx = (n - 1) * n / 2.0;       // suma 0..n-1
            double sxx = (n - 1) * n * (2 * n - 1) / 6.0;
            double sy = series.Sum();
            double sxy = 0;
            for (int i = 0; i < series.Length; i++) sxy += i * series[i];
            double denom = (n * sxx - sx * sx);
            if (denom == 0) return 0;
            double slope = (n * sxy - sx * sy) / denom;

            // Z-score slope w relacji do odchyleń serii
            double std = StdDev(series);
            return std == 0 ? 0 : slope / std;
        }

        private static bool DetectInsideOrNr7(Quote[] q)
        {
            if (q.Length < 8) return false;
            // Inside: zakres ostatniej świecy w całości mieści się w poprzedniej
            var a = q[^1]; var b = q[^2];
            bool inside = (a.High <= b.High && a.Low >= b.Low);
            // NR7: najmniejszy range z 7 ostatnich
            var last7 = q[^7..];
            decimal lastRange = a.High - a.Low;
            decimal minRange = last7.Min(x => x.High - x.Low);
            bool nr7 = lastRange <= minRange;
            return inside || nr7;
        }

        private static double PercentileRank(double[] values, double v)
        {
            if (values.Length == 0) return 50;
            int count = values.Count(x => x <= v);
            return 100.0 * count / values.Length;
        }
    }
}

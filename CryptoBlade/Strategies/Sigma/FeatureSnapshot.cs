using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Sigma.Helpers;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ticker = CryptoBlade.Models.Ticker;

namespace CryptoBlade.Strategies.Sigma
{
    /// <summary>
    /// Snapshot cech rynku używanych przez RegimeEngine.
    /// Zasada: brak danych = double.NaN (zamiast flag HasX).
    /// </summary>
    public class FeatureSnapshot
    {
        // ====== Cechy surowe (NaN oznacza "brak/niepoliczalne") ======
        public string Symbol { get; private set; } = "";

        // Trend / zmienność / value
        public double Adx1h { get; private set; }             // [0..100] lub NaN
        public double AtrPct1h { get; private set; }          // ATR_1h / Price * 100 (%)
        public double Atr1hAbs { get; private set; }          // ATR_1h w punktach
        public double ZDvwap { get; private set; }            // z-score od D-VWAP (kotwica = start sesji)
        public double ZSlopeDvwap { get; private set; }       // t-stat nachylenia D-VWAP (HAC/Newey-West)
        public double AutoCorr5m { get; private set; }        // autokorelacja lag-1 (shrunk)
        public double Bbw15mPct { get; private set; }         // percentyl BBWidth (mid-rank, 0..100)
        public double Bbw15mRaw { get; private set; }         // surowa szerokość BB (Upper-Lower)/SMA * 100

        // Mikrostruktura / egzekucja
        public double SpreadBps { get; set; }                 // spread bid-ask w bps (NaN jeśli brak)

        // Derywaty / flow
        public double OiDelta1hPct { get; private set; }      // ΔOI$ 1h w %
        public double Funding8h { get; private set; }         // funding 8h w %
        public double BasisPct { get; private set; }          // (Mark-Index)/Index * 100
        public double DeltaCvd5m { get; private set; }        // ΔCVD 5m (jeśli dostępne)
        public double DistToLiqPct { get; private set; }      // dystans do najbliższej likwidacji w %

        // Struktura/patterny (liczone poza klasą – PatternDetectors)
        public bool HasInsideOrNr7 { get; private set; }
        public bool DonchianBreakUp { get; private set; }
        public bool DonchianBreakDown { get; private set; }
        public bool DonchianBreak => DonchianBreakUp || DonchianBreakDown;

        // Sygnały pomocnicze
        public bool Bbw15mExpanding { get; private set; }     // ekspansja BBW vs kilka barów wstecz
        public decimal? OpeningRangeHigh { get; private set; }
        public decimal? OpeningRangeLow { get; private set; }

        // Korelacja z BTC (NaN = brak)
        public double CorrToBtc15m { get; private set; }      // Pearson r (NaN jeśli brak)
        public bool BtcBiasOpposite { get; private set; }     // heurystyka pod Supervisor

        // ========= BUILD =========
        public static async Task<FeatureSnapshot> BuildAsync(
            string symbol,
            Quote[] q1m,
            Quote[] q5m,
            Quote[] q15m,
            Quote[] q1h,
            Ticker ticker,
            IBybitSigmaDataProvider data,
            CancellationToken cancel,
            int sessionStartHourUtc = 0,
            int vwapSlopeWindow = 60)
        {
            SortIfNeeded(q1m);
            SortIfNeeded(q5m);
            SortIfNeeded(q15m);
            SortIfNeeded(q1h);

            var f = new FeatureSnapshot { Symbol = symbol };

            // 1) ADX(1H)
            f.Adx1h = ComputeAdx1h(q1h);

            // 2) ATR% (1H) + ATR abs
            double refPrice = SelectRefPriceDouble(ticker, q1h);
            (f.AtrPct1h, f.Atr1hAbs) = ComputeAtr1h(q1h, refPrice);

            // 3) D-VWAP (1m)
            DateTime anchor = FindCurrentSessionAnchorUtc(q1m, sessionStartHourUtc);
            var (vwapSeries, lastVwap, tpStdDay) = ComputeAnchoredDailyVwapSeries(q1m, anchor);

            // 4) z-score do VWAP (NaN jeżeli niepoliczalne)
            f.ZDvwap = ComputeZDvwap(ticker?.LastPrice, lastVwap, tpStdDay);

            // 5) t-stat nachylenia VWAP (HAC/Newey-West)
            f.ZSlopeDvwap = ComputeSlopeTstatHAC(vwapSeries, Math.Max(10, vwapSlopeWindow));

            // 6) Autokorelacja lag-1 na 5m (shrunk)
            f.AutoCorr5m = ComputeAutoCorrLag1Shrunk(q5m, kappa: 8);

            // 7) BBW percentyl + raw (NaN, gdy brak)
            (f.Bbw15mPct, f.Bbw15mRaw) = ComputeBbwPercentileAndRaw(q15m, 20, 2.0);

            // 8) Inside/NR7 (na 5m – kompresja mikro)
            f.HasInsideOrNr7 = PatternDetectors.HasInsideOrNr7(q5m);

            // 9) Donchian break (15m lub 5m – tu 15m pod reżim BO)
            (f.DonchianBreakUp, f.DonchianBreakDown) = PatternDetectors.DonchianBreak(q15m, period: 20);

            // 10) Ekspansja BBW 15m (prosty proxy: now > sprzed 2 barów)
            f.Bbw15mExpanding = ComputeBbwExpanding(q15m, 20, 2.0, back: 2);

            // 11) Opening Range dla ORB (na 1m)
            (var orHigh, var orLow) = PatternDetectors.OpeningRange(q1m, minutes: 30);
            f.OpeningRangeHigh = (orHigh == 0m && orLow == 0m) ? null : orHigh;
            f.OpeningRangeLow = (orHigh == 0m && orLow == 0m) ? null : orLow;


            // 12) Spread (bps) z providera (NaN, gdy brak)
            f.SpreadBps = await TryGetOrNaN(() => data.GetSpreadBpsAsync(symbol, cancel));

            // 13) Derywaty/flow (NaN, gdy brak)
            f.OiDelta1hPct = await TryGetOrNaN(() => data.GetOpenInterestDelta1hPctAsync(symbol, cancel));
            f.Funding8h = await TryGetOrNaN(() => data.GetFundingRateAsync(symbol, cancel));
            f.BasisPct = await TryGetOrNaN(() => data.GetBasisPctAsync(symbol, cancel));
            f.DeltaCvd5m = await TryGetOrNaN(() => data.GetDeltaCvd5mAsync(symbol, cancel));
            f.DistToLiqPct = await TryGetOrNaN(() => data.GetDistToNearestLiquidationPctAsync(symbol, (ticker?.LastPrice) ?? 0m, cancel));

            // 14) Korelacja do BTC + heurystyka biasu
            await PopulateCorrToBtcAndBiasAsync(f, data, symbol, corrWindow: 80, cancel);

            Sanitize(ref f);
            return f;
        }

        // ========= Kalkulatory cech =========

        public static double ComputeAdx1h(Quote[] q1h)
        {
            if (q1h == null || q1h.Length < 2) return double.NaN;
            var adx = q1h.GetAdx(14).LastOrDefault(x => x != null && x.Adx.HasValue);
            return adx?.Adx ?? double.NaN;
        }

        public static (double atrPct, double atrAbs) ComputeAtr1h(Quote[] q1h, double refPrice)
        {
            double atrAbs = double.NaN;
            if (q1h != null && q1h.Length > 0)
                atrAbs = (double)(q1h.GetAtr(14).LastOrDefault()?.Atr ?? 0);

            if (!double.IsFinite(atrAbs) || !(atrAbs > 0) || !(refPrice > 0))
                return (double.NaN, atrAbs);

            return ((atrAbs / refPrice) * 100.0, atrAbs);
        }

        public static (double[] vwapSeries, double lastVwap, double tpStdDay)
            ComputeAnchoredDailyVwapSeries(Quote[] q1m, DateTime anchorUtc)
        {
            if (q1m == null || q1m.Length == 0)
                return (Array.Empty<double>(), double.NaN, double.NaN);

            var intraday = q1m.Where(q => q.Date >= anchorUtc).ToArray();
            if (intraday.Length < 20) // min rozruch
                return (Array.Empty<double>(), double.NaN, double.NaN);

            int n = intraday.Length;
            double[] tp = new double[n];
            double[] vol = new double[n];

            for (int i = 0; i < n; i++)
            {
                tp[i] = (double)((intraday[i].High + intraday[i].Low + intraday[i].Close) / 3m);
                vol[i] = (double)intraday[i].Volume;
            }

            double[] vwap = new double[n];
            double accPv = 0.0, accV = 0.0;
            for (int i = 0; i < n; i++)
            {
                accPv += tp[i] * vol[i];
                accV += vol[i];
                vwap[i] = (accV > 0.0) ? (accPv / accV) : double.NaN;
            }

            double last = vwap[^1];
            double tpStd = Stats.StdDevSample(tp); // sample SD (korekta Bessela)

            if (!double.IsFinite(last))
            {
                var (rvwap, rlast) = Stats.RollingVwap(q1m, window: 60); // nowa utilka
                vwap = rvwap;
                last = rlast;
                tpStd = Stats.StdDevSample(q1m.TakeLast(60).Select(z => (double)((z.High + z.Low + z.Close) / 3m)).ToArray());
            }

            return (vwap, last, tpStd);
        }

        public static double ComputeZDvwap(decimal? lastPrice, double lastVwap, double tpStdDay)
        {
            if (!(lastPrice > 0) || !double.IsFinite(lastVwap) || !(tpStdDay > 0))
                return double.NaN;

            var z = ((double)lastPrice!.Value - lastVwap) / tpStdDay;
            return Stats.Saturate(z, 10.0);
        }

        /// <summary>
        /// Zwraca: (percentyl BBWidth, BBWidth raw w %). NaN gdy niepoliczalne.
        /// </summary>
        public static (double pct, double lastRaw) ComputeBbwPercentileAndRaw(Quote[] q15m, int bbPeriod, double bbStdMult)
        {
            if (q15m == null || q15m.Length < bbPeriod + 2) return (double.NaN, double.NaN);

            var bb = q15m.GetBollingerBands(bbPeriod, bbStdMult).ToArray();
            var widths = bb.Where(x => x != null && x.Width.HasValue && x.Sma != 0)
                           .Select(x => (double)((x.UpperBand!.Value - x.LowerBand!.Value) / x.Sma!.Value * 100))
                           .ToArray();

            int n = widths.Length;
            if (n == 0) return (double.NaN, double.NaN);

            double last = widths[^1];

            // percentyl mid-rank
            int lt = 0, eq = 0;
            for (int i = 0; i < n; i++)
            {
                if (widths[i] < last) lt++;
                else if (widths[i] == last) eq++;
            }
            double p = (lt + 0.5 * eq) * 100.0 / n;
            return (p, last);
        }

        /// <summary>
        /// Czy BBWidth rośnie względem stanu sprzed 'back' barów.
        /// </summary>
        public static bool ComputeBbwExpanding(Quote[] q15m, int bbPeriod, double bbStdMult, int back = 2)
        {
            if (q15m == null || q15m.Length < bbPeriod + back + 1) return false;
            var bb = q15m.GetBollingerBands(bbPeriod, bbStdMult).ToArray();

            double Bw(int idx)
            {
                var r = bb[idx];
                if (r == null || !r.UpperBand.HasValue || !r.LowerBand.HasValue || r.Sma == 0) return double.NaN;
                return (double)((r.UpperBand.Value - r.LowerBand.Value) / r.Sma!.Value * 100);
            }

            int last = bb.Length - 1;
            int prev = bb.Length - 1 - back;
            var now = Bw(last);
            var was = Bw(prev);
            if (!double.IsFinite(now) || !double.IsFinite(was)) return false;
            return now > was;
        }

        // ===== HAC/Newey–West t-stat dla slope(D-VWAP) =====
        public static double ComputeSlopeTstatHAC(double[] series, int lastK)
        {
            if (series == null) return double.NaN;
            int nAll = series.Length;
            int k = Math.Min(lastK, nAll);
            if (k < 10) return double.NaN;

            var y = series[^k..];

            // Zbuduj punkty (x, y) z NaN-filterem
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
            if (n < 10) return double.NaN;

            // OLS: beta = (X'X)^-1 X'y, gdzie X = [1, x]
            double sx = 0, sy = 0, sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++)
            {
                double x = xs[i], yy = ys[i];
                sx += x;
                sy += yy;
                sxx += x * x;
                sxy += x * yy;
            }
            double denom = (n * sxx - sx * sx);
            if (Math.Abs(denom) < 1e-12) return double.NaN;

            double beta1 = (n * sxy - sx * sy) / denom;         // slope
            double beta0 = (sy - beta1 * sx) / n;               // intercept

            // Residua
            var e = new double[n];
            for (int i = 0; i < n; i++)
                e[i] = ys[i] - (beta0 + beta1 * xs[i]);

            // inv(X'X) dla 2x2
            double a = n, b = sx, c = sx, d = sxx;
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12) return double.NaN;
            double inv00 = d / det, inv01 = -b / det, inv10 = -c / det, inv11 = a / det;

            // Newey–West bandwidth (Bartlett)
            int L = Math.Max(1, (int)Math.Floor(4.0 * Math.Pow(n / 100.0, 2.0 / 9.0)));
            L = Math.Min(L, n - 1);

            // S = Gamma0 + sum_{lag=1..L} w_l (Gamma_l + Gamma_l')
            double S00 = 0, S01 = 0, S11 = 0;

            // Gamma_0
            for (int t = 0; t < n; t++)
            {
                double w0 = e[t] * e[t];
                double x0 = 1.0, x1 = xs[t];

                S00 += w0 * x0 * x0;
                S01 += w0 * x0 * x1;
                S11 += w0 * x1 * x1;
            }

            // lags
            for (int lag = 1; lag <= L; lag++)
            {
                double w = 1.0 - (double)lag / (L + 1.0); // Bartlett
                double A00 = 0, A01 = 0, A11 = 0;

                for (int t = lag; t < n; t++)
                {
                    double et = e[t];
                    double es = e[t - lag];
                    double x0t = 1.0, x1t = xs[t];
                    double x0s = 1.0, x1s = xs[t - lag];

                    A00 += es * et * (x0s * x0t);
                    A01 += es * et * (x0s * x1t);
                    A11 += es * et * (x1s * x1t);
                }

                S00 += w * 2.0 * A00;
                S01 += w * 2.0 * A01;
                S11 += w * 2.0 * A11;
            }

            // Var(beta) ≈ (X'X)^-1 * S * (X'X)^-1; interesuje nas [1,1] (slope)
            double M00 = inv00 * S00 + inv01 * S01;
            double M01 = inv00 * S01 + inv01 * S11;
            double M10 = inv10 * S00 + inv11 * S01;
            double M11 = inv10 * S01 + inv11 * S11;

            double varSlope = M10 * inv01 + M11 * inv11;
            if (!(varSlope > 0)) return double.NaN;

            double seSlope = Math.Sqrt(varSlope);
            if (!(seSlope > 0)) return double.NaN;

            return beta1 / seSlope;
        }

        // ===== Autokorelacja lag-1 (ciągły shrink) =====
        public static double ComputeAutoCorrLag1Shrunk(Quote[] q5m, int kappa = 8)
        {
            if (q5m == null || q5m.Length < 3) return double.NaN;
            int n = q5m.Length;

            var px = new double[n];
            for (int i = 0; i < n; i++) px[i] = (double)q5m[i].Close;

            var r = Stats.ReturnsLog(px);
            int m = r.Length;
            if (m < 3) return double.NaN;

            double mean = Stats.Mean(r);
            double num = 0.0, den = 0.0;
            for (int i = 1; i < m; i++)
                num += (r[i] - mean) * (r[i - 1] - mean);
            for (int i = 0; i < m; i++)
                den += (r[i] - mean) * (r[i] - mean);
            if (!(den > 0)) return double.NaN;

            double rhohat = num / den;

            // shrink: lambda = m / (m + kappa)
            double lambda = (double)m / (m + kappa);
            double rho = lambda * rhohat;

            // clamp
            if (rho > 1) rho = 1;
            if (rho < -1) rho = -1;
            return rho;
        }

        private static async Task PopulateCorrToBtcAndBiasAsync(
           FeatureSnapshot f,
           IBybitSigmaDataProvider data,
           string symbol,
           int corrWindow,
           CancellationToken cancel)
        {
            f.CorrToBtc15m = double.NaN;
            f.BtcBiasOpposite = false;

            try
            {
                var (corr, lastBtcRet) = await data.GetCorrToBtc15mAsync(symbol, corrWindow, cancel);
                f.CorrToBtc15m = corr;

                if (double.IsFinite(f.CorrToBtc15m) && Math.Abs(f.CorrToBtc15m) >= 0.85 && double.IsFinite(f.ZSlopeDvwap))
                {
                    f.BtcBiasOpposite =
                        (lastBtcRet > 0 && f.ZSlopeDvwap < 0) ||
                        (lastBtcRet < 0 && f.ZSlopeDvwap > 0);
                }
            }
            catch
            {
                f.CorrToBtc15m = double.NaN;
                f.BtcBiasOpposite = false;
            }
        }

        // ========= Pomocnicze =========

        public static void SortIfNeeded(Quote[] q)
        {
            if (q == null || q.Length < 2) return;
            for (int i = 1; i < q.Length; i++)
            {
                if (q[i].Date < q[i - 1].Date)
                {
                    Array.Sort(q, (a, b) => a.Date.CompareTo(b.Date));
                    break;
                }
            }
        }

        public static DateTime FindCurrentSessionAnchorUtc(Quote[] q1m, int sessionStartHourUtc)
        {
            DateTime last = (q1m != null && q1m.Length > 0) ? q1m[^1].Date : DateTime.UtcNow;
            var dayAnchor = new DateTime(last.Year, last.Month, last.Day, 0, 0, 0, DateTimeKind.Utc)
                                .AddHours(sessionStartHourUtc);
            if (last < dayAnchor) dayAnchor = dayAnchor.AddDays(-1);
            return dayAnchor;
        }

        public static double SelectRefPriceDouble(Ticker? t, Quote[] q1h)
        {
            if (t != null && t.LastPrice > 0) return (double)t.LastPrice;
            if (q1h != null && q1h.Length > 0) return (double)q1h[^1].Close;
            return double.NaN;
        }

        /// <summary>
        /// Pobiera wartość z providera; w razie błędu/NaN zwraca double.NaN.
        /// </summary>
        public static async Task<double> TryGetOrNaN(Func<Task<double>> f)
        {
            try
            {
                var v = await f();
                return (double.IsNaN(v) || double.IsInfinity(v)) ? double.NaN : v;
            }
            catch
            {
                return double.NaN;
            }
        }

        /// <summary>
        /// Clamp tylko dla wartości skończonych; NaN/Infty pozostają NaN (sygnalizują brak).
        /// </summary>
        public static void Sanitize(ref FeatureSnapshot f)
        {
            f.Adx1h = ClampFinite(f.Adx1h, 0, 100, allowNaN: true);
            f.AtrPct1h = ClampFinite(f.AtrPct1h, -1e6, 1e6, allowNaN: true);
            f.Atr1hAbs = ClampFinite(f.Atr1hAbs, -1e12, 1e12, allowNaN: true);
            f.ZDvwap = ClampFinite(f.ZDvwap, -10, 10, allowNaN: true);
            f.ZSlopeDvwap = ClampFinite(f.ZSlopeDvwap, -100, 100, allowNaN: true);
            f.AutoCorr5m = ClampFinite(f.AutoCorr5m, -1, 1, allowNaN: true);
            f.Bbw15mPct = ClampFinite(f.Bbw15mPct, 0, 100, allowNaN: true);
            f.Bbw15mRaw = ClampFinite(f.Bbw15mRaw, -1e6, 1e6, allowNaN: true);
            f.SpreadBps = ClampFinite(f.SpreadBps, 0, 1e6, allowNaN: true);

            f.OiDelta1hPct = ClampFinite(f.OiDelta1hPct, -1e6, 1e6, allowNaN: true);
            f.Funding8h = ClampFinite(f.Funding8h, -100, 100, allowNaN: true);
            f.BasisPct = ClampFinite(f.BasisPct, -1e4, 1e4, allowNaN: true);
            f.DeltaCvd5m = ClampFinite(f.DeltaCvd5m, -1e15, 1e15, allowNaN: true);
            f.DistToLiqPct = ClampFinite(f.DistToLiqPct, -1e6, 1e6, allowNaN: true);

            f.CorrToBtc15m = ClampFinite(f.CorrToBtc15m, -1, 1, allowNaN: true);
        }

        private static double ClampFinite(double v, double lo, double hi, bool allowNaN)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
                return allowNaN ? double.NaN : 0.0;

            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}

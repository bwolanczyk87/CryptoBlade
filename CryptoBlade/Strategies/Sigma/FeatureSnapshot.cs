using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoExchange.Net.CommonObjects;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ticker = CryptoBlade.Models.Ticker;

namespace CryptoBlade.Strategies.Sigma
{
    public class FeatureSnapshot
    {
        // ====== Cechy surowe ======
        public string Symbol { get; private set; } = "";
        public double Adx1h { get; private set; }
        public double AtrPct1h { get; private set; }
        public double Atr1hAbs { get; private set; }
        public double ZDvwap { get; private set; }
        public double ZSlopeDvwap { get; private set; }
        public double AutoCorr5m { get; private set; }
        public double Bbw15mPct { get; private set; }
        public double Bbw15mRaw { get; private set; }
        public double SpreadBps { get; set; }   // <- jawny set: nadpisujemy z tickera

        // ====== Derywaty/flow ======
        public double OiDelta1hPct { get; private set; }
        public double Funding8h { get; private set; }
        public double BasisPct { get; private set; }
        public double DeltaCvd5m { get; private set; }
        public double DistToLiqPct { get; private set; }

        // ====== Flagi jakości ======
        public bool HasDerivatives { get; private set; }
        public bool HasCvd { get; private set; }
        public bool HasLiq { get; private set; }
        public bool HasVwap { get; private set; }
        public bool HasVol15m { get; private set; }
        public bool HasInsideOrNr7 { get; private set; }


        // ========= BUILD =========
        public static async Task<FeatureSnapshot> BuildAsync(
            string symbol,
            Quote[] q1m,
            Quote[] q5m,
            Quote[] q15m,
            Quote[] q1h,
            Ticker ticker,
            ISigmaDataProvider data,
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
            f.HasVwap = vwapSeries.Length >= 10 && !double.IsNaN(lastVwap);

            // 4) Z-score do VWAP (clamp |z|<=10)
            f.ZDvwap = ComputeZDvwap(ticker?.LastPrice, lastVwap, tpStdDay);

            // 5) t-stat nachylenia VWAP (clamp)
            var tStat = ComputeSlopeTstatHAC(vwapSeries, Math.Max(10, vwapSlopeWindow));
            f.ZSlopeDvwap = Saturate(tStat, 100.0);

            // 6) Autokorelacja lag-1 na 5m
            f.AutoCorr5m = ComputeAutoCorrLag1Shrunk(q5m, kappa: 8);

            // 7) BBW percentyl (mid-rank) + raw
            (f.Bbw15mPct, f.Bbw15mRaw, bool hasVol) = ComputeBbwPercentileAndRaw(q15m, 20, 2.0);
            f.HasVol15m = hasVol;

            // 8) Inside/NR7 (15m lub 1h)
            f.HasInsideOrNr7 = DetectInsideOrNr7(q15m) || DetectInsideOrNr7(q1h);

            // 9) Spread (bps) z providera (może być nadpisany w strategii realnym bid-ask)
            f.SpreadBps = ComputeSpreadBps(ticker) ?? double.MaxValue;

            // 10) Derywaty/flow
            var (oi, okOi) = await TryGet(async () => await data.GetOpenInterestDelta1hPctAsync(symbol, cancel));
            var (fund, okFr) = await TryGet(async () => await data.GetFundingRateAsync(symbol, cancel));
            var (bas, okBs) = await TryGet(async () => await data.GetBasisPctAsync(symbol, cancel));
            var (cvd, okCvd) = await TryGet(async () => await data.GetDeltaCvd5mAsync(symbol, cancel));
            var (liq, okLiq) = await TryGet(async () => await data.GetDistToNearestLiquidationPctAsync(symbol, (ticker?.LastPrice) ?? 0m, cancel));

            f.OiDelta1hPct = oi;
            f.Funding8h = fund;
            f.BasisPct = bas;
            f.DeltaCvd5m = cvd;
            f.DistToLiqPct = liq;

            f.HasDerivatives = okOi || okFr || okBs;
            f.HasCvd = okCvd;
            f.HasLiq = okLiq;

            // Sanity/clamp
            Sanitize(ref f);
            return f;
        }

        // ========= Kalkulatory cech =========

        public static double ComputeAdx1h(Quote[] q1h)
        {
            if (q1h == null || q1h.Length < 2) return 0;
            var adx = q1h.GetAdx(14).LastOrDefault(x => x != null && x.Adx.HasValue);
            return adx?.Adx ?? 0;
        }



        public static (double atrPct, double atrAbs) ComputeAtr1h(Quote[] q1h, double refPrice)
        {
            double atrAbs = 0;
            if (q1h != null && q1h.Length > 0)
                atrAbs = q1h.GetAtr(14).LastOrDefault()?.Atr ?? 0;

            if (!(atrAbs > 0) || refPrice <= 0) return (0, atrAbs);
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
            double tpStd = StdDevSample(tp);
            return (vwap, last, tpStd);
        }

        public static double ComputeZDvwap(decimal? lastPrice, double lastVwap, double tpStdDay)
        {
            if (!(lastPrice > 0) || double.IsNaN(lastVwap) || !(tpStdDay > 0)) return 0;
            var z = ((double)lastPrice!.Value - lastVwap) / tpStdDay;
            return Saturate(z, 10.0);
        }

        public static (double pct, double lastRaw, bool hasVol) ComputeBbwPercentileAndRaw(Quote[] q15m, int bbPeriod, double bbStdMult)
        {
            if (q15m == null || q15m.Length < bbPeriod + 2) return (0, 0, false);
            var bb = q15m.GetBollingerBands(bbPeriod, bbStdMult).ToArray();
            var widths = bb.Where(x => x != null && x.Width.HasValue)
                           .Select(x => (double)x.Width!.Value)
                           .ToArray();
            int n = widths.Length;
            if (n == 0) return (0, 0, false);

            double last = widths[^1];

            int lt = 0, eq = 0; // mid-rank
            for (int i = 0; i < n; i++)
            {
                if (widths[i] < last) lt++;
                else if (widths[i] == last) eq++;
            }
            double p = (lt + 0.5 * eq) * 100.0 / n;
            return (p, last, true);
        }

        public static bool DetectInsideOrNr7(Quote[] q)
        {
            if (q == null || q.Length < 8) return false;
            var a = q[^1];
            var b = q[^2];

            bool inside = a.High <= b.High && a.Low >= b.Low;

            var last7 = q[^7..];
            if (last7.Count(x => x.Volume <= 0) >= 5) return inside;

            decimal lastRange = a.High - a.Low;
            decimal minRange = last7.Min(x => x.High - x.Low);
            bool nr7 = lastRange <= minRange;

            return inside || nr7;
        }

        // ===== HAC/Newey–West t-stat dla slope(D-VWAP) =====
        public static double ComputeSlopeTstatHAC(double[] series, int lastK)
        {
            if (series == null) return 0;
            int nAll = series.Length;
            int k = Math.Min(lastK, nAll);
            if (k < 10) return 0;

            var y = series[^k..];

            // Zbuduj punkty (x, y) z NaN-filterem
            var xs = new List<double>(k);
            var ys = new List<double>(k);
            for (int i = 0; i < k; i++)
            {
                double yi = y[i];
                if (!double.IsNaN(yi) && !double.IsInfinity(yi))
                {
                    xs.Add(i);            // równy krok czasowy
                    ys.Add(yi);
                }
            }
            int n = ys.Count;
            if (n < 10) return 0;

            // X: [1, x], OLS: beta = (X'X)^-1 X'y
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
            if (denom == 0) return 0;

            double beta1 = (n * sxy - sx * sy) / denom;         // slope
            double beta0 = (sy - beta1 * sx) / n;               // intercept

            // Residua
            var e = new double[n];
            for (int i = 0; i < n; i++)
                e[i] = ys[i] - (beta0 + beta1 * xs[i]);

            // X'X oraz jego odwrotność
            // XtX = [[n, sx], [sx, sxx]]
            double a = n, b = sx, c = sx, d = sxx;
            double det = a * d - b * c;
            if (Math.Abs(det) < 1e-12) return 0;
            // inv(X'X)
            double inv00 = d / det;
            double inv01 = -b / det;
            double inv10 = -c / det;
            double inv11 = a / det;

            // Newey–West: bandwidth L (Bartlett weights)
            int L = Math.Max(1, (int)Math.Floor(4.0 * Math.Pow(n / 100.0, 2.0 / 9.0)));
            L = Math.Min(L, n - 1);

            // S = X' diag(e^2) X + sum_{l=1..L} w_l [ X_l' diag(e_l * e_0) X_0 + T' ]
            // pracujemy na macierzach 2x2
            double S00 = 0, S01 = 0, S11 = 0;

            // Gamma_0 = X' diag(e^2) X
            for (int t = 0; t < n; t++)
            {
                double w0 = e[t] * e[t];
                double x0 = 1.0;
                double x1 = xs[t];

                S00 += w0 * x0 * x0;      // (1,1)
                S01 += w0 * x0 * x1;      // (1,2) i (2,1) symetrycznie
                S11 += w0 * x1 * x1;      // (2,2)
            }

            // L-agi Bartlett
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

                    // X_s' diag(e_t * e_s) X_t  (2x2 z iloczynów krzyżowych)
                    A00 += es * et * (x0s * x0t);
                    A01 += es * et * (x0s * x1t);
                    A11 += es * et * (x1s * x1t);
                }

                // dodaj składową symetryczną: A + A'
                S00 += w * 2.0 * A00;
                S01 += w * 2.0 * A01;
                S11 += w * 2.0 * A11;
            }

            // Var(beta) ≈ (X'X)^-1 * S * (X'X)^-1
            // Interesuje nas element [1,1] (slope)
            // M = inv * S * inv
            double M00 = inv00 * S00 + inv01 * S01;
            double M01 = inv00 * S01 + inv01 * S11;
            double M10 = inv10 * S00 + inv11 * S01;
            double M11 = inv10 * S01 + inv11 * S11;

            double varSlope = M10 * inv01 + M11 * inv11;
            if (varSlope < 1e-12) varSlope = 1e-12;

            double seSlope = Math.Sqrt(varSlope);
            if (seSlope < 1e-12) seSlope = 1e-12;

            return beta1 / seSlope;
        }

        // ===== Shrink dla autokorelacji lag-1 (ciągły) =====
        public static double ComputeAutoCorrLag1Shrunk(Quote[] q5m, int kappa = 8)
        {
            if (q5m == null || q5m.Length < 3) return 0;
            int n = q5m.Length;

            var px = new double[n];
            for (int i = 0; i < n; i++) px[i] = (double)q5m[i].Close;

            var r = ReturnsLog(px);
            int m = r.Length;
            if (m < 3) return 0;

            double mean = Mean(r);
            double num = 0.0, den = 0.0;
            for (int i = 1; i < m; i++)
                num += (r[i] - mean) * (r[i - 1] - mean);
            for (int i = 0; i < m; i++)
                den += (r[i] - mean) * (r[i] - mean);
            if (den == 0.0) return 0;

            double rhohat = num / den;

            // shrink: lambda = m / (m + kappa), kappa ~ 5–20
            double lambda = (double)m / (m + kappa);
            double rho = lambda * rhohat;

            // sanity clamp
            if (rho > 1) rho = 1;
            if (rho < -1) rho = -1;
            return rho;
        }

        public static double? ComputeSpreadBps(Ticker? t, decimal? minMidGuard = 1m)
        {
            if (t is null) return null;
            var bid = t.BestBidPrice;
            var ask = t.BestAskPrice;
            if (bid <= 0 || ask <= 0) return double.MaxValue;
            if (ask <= bid) return 0.0; // zdegenerowany arkusz (cross)

            var mid = (bid + ask) / 2m;
            if (minMidGuard.HasValue && mid < minMidGuard.Value) return null;

            var bps = (double)((ask - bid) / mid * 10_000m);
            if (double.IsNaN(bps) || double.IsInfinity(bps)) return null;

            // lekkie sanity na śmieciowe ticki
            if (bps < 0) bps = 0;
            if (bps > 100_000) bps = 100_000;
            return bps;
        }


        // ========= Pomocnicze =========

        public static void SortIfNeeded(Quote[] q)
        {
            if (q == null || q.Length < 2) return;
            for (int i = 1; i < q.Length; i++)
                if (q[i].Date < q[i - 1].Date)
                {
                    Array.Sort(q, (a, b) => a.Date.CompareTo(b.Date));
                    break;
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
            return 0.0;
        }

        public static async Task<(double val, bool ok)> TryGet(Func<Task<double>> f)
        {
            try
            {
                var v = await f();
                bool ok = !(double.IsNaN(v) || double.IsInfinity(v));
                return (ok ? v : 0.0, ok);
            }
            catch { return (0.0, false); }
        }

        public static void Sanitize(ref FeatureSnapshot f)
        {
            f.Adx1h = CleanClamp(f.Adx1h, 100);
            f.AtrPct1h = CleanClamp(f.AtrPct1h, 50);
            f.Atr1hAbs = CleanClamp(f.Atr1hAbs, 1e9);
            f.ZDvwap = CleanClamp(f.ZDvwap, 10);
            f.ZSlopeDvwap = CleanClamp(f.ZSlopeDvwap, 100);
            f.AutoCorr5m = CleanClamp(f.AutoCorr5m, 1);
            f.Bbw15mPct = CleanClamp(f.Bbw15mPct, 100);
            f.Bbw15mRaw = CleanClamp(f.Bbw15mRaw, 1e9);
            f.SpreadBps = CleanClamp(f.SpreadBps, 1e6);

            f.OiDelta1hPct = CleanClamp(f.OiDelta1hPct, 100);
            f.Funding8h = CleanClamp(f.Funding8h, 5);
            f.BasisPct = CleanClamp(f.BasisPct, 20);
            f.DeltaCvd5m = CleanClamp(f.DeltaCvd5m, 1e12);
            f.DistToLiqPct = CleanClamp(f.DistToLiqPct, 1000);
        }

        public static double CleanClamp(double v, double lim)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            if (v > lim) return lim;
            if (v < -lim) return -lim;
            return v;
        }

        public static double[] ReturnsLog(double[] px)
        {
            int n = px.Length;
            if (n < 2) return Array.Empty<double>();
            var r = new double[n - 1];
            for (int i = 1; i < n; i++)
            {
                double p0 = px[i - 1], p1 = px[i];
                if (p0 <= 0 || p1 <= 0) { r[i - 1] = 0; continue; }
                r[i - 1] = Math.Log(p1 / p0);
            }
            return r;
        }

        public static double Mean(double[] v)
        {
            if (v.Length == 0) return 0;
            double s = 0;
            for (int i = 0; i < v.Length; i++) s += v[i];
            return s / v.Length;
        }

        public static double StdDevSample(double[] v)
        {
            int n = v.Length;
            if (n < 2) return 0;
            double m = Mean(v);
            double ss = 0;
            for (int i = 0; i < n; i++)
            {
                double d = v[i] - m;
                ss += d * d;
            }
            return Math.Sqrt(ss / (n - 1));
        }

        public static double Saturate(double v, double limit)
            => v > limit ? limit : v < -limit ? -limit : v;
    }
}

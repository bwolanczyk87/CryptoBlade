using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma;
using Skender.Stock.Indicators;

public class FeatureSnapshot
{
    // ====== Cechy surowe ======
    public string Symbol { get; private set; } = "";
    public double Adx1h { get; private set; }
    public double AtrPct1h { get; private set; }
    public double Atr1hAbs { get; private set; }     // dodatkowo: ATR absolutny (dla diagnostyki)
    public double ZDvwap { get; private set; }       // z-score (Last - D-VWAP)/σ_TP(dzień)
    public double ZSlopeDvwap { get; private set; }  // t-stat nachylenia D-VWAP (ostatnie K punktów)
    public double AutoCorr5m { get; private set; }   // autokorelacja lag-1 log-zwrotów 5m
    public double Bbw15mPct { get; private set; }    // percentyl szerokości BB 15m (mid-rank)
    public double Bbw15mRaw { get; private set; }    // „raw” Width (ostatnia wartość)
    public bool HasInsideOrNr7 { get; private set; } // inside/NR7 15m/1h
    public double SpreadBps { get; private set; }    // globalny gate kosztowy

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

        // 3) D-VWAP (1m, kotwica względem ostatniej świecy)
        DateTime anchor = FindCurrentSessionAnchorUtc(q1m, sessionStartHourUtc);
        var (vwapSeries, lastVwap, tpStdDay) = ComputeAnchoredDailyVwapSeries(q1m, anchor);
        f.HasVwap = vwapSeries.Length >= 10 && !double.IsNaN(lastVwap);

        // 4) ZDvwap: saturacja do |z|≤10
        f.ZDvwap = ComputeZDvwap(ticker?.LastPrice, lastVwap, tpStdDay);

        // 5) ZSlopeDvwap: t-stat slope z OLS na ostatnich K punktach VWAP (clamp)
        var tStat = ComputeSlopeTstat(vwapSeries, Math.Max(10, vwapSlopeWindow));
        f.ZSlopeDvwap = Saturate(tStat, 100.0);

        // 6) Autokorelacja lag-1 na 5m
        f.AutoCorr5m = ComputeAutoCorrLag1(q5m);

        // 7) Percentyl BB width (15m) + raw
        (f.Bbw15mPct, f.Bbw15mRaw, bool hasVol) = ComputeBbwPercentileAndRaw(q15m, bbPeriod: 20, bbStdMult: 2.0);
        f.HasVol15m = hasVol;

        // 8) Inside/NR7 (15m lub 1h) z filtrem na puste wolumeny
        f.HasInsideOrNr7 = DetectInsideOrNr7(q15m) || DetectInsideOrNr7(q1h);

        // 9) Spread (bps) – globalny gate
        var (spr, okSpr) = await TryGet(async () => await data.GetSpreadBpsAsync(symbol, cancel));
        f.SpreadBps = spr;

        // 10) Derywaty/flow (z odróżnieniem „brak danych”)
        var (oi, okOi) = await TryGet(async () => await data.GetOpenInterestDelta1hPctAsync(symbol, cancel));
        f.OiDelta1hPct = oi;

        var (fund, okFund) = await TryGet(async () => await data.GetFundingRateAsync(symbol, cancel));
        f.Funding8h = fund;

        var (basis, okBasis) = await TryGet(async () => await data.GetBasisPctAsync(symbol, cancel));
        f.BasisPct = basis;

        var (cvd, okCvd) = await TryGet(async () => await data.GetDeltaCvd5mAsync(symbol, cancel));
        f.DeltaCvd5m = cvd;

        var lastPx = (ticker?.LastPrice) ?? 0m;
        var (distLiq, okLiq) = await TryGet(async () => await data.GetDistToNearestLiquidationPctAsync(symbol, lastPx, cancel));
        f.DistToLiqPct = distLiq;

        f.HasDerivatives = okOi || okFund || okBasis;
        f.HasCvd = okCvd;
        f.HasLiq = okLiq;

        // NaN/Infinity/outliers → clamp
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
        if (intraday.Length < 20) // minimalny „rozruch”
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
        return Saturate(z, 10.0); // clamp Z
    }

    /// <summary>t-stat nachylenia (slope/SE) OLS na ostatnich K punktach serii.</summary>
    public static double ComputeSlopeTstat(double[] series, int lastK)
    {
        if (series == null) return 0;
        int nAll = series.Length;
        int k = Math.Min(lastK, nAll);
        if (k < 10) return 0;

        var y = series[^k..];
        var pts = new List<(double x, double y)>(k);
        for (int i = 0; i < k; i++)
        {
            double yi = y[i];
            if (!double.IsNaN(yi) && !double.IsInfinity(yi))
                pts.Add((i, yi));
        }
        if (pts.Count < 10) return 0;

        int n = pts.Count;
        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        for (int i = 0; i < n; i++)
        {
            sx += pts[i].x;
            sy += pts[i].y;
            sxx += pts[i].x * pts[i].x;
            sxy += pts[i].x * pts[i].y;
        }
        double denom = (n * sxx - sx * sx);
        if (denom == 0) return 0;

        double slope = (n * sxy - sx * sy) / denom;
        double intercept = (sy - slope * sx) / n;

        double xbar = sx / n;
        double sxxCentered = 0.0;
        for (int i = 0; i < n; i++)
        {
            double dx = pts[i].x - xbar;
            sxxCentered += dx * dx;
        }
        if (sxxCentered == 0) return 0;

        double rss = 0.0;
        for (int i = 0; i < n; i++)
        {
            double yhat = intercept + slope * pts[i].x;
            double e = pts[i].y - yhat;
            rss += e * e;
        }
        double dof = n - 2;
        if (dof <= 0) return 0;

        double sigma2 = rss / dof;
        const double EPS_SIGMA2 = 1e-12;
        if (sigma2 < EPS_SIGMA2) sigma2 = EPS_SIGMA2;

        double seSlope = Math.Sqrt(sigma2 / sxxCentered);
        const double EPS_SE = 1e-12;
        if (seSlope < EPS_SE) seSlope = EPS_SE;

        double t = slope / seSlope;
        return t;
    }

    public static double ComputeAutoCorrLag1(Quote[] q5m)
    {
        if (q5m == null || q5m.Length < 3) return 0;

        int n = q5m.Length;
        double[] px = new double[n];
        for (int i = 0; i < n; i++) px[i] = (double)q5m[i].Close;

        double[] r = ReturnsLog(px);
        if (r.Length < 3) return 0;

        double mean = Mean(r);
        double num = 0.0, den = 0.0;
        for (int i = 1; i < r.Length; i++)
            num += (r[i] - mean) * (r[i - 1] - mean);
        for (int i = 0; i < r.Length; i++)
            den += (r[i] - mean) * (r[i] - mean);

        if (den == 0.0) return 0;
        return num / den;
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

        // mid-rank percentyl: (lt + 0.5*eq) / n
        int lt = 0, eq = 0;
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
        // Early-exit: jeśli większość świec bez wolumenu, NR7 ma znikomy sens
        if (last7.Count(x => x.Volume <= 0) >= 5) return inside;

        decimal lastRange = a.High - a.Low;
        decimal minRange = last7.Min(x => x.High - x.Low);
        bool nr7 = lastRange <= minRange;

        return inside || nr7;
    }

    // ========= Pomocnicze (statystyka, porządkowanie) =========

    public static void SortIfNeeded(Quote[] q)
    {
        if (q == null || q.Length < 2) return;
        bool sorted = true;
        for (int i = 1; i < q.Length; i++)
            if (q[i].Date < q[i - 1].Date) { sorted = false; break; }
        if (!sorted)
            Array.Sort(q, (a, b) => a.Date.CompareTo(b.Date));
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

    // TryGet: rozróżnia „brak danych” od prawdziwego zera
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
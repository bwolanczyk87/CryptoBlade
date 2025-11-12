using CryptoBlade.Exchanges;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma
{
    public sealed class BybitSigmaDataProvider : IBybitSigmaDataProvider
    {
        private readonly ICbFuturesRestClient _rest;
        public BybitSigmaDataProvider(ICbFuturesRestClient rest) => _rest = rest;

        public async Task<double> GetOpenInterestDelta1hPctAsync(string symbol, CancellationToken cancel)
        {
            var points = await _rest.GetOpenInterestAsync(symbol, TimeFrame.OneHour, limit: 2, cancel: cancel);
            if (points == null || points.Length < 2) return double.NaN;

            var prev = points[^2].OpenInterestUsd;
            var last = points[^1].OpenInterestUsd;
            if (prev <= 0) return double.NaN;

            var pct = (double)((last - prev) / prev * 100m);
            return double.IsFinite(pct) ? pct : double.NaN;
        }

        public async Task<double> GetFundingRateAsync(string symbol, CancellationToken cancel)
        {
            var end = DateTime.UtcNow;
            var start = end.AddHours(-24);
            var rates = await _rest.GetFundingRatesAsync(symbol, start, end, cancel);
            var last = rates?.OrderBy(x => x.Time).LastOrDefault()?.Rate ?? 0m;
            return (double)last;
        }

        public async Task<double> GetBasisPctAsync(string symbol, CancellationToken cancel)
        {
            var t = await _rest.GetTickerAsync(symbol, cancel);
            if (t == null || t.LastPrice <= 0) return double.NaN;
            var basis = (t.MarkPrice - t.LastPrice) / t.LastPrice;
            var pct = (double)(basis * 100m);
            return double.IsFinite(pct) ? pct : double.NaN;
        }

        public Task<double> GetDeltaCvd5mAsync(string symbol, CancellationToken cancel)
        {
            var v = SigmaLiveCache.TryGetDeltaCvd5m(symbol, DateTime.UtcNow);
            return Task.FromResult(v.ok ? v.dCvd5m : double.NaN);
        }

        public Task<double> GetDistToNearestLiquidationPctAsync(string symbol, decimal lastPrice, CancellationToken cancel)
        {
            var v = SigmaLiveCache.TryGetDistToLiqPct(symbol, lastPrice, DateTime.UtcNow);
            return Task.FromResult(v.ok ? v.distPct : double.NaN);
        }

        public async Task<double> GetSpreadBpsAsync(string symbol, CancellationToken cancel)
        {
            var ticker = await _rest.GetTickerAsync(symbol, cancel);

            // 1) live z cache (jeśli mamy last do normalizacji) – OK
            decimal last = ticker?.LastPrice ?? 0m;
            var live = last > 0 ? SigmaLiveCache.TryGetSpreadBps(symbol, last) : (spreadBps: 0.0, ok: false);
            if (live.ok) return live.spreadBps;

            // 2) Fallback: licz przez MID (nie przez LastPrice!)
            if (ticker != null && ticker.BestAskPrice > 0 && ticker.BestBidPrice > 0)
            {
                var spr = ticker.BestAskPrice - ticker.BestBidPrice;
                var mid = (ticker.BestAskPrice + ticker.BestBidPrice) / 2m;
                if (mid > 0)
                {
                    var bps = (double)((spr / mid) * 10_000m);
                    return double.IsFinite(bps) ? bps : double.NaN;
                }
            }
            return double.NaN;
        }

        public async Task<Quote[]> GetKlinesAsync(string symbol, TimeFrame tf, int limit, CancellationToken cancel)
        {
            var klines = await _rest.GetKlinesAsync(symbol, tf, limit, cancel);
            if (klines == null || klines.Length == 0) return Array.Empty<Quote>();

            var quotes = new List<Quote>(klines.Length);
            for (int i = 0; i < klines.Length; i++)
            {
                cancel.ThrowIfCancellationRequested();
                quotes.Add(klines[i].ToQuote());
            }

            quotes.Sort((a, b) => a.Date.CompareTo(b.Date));
            return [.. quotes];
        }

        public async Task<(double Corr, double LastBtcRet)> GetCorrToBtc15mAsync(string symbol, int window, CancellationToken cancel)
        {
            if (IsBtcSymbol(symbol))
                return (double.NaN, double.NaN); // supervisor off dla BTC

            // Pobierz świeczki 15m dla symbolu i BTC
            // window+1 żeby mieć tyle samo stóp log co 'window'
            int lim = Math.Max(window + 1, 100);
            var qSym = await GetKlinesAsync(symbol, TimeFrame.FifteenMinutes, lim, cancel);
            var qBtc = await GetKlinesAsync("BTCUSDT", TimeFrame.FifteenMinutes, lim, cancel);

            if (qSym.Length < 3 || qBtc.Length < 3)
                return (double.NaN, double.NaN);

            var rA = Stats.ReturnsLog(qSym.Select(z => (double)z.Close).ToArray());
            var rB = Stats.ReturnsLog(qBtc.Select(z => (double)z.Close).ToArray());

            int m = Math.Min(rA.Length, rB.Length);
            if (m < 3) return (double.NaN, double.NaN);

            int win = Math.Min(window, m);
            if (win < 3) return (double.NaN, double.NaN);

            var corr = Stats.Corr(rA[^win..], rB[^win..]);
            var lastBtcRet = rB[^1];

            return (corr, lastBtcRet);
        }

        private static bool IsBtcSymbol(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var u = s.Replace("-", "").Replace("_", "").ToUpperInvariant();
            return u.StartsWith("BTC"); // obsłuży BTCUSDT, BTCUSD, BTC-PERP itd.
        }
    }
}

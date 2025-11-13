using CryptoBlade.Exchanges;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Helpers;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma
{
    public sealed class BybitSigmaDataProvider(ICbFuturesRestClient rest)
    {
        private readonly ICbFuturesRestClient _rest = rest;

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

        public async Task<(FundingRate? Predicted, FundingRate? LastSettled)> GetFundingSnapshotAsync(string symbol, CancellationToken cancel)
        {
            FundingRate? predicted = null;
            FundingRate? lastSettled = null;

            // 1) Predicted (UI „current funding rate”) + czas kolejnego cyklu z tickera
            var t = await _rest.GetTickerAsync(symbol, cancel);
            if (t != null && t.FundingRate.HasValue && t.NextFundingTime.HasValue)
            {
                // Normalizujemy do % (np. 0.0001 → 0.01)
                var pct = (t.FundingRate.Value) * 100m;
                predicted = new FundingRate
                {
                    Time = DateTime.SpecifyKind(t.NextFundingTime.Value, DateTimeKind.Utc),
                    Rate = pct
                };
            }

            // 2) Ostatnio rozliczona (historyczna) stawka z ostatnich 24h
            var end = DateTime.UtcNow;
            var start = end.AddHours(-24);
            var rates = await _rest.GetFundingRatesAsync(symbol, start, end, cancel);
            if (rates != null && rates.Length > 0)
            {
                var last = rates.OrderBy(x => x.Time).Last();
                lastSettled = new FundingRate
                {
                    Time = DateTime.SpecifyKind(last.Time, DateTimeKind.Utc),
                    Rate = (last.Rate) * 100m
                };
            }

            return (predicted, lastSettled);
        }

        public async Task<double> GetBasisPctAsync(string symbol, CancellationToken cancel)
        {
            var t = await _rest.GetTickerAsync(symbol, cancel);
            if (t == null || t.IndexPrice <= 0 || t.MarkPrice <= 0) return double.NaN;
            var pct = (double)((t.MarkPrice - t.IndexPrice) / t.IndexPrice * 100m);
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
            // 1) Spróbuj z live-cache (najlepsza jakość i zero latency)
            var live = SigmaLiveCache.TryGetSpreadBps(symbol);
            if (live.ok) return live.spreadBps;

            // 2) Fallback: użyj aktualnego tickera i policz po MID (nie po Last!)
            var t = await _rest.GetTickerAsync(symbol, cancel);
            if (t != null && t.BestBidPrice > 0m && t.BestAskPrice > 0m)
            {
                var spr = t.BestAskPrice - t.BestBidPrice;
                var mid = (t.BestAskPrice + t.BestBidPrice) / 2m;
                if (mid > 0m)
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

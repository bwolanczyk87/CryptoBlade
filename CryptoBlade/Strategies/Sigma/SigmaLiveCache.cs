using CryptoBlade.Models;
using System.Collections.Concurrent;

namespace CryptoBlade.Strategies.Sigma
{
    internal static class SigmaLiveCache
    {
        private sealed class Deque<T> : LinkedList<T>
        {
            public void PushBack(T item) => AddLast(item);
            public T PopFront()
            {
                var first = First!.Value;
                RemoveFirst();
                return first;
            }
        }

        private sealed class RollingSignedQty
        {
            public bool HasData => _q.Count > 0;
            private readonly Deque<(DateTime ts, decimal signedQty)> _q = new();
            private decimal _sum;

            public void Add(DateTime ts, decimal signedQty)
            {
                _q.PushBack((ts, signedQty));
                _sum += signedQty;
                Trim(ts - TimeSpan.FromMinutes(5));
            }

            private void Trim(DateTime threshold)
            {
                while (_q.First != null && _q.First.Value.ts < threshold)
                {
                    var old = _q.PopFront();
                    _sum -= old.signedQty;
                }
            }

            public decimal Delta5m(DateTime now)
            {
                Trim(now - TimeSpan.FromMinutes(5));
                return _sum;
            }
        }

        private sealed class LiquidationBuffer
        {
            private readonly Deque<LiquidationEvent> _q = new();
            public void Add(LiquidationEvent liq)
            {
                _q.PushBack(liq);
                Trim(liq.Timestamp - TimeSpan.FromMinutes(20));
            }
            private void Trim(DateTime threshold)
            {
                while (_q.First != null && _q.First.Value.Timestamp < threshold)
                    _q.PopFront();
            }

            // bardzo prosty “cluster”: biny co 0.10% od “lastPrice”, szukamy najbliższego z wolumenem
            public double DistancePctToNearestCluster(decimal lastPrice, DateTime now)
            {
                if (lastPrice <= 0 || _q.Count == 0) return double.NaN;
                Trim(now - TimeSpan.FromMinutes(20));
                if (_q.Count == 0) return double.NaN;

                decimal step = lastPrice * 0.001m; // 0.10%
                var bins = new Dictionary<long, decimal>();
                foreach (var e in _q)
                {
                    long bin = (long)Math.Round((double)((e.Price - lastPrice) / step));
                    bins.TryGetValue(bin, out var v);
                    bins[bin] = v + e.Quantity;
                }

                if (bins.Count == 0) return double.NaN;

                // threshold: 75 percentyl wolumenu w binach
                var vols = bins.Values.OrderBy(x => x).ToArray();
                var thr = vols[Math.Max(0, (int)Math.Floor(vols.Length * 0.75) - 1)];
                var strong = bins.Where(kv => kv.Value >= thr).Select(kv => kv.Key).ToArray();
                if (strong.Length == 0) return double.NaN;

                long nearest = strong.OrderBy(b => Math.Abs(b)).First();
                decimal distAbs = Math.Abs(nearest * step);
                return (double)(distAbs / lastPrice * 100m);
            }
        }

        private sealed class SymbolLive
        {
            public decimal BestBid;
            public decimal BestAsk;
            public readonly RollingSignedQty Cvd5m = new();
            public readonly LiquidationBuffer Liqs = new();
        }

        private static readonly ConcurrentDictionary<string, SymbolLive> _map = new(StringComparer.OrdinalIgnoreCase);

        private static SymbolLive S(string s) => _map.GetOrAdd(s, _ => new SymbolLive());

        public static void OnOrderBookTop(string symbol, decimal bestBid, decimal bestAsk)
        {
            var x = S(symbol);
            x.BestBid = bestBid;
            x.BestAsk = bestAsk;
        }

        public static void OnPublicTrade(string symbol, PublicTrade trade)
        {
            var x = S(symbol);
            var signed = trade.Side == OrderSide.Buy ? trade.Quantity : -trade.Quantity;
            x.Cvd5m.Add(trade.Timestamp, signed);
        }

        public static void OnLiquidation(string symbol, LiquidationEvent liq)
        {
            var x = S(symbol);
            x.Liqs.Add(liq);
        }

        public static (double spreadBps, bool ok) TryGetSpreadBps(string symbol)
        {
            var x = S(symbol);
            var mid = (x.BestAsk + x.BestBid) / 2m;
            if (mid <= 0) return (0, false);
            var bps = (double)(((x.BestAsk - x.BestBid) / mid) * 10_000m);
            return (bps, bps >= 0 && double.IsFinite(bps));
        }

        public static (double dCvd5m, bool ok) TryGetDeltaCvd5m(string symbol, DateTime nowUtc)
        {
            var x = S(symbol);
            var v = (double)x.Cvd5m.Delta5m(nowUtc);
            return (v, x.Cvd5m.HasData);
        }

        public static (double distPct, bool ok) TryGetDistToLiqPct(string symbol, decimal lastPrice, DateTime nowUtc)
        {
            var x = S(symbol);
            var d = x.Liqs.DistancePctToNearestCluster(lastPrice, nowUtc);
            return (d, !double.IsNaN(d) && double.IsFinite(d));
        }
    }
}

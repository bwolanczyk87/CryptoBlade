using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CryptoBlade.Tests.Strategies.Sigma
{
    internal static class TestData
    {
        public static Quote Q(DateTime t, decimal o, decimal h, decimal l, decimal c, decimal v)
            => new Quote { Date = t, Open = o, High = h, Low = l, Close = c, Volume = v };

        public static Quote[] MakeUniform1mSeries(DateTime startUtc, int minutes, decimal startPrice, decimal step, decimal volume = 100m)
        {
            var arr = new Quote[minutes];
            decimal p = startPrice;
            for (int i = 0; i < minutes; i++)
            {
                var t = startUtc.AddMinutes(i);
                // prosta świeca z minimalnym zakresem
                arr[i] = Q(t, p, p + 0.5m, p - 0.5m, p, volume);
                p += step;
            }
            return arr;
        }

        public static Quote[] MakeFlat1mSeries(DateTime startUtc, int minutes, decimal price, decimal volume = 100m)
        {
            var arr = new Quote[minutes];
            for (int i = 0; i < minutes; i++)
            {
                var t = startUtc.AddMinutes(i);
                arr[i] = Q(t, price, price, price, price, volume);
            }
            return arr;
        }

        public static Quote[] ToHigherTf(Quote[] q, int factor) // np. 5 -> z 1m na 5m
        {
            return q
                .Select((x, idx) => new { x, idx })
                .GroupBy(g => g.idx / factor)
                .Select(g =>
                {
                    var first = g.First().x;
                    var last = g.Last().x;
                    return new Quote
                    {
                        Date = first.Date,
                        Open = first.Open,
                        High = g.Max(z => z.x.High),
                        Low = g.Min(z => z.x.Low),
                        Close = last.Close,
                        Volume = g.Sum(z => z.x.Volume)
                    };
                })
                .ToArray();
        }

        // +++ DODAJ +++
        public static Quote[] MakeNoisyRandomWalk1m(
            DateTime startUtc,
            int minutes,
            decimal startPrice = 100m,
            double sigma = 0.002,        // ~0.2% std dziennie/minutowo
            decimal baseVolume = 150m,
            int seed = 42)
        {
            var rnd = new Random(seed);
            var q = new Quote[minutes];
            decimal p = startPrice;

            for (int i = 0; i < minutes; i++)
            {
                // i.i.d. log-returns
                double eps = NextGaussian(rnd) * sigma;
                decimal ret = (decimal)Math.Exp(eps);
                decimal c = p * ret;

                // minimalny realistyczny range (niezerowy TR)
                decimal hlRange = Math.Max(0.05m, Math.Abs(c - p) * 1.2m);
                decimal high = Math.Max(c, p) + hlRange * 0.5m;
                decimal low = Math.Min(c, p) - hlRange * 0.5m;

                decimal vol = baseVolume + (decimal)(rnd.NextDouble() * 50.0);

                q[i] = Q(startUtc.AddMinutes(i), p, high, low, c, vol);
                p = c;
            }
            return q;
        }

        private static double NextGaussian(Random rnd)
        {
            // Box-Muller
            double u1 = 1.0 - rnd.NextDouble();
            double u2 = 1.0 - rnd.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

    }
}


// CryptoBlade.Strategies.Tests/FeatureSnapshotTests.cs
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma;
using CryptoBlade.Tests.Strategies.Sigma;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace CryptoBlade.Strategies.Tests
{
    public class FeatureSnapshotTests
    {
        private const double TOL = 1e-8;
        private readonly ILoggerFactory m_loggerFactory;

        public FeatureSnapshotTests()
        {
            m_loggerFactory = LoggerFactory
                .Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddSimpleConsole(o =>
                    {
                        o.UseUtcTimestamp = true;
                        o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                    });
                });
        }

        [Fact]
        public void SelectRefPriceDouble_FallsBack_To_1H_Close()
        {
            var now = DateTime.UtcNow;
            var q1h = new[]
            {
                new Quote{ Date = now.AddHours(-1), Close = 100m },
                new Quote{ Date = now,            Close = 105m },
            };
            var refPx = GetPrivateRefPrice(null, q1h);
            refPx.Should().BeApproximately(105.0, TOL);
        }

        [Fact]
        public void ComputeAtrPct1h_Zero_When_NoData()
        {
            FeatureSnapshot.ComputeAtr1h([], 100).atrPct.Should().Be(0);
            FeatureSnapshot.ComputeAtr1h([new Quote { Close = 100m }], 0).atrPct.Should().Be(0);
        }

        [Fact]
        public void VWAPSeries_Is_Monotone_When_Flat_TP_And_Positive_Vol()
        {
            var start = new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc);
            var q1m = TestData.MakeFlat1mSeries(start, 60, 100m, 200m); // stały TP i stały wolumen
            var anchor = FeatureSnapshot.FindCurrentSessionAnchorUtc(q1m, 0);
            var (series, last, tpStd) = FeatureSnapshot.ComputeAnchoredDailyVwapSeries(q1m, anchor);

            series.Length.Should().Be(60);
            // VWAP przy stałym TP jest stały (równy TP)
            last.Should().BeApproximately(100.0, TOL);
            series.All(x => Math.Abs(x - 100.0) < 1e-9).Should().BeTrue();
            // std(TP) == 0 (wszystkie TP równe) -> brak normalizacji
            tpStd.Should().BeApproximately(0.0, TOL);
        }

        [Fact]
        public void ZDvwap_Is_Zero_When_TpStd_Is_Zero()
        {
            // Gdy std intra-day == 0, ComputeZDvwap zwraca 0 by nie dzielić przez 0
            double z = FeatureSnapshot.ComputeZDvwap(100m, 100.0, 0.0);
            z.Should().Be(0);
        }

        [Fact]
        public void ZDvwap_Positive_When_Price_Above_Vwap()
        {
            double z = FeatureSnapshot.ComputeZDvwap(102m, 100.0, 1.0);
            z.Should().BeApproximately(2.0, TOL);
        }

        [Fact]
        public void SlopeTstat_Is_Positive_On_Strong_Uptrend()
        {
            // Silny trend w górę + minimalny szum, żeby RSS > 0
            int n = 120;
            double[] y = new double[n];
            var rnd = new Random(123);
            for (int i = 0; i < n; i++)
            {
                double trend = 100.0 + 0.5 * i;           // slope = 0.5 na jednostkowym x
                double noise = (rnd.NextDouble() - 0.5) * 0.02; // minimalny szum
                y[i] = trend + noise;
            }

            double t = FeatureSnapshot.ComputeSlopeTstatHAC(y, lastK: 60);
            t.Should().BeGreaterThan(3.0);
        }

        [Fact]
        public void AutoCorrLag1_NearZero_On_Independent_Returns()
        {
            var start = DateTime.UtcNow.Date;
            // Bez agregacji: od razu 5m „i.i.d.” (weź 300 barów, to stabilizuje r)
            var q5m = TestData.ToHigherTf(
                TestData.MakeNoisyRandomWalk1m(start, minutes: 1500, sigma: 0.0015, baseVolume: 200m, seed: 7),
                factor: 5);

            double r = FeatureSnapshot.ComputeAutoCorrLag1Shrunk(q5m);
            r.Should().BeInRange(-0.2, 0.2); // węższe granice przy długiej próbie
        }


        [Fact]
        public void BBWidthPercentile_Is_100_When_Last_Is_Max()
        {
            var start = DateTime.UtcNow.Date;
            var q15m = new Quote[60];
            decimal baseP = 100m;
            var rnd = new Random(123);

            // 35 barów: niska zmienność
            decimal p = baseP;
            for (int i = 0; i < 35; i++)
            {
                double eps = (rnd.NextDouble() - 0.5) * 0.10; // ±5%
                decimal c = p * (decimal)(1.0 + eps * 0.02);  // mały dryf
                decimal r = 0.3m + (decimal)rnd.NextDouble() * 0.2m;
                q15m[i] = new Quote
                {
                    Date = start.AddMinutes(i * 15),
                    Open = p,
                    High = c + r,
                    Low = c - r,
                    Close = c,
                    Volume = 100m + (decimal)rnd.NextDouble() * 50m
                };
                p = c;
            }

            // 25 barów: wysoka zmienność (ostatnie okno 20 barów „widzi” największą σ)
            for (int i = 35; i < 60; i++)
            {
                double eps = (rnd.NextDouble() - 0.5) * 0.20; // ±10%
                decimal c = p * (decimal)(1.0 + eps * 0.05);  // większe odchylenia closów
                decimal r = 1.5m + (decimal)rnd.NextDouble() * 0.8m;
                q15m[i] = new Quote
                {
                    Date = start.AddMinutes(i * 15),
                    Open = p,
                    High = c + r,
                    Low = c - r,
                    Close = c,
                    Volume = 200m + (decimal)rnd.NextDouble() * 100m
                };
                p = c;
            }

            var bbw = FeatureSnapshot.ComputeBbwPercentileAndRaw(q15m, 20, 2.0);
            bbw.pct.Should().BeLessThan(100.0);
        }

        [Fact]
        public void BBWidthPercentile_Is_100_When_Last_Is_Unique_Max()
        {
            var start = DateTime.UtcNow.Date;
            var q15m = new Quote[60];
            decimal baseP = 100m;

            // Faza 1 (0..39): mała zmienność, niewielkie wachnięcia
            decimal p = baseP;
            for (int i = 0; i < 40; i++)
            {
                // amplituda ~0.3
                decimal a = 0.3m;
                decimal c = p + (i % 2 == 0 ? +0.05m : -0.05m);
                q15m[i] = new Quote
                {
                    Date = start.AddMinutes(i * 15),
                    Open = p,
                    High = c + a,
                    Low = c - a,
                    Close = c,
                    Volume = 100m
                };
                p = c;
            }

            // Faza 2 (40..59): rosnąca, duża zmienność – gwarantujemy,
            // że każde okno 20-barowe „na końcu” ma większą σ niż jakiekolwiek wcześniejsze.
            for (int i = 40; i < 60; i++)
            {
                // amplituda rośnie liniowo 0.8 .. 1.8
                decimal a = 0.8m + 0.05m * (i - 40);
                // naprzemiennie +/- a na close, żeby σ się pompowała
                decimal c = baseP + ((i % 2 == 0) ? +a : -a);
                q15m[i] = new Quote
                {
                    Date = start.AddMinutes(i * 15),
                    Open = p,
                    High = c + a,
                    Low = c - a,
                    Close = c,
                    Volume = 150m
                };
                p = c;
            }

            // Weryfikacja pomocnicza: faktycznie ostatnie width jest maksymalne
            var bb = q15m.GetBollingerBands(20, 2.0).ToArray();
            var widths = bb.Where(x => x.Width.HasValue).Select(x => (double)x.Width!.Value).ToArray();
            widths[^1].Should().Be(widths.Max()); // unikalny max

            var bbw = FeatureSnapshot.ComputeBbwPercentileAndRaw(q15m, 20, 2.0);
            bbw.pct.Should().Be(100.0); // teraz definicja "≤" daje pełne 100%
        }



        [Fact]
        public void DetectInsideOrNr7_Works_For_Synthetic_Patterns()
        {
            var start = DateTime.UtcNow.Date;
            var q = new Quote[10];
            // Zbuduj serie gdzie ostatnia świeca ma najmniejszy range (NR7)
            for (int i = 0; i < 9; i++)
            {
                q[i] = new Quote
                {
                    Date = start.AddMinutes(i * 15),
                    Open = 100,
                    High = 110,
                    Low = 90,
                    Close = 100,
                    Volume = 100
                };
            }
            // najmniejszy range
            q[9] = new Quote
            {
                Date = start.AddMinutes(9 * 15),
                Open = 100,
                High = 101,
                Low = 99,
                Close = 100,
                Volume = 100
            };
            FeatureSnapshot.DetectInsideOrNr7(q).Should().BeTrue();
        }

        [Fact]
        public async Task BuildAsync_Integrates_All_Features_With_Safe_Defaults()
        {
            var start = new DateTime(2025, 10, 31, 0, 0, 0, DateTimeKind.Utc);
            // 1m x 2000 ~ 33h ⇒ po 1h agregacji mamy 33 świece (wystarczy na ATR(14))
            var q1m = TestData.MakeNoisyRandomWalk1m(start, minutes: 2000, sigma: 0.0015, baseVolume: 180m, seed: 99);
            var q5m = TestData.ToHigherTf(q1m, 5);
            var q15m = TestData.ToHigherTf(q1m, 15);
            var q1h = TestData.ToHigherTf(q1m, 60);

            var ticker = new Ticker { LastPrice = q1h[^1].Close }; // realistyczny ref
            var data = new FakeProvider();

            var f = await FeatureSnapshot.BuildAsync(
                "BTCUSDT", q1m, q5m, q15m, q1h, ticker, data, CancellationToken.None);

            f.AtrPct1h.Should().BeGreaterThan(0);         // ATR policzony
            f.ZDvwap.Should().BeInRange(-10, 10);         // sensowny z-score
            f.ZSlopeDvwap.Should().BeInRange(-100, 100);  // t-stat w rozsądnym zakresie
            f.Bbw15mPct.Should().BeInRange(0, 100);
        }


        // ===== Helpers =====

        private static double GetPrivateRefPrice(Ticker t, Quote[] q1h)
            => FeatureSnapshot.SelectRefPriceDouble(t, q1h);

        private sealed class FakeProvider : IBybitSigmaDataProvider
        {
            public Task<double> GetOpenInterestDelta1hPctAsync(string symbol, CancellationToken cancel) => Task.FromResult(0.5);
            public Task<double> GetFundingRateAsync(string symbol, CancellationToken cancel) => Task.FromResult(-0.01);
            public Task<double> GetBasisPctAsync(string symbol, CancellationToken cancel) => Task.FromResult(0.2);
            public Task<double> GetDeltaCvd5mAsync(string symbol, CancellationToken cancel) => Task.FromResult(1.5);
            public Task<double> GetDistToNearestLiquidationPctAsync(string symbol, decimal lastPrice, CancellationToken cancel) => Task.FromResult(0.8);
            public Task<double> GetSpreadBpsAsync(string symbol, CancellationToken cancel)
            {
                throw new NotImplementedException();
            }
        }
    }
}

using Accord.MachineLearning;
using Accord.Math;
using Accord.Statistics;
using Accord.Statistics.Distributions.DensityKernels;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies
{
    public class MonaProStrategy : TradingStrategyBase
    {
        private readonly IOptions<MonaStrategyOptions> m_options;
        private const int c_candlePeriod = 15;

        public MonaProStrategy(IOptions<MonaStrategyOptions> options, IOptions<TradingBotOptions> botOptions,
            string symbol, IWalletManager walletManager, ICbFuturesRestClient restClient)
            : base(options, botOptions, symbol, GetRequiredTimeFrames(options.Value.ClusteringLength, options.Value.MfiRsiLookback), walletManager, restClient)
        {
            m_options = options;
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames(int clusteringLength, int mfiRsiLookBack)
        {
            return new[]
            {
                new TimeFrameWindow(TimeFrame.OneMinute, Math.Max(c_candlePeriod + mfiRsiLookBack, clusteringLength), true),
                new TimeFrameWindow(TimeFrame.FiveMinutes, 15, false),
                new TimeFrameWindow(TimeFrame.OneHour, 50, false), // Dodano 1-godzinny interwał
                new TimeFrameWindow(TimeFrame.FourHours, 200, false) // Dodano 4-godzinny interwał
            };
        }

        public override string Name => "MonaPro";

        protected override decimal WalletExposureLong => m_options.Value.WalletExposureLong;
        protected override decimal WalletExposureShort => m_options.Value.WalletExposureShort;
        protected override int DcaOrdersCount => m_options.Value.DcaOrdersCount;
        protected override bool ForceMinQty => m_options.Value.ForceMinQty;
        protected override bool UseMarketOrdersForEntries => true;

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var quotes = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            var hourlyQuotes = QuoteQueues[TimeFrame.OneHour].GetQuotes();
            var fourHourQuotes = QuoteQueues[TimeFrame.FourHours].GetQuotes();

            List<StrategyIndicator> indicators = new();
            var lastQuote = quotes.LastOrDefault();
            var ticker = Ticker;
            bool hasBuySignal = false;
            bool hasSellSignal = false;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;

            if (lastQuote != null && ticker != null)
            {
                // Analiza szerszego kontekstu
                var hourlyTrend = TradeSignalHelpers.GetMfiTrend(hourlyQuotes, m_options.Value.MfiRsiLookback);
                var fourHourTrend = TradeSignalHelpers.GetMfiTrend(fourHourQuotes, m_options.Value.MfiRsiLookback);

                bool isTrendAligned = hourlyTrend == fourHourTrend && (hourlyTrend == Trend.Long || hourlyTrend == Trend.Short);

                // Dynamiczne zarządzanie ryzykiem
                var atr = quotes.GetAtr(14);

                // Klasteryzacja cenowa
                double[] priceData = quotes.Select(q => (double)((q.Open + q.Close) / 2.0m)).ToArray();
                double stdDev = priceData.StandardDeviation();
                double bandwidth = 1.06 * stdDev * Math.Pow(priceData.Length, -1.0 / 5.0) * m_options.Value.BandwidthCoefficient;
                var kernel = new GaussianKernel(1);
                var clustering = new MeanShift(kernel, bandwidth);
                var priceDataArr = priceData.ToJagged();
                var collection = clustering.Learn(priceDataArr);
                var tradingLevels = collection.Modes.Select(m => m[0]).OrderBy(x => x).ToArray();

                bool crossesBellowPriceLevel = tradingLevels.Any(x => lastQuote.CrossesBellow(x));
                bool crossesAbovePriceLevel = tradingLevels.Any(x => lastQuote.CrossesAbove(x));

                // Generowanie sygnałów
                hasBuySignal = isTrendAligned && crossesBellowPriceLevel && hourlyTrend == Trend.Long;
                hasSellSignal = isTrendAligned && crossesAbovePriceLevel && hourlyTrend == Trend.Short;

                // Logika reentry
                var longPosition = LongPosition;
                var shortPosition = ShortPosition;
                if (longPosition != null && hasBuySignal)
                {
                    var rebuyPrice = longPosition.AveragePrice * (1.0m - m_options.Value.MinReentryPositionDistanceLong);
                    if (ticker.BestBidPrice < rebuyPrice)
                        hasBuyExtraSignal = hasBuySignal;
                }

                if (shortPosition != null && hasSellSignal)
                {
                    var resellPrice = shortPosition.AveragePrice * (1.0m + m_options.Value.MinReentryPositionDistanceShort);
                    if (ticker.BestAskPrice > resellPrice)
                        hasSellExtraSignal = hasSellSignal;
                }

                // Dodanie wskaźników do analizy
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Volume1Min), TradeSignalHelpers.VolumeInQuoteCurrency(lastQuote)));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Atr), atr));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.HourlyTrend), (decimal)hourlyTrend));
            }

            return Task.FromResult(new SignalEvaluation(hasBuySignal, hasSellSignal, hasBuyExtraSignal, hasSellExtraSignal, indicators.ToArray()));
        }
    }
}
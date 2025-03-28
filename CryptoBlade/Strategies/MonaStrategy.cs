﻿using Accord.MachineLearning;
using Accord.Statistics;
using Accord.Statistics.Distributions.DensityKernels;
using Accord.Statistics.Kernels;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Accord.Math;
using Accord.Statistics.Models.Regression.Linear;
using Skender.Stock.Indicators;
using CryptoBlade.Configuration;

namespace CryptoBlade.Strategies
{
    public class MonaStrategy : TradingStrategyBase
    {
        private readonly IOptions<MonaStrategyOptions> m_options;
        private const int c_candlePeriod = 15;

        public MonaStrategy(IOptions<MonaStrategyOptions> options, IOptions<TradingBotOptions> botOptions,
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
            };
        }

        public override string Name
        {
            get { return "Mona"; }
        }

        protected override decimal WalletExposureLong
        {
            get { return m_options.Value.WalletExposureLong; }
        }

        protected override decimal WalletExposureShort
        {
            get { return m_options.Value.WalletExposureShort; }
        }

        protected override int DcaOrdersCount
        {
            get { return m_options.Value.DcaOrdersCount; }
        }

        protected override bool ForceMinQty
        {
            get { return m_options.Value.ForceMinQty; }
        }

        protected override bool UseMarketOrdersForEntries => true;

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var quotes = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            List<StrategyIndicator> indicators = new();
            var lastQuote = quotes.LastOrDefault();
            var ticker = Ticker;
            bool hasBuySignal = false;
            bool hasSellSignal = false;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;

            if (lastQuote != null && ticker != null)
            {
                bool canBeTraded = (lastQuote.Date - SymbolInfo.LaunchTime).TotalDays > m_options.Value.InitialUntradableDays;
                var spread5Min = TradeSignalHelpers.Get5MinSpread(quotes);
                var volume = TradeSignalHelpers.VolumeInQuoteCurrency(lastQuote);
                bool hasMinSpread = spread5Min > m_options.Value.MinimumPriceDistance;
                bool hasMinVolume = volume >= m_options.Value.MinimumVolume;
                var mfiRsiTrend = TradeSignalHelpers.GetMfiTrend(quotes, m_options.Value.MfiRsiLookback);
                bool hasBasicConditions = canBeTraded && hasMinSpread && hasMinVolume && (mfiRsiTrend == Trend.Long || mfiRsiTrend == Trend.Short);
                bool crossesBellowPriceLevel = false;
                bool crossesAbovePriceLevel = false;
                if (hasBasicConditions)
                {
                    double[] priceData = new double[quotes.Length];
                    for (int i = 0; i < quotes.Length; i++)
                    {
                        var averagePrice = (quotes[i].Open + quotes[i].Close) / 2.0m;
                        priceData[i] = (double)averagePrice;
                    }
                    double stdDev = priceData.StandardDeviation();
                    double bandwidth = 1.06 * stdDev * Math.Pow(priceData.Length, -1.0 / 5.0);
                    var kernel = new GaussianKernel(1);
                    bandwidth *= m_options.Value.BandwidthCoefficient;
                    var clustering = new MeanShift(kernel, bandwidth);
                    var priceDataArr = priceData.ToJagged();
                    var collection = clustering.Learn(priceDataArr);
                    List<double> tradingLevelsList = new();
                    foreach (double[] collectionMode in collection.Modes)
                        tradingLevelsList.Add(collectionMode[0]);
                    var tradingLevels = tradingLevelsList.OrderBy(x => x).ToArray();
                    if (tradingLevels.Length > 0)
                    {
                        double top = tradingLevels.Max();
                        if ((double)ticker.BestBidPrice < top)
                            crossesBellowPriceLevel = tradingLevels.Any(x => lastQuote.CrossesBellow(x));
                        double bottom = tradingLevels.Min();
                        if ((double)ticker.BestAskPrice > bottom)
                            crossesAbovePriceLevel = tradingLevels.Any(x => lastQuote.CrossesAbove(x));
                    }
                }

                hasBuySignal = hasMinVolume
                               && hasMinSpread
                               && canBeTraded
                               && crossesBellowPriceLevel
                               && mfiRsiTrend == Trend.Long;

                hasSellSignal = hasMinVolume
                                && hasMinSpread
                                && canBeTraded
                                && crossesAbovePriceLevel
                                && mfiRsiTrend == Trend.Short;

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

                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Volume1Min), volume));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.MainTimeFrameVolume), volume));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Spread5Min), spread5Min));
            }

            return Task.FromResult(new SignalEvaluation(hasBuySignal, hasSellSignal, hasBuyExtraSignal, hasSellExtraSignal, indicators.ToArray()));
        }
    }
}

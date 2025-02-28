using CryptoBlade.Configuration;
using Accord.MachineLearning;

namespace CryptoBlade.Strategies {
public interface ITradingStrategyFactory
    {
        ITradingStrategy CreateStrategy(TradingBotOptions config, string symbol);
    }

public class MonaStrategy : TradingStrategyBase
    {
        private readonly IOptions<MonaStrategyOptions> m_options;
        private const int c_candlePeriod = 15;

        public MonaStrategy(IOptions<MonaStrategyOptions> options,
            string symbol, IWalletManager walletManager, ICbFuturesRestClient restClient)
            : base(options, symbol, GetRequiredTimeFrames(options.Value.ClusteringLength, options.Value.MfiRsiLookback), walletManager, restClient)
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

public class MonaStrategyOptions : TradingStrategyBaseOptions
    {
        public decimal MinimumVolume { get; set; }

        public decimal MinimumPriceDistance { get; set; }

        public decimal MinReentryPositionDistanceLong { get; set; } = 0.02m;

        public decimal MinReentryPositionDistanceShort { get; set; } = 0.05m;

        public int ClusteringLength { get; set; } = 480;

        public double BandwidthCoefficient { get; set; } = 0.3;

        public int MfiRsiLookback { get; set; } = 5;
    }

public static class StrategyNames
    {
        public const string AutoHedge = "AutoHedge";
        public const string MfiRsiCandlePrecise = "MfiRsiCandlePrecise";
        public const string MfiRsiEriTrend = "MfiRsiEriTrend";
        public const string LinearRegression = "LinearRegression";
        public const string Tartaglia = "Tartaglia";
        public const string Mona = "Mona";
        public const string Qiqi = "Qiqi";
    }

public enum StrategySelectPreference
    {
        Volume,
        NormalizedAverageTrueRange,
    }

public class TradingStrategyBaseOptions : TradingStrategyCommonBaseOptions
    {
        public int DcaOrdersCount { get; set; }

        public bool ForceMinQty { get; set; }

        public decimal MinProfitRate { get; set; } = 0.0006m;

        public decimal QtyFactorLong { get; set; } = 1.0m;
        
        public decimal QtyFactorShort { get; set; } = 1.0m;
        
        public bool EnableRecursiveQtyFactorLong { get; set; }
        
        public bool EnableRecursiveQtyFactorShort { get; set; }
    }

public class TradingStrategyFactory : ITradingStrategyFactory
    {
        private readonly IWalletManager m_walletManager;
        private readonly ICbFuturesRestClient m_restClient;

        public TradingStrategyFactory(IWalletManager walletManager, ICbFuturesRestClient restClient)
        {
            m_walletManager = walletManager;
            m_restClient = restClient;
        }

        public ITradingStrategy CreateStrategy(TradingBotOptions config, string symbol)
        {
            string strategyName = config.StrategyName;
            if (string.Equals(StrategyNames.AutoHedge, strategyName, StringComparison.OrdinalIgnoreCase))
                return CreateAutoHedgeStrategy(config, symbol);

            if (string.Equals(StrategyNames.MfiRsiCandlePrecise, strategyName, StringComparison.OrdinalIgnoreCase))
                return CreateMfiRsiCandlePreciseStrategy(config, symbol);

            if (string.Equals(StrategyNames.MfiRsiEriTrend, strategyName, StringComparison.OrdinalIgnoreCase))
                return CreateMfiRsiEriTrendPreciseStrategy(config, symbol);

            if (string.Equals(StrategyNames.LinearRegression, strategyName, StringComparison.OrdinalIgnoreCase))
                return CreateLinearRegressionStrategy(config, symbol);

            if (string.Equals(StrategyNames.Tartaglia, strategyName, StringComparison.OrdinalIgnoreCase))
                return CreateTartagliaStrategy(config, symbol);

            if (string.Equals(StrategyNames.Mona, strategyName, StringComparison.OrdinalIgnoreCase))
                return CreateMonaStrategy(config, symbol);

            if (string.Equals(StrategyNames.Qiqi, strategyName, StringComparison.OrdinalIgnoreCase))
                return CreateQiqiStrategy(config, symbol);

            return CreateAutoHedgeStrategy(config, symbol);
        }

        private ITradingStrategy CreateAutoHedgeStrategy(TradingBotOptions config, string symbol)
        {
            var options = CreateTradeOptions<AutoHedgeStrategyOptions>(config, symbol,
                strategyOptions =>
                {
                    strategyOptions.MinimumPriceDistance = config.MinimumPriceDistance;
                    strategyOptions.MinimumVolume = config.MinimumVolume;
                    strategyOptions.MinReentryPositionDistanceLong = config.Strategies.AutoHedge.MinReentryPositionDistanceLong;
                    strategyOptions.MinReentryPositionDistanceShort = config.Strategies.AutoHedge.MinReentryPositionDistanceShort;
                });
            return new AutoHedgeStrategy(options, symbol, m_walletManager, m_restClient);
        }

        private ITradingStrategy CreateMfiRsiCandlePreciseStrategy(TradingBotOptions config, string symbol)
        {
            var options = CreateTradeOptions<MfiRsiCandlePreciseTradingStrategyOptions>(config, symbol,
                strategyOptions =>
                {
                    strategyOptions.MinimumPriceDistance = config.MinimumPriceDistance;
                    strategyOptions.MinimumVolume = config.MinimumVolume;
                });
            return new MfiRsiCandlePreciseTradingStrategy(options, symbol, m_walletManager, m_restClient);
        }

        private ITradingStrategy CreateMfiRsiEriTrendPreciseStrategy(TradingBotOptions config, string symbol)
        {
            var options = CreateTradeOptions<MfiRsiEriTrendTradingStrategyOptions>(config, symbol,
                strategyOptions =>
                {
                    strategyOptions.MinimumPriceDistance = config.MinimumPriceDistance;
                    strategyOptions.MinimumVolume = config.MinimumVolume;
                    strategyOptions.MinReentryPositionDistanceLong = config.Strategies.MfiRsiEriTrend.MinReentryPositionDistanceLong;
                    strategyOptions.MinReentryPositionDistanceShort = config.Strategies.MfiRsiEriTrend.MinReentryPositionDistanceShort;
                    strategyOptions.MfiRsiLookbackPeriod = config.Strategies.MfiRsiEriTrend.MfiRsiLookbackPeriod;
                    strategyOptions.UseEriOnly = config.Strategies.MfiRsiEriTrend.UseEriOnly;
                });
            return new MfiRsiEriTrendTradingStrategy(options, symbol, m_walletManager, m_restClient);
        }

        private ITradingStrategy CreateLinearRegressionStrategy(TradingBotOptions config, string symbol)
        {
            var options = CreateTradeOptions<LinearRegressionStrategyOptions>(config, symbol,
                strategyOptions =>
                {
                    strategyOptions.MinimumPriceDistance = config.MinimumPriceDistance;
                    strategyOptions.MinimumVolume = config.MinimumVolume;
                    strategyOptions.ChannelLength = config.Strategies.LinearRegression.ChannelLength;
                    strategyOptions.StandardDeviation = config.Strategies.LinearRegression.StandardDeviation;
                });
            return new LinearRegressionStrategy(options, symbol, m_walletManager, m_restClient);
        }

        private ITradingStrategy CreateTartagliaStrategy(TradingBotOptions config, string symbol)
        {
            var options = CreateTradeOptions<TartagliaStrategyOptions>(config, symbol,
                strategyOptions =>
                {
                    strategyOptions.MinimumPriceDistance = config.MinimumPriceDistance;
                    strategyOptions.MinimumVolume = config.MinimumVolume;
                    strategyOptions.ChannelLengthLong = config.Strategies.Tartaglia.ChannelLengthLong;
                    strategyOptions.ChannelLengthShort = config.Strategies.Tartaglia.ChannelLengthShort;
                    strategyOptions.StandardDeviationLong = config.Strategies.Tartaglia.StandardDeviationLong;
                    strategyOptions.StandardDeviationShort = config.Strategies.Tartaglia.StandardDeviationShort;
                    strategyOptions.MinReentryPositionDistanceLong = config.Strategies.Tartaglia.MinReentryPositionDistanceLong;
                    strategyOptions.MinReentryPositionDistanceShort = config.Strategies.Tartaglia.MinReentryPositionDistanceShort;
                });
            return new TartagliaStrategy(options, symbol, m_walletManager, m_restClient);
        }

        private ITradingStrategy CreateMonaStrategy(TradingBotOptions config, string symbol)
        {
            var options = CreateTradeOptions<MonaStrategyOptions>(config, symbol,
                strategyOptions =>
                {
                    strategyOptions.MinimumPriceDistance = config.MinimumPriceDistance;
                    strategyOptions.MinimumVolume = config.MinimumVolume;
                    strategyOptions.BandwidthCoefficient = config.Strategies.Mona.BandwidthCoefficient;
                    strategyOptions.MinReentryPositionDistanceLong = config.Strategies.Mona.MinReentryPositionDistanceLong;
                    strategyOptions.MinReentryPositionDistanceShort = config.Strategies.Mona.MinReentryPositionDistanceShort;
                    strategyOptions.ClusteringLength = config.Strategies.Mona.ClusteringLength;
                    strategyOptions.MfiRsiLookback = config.Strategies.Mona.MfiRsiLookback;
                });
            return new MonaStrategy(options, symbol, m_walletManager, m_restClient);
        }

        private ITradingStrategy CreateQiqiStrategy(TradingBotOptions config, string symbol)
        {
            var options = CreateRecursiveTradeOptions<QiqiStrategyOptions>(config, symbol,
                strategyOptions =>
                {
                    strategyOptions.QflBellowPercentEnterLong = config.Strategies.Qiqi.QflBellowPercentEnterLong;
                    strategyOptions.RsiTakeProfitLong = config.Strategies.Qiqi.RsiTakeProfitLong;
                    strategyOptions.QflAbovePercentEnterShort = config.Strategies.Qiqi.QflAbovePercentEnterShort;
                    strategyOptions.RsiTakeProfitShort = config.Strategies.Qiqi.RsiTakeProfitShort;
                    strategyOptions.MaxTimeStuck = config.Strategies.Qiqi.MaxTimeStuck;
                    strategyOptions.TakeProfitPercentLong = config.Strategies.Qiqi.TakeProfitPercentLong;
                    strategyOptions.TakeProfitPercentShort = config.Strategies.Qiqi.TakeProfitPercentShort;
                });
            return new QiqiStrategy(options, symbol, m_walletManager, m_restClient);
        }

        private IOptions<TOptions> CreateRecursiveTradeOptions<TOptions>(TradingBotOptions config, string symbol, Action<TOptions> optionsSetup)
            where TOptions : RecursiveStrategyBaseOptions, new()
        {
            bool isBackTest = config.IsBackTest();
            int initialUntradableDays = isBackTest ? config.BackTest.InitialUntradableDays : 0;
            var options = new TOptions
            {
                WalletExposureLong = config.WalletExposureLong,
                WalletExposureShort = config.WalletExposureShort,
                TradingMode = GetTradingMode(config, symbol),
                MaxAbsFundingRate = config.MaxAbsFundingRate,
                FeeRate = config.MakerFeeRate,
                ForceUnstuckPercentStep = config.Unstucking.ForceUnstuckPercentStep,
                SlowUnstuckPercentStep = config.Unstucking.SlowUnstuckPercentStep,
                InitialUntradableDays = initialUntradableDays,
                IgnoreInconsistency = isBackTest,
                NormalizedAverageTrueRangePeriod = config.NormalizedAverageTrueRangePeriod,
                StrategySelectPreference = config.StrategySelectPreference,
                DDownFactorLong = config.Strategies.Recursive.DDownFactorLong,
                InitialQtyPctLong = config.Strategies.Recursive.InitialQtyPctLong,
                ReentryPositionPriceDistanceLong = config.Strategies.Recursive.ReentryPositionPriceDistanceLong,
                ReentryPositionPriceDistanceWalletExposureWeightingLong = config.Strategies.Recursive.ReentryPositionPriceDistanceWalletExposureWeightingLong,
                DDownFactorShort = config.Strategies.Recursive.DDownFactorShort,
                InitialQtyPctShort = config.Strategies.Recursive.InitialQtyPctShort,
                ReentryPositionPriceDistanceShort = config.Strategies.Recursive.ReentryPositionPriceDistanceShort,
                ReentryPositionPriceDistanceWalletExposureWeightingShort = config.Strategies.Recursive.ReentryPositionPriceDistanceWalletExposureWeightingShort,
            };
            optionsSetup(options);
            return Options.Create(options);
        }

        private IOptions<TOptions> CreateTradeOptions<TOptions>(TradingBotOptions config, string symbol, Action<TOptions> optionsSetup) 
            where TOptions : TradingStrategyBaseOptions, new()
        {
            bool isBackTest = config.IsBackTest();
            int initialUntradableDays = isBackTest ? config.BackTest.InitialUntradableDays : 0;
            var options = new TOptions
            {
                DcaOrdersCount = config.DcaOrdersCount,
                WalletExposureLong = config.WalletExposureLong,
                WalletExposureShort = config.WalletExposureShort,
                ForceMinQty = config.ForceMinQty,
                TradingMode = GetTradingMode(config, symbol),
                MaxAbsFundingRate = config.MaxAbsFundingRate,
                FeeRate = config.MakerFeeRate,
                MinProfitRate = config.MinProfitRate,
                ForceUnstuckPercentStep = config.Unstucking.ForceUnstuckPercentStep,
                SlowUnstuckPercentStep = config.Unstucking.SlowUnstuckPercentStep,
                InitialUntradableDays = initialUntradableDays,
                EnableRecursiveQtyFactorLong = config.EnableRecursiveQtyFactorLong,
                EnableRecursiveQtyFactorShort = config.EnableRecursiveQtyFactorShort,
                QtyFactorLong = config.QtyFactorLong,
                QtyFactorShort = config.QtyFactorShort,
                IgnoreInconsistency = isBackTest,
                NormalizedAverageTrueRangePeriod = config.NormalizedAverageTrueRangePeriod,
                StrategySelectPreference = config.StrategySelectPreference,
            };
            optionsSetup(options);
            return Options.Create(options);
        }

        private TradingMode GetTradingMode(TradingBotOptions config, string symbol)
        {
            var tradingMode = config.SymbolTradingModes.FirstOrDefault(x =>
                string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
            if(tradingMode != null)
                return tradingMode.TradingMode;
            return config.TradingMode;
        }
    }
}

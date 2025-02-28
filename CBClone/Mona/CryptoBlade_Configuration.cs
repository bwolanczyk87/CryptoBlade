using GeneticSharp;
using CryptoBlade.Optimizer;
using System.Text.Json;

namespace CryptoBlade.Configuration {
public class BackTest
    {
        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public decimal InitialBalance { get; set; } = 5000;

        public TimeSpan StartupCandleData { get; set; } = TimeSpan.FromDays(1);

        public string ResultFileName { get; set; } = "result.json";

        public string ResultDetailedFileName { get; set; } = "result_detailed.json";

        public int InitialUntradableDays { get; set; } = 30;

        public DataSource DataSource { get; set; } = DataSource.Bybit;
    }

public enum BotMode
    {
        Live,
        Backtest,
        Optimizer
    }

public class ConfigConstants
    {
        public const string DefaultHistoricalDataDirectory = "HistoricalData";
        public const string BackTestsDirectory = "BackTests";
    }

public class CriticalMode
    {
        public bool EnableCriticalModeLong { get; set; }

        public bool EnableCriticalModeShort { get; set; }

        public decimal WalletExposureThresholdLong { get; set; } = 0.3m;

        public decimal WalletExposureThresholdShort { get; set; } = 0.3m;
    }

public enum DataSource
    {
        Bybit,
        Binance,
    }

public class DynamicBotCount
    {
        public decimal TargetLongExposure { get; set; } = 1.0m;

        public decimal TargetShortExposure { get; set; } = 1.0m;

        public int MaxLongStrategies { get; set; } = 5;

        public int MaxShortStrategies { get; set; } = 5;

        public int MaxDynamicStrategyOpenPerStep { get; set; } = 1;

        public TimeSpan Step { get; set; } = TimeSpan.FromMinutes(5);
    }

public enum Exchange
    {
        Bybit,
    }

public class ExchangeAccount
    {
        public string Name { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
        public Exchange Exchange { get; set; } = Exchange.Bybit;
        public bool IsDemo { get; set; }

        public bool HasApiCredentials()
        {
            return !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);
        }
    }

public class FitnessOptions
    {
        public double RunningDaysPreference { get; set; } = 0.1;
        public double AvgDailyGainPreference { get; set; } = 0.7;
        public double LowestEquityToBalancePreference { get; set; } = 0.2;
        public double EquityBalanceNrmsePreference { get; set; } = 0.0;
        public double AdgNrmseErrorPreference { get; set; } = 0.0;
        public double MaxAvgDailyGainPercent { get; set; } = 5.0;
        public double MinAvgDailyGainPercent { get; set; } = -5.0;
    }

public class GeneticAlgorithmOptions
    {
        public int GenerationCount { get; set; } = 200;
        public float MutationProbability { get; set; } = 0.01f;
        public float CrossoverProbability { get; set; } = 0.95f;
        public int MinPopulationSize { get; set; } = 200;
        public int MaxPopulationSize { get; set; } = 250;
        public MutationStrategy MutationStrategy { get; set; } = MutationStrategy.UniformMutation;
        public SelectionStrategy SelectionStrategy { get; set; } = SelectionStrategy.RankSelection;
        public float MutationMultiplier { get; set; } = 2.0f;
        public float MaxMutationProbability { get; set; } = 0.8f;
        public FitnessOptions FitnessOptions { get; set; } = new FitnessOptions();
    }

public class Mona
    {
        public decimal MinReentryPositionDistanceLong { get; set; } = 0.02m;

        public decimal MinReentryPositionDistanceShort { get; set; } = 0.05m;

        public int ClusteringLength { get; set; } = 480;

        public double BandwidthCoefficient { get; set; } = 0.3;

        public int MfiRsiLookback { get; set; } = 5;
    }

public enum MutationStrategy
    {
        FlipBitMutation,
        UniformMutation,
    }

public class OptimizerOptions
    {
        public GeneticAlgorithmOptions GeneticAlgorithm { get; set; } = new GeneticAlgorithmOptions();
        public TartagliaOptimizerOptions Tartaglia { get; set; } = new TartagliaOptimizerOptions();
        public AutoHedgeOptimizerOptions AutoHedge { get; set; } = new AutoHedgeOptimizerOptions();
        public MfiRsiEriTrendOptimizerOptions MfiRsiEriTrend { get; set; } = new MfiRsiEriTrendOptimizerOptions();
        public TradingBotOptimizerOptions TradingBot { get; set; } = new TradingBotOptimizerOptions();
        public RecursiveStrategyOptimizerOptions RecursiveStrategy { get; set; } = new RecursiveStrategyOptimizerOptions();
        public QiqiOptimizerOptions Qiqi { get; set; } = new QiqiOptimizerOptions();
        public string SessionId { get; set; } = "Session01";
        public bool EnableHistoricalDataCaching { get; set; } = true;
        public int ParallelTasks { get; set; } = 10;
    }

public class RecursiveStrategyOptimizerOptions
    {
        public OptimizerFloatRange DDownFactorLong { get; set; } = new OptimizerFloatRange(0.1f, 3.0f, 3);

        public OptimizerFloatRange InitialQtyPctLong { get; set; } = new OptimizerFloatRange(0.001f, 0.2f, 3);

        public OptimizerFloatRange ReentryPositionPriceDistanceLong { get; set; } = new OptimizerFloatRange(0.0001f, 0.2f, 4);

        public OptimizerFloatRange ReentryPositionPriceDistanceWalletExposureWeightingLong { get; set; } = new OptimizerFloatRange(0.0f, 3.0f, 3);

        public OptimizerFloatRange DDownFactorShort { get; set; } = new OptimizerFloatRange(0.1f, 3.0f, 3);

        public OptimizerFloatRange InitialQtyPctShort { get; set; } = new OptimizerFloatRange(0.001f, 0.2f, 3);

        public OptimizerFloatRange ReentryPositionPriceDistanceShort { get; set; } = new OptimizerFloatRange(0.0001f, 0.2f, 4);

        public OptimizerFloatRange ReentryPositionPriceDistanceWalletExposureWeightingShort { get; set; } = new OptimizerFloatRange(0.0f, 3.0f, 3);
    }

public class RecursiveStrategyOptions
    {
        public double DDownFactorLong { get; set; } = 2.0;

        public double InitialQtyPctLong { get; set; } = 0.003;

        public double ReentryPositionPriceDistanceLong { get; set; } = 0.01;

        public double ReentryPositionPriceDistanceWalletExposureWeightingLong { get; set; } = 2.11;

        public double DDownFactorShort { get; set; } = 2.0;

        public double InitialQtyPctShort { get; set; } = 0.003;

        public double ReentryPositionPriceDistanceShort { get; set; } = 0.01;

        public double ReentryPositionPriceDistanceWalletExposureWeightingShort { get; set; } = 2.11;
    }

public enum SelectionStrategy
    {
        TournamentSelection,
        RankSelection,
    }

public class StrategyOptions
    {
        public AutoHedge AutoHedge { get; set; } = new AutoHedge();
        public LinearRegression LinearRegression { get; set; } = new LinearRegression();
        public Tartaglia Tartaglia { get; set; } = new Tartaglia();
        public Mona Mona { get; set; } = new Mona();
        public MfiRsiEriTrend MfiRsiEriTrend { get; set; } = new MfiRsiEriTrend();
        public RecursiveStrategyOptions Recursive { get; set; } = new RecursiveStrategyOptions();
        public Qiqi Qiqi { get; set; } = new Qiqi();
    }

public class SymbolTradingMode
    {
        public string Symbol { get; set; } = string.Empty;
        public TradingMode TradingMode { get; set; } = TradingMode.Normal;
    }

public class TradingBotOptimizerOptions
    {
        public OptimizerFloatRange WalletExposureLong { get; set; } = new OptimizerFloatRange(0, 3, 2);
        public OptimizerFloatRange WalletExposureShort { get; set; } = new OptimizerFloatRange(0, 3, 2);
        public OptimizerFloatRange QtyFactorLong { get; set; } = new OptimizerFloatRange(0.001f, 3, 3);
        public OptimizerFloatRange QtyFactorShort { get; set; } = new OptimizerFloatRange(0.001f, 3, 3);
        public OptimizerBoolRange EnableRecursiveQtyFactorLong { get; set; } = new OptimizerBoolRange(false, true);
        public OptimizerBoolRange EnableRecursiveQtyFactorShort { get; set; } = new OptimizerBoolRange(false, true);
        public OptimizerIntRange DcaOrdersCount { get; set; } = new OptimizerIntRange(1, 5000);
        public OptimizerBoolRange UnstuckingEnabled { get; set; } = new OptimizerBoolRange(false, true);
        public OptimizerFloatRange SlowUnstuckThresholdPercent { get; set; } = new OptimizerFloatRange(-1.0f, -0.01f, 2);
        public OptimizerFloatRange SlowUnstuckPositionThresholdPercent { get; set; } = new OptimizerFloatRange(-1.0f, -0.01f, 2);
        public OptimizerFloatRange SlowUnstuckPercentStep { get; set; } = new OptimizerFloatRange(0.01f, 1.0f, 2);
        public OptimizerFloatRange ForceUnstuckThresholdPercent { get; set; } = new OptimizerFloatRange(-1.0f, -0.01f, 2);
        public OptimizerFloatRange ForceUnstuckPositionThresholdPercent { get; set; } = new OptimizerFloatRange(-1.0f, -0.01f, 2);
        public OptimizerFloatRange ForceUnstuckPercentStep { get; set; } = new OptimizerFloatRange(0.01f, 1.0f, 2);
        public OptimizerBoolRange ForceKillTheWorst { get; set; } = new OptimizerBoolRange(false, true);
        public OptimizerIntRange MinimumVolume { get; set; } = new OptimizerIntRange(1000, 30000);
        public OptimizerFloatRange MinimumPriceDistance { get; set; } = new OptimizerFloatRange(0.015f, 0.03f, 3);
        public OptimizerFloatRange MinProfitRate { get; set; } = new OptimizerFloatRange(0.0006f, 0.01f, 4);
        public OptimizerFloatRange TargetLongExposure { get; set; } = new OptimizerFloatRange(0.0f, 3.0f, 2);
        public OptimizerFloatRange TargetShortExposure { get; set; } = new OptimizerFloatRange(0.0f, 3.0f, 2);
        public OptimizerIntRange MaxLongStrategies { get; set; } = new OptimizerIntRange(0, 15);
        public OptimizerIntRange MaxShortStrategies { get; set; } = new OptimizerIntRange(0, 15);
        public OptimizerBoolRange EnableCriticalModeLong { get; set; } = new OptimizerBoolRange(false, true);
        public OptimizerBoolRange EnableCriticalModeShort { get; set; } = new OptimizerBoolRange(false, true);
        public OptimizerFloatRange CriticalModelWalletExposureThresholdLong { get; set; } = new OptimizerFloatRange(0.0f, 3.0f, 2);
        public OptimizerFloatRange CriticalModelWalletExposureThresholdShort { get; set; } = new OptimizerFloatRange(0.0f, 3.0f, 2);
        public OptimizerFloatRange SpotRebalancingRatio { get; set; } = new OptimizerFloatRange(0.0f, 1.0f, 2);
    }

public class TradingBotOptions
    {
        public BotMode BotMode { get; set; } = BotMode.Backtest;
        public ExchangeAccount[] Accounts { get; set; } = Array.Empty<ExchangeAccount>();
        public string AccountName { get; set; } = string.Empty;
        public int MaxRunningStrategies { get; set; } = 15;
        public int DcaOrdersCount { get; set; } = 1000;
        public DynamicBotCount DynamicBotCount { get; set; } = new DynamicBotCount();
        public decimal WalletExposureLong { get; set; } = 1.0m;
        public decimal WalletExposureShort { get; set; } = 1.0m;
        public string[] Whitelist { get; set; } = Array.Empty<string>();
        public string[] Blacklist { get; set; } = Array.Empty<string>();
        public SymbolTradingMode[] SymbolTradingModes { get; set; } = Array.Empty<SymbolTradingMode>();
        public decimal MinimumVolume { get; set; }
        public decimal MinimumPriceDistance { get; set; }
        public string StrategyName { get; set; } = "AutoHedge";
        public TradingMode TradingMode { get; set; } = TradingMode.Normal;
        public bool ForceMinQty { get; set; } = true;
        public decimal QtyFactorLong { get; set; } = 1.0m;
        public decimal QtyFactorShort { get; set; } = 1.0m;
        public bool EnableRecursiveQtyFactorLong { get; set; }
        public bool EnableRecursiveQtyFactorShort { get; set; }
        public int PlaceOrderAttempts { get; set; } = 3;
        public decimal MaxAbsFundingRate { get; set; } = 0.0004m;
        public decimal MakerFeeRate { get; set; } = 0.0002m;
        public decimal TakerFeeRate { get; set; } = 0.00055m;
        public decimal MinProfitRate { get; set; } = 0.0006m;
        public decimal SpotRebalancingRatio { get; set; } = 0.5m;
        public StrategySelectPreference StrategySelectPreference { get; set; } = StrategySelectPreference.Volume;
        public int NormalizedAverageTrueRangePeriod { get; set; } = 14;
        public decimal MinNormalizedAverageTrueRangePeriod { get; set; } = 1.0m;
        public BackTest BackTest { get; set; } = new BackTest();
        public Unstucking Unstucking { get; set; } = new Unstucking();
        public StrategyOptions Strategies { get; set; } = new StrategyOptions();
        public CriticalMode CriticalMode { get; set; } = new CriticalMode();
        public OptimizerOptions Optimizer { get; set; } = new OptimizerOptions();
    }

public enum TradingMode
    {
        Normal,
        Dynamic,
        Readonly,
        DynamicBackTest,
    }

public class Unstucking
    {
        public bool Enabled { get; set; } = true;
        public decimal SlowUnstuckThresholdPercent { get; set; } = -0.1m;
        public decimal SlowUnstuckPositionThresholdPercent { get; set; } = -0.01m;
        public decimal ForceUnstuckThresholdPercent { get; set; } = -0.3m;
        public decimal ForceUnstuckPositionThresholdPercent { get; set; } = -0.005m;
        public decimal SlowUnstuckPercentStep { get; set; } = 0.05m;
        public decimal ForceUnstuckPercentStep { get; set; } = 0.1m;
        public bool ForceKillTheWorst { get; set; } = false;
    }
}

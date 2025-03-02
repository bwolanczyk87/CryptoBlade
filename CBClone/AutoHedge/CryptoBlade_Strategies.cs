// *** METADATA ***
// Version: 1.0.0
// Generated: 2025-03-02 11:24:55 UTC
// Module: CryptoBlade.Strategies
// ****************

// *** INDEX OF INCLUDED FILES ***
1. AutoHedgeStrategy.cs
2. AutoHedgeStrategyOptions.cs
3. ITradingStrategyFactory.cs
4. StrategyNames.cs
5. StrategySelectPreference.cs
6. TradingStrategyBaseOptions.cs
7. TradingStrategyFactory.cs
8. ExecuteParams.cs
9. IndicatorType.cs
10. ITradingStrategy.cs
11. RecursiveStrategyBase.cs
12. RecursiveStrategyBaseOptions.cs
13. ReentryMultiplier.cs
14. StrategyIndicator.cs
15. TimeFrameWindow.cs
16. TradingStrategyBase.cs
17. TradingStrategyCommonBase.cs
18. TradingStrategyCommonBaseOptions.cs
19. Trend.cs
20. BybitErrorCodes.cs
21. ExchangePolicies.cs
22. Balance.cs
23. IWalletManager.cs
24. NullWalletManager.cs
25. WalletManager.cs
26. WalletType.cs
// *******************************

using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;

// ==== FILE #1: AutoHedgeStrategy.cs ====
namespace CryptoBlade.Strategies {
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies
{
    public class AutoHedgeStrategy : TradingStrategyBase
    {
        private readonly IOptions<AutoHedgeStrategyOptions> m_options;
        private const int c_candlePeriod = 15;

        public AutoHedgeStrategy(IOptions<AutoHedgeStrategyOptions> options,
            string symbol, IWalletManager walletManager, ICbFuturesRestClient restClient) 
            : base(options, symbol, GetRequiredTimeFrames(), walletManager, restClient)
        {
            m_options = options;
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames()
        {
            return new[]
            {
                new TimeFrameWindow(TimeFrame.OneMinute, c_candlePeriod, true),
                new TimeFrameWindow(TimeFrame.FiveMinutes, c_candlePeriod, false),
            };
        }

        public override string Name
        {
            get { return "AutoHedge"; }
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
            get {return m_options.Value.ForceMinQty; }
        }

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var quotes = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            List<StrategyIndicator> indicators = new();
            var lastQuote = quotes.LastOrDefault();
            bool hasBuySignal = false;
            bool hasSellSignal = false;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;
            
            if (lastQuote != null)
            {
                bool canBeTraded = (lastQuote.Date - SymbolInfo.LaunchTime).TotalDays > m_options.Value.InitialUntradableDays;
                var spread5Min = TradeSignalHelpers.Get5MinSpread(quotes);
                var volume = TradeSignalHelpers.VolumeInQuoteCurrency(lastQuote);
                var sma = quotes.GetSma(14);
                var lastSma = sma.LastOrDefault();
                var trendPercent = TradeSignalHelpers.GetTrendPercent(lastSma, lastQuote);
                var trend = TradeSignalHelpers.GetTrend(trendPercent);
                var ma3High = quotes.Use(CandlePart.High).GetSma(3);
                var ma3Low = quotes.Use(CandlePart.Low).GetSma(3);
                var ma6High = quotes.Use(CandlePart.High).GetSma(6);
                var ma6Low = quotes.Use(CandlePart.Low).GetSma(6);
                
                var ma3HighLast = ma3High.LastOrDefault();
                var ma3LowLast = ma3Low.LastOrDefault();
                var ma6HighLast = ma6High.LastOrDefault();
                var ma6LowLast = ma6Low.LastOrDefault();

                bool hasAllRequiredMa = ma6HighLast != null 
                    && ma6HighLast.Sma.HasValue
                    && ma6LowLast != null
                    && ma6LowLast.Sma.HasValue
                    && ma3HighLast != null
                    && ma3HighLast.Sma.HasValue
                    && ma3LowLast != null
                    && ma3LowLast.Sma.HasValue;

                var ticker = Ticker;

                bool hasMinSpread = spread5Min > m_options.Value.MinimumPriceDistance;
                bool hasMinVolume = volume >= m_options.Value.MinimumVolume;
                bool shouldShort = false;
                bool shouldLong = false;
                bool shouldAddToShort = false;
                bool shouldAddToLong = false;
                bool hasMinShortPriceDistance = false;
                bool hasMinLongPriceDistance = false;
                if (ticker != null 
                    && ma3HighLast != null && ma3HighLast.Sma.HasValue
                    && ma3LowLast != null && ma3LowLast.Sma.HasValue
                    && ma6HighLast != null && ma6HighLast.Sma.HasValue
                    && ma6LowLast != null && ma6LowLast.Sma.HasValue)
                {
                    shouldShort = TradeSignalHelpers.ShortCounterTradeCondition(ticker.BestAskPrice, (decimal)ma3HighLast.Sma!.Value);
                    shouldLong = TradeSignalHelpers.LongCounterTradeCondition(ticker.BestBidPrice, (decimal)ma3LowLast.Sma!.Value);
                    shouldAddToShort = TradeSignalHelpers.ShortCounterTradeCondition(ticker.BestAskPrice, (decimal)ma6HighLast.Sma!.Value);
                    shouldAddToLong = TradeSignalHelpers.LongCounterTradeCondition(ticker.BestBidPrice, (decimal)ma6LowLast.Sma!.Value);
                }

                Position? longPosition = LongPosition;
                Position? shortPosition = ShortPosition;
                hasBuySignal = hasMinVolume
                               && shouldLong
                               && hasAllRequiredMa
                               && trend == Trend.Long
                               && hasMinSpread
                               && canBeTraded;

                hasSellSignal = hasMinVolume 
                                && shouldShort 
                                && hasAllRequiredMa 
                                && trend == Trend.Short 
                                && hasMinSpread
                                && canBeTraded;

                if (ticker != null && longPosition != null)
                {
                    var rebuyPrice = longPosition.AveragePrice * (1.0m - m_options.Value.MinReentryPositionDistanceLong);
                    if (ticker.BestBidPrice < rebuyPrice)
                        hasMinLongPriceDistance = hasBuySignal;
                }

                if (ticker != null && shortPosition != null)
                {
                    var resellPrice = shortPosition.AveragePrice * (1.0m + m_options.Value.MinReentryPositionDistanceShort);
                    if (ticker.BestAskPrice > resellPrice)
                        hasMinShortPriceDistance = hasSellSignal;
                }

                hasBuyExtraSignal = hasMinVolume 
                                    && shouldAddToLong 
                                    && hasAllRequiredMa 
                                    && trend == Trend.Long 
                                    && hasMinSpread 
                                    && longPosition != null
                                    && ticker != null
                                    && ticker.BestBidPrice < longPosition.AveragePrice
                                    && hasMinLongPriceDistance
                                    && canBeTraded;

                hasSellExtraSignal = hasMinVolume 
                                     && shouldAddToShort 
                                     && hasAllRequiredMa 
                                     && trend == Trend.Short 
                                     && hasMinSpread
                                     && shortPosition != null
                                     && ticker != null
                                     && ticker.BestAskPrice > shortPosition.AveragePrice 
                                     && hasMinShortPriceDistance
                                     && canBeTraded;

                if (hasAllRequiredMa)
                {
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma3High), ma3HighLast!.Sma!.Value));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma3Low), ma3LowLast!.Sma!.Value));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma6High), ma6HighLast!.Sma!.Value));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma6Low), ma6LowLast!.Sma!.Value));
                }

                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Volume1Min), volume));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.MainTimeFrameVolume), volume));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Spread5Min), spread5Min));
                if (trendPercent != null)
                {
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.TrendPercent), trendPercent));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Trend), trend));
                }
            }

            return Task.FromResult(new SignalEvaluation(hasBuySignal, hasSellSignal, hasBuyExtraSignal, hasSellExtraSignal, indicators.ToArray()));
        }
    }
}
}

// -----------------------------

// ==== FILE #2: AutoHedgeStrategyOptions.cs ====
namespace CryptoBlade.Strategies
{
    public class AutoHedgeStrategyOptions : TradingStrategyBaseOptions
    {
        public decimal MinimumVolume { get; set; }

        public decimal MinimumPriceDistance { get; set; }

        public decimal MinReentryPositionDistanceLong { get; set; }

        public decimal MinReentryPositionDistanceShort { get; set; }
    }
}

// -----------------------------

// ==== FILE #3: ITradingStrategyFactory.cs ====
namespace CryptoBlade.Strategies {
// Uwaga: Ten plik zawiera interfejs – zadbaj o pełną dokumentację!
using CryptoBlade.Strategies.Common;

namespace CryptoBlade.Strategies
{
    public interface ITradingStrategyFactory
    {
        ITradingStrategy CreateStrategy(TradingBotOptions config, string symbol);
    }
}
}

// -----------------------------

// ==== FILE #4: StrategyNames.cs ====
namespace CryptoBlade.Strategies
{
    public static class StrategyNames
    {
        public const string AutoHedge = "AutoHedge";
        public const string LinearRegression = "LinearRegression";
        public const string MfiRsiCandlePrecise = "MfiRsiCandlePrecise";
        public const string MfiRsiEriTrend = "MfiRsiEriTrend";
        public const string Mona = "Mona";
        public const string Qiqi = "Qiqi";
        public const string Tartaglia = "Tartaglia";
    }
}

// -----------------------------

// ==== FILE #5: StrategySelectPreference.cs ====
namespace CryptoBlade.Strategies
{
    public enum StrategySelectPreference
    {
        Volume,
        NormalizedAverageTrueRange,
    }
}

// -----------------------------

// ==== FILE #6: TradingStrategyBaseOptions.cs ====
namespace CryptoBlade.Strategies {
using CryptoBlade.Strategies.Common;

namespace CryptoBlade.Strategies
{
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
}
}

// -----------------------------

// ==== FILE #7: TradingStrategyFactory.cs ====
namespace CryptoBlade.Strategies {
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Strategies
{
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
}

// -----------------------------

// ==== FILE #8: ExecuteParams.cs ====
namespace CryptoBlade.Strategies.Common
{
    public record struct ExecuteParams(bool AllowLongOpen, bool AllowShortOpen, bool AllowExtraLong, bool AllowExtraShort, bool LongUnstucking, bool ShortUnstucking);
}

// -----------------------------

// ==== FILE #9: IndicatorType.cs ====
// Uwaga: Ten plik zawiera interfejs – zadbaj o pełną dokumentację!
namespace CryptoBlade.Strategies.Common
{
    public enum IndicatorType
    {
        Sell,
        SellExtra,
        Buy,
        BuyExtra,
        Volume1Min,
        Spread5Min,
        Mfi1Min,
        Rsi1Min,
        LastPrice,
        TrendPercent,
        Trend,
        Ma3High,
        Ma3Low,
        Ma6High,
        Ma6Low,
        LongTakeProfit,
        ShortTakeProfit,
        MainTimeFrameVolume,
        MfiTrend,
        EriTrend,
        LinearRegressionPriceShort,
        LinearRegressionPriceLong,
        NormalizedAverageTrueRange
    }
}

// -----------------------------

// ==== FILE #10: ITradingStrategy.cs ====
// Uwaga: Ten plik zawiera interfejs – zadbaj o pełną dokumentację!
namespace CryptoBlade.Strategies.Common
{
    public interface ITradingStrategy
    {
        string Name { get; }

        bool IsInTrade { get; }
        
        bool IsInLongTrade { get; }
        
        bool IsInShortTrade { get; }

        string Symbol { get; }

        SymbolInfo SymbolInfo { get; }

        decimal? DynamicQtyShort { get; }

        decimal? DynamicQtyLong { get; }

        decimal? RecommendedMinBalance { get; }

        bool HasSellSignal { get; }

        bool HasBuySignal { get; }

        bool HasSellExtraSignal { get; }
        
        bool HasBuyExtraSignal { get; }

        bool ConsistentData { get; }

        decimal? UnrealizedLongPnlPercent { get; }

        decimal? UnrealizedShortPnlPercent { get; }

        Ticker? Ticker { get; }

        DateTime LastTickerUpdate { get; }

        DateTime LastCandleUpdate { get; }

        StrategyIndicator[] Indicators { get; }

        TimeFrameWindow[] RequiredTimeFrameWindows { get; }

        decimal? CurrentExposureLong { get;  }
        
        decimal? CurrentExposureShort { get; }

        Task UpdateTradingStateAsync(Position? longPosition, Position? shortPosition, Order[] openOrders, CancellationToken cancel);

        Task SetupSymbolAsync(SymbolInfo symbol, CancellationToken cancel);

        Task InitializeAsync(Candle[] candles, Ticker ticker, CancellationToken cancel);

        Task ExecuteAsync(ExecuteParams executeParams, CancellationToken cancel);

        Task ExecuteUnstuckAsync(bool unstuckLong, bool unstuckShort, bool forceUnstuckLong, bool forceUnstuckShort, bool forceKill, CancellationToken cancel);

        Task AddCandleDataAsync(Candle candle, CancellationToken cancel);

        Task UpdatePriceDataSync(Ticker ticker, CancellationToken cancel);

        Task EvaluateSignalsAsync(CancellationToken cancel);
    }
}

// -----------------------------

// ==== FILE #11: RecursiveStrategyBase.cs ====
namespace CryptoBlade.Strategies {
using CryptoBlade.Helpers;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Strategies.Common
{
    public abstract class RecursiveStrategyBase : TradingStrategyCommonBase
    {
        private readonly IOptions<RecursiveStrategyBaseOptions> m_options;

        protected RecursiveStrategyBase(IOptions<RecursiveStrategyBaseOptions> options, string symbol,
            TimeFrameWindow[] requiredTimeFrames, IWalletManager walletManager,
            ICbFuturesRestClient cbFuturesRestClient) : base(options, symbol, requiredTimeFrames, walletManager,
            cbFuturesRestClient)
        {
            m_options = options;
        }

        protected override Task<decimal?> CalculateMinBalanceAsync()
        {
            var minLong = CalculateMinBalanceLongAsync();
            var minShort = CalculateMinBalanceShortAsync();
            decimal? minBalance = null;
            if (minLong.Result.HasValue && minShort.Result.HasValue)
                minBalance = Math.Max(minLong.Result.Value, minShort.Result.Value);
            else if (minLong.Result.HasValue)
                minBalance = minLong.Result.Value;
            else if (minShort.Result.HasValue)
                minBalance = minShort.Result.Value;
            return Task.FromResult(minBalance);
        }

        protected Task<decimal?> CalculateMinBalanceLongAsync()
        {
            var ticker = Ticker;
            if (ticker == null)
                return Task.FromResult<decimal?>(null);
            if (!SymbolInfo.MinOrderQty.HasValue)
                return Task.FromResult<decimal?>(null);
            if (WalletExposureLong <= 0)
                return Task.FromResult<decimal?>(null);
            if (m_options.Value.InitialQtyPctLong <= 0)
                return Task.FromResult<decimal?>(null);

            var minBalance = (double)ticker.BestAskPrice
                             * (double)SymbolInfo.MinOrderQty.Value
                             / (m_options.Value.InitialQtyPctLong
                                * (double)WalletExposureLong);
            var minBalanceRounded = (decimal?)Math.Round(minBalance);
            return Task.FromResult(minBalanceRounded);
        }

        protected Task<decimal?> CalculateMinBalanceShortAsync()
        {
            var ticker = Ticker;
            if (ticker == null)
                return Task.FromResult<decimal?>(null);
            if (!SymbolInfo.MinOrderQty.HasValue)
                return Task.FromResult<decimal?>(null);
            if (WalletExposureShort <= 0)
                return Task.FromResult<decimal?>(null);
            if (m_options.Value.InitialQtyPctShort <= 0)
                return Task.FromResult<decimal?>(null);

            var minBalance = (double)ticker.BestAskPrice
                             * (double)SymbolInfo.MinOrderQty.Value
                             / (m_options.Value.InitialQtyPctShort
                                * (double)WalletExposureShort);
            var minBalanceRounded = (decimal?)Math.Round(minBalance);
            return Task.FromResult(minBalanceRounded);
        }

        protected override async Task CalculateDynamicQtyAsync()
        {
            DynamicQtyShort = null;
            DynamicQtyLong = null;

            var balance = WalletManager.Contract.WalletBalance;
            var existingLongPosition = LongPosition;
            var minLongBalance = await CalculateMinBalanceLongAsync();
            var shouldCalculateMaxQtyLong = existingLongPosition != null || balance >= minLongBalance;
            if (shouldCalculateMaxQtyLong)
            {
                var longPosition = await CalculateNextGridLongPositionAsync();
                if (longPosition.HasValue)
                    DynamicQtyLong = (decimal)longPosition.Value.Qty;
                MaxQtyLong = long.MaxValue; // this is handled by grid
            }

            var existingShortPosition = ShortPosition;
            var minShortBalance = await CalculateMinBalanceShortAsync();
            var shouldCalculateMaxQtyShort = existingShortPosition != null || balance >= minShortBalance;
            if (shouldCalculateMaxQtyShort)
            {
                var shortPosition = await CalculateNextGridShortPositionAsync();
                if (shortPosition.HasValue)
                    DynamicQtyShort = (decimal)shortPosition.Value.Qty;
                MaxQtyShort = long.MaxValue; // this is handled by grid
            }
        }

        protected virtual Task<ReentryMultiplier> CalculateReentryMultiplierLongAsync()
        {
            return Task.FromResult(new ReentryMultiplier(1.0, 1.0));
        }

        protected virtual Task<ReentryMultiplier> CalculateReentryMultiplierShortAsync()
        {
            return Task.FromResult(new ReentryMultiplier(1.0, 1.0));
        }

        protected async Task<GridPosition?> CalculateNextGridLongPositionAsync()
        {
            var longPosition = LongPosition;
            var balance = WalletManager.Contract.WalletBalance;
            if (!balance.HasValue)
                return null;
            var symbolInfo = SymbolInfo;
            if (!symbolInfo.QtyStep.HasValue)
                return null;
            if (!symbolInfo.MinOrderQty.HasValue)
                return null;
            var ticker = Ticker;
            if (ticker == null)
                return null;
            double positionSize = 0;
            double entryPrice = 0;
            if (longPosition != null)
            {
                positionSize = (double)longPosition.Quantity;
                entryPrice = (double)longPosition.AveragePrice;
            }

            bool inverse = false;
            double qtyStep = (double)symbolInfo.QtyStep.Value;
            if (qtyStep == 0)
                return null;
            var priceStep = 1 / Math.Pow(10, (int)symbolInfo.PriceScale);
            double minQty = (double)symbolInfo.MinOrderQty.Value;
            double minCost = 0.0;
            double cMultiplier = 1.0;
            double initialQtyPct = m_options.Value.InitialQtyPctLong;
            double ddownFactor = m_options.Value.DDownFactorLong;
            double walletExposureLimit = (double)m_options.Value.WalletExposureLong;
            var reentryMultiplier = await CalculateReentryMultiplierLongAsync();
            double reentryPositionPriceDistance = m_options.Value.ReentryPositionPriceDistanceLong;
            reentryPositionPriceDistance *= reentryMultiplier.DistanceMultiplier;
            double reentryPositionPriceDistanceWalletExposureWeighting = 
                m_options.Value.ReentryPositionPriceDistanceWalletExposureWeightingLong * reentryMultiplier.WeightMultiplier;
            var highestBid = (double)ticker.BestBidPrice;
            var longEntry = GridHelpers.CalcRecursiveEntryLong(
                (double)balance.Value,
                positionSize,
                entryPrice,
                highestBid,
                inverse,
                qtyStep,
                priceStep,
                minQty,
                minCost,
                cMultiplier,
                initialQtyPct,
                ddownFactor,
                reentryPositionPriceDistance,
                reentryPositionPriceDistanceWalletExposureWeighting,
                walletExposureLimit);

            return longEntry;
        }

        protected async Task<GridPosition?> CalculateNextGridShortPositionAsync()
        {
            var shortPosition = ShortPosition;
            var balance = WalletManager.Contract.WalletBalance;
            if (!balance.HasValue)
                return null;
            var symbolInfo = SymbolInfo;
            if (!symbolInfo.QtyStep.HasValue)
                return null;
            if (!symbolInfo.MinOrderQty.HasValue)
                return null;
            var ticker = Ticker;
            if (ticker == null)
                return null;
            double positionSize = 0;
            double entryPrice = 0;
            if (shortPosition != null)
            {
                positionSize = (double)shortPosition.Quantity;
                entryPrice = (double)shortPosition.AveragePrice;
            }

            bool inverse = false;
            double qtyStep = (double)symbolInfo.QtyStep.Value;
            if (qtyStep == 0)
                return null;
            var priceStep = 1 / Math.Pow(10, (int)symbolInfo.PriceScale);
            double minQty = (double)symbolInfo.MinOrderQty.Value;
            double minCost = 0.0;
            double cMultiplier = 1.0;
            double initialQtyPct = m_options.Value.InitialQtyPctShort;
            double ddownFactor = m_options.Value.DDownFactorShort;
            double walletExposureLimit = (double)m_options.Value.WalletExposureShort;
            var reentryMultiplier = await CalculateReentryMultiplierShortAsync();
            double reentryPositionPriceDistance = m_options.Value.ReentryPositionPriceDistanceShort;
            reentryPositionPriceDistance *= reentryMultiplier.DistanceMultiplier;
            double reentryPositionPriceDistanceWalletExposureWeighting =
                m_options.Value.ReentryPositionPriceDistanceWalletExposureWeightingShort * reentryMultiplier.WeightMultiplier;
            var lowestAsk = (double)ticker.BestAskPrice;
            var shortEntry = GridHelpers.CalcRecursiveEntryShort(
                (double)balance.Value,
                positionSize,
                entryPrice,
                lowestAsk,
                inverse,
                qtyStep,
                priceStep,
                minQty,
                minCost,
                cMultiplier,
                initialQtyPct,
                ddownFactor,
                reentryPositionPriceDistance,
                reentryPositionPriceDistanceWalletExposureWeighting,
                walletExposureLimit);

            return shortEntry;
        }
    }
}
}

// -----------------------------

// ==== FILE #12: RecursiveStrategyBaseOptions.cs ====
namespace CryptoBlade.Strategies.Common
{
    public class RecursiveStrategyBaseOptions : TradingStrategyCommonBaseOptions
    {
        public double DDownFactorLong { get; set; }

        public double InitialQtyPctLong { get; set; }

        public double ReentryPositionPriceDistanceLong { get; set; }

        public double ReentryPositionPriceDistanceWalletExposureWeightingLong { get; set; }

        public double DDownFactorShort { get; set; }

        public double InitialQtyPctShort { get; set; }

        public double ReentryPositionPriceDistanceShort { get; set; }

        public double ReentryPositionPriceDistanceWalletExposureWeightingShort { get; set; }
    }
}

// -----------------------------

// ==== FILE #13: ReentryMultiplier.cs ====
namespace CryptoBlade.Strategies.Common
{
    public readonly record struct ReentryMultiplier(double DistanceMultiplier, double WeightMultiplier);
}

// -----------------------------

// ==== FILE #14: StrategyIndicator.cs ====
namespace CryptoBlade.Strategies.Common
{
    public readonly record struct StrategyIndicator(string Name, object Value);
}

// -----------------------------

// ==== FILE #15: TimeFrameWindow.cs ====
namespace CryptoBlade.Strategies.Common
{
    public readonly record struct TimeFrameWindow(TimeFrame TimeFrame, int WindowSize, bool Primary);
}

// -----------------------------

// ==== FILE #16: TradingStrategyBase.cs ====
namespace CryptoBlade.Strategies {
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Strategies.Common
{
    public abstract class TradingStrategyBase : TradingStrategyCommonBase
    {
        private readonly IOptions<TradingStrategyBaseOptions> m_options;

        protected TradingStrategyBase(IOptions<TradingStrategyBaseOptions> options,
            string symbol, 
            TimeFrameWindow[] requiredTimeFrames, 
            IWalletManager walletManager,
            ICbFuturesRestClient cbFuturesRestClient) 
            : base(options, symbol, requiredTimeFrames, walletManager, cbFuturesRestClient)
        {
            m_options = options;
        }

        protected abstract int DcaOrdersCount { get; }
        protected abstract bool ForceMinQty { get; }

        protected override Task<decimal?> CalculateMinBalanceAsync()
        {
            var ticker = Ticker;
            if (ticker == null)
                return Task.FromResult<decimal?>(null);
            var minExposure = Math.Min(WalletExposureLong, WalletExposureShort);
            if (minExposure == 0)
                minExposure = Math.Max(WalletExposureLong, WalletExposureShort);

            var recommendedMinBalance = SymbolInfo.CalculateMinBalance(ticker.BestAskPrice, minExposure, DcaOrdersCount);
            return Task.FromResult(recommendedMinBalance);
        }

        protected override async Task CalculateDynamicQtyAsync()
        {
            if (!m_options.Value.EnableRecursiveQtyFactorLong)
                await CalculateDynamicQtyLongFixedAsync();
            else
                await CalculateDynamicQtyLongFactorAsync();
            
            if(!m_options.Value.EnableRecursiveQtyFactorShort)
                await CalculateDynamicQtyShortFixedAsync();
            else
                await CalculateDynamicQtyShortFactorAsync();
            var dynamicQtyShort = DynamicQtyShort;
            var dynamicQtyLong = DynamicQtyLong;
            MaxQtyShort = null;
            MaxQtyLong = null;
            if (dynamicQtyShort.HasValue)
                MaxQtyShort = DcaOrdersCount * dynamicQtyShort.Value;
            if (dynamicQtyLong.HasValue)
                MaxQtyLong = DcaOrdersCount * dynamicQtyLong.Value;
        }

        protected virtual Task CalculateDynamicQtyShortFixedAsync()
        {
            var ticker = Ticker;
            if (ticker == null)
                return Task.CompletedTask;

            if (!DynamicQtyShort.HasValue || !IsInTrade)
                DynamicQtyShort = CalculateDynamicQty(ticker.BestAskPrice, WalletExposureShort);

            return Task.CompletedTask;
        }

        protected virtual Task CalculateDynamicQtyLongFixedAsync()
        {
            var ticker = Ticker;
            if (ticker == null)
                return Task.CompletedTask;

            if (!DynamicQtyLong.HasValue || !IsInTrade)
                DynamicQtyLong = CalculateDynamicQty(ticker.BestBidPrice, WalletExposureLong);

            return Task.CompletedTask;
        }

        protected virtual Task CalculateDynamicQtyLongFactorAsync()
        {
            var ticker = Ticker;
            if (ticker == null)
                return Task.CompletedTask; 
            var longPosition = LongPosition; 
                
            if (longPosition == null)
                DynamicQtyLong = CalculateDynamicQty(ticker.BestBidPrice, WalletExposureLong);
            else
            {
                var walletBalance = WalletManager.Contract.WalletBalance;
                var positionValue = longPosition.Quantity * longPosition.AveragePrice;
                var remainingExposure = (m_options.Value.WalletExposureLong * walletBalance) - positionValue;
                if (remainingExposure <= 0)
                    DynamicQtyLong = null;
                else
                {
                    var remainingQty = remainingExposure / ticker.BestBidPrice;
                    DynamicQtyLong = longPosition.Quantity * m_options.Value.QtyFactorLong;
                    if (DynamicQtyLong > remainingQty)
                        DynamicQtyLong = remainingQty;
                    var symbolInfo = SymbolInfo;
                    if (symbolInfo.QtyStep.HasValue)
                        DynamicQtyLong -= (DynamicQtyLong % symbolInfo.QtyStep.Value);
                    if (SymbolInfo.MinOrderQty > DynamicQtyLong)
                        DynamicQtyLong = SymbolInfo.MinOrderQty;
                }
            }
            
            return Task.CompletedTask;
        }

        protected virtual Task CalculateDynamicQtyShortFactorAsync()
        {
            var ticker = Ticker;
            if (ticker == null)
                return Task.CompletedTask;
            var shortPosition = ShortPosition;
            if (shortPosition == null)
                DynamicQtyShort = CalculateDynamicQty(ticker.BestAskPrice, WalletExposureShort);
            else
            {
                var walletBalance = WalletManager.Contract.WalletBalance;
                var positionValue = shortPosition.Quantity * shortPosition.AveragePrice;
                var remainingExposure = (m_options.Value.WalletExposureShort * walletBalance) - positionValue;
                if(remainingExposure <= 0)
                    DynamicQtyShort = null;
                else
                {
                    var remainingQty = remainingExposure / ticker.BestAskPrice;
                    DynamicQtyShort = shortPosition.Quantity * m_options.Value.QtyFactorShort;
                    if (DynamicQtyShort > remainingQty)
                        DynamicQtyShort = remainingQty;
                    var symbolInfo = SymbolInfo;
                    if(symbolInfo.QtyStep.HasValue)
                        DynamicQtyShort -= (DynamicQtyShort % symbolInfo.QtyStep.Value);
                    if (SymbolInfo.MinOrderQty > DynamicQtyShort)
                        DynamicQtyShort = SymbolInfo.MinOrderQty;
                }
            }

            return Task.CompletedTask;
        }

        protected override Task CalculateTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            var ticker = Ticker;
            if(ticker == null)
                return Task.CompletedTask;
            var quotes5Min = QuoteQueues[TimeFrame.FiveMinutes].GetQuotes();
            if (quotes5Min.Length < 1)
                return Task.CompletedTask;
            var quotes1Min = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            if (quotes1Min.Length < 1)
                return Task.CompletedTask;
            var spread5Min = TradeSignalHelpers.Get5MinSpread(quotes1Min);
            var longPosition = LongPosition;
            var shortPosition = ShortPosition;
            decimal? shortTakeProfit = null;
            if (shortPosition != null)
                shortTakeProfit = TradingHelpers.CalculateShortTakeProfit(
                    shortPosition,
                    SymbolInfo,
                    quotes5Min,
                    spread5Min,
                    ticker,
                    m_options.Value.FeeRate,
                    m_options.Value.MinProfitRate);
            ShortTakeProfitPrice = shortTakeProfit;
            if (shortTakeProfit.HasValue)
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.ShortTakeProfit), shortTakeProfit.Value));

            decimal? longTakeProfit = null;
            if (longPosition != null)
                longTakeProfit = TradingHelpers.CalculateLongTakeProfit(
                    longPosition,
                    SymbolInfo,
                    quotes5Min,
                    spread5Min,
                    ticker,
                    m_options.Value.FeeRate,
                    m_options.Value.MinProfitRate);
            LongTakeProfitPrice = longTakeProfit;
            if (longTakeProfit.HasValue)
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.LongTakeProfit), longTakeProfit.Value));

            return Task.CompletedTask;
        }

        private decimal? CalculateDynamicQty(decimal price, decimal walletExposure)
        {
            var dynamicQty = SymbolInfo.CalculateQuantity(WalletManager, price, walletExposure, DcaOrdersCount);
            if (!dynamicQty.HasValue && ForceMinQty) // we could not calculate a quantity so we will use the minimum
                dynamicQty = SymbolInfo.MinOrderQty;

            if (dynamicQty.HasValue && dynamicQty.Value < SymbolInfo.MinOrderQty)
                dynamicQty = ForceMinQty ? SymbolInfo.MinOrderQty : null;

            bool isInTrade = IsInTrade;
            if (!dynamicQty.HasValue && isInTrade)
            {
                // we are in a trade and we could not calculate a quantity so we will use the minimum
                dynamicQty = SymbolInfo.MinOrderQty;
            }

            return dynamicQty;
        }
    }
}
}

// -----------------------------

// ==== FILE #17: TradingStrategyCommonBase.cs ====
namespace CryptoBlade.Strategies {
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;
using System.Threading.Channels;
using CryptoBlade.Mapping;

namespace CryptoBlade.Strategies.Common
{
    public abstract class TradingStrategyCommonBase : ITradingStrategy
    {
        private readonly IOptions<TradingStrategyCommonBaseOptions> m_options;
        private readonly Channel<Candle> m_candleBuffer;
        private const int c_defaultCandleBufferSize = 1000;
        private readonly ICbFuturesRestClient m_cbFuturesRestClient;
        private readonly ILogger m_logger;
        private readonly Random m_random = new Random();

        protected TradingStrategyCommonBase(IOptions<TradingStrategyCommonBaseOptions> options,
            string symbol,
            TimeFrameWindow[] requiredTimeFrames,
            IWalletManager walletManager,
            ICbFuturesRestClient cbFuturesRestClient)
        {
            m_logger = ApplicationLogging.CreateLogger(GetType().FullName ?? nameof(TradingStrategyBase));
            RequiredTimeFrameWindows = requiredTimeFrames;
            if (options.Value.StrategySelectPreference == StrategySelectPreference.NormalizedAverageTrueRange)
            {
                var existingOneHourWindow = requiredTimeFrames.FirstOrDefault(x => x.TimeFrame == TimeFrame.OneHour);
                var oneHourLength = options.Value.NormalizedAverageTrueRangePeriod;
                if (existingOneHourWindow.WindowSize > oneHourLength)
                    oneHourLength = existingOneHourWindow.WindowSize;

                RequiredTimeFrameWindows = RequiredTimeFrameWindows
                    .Where(x => x.TimeFrame != TimeFrame.OneHour)
                    .Append(new TimeFrameWindow(TimeFrame.OneHour, oneHourLength, false))
                    .ToArray();
            }
            WalletManager = walletManager;
            m_cbFuturesRestClient = cbFuturesRestClient;
            m_options = options;
            Symbol = symbol;
            QuoteQueues = new Dictionary<TimeFrame, QuoteQueue>();
            foreach (TimeFrameWindow requiredTimeFrame in requiredTimeFrames)
                QuoteQueues[requiredTimeFrame.TimeFrame] = new(requiredTimeFrame.WindowSize, requiredTimeFrame.TimeFrame);
            foreach (TimeFrame timeFrame in Enum.GetValues<TimeFrame>())
            {
                if (!QuoteQueues.ContainsKey(timeFrame))
                    QuoteQueues[timeFrame] = new(c_defaultCandleBufferSize, timeFrame);
            }
            m_candleBuffer = Channel.CreateBounded<Candle>(c_defaultCandleBufferSize);
            Indicators = Array.Empty<StrategyIndicator>();
            BuyOrders = Array.Empty<Order>();
            SellOrders = Array.Empty<Order>();
            LongTakeProfitOrders = Array.Empty<Order>();
            ShortTakeProfitOrders = Array.Empty<Order>();
        }

        public abstract string Name { get; }

        public bool IsInTrade
        {
            get { return IsInLongTrade || IsInShortTrade; }
        }
        public bool IsInLongTrade { get; protected set; }
        public bool IsInShortTrade { get; protected set; }

        public string Symbol { get; }
        public SymbolInfo SymbolInfo { get; private set; }
        public decimal? DynamicQtyShort { get; protected set; }
        public decimal? DynamicQtyLong { get; protected set; }
        public decimal? MaxQtyShort { get; protected set; }
        public decimal? MaxQtyLong { get; protected set; }
        public decimal? RecommendedMinBalance { get; protected set; }
        public bool HasSellSignal { get; protected set; }
        public bool HasBuySignal { get; protected set; }
        public bool HasSellExtraSignal { get; protected set; }
        public bool HasBuyExtraSignal { get; protected set; }
        public bool ConsistentData { get; protected set; }
        public decimal? UnrealizedLongPnlPercent { get; protected set; }
        public decimal? UnrealizedShortPnlPercent { get; protected set; }
        public Ticker? Ticker { get; protected set; }
        public DateTime LastTickerUpdate { get; protected set; }
        public DateTime LastCandleUpdate { get; protected set; }
        public StrategyIndicator[] Indicators { get; protected set; }
        public TimeFrameWindow[] RequiredTimeFrameWindows { get; }
        protected Position? LongPosition { get; set; }
        protected Position? ShortPosition { get; set; }
        protected Order[] BuyOrders { get; set; }
        protected Order[] SellOrders { get; set; }
        protected Order[] LongTakeProfitOrders { get; set; }
        protected Order[] ShortTakeProfitOrders { get; set; }
        protected decimal? ShortTakeProfitPrice { get; set; }
        protected decimal? LongTakeProfitPrice { get; set; }
        public DateTime? NextShortProfitReplacement { get; set; }
        public DateTime? NextLongProfitReplacement { get; set; }
        public DateTime? LastCandleLongOrder { get; set; }
        public DateTime? LastCandleShortOrder { get; set; }
        public DateTime? LastCandleLongUnstuckOrder { get; set; }
        public DateTime? LastCandleShortUnstuckOrder { get; set; }
        public decimal? CurrentExposureLong { get; protected set; }
        public decimal? CurrentExposureShort { get; protected set; }
        protected virtual bool UseMarketOrdersForEntries => false;
        protected Dictionary<TimeFrame, QuoteQueue> QuoteQueues { get; }
        protected bool QueueInitialized { get; private set; }
        protected abstract decimal WalletExposureLong { get; }
        protected abstract decimal WalletExposureShort { get; }
        protected IWalletManager WalletManager { get; }

        public Task UpdateTradingStateAsync(Position? longPosition, Position? shortPosition, Order[] orders, CancellationToken cancel)
        {
            BuyOrders = orders.Where(x => x.Side == OrderSide.Buy && x.ReduceOnly.HasValue && !x.ReduceOnly.Value).ToArray();
            SellOrders = orders.Where(x => x.Side == OrderSide.Sell && x.ReduceOnly.HasValue && !x.ReduceOnly.Value).ToArray();
            LongTakeProfitOrders = orders.Where(x => x.Side == OrderSide.Sell && x.ReduceOnly.HasValue && x.ReduceOnly.Value).ToArray();
            ShortTakeProfitOrders = orders.Where(x => x.Side == OrderSide.Buy && x.ReduceOnly.HasValue && x.ReduceOnly.Value).ToArray();
            LongPosition = longPosition;
            ShortPosition = shortPosition;
            IsInLongTrade = longPosition != null || BuyOrders.Length > 0;
            IsInShortTrade = shortPosition != null || SellOrders.Length > 0;
            UnrealizedLongPnlPercent = null;
            UnrealizedShortPnlPercent = null;
            CurrentExposureLong = null;
            CurrentExposureShort = null;
            var balance = WalletManager.Contract;
            if (longPosition != null && Ticker != null && balance.WalletBalance.HasValue && balance.WalletBalance.Value > 0)
            {
                var longValue = longPosition.Quantity * Ticker.LastPrice;
                CurrentExposureLong = longValue / balance.WalletBalance.Value;
                var longPositionValue = (Ticker.LastPrice - longPosition.AveragePrice) * longPosition.Quantity;
                UnrealizedLongPnlPercent = longPositionValue / balance.WalletBalance.Value;
            }

            if (shortPosition != null && Ticker != null && balance.WalletBalance.HasValue && balance.WalletBalance.Value > 0)
            {
                var shortValue = shortPosition.Quantity * Ticker.LastPrice;
                CurrentExposureShort = shortValue / balance.WalletBalance.Value;
                var shortPositionValue = (shortPosition.AveragePrice - Ticker.LastPrice) * shortPosition.Quantity;
                UnrealizedShortPnlPercent = shortPositionValue / balance.WalletBalance.Value;
            }

            m_logger.LogDebug(
                $"{Name}: {Symbol} Long position: '{longPosition?.Quantity} @ {longPosition?.AveragePrice}' Short position: '{shortPosition?.Quantity} @ {shortPosition?.AveragePrice}' InTrade: '{IsInTrade}'");
            return Task.CompletedTask;
        }

        public async Task SetupSymbolAsync(SymbolInfo symbol, CancellationToken cancel)
        {
            SymbolInfo = symbol;
            if (m_options.Value.TradingMode != TradingMode.Readonly)
            {
                m_logger.LogInformation($"Setting up trading configuration for symbol {symbol.Name}");
                bool leverageOk = await m_cbFuturesRestClient.SetLeverageAsync(symbol, cancel);
                if (!leverageOk)
                    throw new InvalidOperationException("Failed to setup leverage.");

                m_logger.LogInformation($"Leverage set to {symbol.MaxLeverage} for {symbol.Name}");

                bool modeOk = await m_cbFuturesRestClient.SwitchPositionModeAsync(PositionMode.Hedge, symbol.Name, cancel);
                if (!modeOk)
                    throw new InvalidOperationException("Failed to setup position mode.");

                m_logger.LogInformation($"Position mode set to {PositionMode.Hedge} for {symbol.Name}");


                //bool crossModeOk = await m_cbFuturesRestClient.SwitchCrossIsolatedMarginAsync(symbol, TradeMode.CrossMargin, cancel);
                //if (!crossModeOk)
                //    throw new InvalidOperationException("Failed to setup cross mode.");

                //m_logger.LogInformation($"Cross mode set to {TradeMode.CrossMargin} for {symbol.Name}");
                m_logger.LogInformation($"Symbol {symbol.Name} setup completed");
            }
        }

        public async Task InitializeAsync(Candle[] candles, Ticker ticker, CancellationToken cancel)
        {
            bool consistent = true;
            QueueInitialized = false;
            foreach (var queue in QuoteQueues.Values)
                queue.Clear();

            foreach (var candle in candles)
            {
                bool candleConsistent = QuoteQueues[candle.TimeFrame].Enqueue(candle.ToQuote());
                if (!candleConsistent)
                    consistent = false;
            }

            QueueInitialized = consistent;

            await ProcessCandleBuffer();

            Ticker = ticker;
            ConsistentData = consistent;
        }

        public async Task ExecuteAsync(ExecuteParams executeParams, CancellationToken cancel)
        {
            bool isLive = m_options.Value.TradingMode == TradingMode.Normal
                          || m_options.Value.TradingMode == TradingMode.Dynamic
                          || m_options.Value.TradingMode == TradingMode.Readonly;
            if (isLive)
            {
                int jitter = m_random.Next(500, 5500);
                await Task.Delay(jitter, cancel); // random delay to lower probability of hitting rate limits until we have a better solution
            }

            m_logger.LogDebug($"{Name}: {Symbol} Executing strategy. TradingMode: {m_options.Value.TradingMode}");

            var buyOrders = BuyOrders;
            var sellOrders = SellOrders;
            var longPosition = LongPosition;
            var shortPosition = ShortPosition;
            bool hasBuySignal = HasBuySignal;
            bool hasSellSignal = HasSellSignal;
            bool hasBuyExtraSignal = HasBuyExtraSignal;
            bool hasSellExtraSignal = HasSellExtraSignal;
            decimal? dynamicQtyShort = DynamicQtyShort;
            decimal? dynamicQtyLong = DynamicQtyLong;
            var ticker = Ticker;
            var longTakeProfitOrders = LongTakeProfitOrders;
            var shortTakeProfitOrders = ShortTakeProfitOrders;
            decimal? longTakeProfitPrice = LongTakeProfitPrice;
            decimal? shortTakeProfitPrice = ShortTakeProfitPrice;
            DateTime utcNow = ticker?.Timestamp ?? DateTime.UtcNow;
            TimeSpan replacementTime = TimeSpan.FromMinutes(4.5);
            decimal? maxShortQty = MaxQtyShort;
            decimal? maxLongQty = MaxQtyLong;
            decimal? longExposure = null;
            decimal? shortExposure = null;
            var primary = RequiredTimeFrameWindows.First(x => x.Primary).TimeFrame;
            var lastPrimaryQuote = QuoteQueues[primary].GetQuotes().LastOrDefault();
            if (longPosition != null)
                longExposure = longPosition.Quantity * longPosition.AveragePrice;
            if (shortPosition != null)
                shortExposure = shortPosition.Quantity * shortPosition.AveragePrice;
            var wallet = WalletManager.Contract;
            decimal? walletLongExposure = null;
            decimal? walletShortExposure = null;
            if (wallet.WalletBalance.HasValue && longExposure.HasValue && wallet.WalletBalance.Value > 0)
                walletLongExposure = longExposure / wallet.WalletBalance;
            if (wallet.WalletBalance.HasValue && shortExposure.HasValue && wallet.WalletBalance.Value > 0)
                walletShortExposure = shortExposure / wallet.WalletBalance;

            // log variables above
            m_logger.LogDebug($"{Name}: {Symbol} Buy orders: '{buyOrders.Length}'; Sell orders: '{sellOrders.Length}'; Long position: '{longPosition?.Quantity}'; Short position: '{shortPosition?.Quantity}'; Has buy signal: '{hasBuySignal}'; Has sell signal: '{hasSellSignal}'; Has buy extra signal: '{hasBuyExtraSignal}'; Has sell extra signal: '{hasSellExtraSignal}'. Allow long open: '{executeParams.AllowLongOpen}' Allow short open: '{executeParams.AllowShortOpen}'");

            if (m_options.Value.TradingMode == TradingMode.Readonly)
            {
                m_logger.LogDebug($"{Name}: {Symbol} Finished executing strategy. ReadOnly: {m_options.Value.TradingMode}");
                return;
            }

            if (!hasBuySignal && !hasBuyExtraSignal && buyOrders.Any())
            {
                m_logger.LogDebug($"{Name}: {Symbol} no buy signal. Canceling buy orders.");
                // cancel outstanding buy orders
                foreach (Order buyOrder in buyOrders)
                {
                    bool canceled = await CancelOrderAsync(buyOrder.OrderId, cancel);
                    m_logger.LogDebug($"{Name}: {Symbol} Canceling buy order '{buyOrder.OrderId}' {(canceled ? "succeeded" : "failed")}");
                }
            }

            if (!hasSellSignal && !hasSellExtraSignal && sellOrders.Any())
            {
                m_logger.LogDebug($"{Name}: {Symbol} no sell signal. Canceling sell orders.");
                // cancel outstanding sell orders
                foreach (Order sellOrder in sellOrders)
                {
                    bool canceled = await CancelOrderAsync(sellOrder.OrderId, cancel);
                    m_logger.LogDebug($"{Name}: {Symbol} Canceling sell order '{sellOrder.OrderId}' {(canceled ? "succeeded" : "failed")}");
                }
            }

            bool canOpenLongPosition = (m_options.Value.TradingMode == TradingMode.Normal
                                        || m_options.Value.TradingMode == TradingMode.Dynamic
                                        || m_options.Value.TradingMode == TradingMode.DynamicBackTest)
                                       && executeParams.AllowLongOpen;
            bool canOpenShortPosition = (m_options.Value.TradingMode == TradingMode.Normal
                                         || m_options.Value.TradingMode == TradingMode.Dynamic
                                         || m_options.Value.TradingMode == TradingMode.DynamicBackTest)
                                        && executeParams.AllowShortOpen;
            if (ticker != null && lastPrimaryQuote != null)
            {
                if (hasBuySignal
                    && longPosition == null
                    && !buyOrders.Any()
                    && NoTradeForCandle(lastPrimaryQuote, LastCandleLongOrder)
                    && dynamicQtyLong.HasValue
                    && dynamicQtyLong.Value > 0
                    && canOpenLongPosition
                    && LongFundingWithinLimit(ticker))
                {
                    m_logger.LogDebug($"{Name}: {Symbol} trying to open long position");
                    if (UseMarketOrdersForEntries)
                        await PlaceMarketBuyOrderAsync(dynamicQtyLong.Value, ticker.BestBidPrice, lastPrimaryQuote.Date, cancel);
                    else
                        await PlaceLimitBuyOrderAsync(dynamicQtyLong.Value, ticker.BestBidPrice, lastPrimaryQuote.Date, cancel);
                }

                if (hasSellSignal
                    && shortPosition == null
                    && !sellOrders.Any()
                    && NoTradeForCandle(lastPrimaryQuote, LastCandleShortOrder)
                    && dynamicQtyShort.HasValue
                    && dynamicQtyShort.Value > 0
                    && canOpenShortPosition
                    && ShortFundingWithinLimit(ticker))
                {
                    m_logger.LogDebug($"{Name}: {Symbol} trying to open short position");
                    if (UseMarketOrdersForEntries)
                        await PlaceMarketSellOrderAsync(dynamicQtyShort.Value, ticker.BestAskPrice, lastPrimaryQuote.Date, cancel);
                    else
                        await PlaceLimitSellOrderAsync(dynamicQtyShort.Value, ticker.BestAskPrice, lastPrimaryQuote.Date, cancel);
                }

                if (hasBuyExtraSignal
                    && longPosition != null
                    && maxLongQty.HasValue
                    && longPosition.Quantity < maxLongQty.Value
                    && walletLongExposure.HasValue && walletLongExposure.Value < m_options.Value.WalletExposureLong
                    && !buyOrders.Any()
                    && dynamicQtyLong.HasValue
                    && dynamicQtyLong.Value > 0
                    && NoTradeForCandle(lastPrimaryQuote, LastCandleLongOrder)
                    && LongFundingWithinLimit(ticker)
                    && !executeParams.LongUnstucking
                    && executeParams.AllowExtraLong)
                {
                    m_logger.LogDebug($"{Name}: {Symbol} trying to add to open long position");
                    if (UseMarketOrdersForEntries)
                        await PlaceMarketBuyOrderAsync(dynamicQtyLong.Value, ticker.BestBidPrice, lastPrimaryQuote.Date, cancel);
                    else
                        await PlaceLimitBuyOrderAsync(dynamicQtyLong.Value, ticker.BestBidPrice, lastPrimaryQuote.Date, cancel);
                }

                if (hasSellExtraSignal
                    && shortPosition != null
                    && maxShortQty.HasValue
                    && shortPosition.Quantity < maxShortQty.Value
                    && walletShortExposure.HasValue && walletShortExposure.Value < m_options.Value.WalletExposureShort
                    && !sellOrders.Any()
                    && dynamicQtyShort.HasValue
                    && dynamicQtyShort.Value > 0
                    && NoTradeForCandle(lastPrimaryQuote, LastCandleShortOrder)
                    && ShortFundingWithinLimit(ticker)
                    && !executeParams.ShortUnstucking
                    && executeParams.AllowExtraShort)
                {
                    m_logger.LogDebug($"{Name}: {Symbol} trying to add to open short position");
                    if (UseMarketOrdersForEntries)
                        await PlaceMarketSellOrderAsync(dynamicQtyShort.Value, ticker.BestAskPrice, lastPrimaryQuote.Date, cancel);
                    else
                        await PlaceLimitSellOrderAsync(dynamicQtyShort.Value, ticker.BestAskPrice, lastPrimaryQuote.Date, cancel);
                }
            }

            bool hasPlacedOrder = lastPrimaryQuote != null
                                  && (LastCandleLongOrder.HasValue && LastCandleLongOrder.Value == lastPrimaryQuote.Date
                                      || LastCandleShortOrder.HasValue && LastCandleShortOrder.Value == lastPrimaryQuote.Date);
            // do not place take profit orders if we have placed an order for the current candle
            // quantity would not be valid
            if (longPosition != null
                && longTakeProfitPrice.HasValue
                && !hasPlacedOrder
                && !executeParams.LongUnstucking)
            {
                decimal longTakeProfitQty = longTakeProfitOrders.Length > 0 ? longTakeProfitOrders.Sum(x => x.Quantity) : 0;
                if (longTakeProfitQty != longPosition.Quantity || NextLongProfitReplacement == null || (NextLongProfitReplacement != null && utcNow > NextLongProfitReplacement))
                {
                    foreach (Order longTakeProfitOrder in longTakeProfitOrders)
                    {
                        m_logger.LogDebug($"{Name}: {Symbol} Canceling long take profit order '{longTakeProfitOrder.OrderId}'");
                        await CancelOrderAsync(longTakeProfitOrder.OrderId, cancel);
                    }
                    m_logger.LogDebug($"{Name}: {Symbol} Placing long take profit order for '{longPosition.Quantity}' @ '{longTakeProfitPrice.Value}'");
                    await PlaceLongTakeProfitOrderAsync(longPosition.Quantity, longTakeProfitPrice.Value, false, cancel);
                    NextLongProfitReplacement = utcNow + replacementTime;
                }
            }

            if (shortPosition != null
                && shortTakeProfitPrice.HasValue
                && !hasPlacedOrder
                && !executeParams.ShortUnstucking)
            {
                decimal shortTakeProfitQty = shortTakeProfitOrders.Length > 0 ? shortTakeProfitOrders.Sum(x => x.Quantity) : 0;
                if ((shortTakeProfitQty != shortPosition.Quantity) || NextShortProfitReplacement == null || (NextShortProfitReplacement != null && utcNow > NextShortProfitReplacement))
                {
                    foreach (Order shortTakeProfitOrder in shortTakeProfitOrders)
                    {
                        m_logger.LogDebug($"{Name}: {Symbol} Canceling short take profit order '{shortTakeProfitOrder.OrderId}'");
                        await CancelOrderAsync(shortTakeProfitOrder.OrderId, cancel);
                    }
                    m_logger.LogDebug($"{Name}: {Symbol} Placing short take profit order for '{shortPosition.Quantity}' @ '{shortTakeProfitPrice.Value}'");
                    await PlaceShortTakeProfitOrderAsync(shortPosition.Quantity, shortTakeProfitPrice.Value, false, cancel);
                    NextShortProfitReplacement = utcNow + replacementTime;
                }
            }

            m_logger.LogDebug($"{Name}: {Symbol} Finished executing strategy. TradingMode: {m_options.Value.TradingMode}");
        }

        public async Task ExecuteUnstuckAsync(bool unstuckLong, bool unstuckShort, bool forceUnstuckLong, bool forceUnstuckShort, bool forceKill, CancellationToken cancel)
        {
            var primary = RequiredTimeFrameWindows.First(x => x.Primary).TimeFrame;
            var lastPrimaryQuote = QuoteQueues[primary].GetQuotes().LastOrDefault();
            if (lastPrimaryQuote == null)
                return;
            if (unstuckLong)
            {
                bool noTradeForCandle = NoTradeForCandle(lastPrimaryQuote, LastCandleLongUnstuckOrder);
                bool regularUnstuck = noTradeForCandle && (HasSellSignal || HasSellExtraSignal);
                if (regularUnstuck || forceUnstuckLong)
                {
                    m_logger.LogDebug($"{Name}: {Symbol} Unstuck long position");
                    var orderPlaced = await ExecuteLongUnstuckAsync(forceUnstuckLong, forceKill, cancel);
                    if (orderPlaced)
                        LastCandleLongUnstuckOrder = lastPrimaryQuote.Date;
                }
            }

            if (unstuckShort)
            {
                bool noTradeForCandle = NoTradeForCandle(lastPrimaryQuote, LastCandleShortUnstuckOrder);
                bool regularUnstuck = noTradeForCandle && (HasBuySignal || HasBuyExtraSignal);
                if (regularUnstuck || forceUnstuckShort)
                {
                    m_logger.LogDebug($"{Name}: {Symbol} Unstuck short position");
                    var orderPlaced = await ExecuteShortUnstuckAsync(forceUnstuckShort, forceKill, cancel);
                    if (orderPlaced)
                        LastCandleShortUnstuckOrder = lastPrimaryQuote.Date;
                }
            }
        }

        private async Task<bool> ExecuteLongUnstuckAsync(bool force, bool forceKill, CancellationToken cancel)
        {
            var longPosition = LongPosition;
            if (longPosition == null)
                return false;
            var longTakeProfitOrders = LongTakeProfitOrders;
            var ticker = Ticker;
            if (ticker == null)
                return false;

            decimal unstuckQuantity = forceKill
                ? longPosition.Quantity
                : CalculateUnstuckingQuantity(longPosition.Quantity, force);

            foreach (Order longTakeProfitOrder in longTakeProfitOrders)
            {
                m_logger.LogDebug($"{Name}: {Symbol} Canceling long take profit order '{longTakeProfitOrder.OrderId}'");
                await CancelOrderAsync(longTakeProfitOrder.OrderId, cancel);
            }

            bool orderPlaced = await PlaceLongTakeProfitOrderAsync(unstuckQuantity, ticker.BestAskPrice, force, cancel);
            return orderPlaced;
        }

        private async Task<bool> ExecuteShortUnstuckAsync(bool force, bool forceKill, CancellationToken cancel)
        {
            var shortPosition = ShortPosition;
            if (shortPosition == null)
                return false;
            var shortTakeProfitOrders = ShortTakeProfitOrders;
            var ticker = Ticker;
            if (ticker == null)
                return false;

            decimal unstuckQuantity = forceKill
                ? shortPosition.Quantity
                : CalculateUnstuckingQuantity(shortPosition.Quantity, force);

            foreach (Order shortTakeProfitOrder in shortTakeProfitOrders)
            {
                m_logger.LogDebug($"{Name}: {Symbol} Canceling short take profit order '{shortTakeProfitOrder.OrderId}'");
                await CancelOrderAsync(shortTakeProfitOrder.OrderId, cancel);
            }

            bool orderPlaced = await PlaceShortTakeProfitOrderAsync(unstuckQuantity, ticker.BestBidPrice, force, cancel);
            return orderPlaced;
        }

        private decimal CalculateUnstuckingQuantity(decimal positionQuantity, bool force)
        {
            if (!SymbolInfo.QtyStep.HasValue)
                return positionQuantity; // this should not happen

            decimal unstuckQuantity;
            if (force)
                unstuckQuantity = positionQuantity * m_options.Value.ForceUnstuckPercentStep;
            else
                unstuckQuantity = positionQuantity * m_options.Value.SlowUnstuckPercentStep;

            unstuckQuantity -= (unstuckQuantity % SymbolInfo.QtyStep.Value);
            if (unstuckQuantity > positionQuantity)
                unstuckQuantity = positionQuantity;

            return unstuckQuantity;
        }

        public async Task AddCandleDataAsync(Candle candle, CancellationToken cancel)
        {
            // we need to first subscribe to candles so we don't miss any
            // we need to put them in a buffer so we can process them in order
            // after initializing the queue we can process the buffer
            // If we get inconsistent data we will reinitialize queue while still receiving candles
            // eventually we will get consistent data and can process the buffer
            // duplicate candles will be ignored by the queue
            m_candleBuffer.Writer.TryWrite(candle);
            if (QueueInitialized)
            {
                await ProcessCandleBuffer();
            }
        }

        public Task UpdatePriceDataSync(Ticker ticker, CancellationToken cancel)
        {
            Ticker = ticker;
            LastTickerUpdate = ticker.Timestamp;
            return Task.CompletedTask;
        }

        protected abstract Task CalculateDynamicQtyAsync();

        protected abstract Task CalculateTakeProfitAsync(IList<StrategyIndicator> indicators);

        protected abstract Task<decimal?> CalculateMinBalanceAsync();

        public virtual async Task EvaluateSignalsAsync(CancellationToken cancel)
        {
            HasBuySignal = false;
            HasSellSignal = false;
            HasSellExtraSignal = false;
            HasBuyExtraSignal = false;
            Indicators = Array.Empty<StrategyIndicator>();
            if (!ConsistentData)
                return;
            var ticker = Ticker;
            if (ticker == null)
                return;

            RecommendedMinBalance = await CalculateMinBalanceAsync();

            await CalculateDynamicQtyAsync();

            var signalEvaluation = await EvaluateSignalsInnerAsync(cancel);
            HasBuySignal = signalEvaluation.BuySignal;
            HasSellSignal = signalEvaluation.SellSignal;
            HasBuyExtraSignal = signalEvaluation.HasBuyExtraSignal;
            HasSellExtraSignal = signalEvaluation.HasSellExtraSignal;
            var indicators = new List<StrategyIndicator>
            {
                new StrategyIndicator(nameof(IndicatorType.Buy), HasBuySignal),
                new StrategyIndicator(nameof(IndicatorType.BuyExtra), HasBuyExtraSignal),
                new StrategyIndicator(nameof(IndicatorType.Sell), HasSellSignal),
                new StrategyIndicator(nameof(IndicatorType.SellExtra), HasSellExtraSignal)
            };
            indicators.AddRange(signalEvaluation.Indicators);
            if (m_options.Value.StrategySelectPreference == StrategySelectPreference.NormalizedAverageTrueRange)
            {
                var quotes = QuoteQueues[TimeFrame.OneHour].GetQuotes();
                var atr = quotes.GetAtr();
                var lastAtr = atr.LastOrDefault();
                if (lastAtr != null && lastAtr.Atr.HasValue)
                {
                    var normalizedAtr = (lastAtr.Atr.Value / (double)ticker.BestAskPrice) * 100;
                    normalizedAtr = Math.Round(normalizedAtr, 6);
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.NormalizedAverageTrueRange), (decimal)normalizedAtr));
                }
            }

            await CalculateTakeProfitAsync(indicators);
            Indicators = indicators.ToArray();
        }

        protected abstract Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel);

        private Task ProcessCandleBuffer()
        {
            while (m_candleBuffer.Reader.TryRead(out var bufferedCandle))
            {
                bool consistent = QuoteQueues[bufferedCandle.TimeFrame].Enqueue(bufferedCandle.ToQuote());
                if (!consistent)
                {
                    if (!m_options.Value.IgnoreInconsistency)
                    {
                        ConsistentData = false;
                        QueueInitialized = false;
                    }

                    m_logger.LogWarning($"Inconsistent data for {bufferedCandle.TimeFrame} candle {bufferedCandle.StartTime} for symbol {Symbol}");
                }
                if (bufferedCandle.TimeFrame == TimeFrame.OneMinute)
                    LastCandleUpdate = bufferedCandle.StartTime + bufferedCandle.TimeFrame.ToTimeSpan();
            }

            return Task.CompletedTask;
        }

        private async Task<bool> CancelOrderAsync(string orderId, CancellationToken cancel)
        {
            bool res = await m_cbFuturesRestClient.CancelOrderAsync(Symbol, orderId, cancel);
            return res;
        }

        private async Task PlaceLimitBuyOrderAsync(decimal qty, decimal bidPrice, DateTime candleTime, CancellationToken cancel)
        {
            bool placed = await m_cbFuturesRestClient.PlaceLimitBuyOrderAsync(Symbol, qty, bidPrice, cancel);
            if (placed)
                LastCandleLongOrder = candleTime;
        }

        private async Task PlaceLimitSellOrderAsync(decimal qty, decimal askPrice, DateTime candleTime, CancellationToken cancel)
        {
            bool placed = await m_cbFuturesRestClient.PlaceLimitSellOrderAsync(Symbol, qty, askPrice, cancel);
            if (placed)
                LastCandleShortOrder = candleTime;
        }

        private async Task PlaceMarketBuyOrderAsync(decimal qty, decimal bidPrice, DateTime candleTime, CancellationToken cancel)
        {
            bool placed = await m_cbFuturesRestClient.PlaceMarketBuyOrderAsync(Symbol, qty, bidPrice, cancel);
            if (placed)
                LastCandleLongOrder = candleTime;
        }

        private async Task PlaceMarketSellOrderAsync(decimal qty, decimal askPrice, DateTime candleTime, CancellationToken cancel)
        {
            bool placed = await m_cbFuturesRestClient.PlaceMarketSellOrderAsync(Symbol, qty, askPrice, cancel);
            if (placed)
                LastCandleShortOrder = candleTime;
        }

        private async Task<bool> PlaceLongTakeProfitOrderAsync(decimal qty, decimal price, bool force, CancellationToken cancel)
        {
            var orderPlaced = await m_cbFuturesRestClient.PlaceLongTakeProfitOrderAsync(Symbol, qty, price, force, cancel);
            return orderPlaced;
        }

        private async Task<bool> PlaceShortTakeProfitOrderAsync(decimal qty, decimal price, bool force, CancellationToken cancel)
        {
            var orderPlaced = await m_cbFuturesRestClient.PlaceShortTakeProfitOrderAsync(Symbol, qty, price, force, cancel);
            return orderPlaced;
        }

        private static bool NoTradeForCandle(Quote candle, DateTime? lastTrade)
        {
            if (lastTrade == null)
                return true;
            return lastTrade.Value < candle.Date;
        }

        private bool ShortFundingWithinLimit(Ticker ticker)
        {
            if (!ticker.FundingRate.HasValue)
                return true;
            return ticker.FundingRate.Value >= -m_options.Value.MaxAbsFundingRate;
        }

        private bool LongFundingWithinLimit(Ticker ticker)
        {
            if (!ticker.FundingRate.HasValue)
                return true;
            return ticker.FundingRate.Value <= m_options.Value.MaxAbsFundingRate;
        }

        protected readonly record struct SignalEvaluation(bool BuySignal,
            bool SellSignal,
            bool HasBuyExtraSignal,
            bool HasSellExtraSignal,
            StrategyIndicator[] Indicators);
    }
}
}

// -----------------------------

// ==== FILE #18: TradingStrategyCommonBaseOptions.cs ====
namespace CryptoBlade.Strategies.Common
{
    public class TradingStrategyCommonBaseOptions
    {
        public decimal WalletExposureLong { get; set; }

        public decimal WalletExposureShort { get; set; }

        public TradingMode TradingMode { get; set; }

        public decimal MaxAbsFundingRate { get; set; } = 0.0004m;

        public decimal FeeRate { get; set; } = 0.0002m;

        public decimal SlowUnstuckPercentStep { get; set; } = 0.05m;

        public decimal ForceUnstuckPercentStep { get; set; } = 0.1m;

        public int InitialUntradableDays { get; set; }

        public bool IgnoreInconsistency { get; set; }

        public StrategySelectPreference StrategySelectPreference { get; set; } = StrategySelectPreference.Volume;

        public int NormalizedAverageTrueRangePeriod { get; set; } = 14;
    }
}

// -----------------------------

// ==== FILE #19: Trend.cs ====
namespace CryptoBlade.Strategies.Common
{
    public enum Trend
    {
        Neutral,
        Long,
        Short,
    }
}

// -----------------------------

// ==== FILE #20: BybitErrorCodes.cs ====
namespace CryptoBlade.Strategies.Policies
{
    public enum BybitErrorCodes
    {
        LeverageNotChanged = 110043,
        PositionModeNotChanged = 110025,
        CrossModeNotModified = 110026,
        TooManyVisits = 10006,
        IpRateLimit = 10018,
    }
}

// -----------------------------

// ==== FILE #21: ExchangePolicies.cs ====
namespace CryptoBlade.Strategies {
using CryptoBlade.Helpers;
using CryptoExchange.Net.Objects;
using Polly;
using Polly.Retry;
using System;

namespace CryptoBlade.Strategies.Policies
{
    public static class ExchangePolicies
    {
        private static readonly ILogger s_logger = ApplicationLogging.CreateLogger("Policies");
        private static readonly Random s_random = new Random();
        private static readonly object s_lock = new object();

        private static TimeSpan GetRandomDelay(int maxSeconds)
        {
            lock (s_lock)
            {
                return TimeSpan.FromSeconds(s_random.Next(5, maxSeconds));
            }
        }

        public static AsyncRetryPolicy RetryForever { get; } = Policy
            .Handle<Exception>(exception => exception is not OperationCanceledException)
            .WaitAndRetryForeverAsync(_ => GetRandomDelay(10), (exception, _) =>
            {
                if (exception != null)
                    s_logger.LogWarning(exception, "Error with Exchange API. Retrying...");
                else
                    s_logger.LogWarning("Error with Exchange API. Retrying...");
            });

        public static AsyncRetryPolicy<WebCallResult> RetryTooManyVisits { get; } = Policy
            .Handle<Exception>(exception => exception is not OperationCanceledException)
            .OrResult<WebCallResult>(r => r.Error != null && r.Error.Code.HasValue && (r.Error.Code == (int)BybitErrorCodes.TooManyVisits || r.Error.Code == (int)BybitErrorCodes.IpRateLimit))
            .WaitAndRetryForeverAsync(_ => GetRandomDelay(10), (result, _) =>
            {
                if (result.Exception != null)
                    s_logger.LogWarning(result.Exception, "Error with Exchange API. Retrying...");
                else
                    s_logger.LogWarning("Error with Exchange API. Retrying...");
                WaitWhenIpRateLimit(result.Result);
            });

        private static void WaitWhenIpRateLimit(WebCallResult? result)
        {
            if (result == null)
                return;
            if (result.Error != null &&
                result.Error.Code == (int)BybitErrorCodes.IpRateLimit)
            {
                Task.Delay(TimeSpan.FromMinutes(5)).Wait();
            }
        }
    }

    public static class ExchangePolicies<T>
    {
        // ReSharper disable StaticMemberInGenericType
        private static readonly ILogger s_logger = ApplicationLogging.CreateLogger("Policies");
        // ReSharper restore StaticMemberInGenericType

        private static readonly Random s_random = new Random();
        private static readonly object s_lock = new object();

        private static TimeSpan GetRandomDelay(int maxSeconds)
        {
            lock (s_lock)
            {
                return TimeSpan.FromSeconds(s_random.Next(1, maxSeconds));
            }
        }

        public static AsyncRetryPolicy<WebCallResult<T>> RetryTooManyVisits { get; } = Policy
            .Handle<Exception>(exception => exception is not OperationCanceledException)
            .OrResult<WebCallResult<T>>(r => r.Error != null && r.Error.Code.HasValue && (r.Error.Code == (int)BybitErrorCodes.TooManyVisits || r.Error.Code == (int)BybitErrorCodes.IpRateLimit))
            .WaitAndRetryForeverAsync(_ => GetRandomDelay(10), (result, _) =>
            {
                if (result.Exception != null)
                    s_logger.LogWarning(result.Exception, "Error with Exchange API. Retrying with delay...");
                else
                    s_logger.LogWarning("Too many visits. Retrying with delay...");
                WaitWhenIpRateLimit(result.Result);
            });

        public static AsyncRetryPolicy<WebCallResult<BybitResponse<T>>> RetryTooManyVisitsBybitResponse { get; } = Policy
            .Handle<Exception>(exception => exception is not OperationCanceledException)
            .OrResult<WebCallResult<BybitResponse<T>>>(r => r.Error != null && r.Error.Code.HasValue && (r.Error.Code == (int)BybitErrorCodes.TooManyVisits || r.Error.Code == (int)BybitErrorCodes.IpRateLimit))
            .WaitAndRetryForeverAsync(_ => GetRandomDelay(10), (result, _) =>
            {
                if (result.Exception != null)
                    s_logger.LogWarning(result.Exception, "Error with Exchange API. Retrying with delay...");
                else
                    s_logger.LogWarning("Too many visits. Retrying with delay...");
                WaitWhenIpRateLimit(result.Result);
            });

        private static void WaitWhenIpRateLimit(WebCallResult<BybitResponse<T>>? result)
        {
            if (result == null)
                return;
            if (result.Error != null &&
                result.Error.Code == (int)BybitErrorCodes.IpRateLimit)
            {
                Task.Delay(TimeSpan.FromMinutes(5)).Wait();
            }
        }

        private static void WaitWhenIpRateLimit(WebCallResult<T>? result)
        {
            if (result == null)
                return;
            if (result.Error != null &&
                result.Error.Code == (int)BybitErrorCodes.IpRateLimit)
            {
                Task.Delay(TimeSpan.FromMinutes(5)).Wait();
            }
        }
    }
}
}

// -----------------------------

// ==== FILE #22: Balance.cs ====
namespace CryptoBlade.Strategies.Wallet
{
    public readonly record struct Balance(
        decimal? Equity,
        decimal? WalletBalance,
        decimal? UnrealizedPnl,
        decimal? RealizedPnl);
}

// -----------------------------

// ==== FILE #23: IWalletManager.cs ====
// Uwaga: Ten plik zawiera interfejs – zadbaj o pełną dokumentację!
namespace CryptoBlade.Strategies.Wallet
{
    public interface IWalletManager
    {
        Balance Contract { get; }

        Task StartAsync(CancellationToken cancel);

        Task StopAsync(CancellationToken cancel);
    }
}

// -----------------------------

// ==== FILE #24: NullWalletManager.cs ====
namespace CryptoBlade.Strategies.Wallet
{
    public class NullWalletManager : IWalletManager
    {
        public Balance Contract { get; } = new Balance();

        public Task StartAsync(CancellationToken cancel)
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancel)
        {
            return Task.CompletedTask;
        }
    }
}

// -----------------------------

// ==== FILE #25: WalletManager.cs ====
namespace CryptoBlade.Strategies.Wallet
{
    public class WalletManager : IWalletManager
    {
        private readonly ICbFuturesRestClient m_restClient;
        private readonly ICbFuturesSocketClient m_socketClient;
        private IUpdateSubscription? m_walletSubscription;
        private CancellationTokenSource? m_cancellationTokenSource;
        private readonly ILogger<WalletManager> m_logger;
        private Task? m_initTask;

        public WalletManager(ILogger<WalletManager> logger,
            ICbFuturesRestClient restClient,
            ICbFuturesSocketClient socketClient)
        {
            m_restClient = restClient;
            m_socketClient = socketClient;
            m_logger = logger;
            m_cancellationTokenSource = new CancellationTokenSource();
        }

        public Balance Contract { get; private set; }

        public Task StartAsync(CancellationToken cancel)
        {
            m_cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            m_initTask = Task.Run(async () =>
            {
                var subscription = await m_socketClient.SubscribeToWalletUpdatesAsync(OnWalletUpdate, m_cancellationTokenSource.Token);
                subscription.AutoReconnect(m_logger);
                m_walletSubscription = subscription;

                Contract = await m_restClient.GetBalancesAsync(cancel);

            }, cancel);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancel)
        {
            var walletSubscription = m_walletSubscription;
            if (walletSubscription != null)
                await walletSubscription.CloseAsync();
            m_walletSubscription = null;
            m_cancellationTokenSource?.Cancel();
            m_cancellationTokenSource?.Dispose();
        }

        private void OnWalletUpdate(Balance obj)
        {
            Contract = obj;
        }
    }
}

// -----------------------------

// ==== FILE #26: WalletType.cs ====
namespace CryptoBlade.Strategies.Wallet
{
    public enum WalletType
    {
        Contract,
    }
}

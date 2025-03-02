// *** METADATA ***
// Version: 1.0.0
// Generated: 2025-03-02 11:24:55 UTC
// Module: CryptoBlade.Services
// ****************

// *** INDEX OF INCLUDED FILES ***
1. DefaultTradingStrategyManager.cs
2. DynamicTradingStrategyManager.cs
3. ITradeStrategyManager.cs
4. NullTradeStrategyManager.cs
5. OptimizerHostedService.cs
6. TradeStrategyManagerBase.cs
7. TradingHostedService.cs
// *******************************

using Bert.RateLimiters;
using CryptoBlade.Configuration;
using CryptoBlade.Optimizer;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using System.Threading.Channels;

// ==== FILE #1: DefaultTradingStrategyManager.cs ====
namespace CryptoBlade.Services {
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Services
{
    public class DefaultTradingStrategyManager : TradeStrategyManagerBase
    {
        private readonly ILogger<DefaultTradingStrategyManager> m_logger;
        private readonly IOptions<TradingBotOptions> m_options;

        public DefaultTradingStrategyManager(IOptions<TradingBotOptions> options, 
            ILogger<DefaultTradingStrategyManager> logger, 
            ITradingStrategyFactory strategyFactory,
            ICbFuturesRestClient restClient,
            ICbFuturesSocketClient socketClient, 
            IWalletManager walletManager) 
            : base(options, logger, strategyFactory, restClient, socketClient, walletManager)
        {
            m_options = options;
            m_logger = logger;
        }

        protected override async Task StrategyExecutionAsync(CancellationToken cancel)
        {
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    try
                    {
                        await ProcessStrategyDataAsync(cancel);

                        var hasInconsistent = Strategies.Values.Any(x => !x.ConsistentData);
                        if (hasInconsistent)
                        {
                            m_logger.LogWarning("Some strategies have inconsistent data. Reinitialize.");
                            await ReInitializeStrategies(cancel);
                        }

                        var strategyState = await UpdateTradingStatesAsync(cancel);
                        m_logger.LogDebug(
                            "Total long exposure: {LongExposure}, total short exposure: {ShortExposure}, long WE: {LongWE}, short WE: {ShortWE}",
                            strategyState.TotalLongExposure,
                            strategyState.TotalShortExposure,
                            strategyState.TotalWalletLongExposure,
                            strategyState.TotalWalletShortExposure);


                        List<string> symbolsToProcess = new List<string>();

                        using (await Lock.LockAsync())
                        {
                            while (StrategyExecutionChannel.Reader.TryRead(out var symbol))
                                symbolsToProcess.Add(symbol);

                            var inTradeSymbols = symbolsToProcess
                                .Where(x => Strategies.TryGetValue(x, out var strategy) && strategy.IsInTrade)
                                .Distinct()
                                .ToArray();

                            int remainingSlots = m_options.Value.MaxRunningStrategies -
                                                 Strategies.Values.Count(x => x.IsInTrade);
                            var strategiesWithMostVolume = Strategies.Select(x => new
                                {
                                    Strategy = x,
                                    x.Value.Indicators
                                })
                                .Where(x => x.Indicators.Any(i =>
                                    i.Name == nameof(IndicatorType.MainTimeFrameVolume) && i.Value is decimal))
                                .Where(x => !x.Strategy.Value.IsInTrade &&
                                            (x.Strategy.Value.HasBuySignal && x.Strategy.Value.DynamicQtyLong.HasValue) 
                                            || (x.Strategy.Value.HasSellSignal && x.Strategy.Value.DynamicQtyShort.HasValue))
                                .Select(x => new
                                {
                                    Strategy = x,
                                    MainTimeFrameVolume = (decimal)x.Indicators
                                        .First(i => i.Name == nameof(IndicatorType.MainTimeFrameVolume)).Value
                                })
                                .OrderByDescending(x => x.MainTimeFrameVolume)
                                .Take(remainingSlots)
                                .Select(x => x.Strategy.Strategy.Value.Symbol)
                                .ToArray();

                            List<Task> executionTasks = new List<Task>();
                            if (inTradeSymbols.Any())
                            {
                                var executeParams =
                                    inTradeSymbols.ToDictionary(x => x, _ => new ExecuteParams(true, true, true, true,false, false));
                                await PrepareStrategyExecutionAsync(executionTasks, inTradeSymbols, executeParams,
                                    cancel);
                            }


                            if (strategiesWithMostVolume.Any())
                            {
                                var executeParams =
                                    strategiesWithMostVolume.ToDictionary(x => x, _ => new ExecuteParams(true, true, true, true, false, false));
                                await PrepareStrategyExecutionAsync(executionTasks, strategiesWithMostVolume,
                                    executeParams, cancel);
                            }

                            LogRemainingSlots(remainingSlots);
                            await Task.WhenAll(executionTasks);
                            DateTime utcNow = DateTime.UtcNow;
                            Interlocked.Exchange(ref m_lastExecutionTimestamp, utcNow.Ticks);
                        }
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e, "Error while executing strategies");
                    }
                    finally
                    {
                        // wait a little bit so we are not rate limited
                        await StrategyExecutionNextCycleDelayAsync(cancel);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                m_logger.LogInformation("Strategy execution cancelled.");
            }
        }
    }
}
}

// -----------------------------

// ==== FILE #2: DynamicTradingStrategyManager.cs ====
namespace CryptoBlade.Services {
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Services
{
    public class DynamicTradingStrategyManager : TradeStrategyManagerBase
    {
        private readonly record struct UnstuckingSymbols(HashSet<string> LongUnstucking, HashSet<string> ShortUnstucking);
        private readonly ILogger<DynamicTradingStrategyManager> m_logger;
        private readonly IOptions<TradingBotOptions> m_options;
        private readonly RollingWindowThrottler m_strategyShortThrottler;
        private readonly RollingWindowThrottler m_strategyLongThrottler;

        public DynamicTradingStrategyManager(IOptions<TradingBotOptions> options, 
            ILogger<DynamicTradingStrategyManager> logger, 
            ITradingStrategyFactory strategyFactory, 
            ICbFuturesRestClient restClient,
            ICbFuturesSocketClient socketClient, 
            IWalletManager walletManager) 
            : base(options, logger, strategyFactory, restClient, socketClient, walletManager)
        {
            m_options = options;
            m_logger = logger;
            DynamicBotCount dynamicBotCount = m_options.Value.DynamicBotCount;
            m_strategyShortThrottler = new RollingWindowThrottler(m_options.Value.DynamicBotCount.MaxDynamicStrategyOpenPerStep,
                dynamicBotCount.Step);
            m_strategyLongThrottler = new RollingWindowThrottler(m_options.Value.DynamicBotCount.MaxDynamicStrategyOpenPerStep,
                dynamicBotCount.Step);
        }

        protected virtual Task<bool> ShouldShortThrottleAsync(CancellationToken cancel)
        {
            bool shouldThrottle = m_strategyShortThrottler.ShouldThrottle(1, out _);
            return Task.FromResult(shouldThrottle);
        }

        protected virtual Task<bool> ShouldLongThrottleAsync(CancellationToken cancel)
        {
            bool shouldThrottle = m_strategyLongThrottler.ShouldThrottle(1, out _);
            return Task.FromResult(shouldThrottle);
        }

        private async Task<UnstuckingSymbols> ExecutePriorityUnstuckAsync(StrategyState strategyState, CancellationToken cancel)
        {
            Dictionary<string, ExecuteUnstuckParams> executeUnstuckParams = new Dictionary<string, ExecuteUnstuckParams>();
            Unstucking unstucking = m_options.Value.Unstucking;
            if (strategyState.UnrealizedPnlPercent.HasValue &&
                strategyState.UnrealizedPnlPercent.Value < unstucking.ForceUnstuckThresholdPercent)
            {
                if (m_options.Value.Unstucking.ForceKillTheWorst)
                {
                    var worstShort = Strategies.Values
                        .Where(x => x.IsInTrade && x.UnrealizedShortPnlPercent.HasValue &&
                                    x.UnrealizedShortPnlPercent.Value < unstucking.ForceUnstuckPositionThresholdPercent)
                        .MinBy(x => x.UnrealizedShortPnlPercent);
                    var worstLong = Strategies.Values
                        .Where(x => x.IsInTrade && x.UnrealizedLongPnlPercent.HasValue && 
                                    x.UnrealizedLongPnlPercent.Value < unstucking.ForceUnstuckPositionThresholdPercent)
                        .MinBy(x => x.UnrealizedLongPnlPercent);
                    ITradingStrategy? worst = null;
                    if (worstShort != null)
                        worst = worstShort;
                    if (worstLong != null)
                    {
                        if(worst == null)
                            worst = worstLong;
                        else if (worstLong.UnrealizedLongPnlPercent < worst.UnrealizedShortPnlPercent)
                            worst = worstLong;
                    }

                    if (worst != null)
                    {
                        executeUnstuckParams[worst.Symbol] = new ExecuteUnstuckParams
                        {
                            UnstuckShort = worstShort != null,
                            UnstuckLong = worstLong != null,
                            ForceUnstuckShort = worstShort != null,
                            ForceUnstuckLong = worstLong != null,
                            ForceKill = true,
                        };
                    }
                }
                else
                {
                    var strategiesWithShortLoss = Strategies.Values
                        .Where(x => x.IsInTrade && x.UnrealizedShortPnlPercent.HasValue &&
                                    x.UnrealizedShortPnlPercent.Value < unstucking.ForceUnstuckPositionThresholdPercent)
                        .Select(x => x.Symbol);
                    foreach (string s in strategiesWithShortLoss)
                    {
                        executeUnstuckParams.TryGetValue(s, out var unstuckArgs);
                        executeUnstuckParams[s] = unstuckArgs with { UnstuckShort = true, ForceUnstuckShort = true };
                    }

                    var strategiesWithLongLoss = Strategies.Values
                        .Where(x => x.IsInTrade && x.UnrealizedLongPnlPercent.HasValue &&
                                    x.UnrealizedLongPnlPercent.Value < unstucking.ForceUnstuckPositionThresholdPercent)
                        .Select(x => x.Symbol);
                    foreach (string s in strategiesWithLongLoss)
                    {
                        executeUnstuckParams.TryGetValue(s, out var unstuckArgs);
                        executeUnstuckParams[s] = unstuckArgs with { UnstuckLong = true, ForceUnstuckLong = true };
                    }
                }
            }
            else if (strategyState.UnrealizedPnlPercent.HasValue &&
                     strategyState.UnrealizedPnlPercent.Value < unstucking.SlowUnstuckThresholdPercent)
            {
                var strategiesWithShortLoss = Strategies.Values
                    .Where(x => x.IsInTrade && x.UnrealizedShortPnlPercent.HasValue &&
                                x.UnrealizedShortPnlPercent.Value < unstucking.SlowUnstuckPositionThresholdPercent)
                    .Select(x => x.Symbol);
                foreach (string s in strategiesWithShortLoss)
                {
                    executeUnstuckParams.TryGetValue(s, out var unstuckArgs);
                    executeUnstuckParams[s] = unstuckArgs with { UnstuckShort = true };
                }

                var strategiesWithLongLoss = Strategies.Values
                    .Where(x => x.IsInTrade 
                                && x.UnrealizedLongPnlPercent.HasValue 
                                && x.UnrealizedLongPnlPercent.Value < unstucking.SlowUnstuckPositionThresholdPercent)
                    .Select(x => x.Symbol);
                foreach (string s in strategiesWithLongLoss)
                {
                    executeUnstuckParams.TryGetValue(s, out var unstuckArgs);
                    executeUnstuckParams[s] = unstuckArgs with { UnstuckLong = true };
                }
            }

            try
            {
                List<Task> executionTasks = new List<Task>();
                await PrepareStrategyUnstuckExecutionAsync(executionTasks, executeUnstuckParams.Keys.ToArray(), executeUnstuckParams,
                    cancel);
                await Task.WhenAll(executionTasks);
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Error executing unstuck");
            }

            HashSet<string> shortUnstucking = new HashSet<string>(
                executeUnstuckParams
                    .Where(x => x.Value.UnstuckShort || x.Value.ForceUnstuckShort).Select(x => x.Key));
            HashSet<string> longUnstucking = new HashSet<string>(executeUnstuckParams
                .Where(x => x.Value.UnstuckLong || x.Value.ForceUnstuckLong).Select(x => x.Key));
            UnstuckingSymbols unstuckingSymbols = new UnstuckingSymbols(longUnstucking, shortUnstucking);

            return unstuckingSymbols;
        }

        protected override async Task StrategyExecutionAsync(CancellationToken cancel)
        {
            try
            {
                DynamicBotCount dynamicBotCount = m_options.Value.DynamicBotCount;
                while (!cancel.IsCancellationRequested)
                {
                    try
                    {
                        var hasInconsistent = Strategies.Values.Any(x => !x.ConsistentData);
                        if (hasInconsistent)
                        {
                            m_logger.LogWarning("Some strategies have inconsistent data. Reinitialize.");
                            await ReInitializeStrategies(cancel);
                        }

                        bool canContinue = await ProcessStrategyDataAsync(cancel);
                        if (!canContinue)
                            break;

                        var strategyState = await UpdateTradingStatesAsync(cancel);
                        m_logger.LogDebug(
                            "Total long exposure: {LongExposure}, total short exposure: {ShortExposure}, long WE: {LongWE}, short WE: {ShortWE}",
                            strategyState.TotalLongExposure,
                            strategyState.TotalShortExposure,
                            strategyState.TotalWalletLongExposure,
                            strategyState.TotalWalletShortExposure);

                        List<string> symbolsToProcess = new List<string>();
                        using (await Lock.LockAsync())
                        {
                            while (StrategyExecutionChannel.Reader.TryRead(out var symbol))
                                symbolsToProcess.Add(symbol);
                            HashSet<string> unstuckingLong = new HashSet<string>();
                            HashSet<string> unstuckingShort = new HashSet<string>();
                            if (m_options.Value.Unstucking.Enabled)
                            {
                                var unstuckingSymbolsLocal = await ExecutePriorityUnstuckAsync(strategyState, cancel);
                                unstuckingLong = new HashSet<string>(unstuckingSymbolsLocal.LongUnstucking);
                                unstuckingShort = new HashSet<string>(unstuckingSymbolsLocal.ShortUnstucking);
                            }

                            // exclude unstucking symbols from processing, it can probably be optimized to allow execution for some positions
                            var inTradeSymbols = symbolsToProcess
                                .Where(x => Strategies.TryGetValue(x, out var strategy) && strategy.IsInTrade)
                                .Distinct()
                                .ToArray();

                            bool criticalLong = m_options.Value.CriticalMode.EnableCriticalModeLong 
                                                && strategyState.TotalWalletLongExposure > m_options.Value.CriticalMode.WalletExposureThresholdLong;
                            bool criticalShort = m_options.Value.CriticalMode.EnableCriticalModeShort 
                                                 && strategyState.TotalWalletShortExposure > m_options.Value.CriticalMode.WalletExposureThresholdShort;

                            // by default already trading strategies can only maintain existing positions
                            Dictionary<string, ExecuteParams> executeParams =
                                inTradeSymbols.ToDictionary(x => x, x => new ExecuteParams(
                                    false, 
                                    false, 
                                    !criticalLong,
                                    !criticalShort,
                                    unstuckingLong.Contains(x), 
                                    unstuckingShort.Contains(x)));
                            
                            if (criticalLong)
                            {
                                // select highest exposure strategy to continue trading
                                var highestExposure = Strategies.Values
                                    .Where(x => x.IsInLongTrade && x.CurrentExposureLong.HasValue)
                                    .MaxBy(x => x.CurrentExposureLong!.Value);
                                if (highestExposure != null)
                                {
                                    executeParams.TryGetValue(highestExposure.Symbol, out var existingParams);
                                    executeParams[highestExposure.Symbol] =
                                        existingParams with { AllowExtraLong = true };
                                }
                            }

                            if (criticalShort)
                            {
                                // select highest exposure strategy to continue trading
                                var highestExposure = Strategies.Values
                                    .Where(x => x.IsInShortTrade && x.CurrentExposureShort.HasValue)
                                    .MaxBy(x => x.CurrentExposureShort!.Value);
                                if (highestExposure != null)
                                {
                                    executeParams.TryGetValue(highestExposure.Symbol, out var existingParams);
                                    executeParams[highestExposure.Symbol] =
                                        existingParams with { AllowExtraShort = true };
                                }
                            }

                            var inLongTradeSymbols = Strategies.Values.Where(x => x.IsInLongTrade).ToArray();
                            var inShortTradeSymbols = Strategies.Values.Where(x => x.IsInShortTrade).ToArray();
                            m_logger.LogDebug(
                                "Long strategies: '{LongStrategies}', short strategies: '{ShortStrategies}'",
                                inLongTradeSymbols.Length, inShortTradeSymbols.Length);

                            int remainingLongSlots = dynamicBotCount.MaxLongStrategies - inLongTradeSymbols.Length;
                            LogRemainingLongSlots(remainingLongSlots);
                            int remainingShortSlots = dynamicBotCount.MaxShortStrategies - inShortTradeSymbols.Length;
                            LogRemainingShortSlots(remainingShortSlots);
                            bool canAddLongPositions = remainingLongSlots > 0
                                                       && strategyState.TotalWalletLongExposure.HasValue
                                                       && strategyState.TotalWalletLongExposure.Value <
                                                       dynamicBotCount.TargetLongExposure
                                                       && !criticalLong;
                            bool canAddShortPositions = remainingShortSlots > 0
                                                        && strategyState.TotalWalletShortExposure.HasValue
                                                        && strategyState.TotalWalletShortExposure.Value <
                                                        dynamicBotCount.TargetShortExposure
                                                        && !criticalShort;
                            m_logger.LogDebug(
                                "Can add long positions: '{CanAddLongPositions}', can add short positions: '{CanAddShortPositions}'.",
                                canAddLongPositions,
                                canAddShortPositions);
                            // we need to put it back to hashset, we might open opposite position on the same symbol
                            HashSet<string> tradeSymbols = new HashSet<string>();
                            foreach (string inTradeSymbol in inTradeSymbols)
                                tradeSymbols.Add(inTradeSymbol);

                            string strategySelectIndicator;
                            decimal minSelectValue;
                            switch (m_options.Value.StrategySelectPreference)
                            {
                                case StrategySelectPreference.Volume:
                                    strategySelectIndicator = nameof(IndicatorType.MainTimeFrameVolume);
                                    minSelectValue = m_options.Value.MinimumVolume;
                                    break;
                                case StrategySelectPreference.NormalizedAverageTrueRange:
                                    strategySelectIndicator = nameof(IndicatorType.NormalizedAverageTrueRange);
                                    minSelectValue = m_options.Value.MinNormalizedAverageTrueRangePeriod;
                                    break;
                                default:
                                    throw new ArgumentOutOfRangeException();
                            }

                            if (canAddLongPositions)
                            {
                                int longStrategiesPerStep = Math.Min(dynamicBotCount.MaxDynamicStrategyOpenPerStep,
                                    remainingLongSlots);
                                var longStrategyCandidates = Strategies.Select(x => new
                                    {
                                        Strategy = x,
                                        x.Value.Indicators
                                    })
                                    .Where(x => x.Indicators.Any(i =>
                                        i.Name == strategySelectIndicator && i.Value is decimal value && value >= minSelectValue))
                                    .Where(x => !x.Strategy.Value.IsInLongTrade && x.Strategy.Value.HasBuySignal &&
                                                x.Strategy.Value.DynamicQtyLong.HasValue)
                                    .Select(x => new
                                    {
                                        Strategy = x,
                                        StrategySelectIndicator = (decimal)x.Indicators
                                            .First(i => i.Name == strategySelectIndicator).Value
                                    })
                                    .OrderByDescending(x => x.StrategySelectIndicator)
                                    .Take(longStrategiesPerStep)
                                    .Select(x => x.Strategy.Strategy.Value.Symbol)
                                    .ToArray();
                                foreach (string longStrategyCandidate in longStrategyCandidates)
                                {
                                    bool shouldThrottle = await ShouldLongThrottleAsync(cancel);
                                    if (shouldThrottle)
                                        break;
                                    m_logger.LogDebug("Adding long strategy '{LongStrategyCandidate}'",
                                        longStrategyCandidate);
                                    tradeSymbols.Add(longStrategyCandidate);
                                    executeParams.TryGetValue(longStrategyCandidate, out var existingParams);
                                    executeParams[longStrategyCandidate] = existingParams with { AllowLongOpen = true };
                                }
                            }

                            if (canAddShortPositions)
                            {
                                int shortStrategiesPerStep = Math.Min(dynamicBotCount.MaxDynamicStrategyOpenPerStep,
                                    remainingShortSlots);
                                var shortStrategyCandidates = Strategies.Select(x => new
                                    {
                                        Strategy = x,
                                        x.Value.Indicators
                                    })
                                    .Where(x => x.Indicators.Any(i =>
                                        i.Name == strategySelectIndicator && i.Value is decimal value && value >= minSelectValue))
                                    .Where(x => !x.Strategy.Value.IsInShortTrade && x.Strategy.Value.HasSellSignal &&
                                                x.Strategy.Value.DynamicQtyShort.HasValue)
                                    .Select(x => new
                                    {
                                        Strategy = x,
                                        StrategySelectIndicator = (decimal)x.Indicators
                                            .First(i => i.Name == strategySelectIndicator).Value
                                    })
                                    .OrderByDescending(x => x.StrategySelectIndicator)
                                    .Take(shortStrategiesPerStep)
                                    .Select(x => x.Strategy.Strategy.Value.Symbol)
                                    .ToArray();
                                foreach (string shortStrategyCandidate in shortStrategyCandidates)
                                {
                                    bool shouldThrottle = await ShouldShortThrottleAsync(cancel);
                                    if (shouldThrottle)
                                        break;
                                    m_logger.LogDebug("Adding short strategy '{ShortStrategyCandidate}'",
                                        shortStrategyCandidate);
                                    tradeSymbols.Add(shortStrategyCandidate);
                                    executeParams.TryGetValue(shortStrategyCandidate, out var existingParams);
                                    executeParams[shortStrategyCandidate] =
                                        existingParams with { AllowShortOpen = true };
                                }
                            }

                            List<Task> executionTasks = new List<Task>();
                            await PrepareStrategyExecutionAsync(executionTasks, tradeSymbols.ToArray(), executeParams,
                                cancel);
                            await Task.WhenAll(executionTasks);
                            DateTime utcNow = DateTime.UtcNow;
                            Interlocked.Exchange(ref m_lastExecutionTimestamp, utcNow.Ticks);
                        }
                    }
                    catch (Exception e)
                    {
                        if(e is not OperationCanceledException)
                            m_logger.LogError(e, "Error while executing strategies");
                    }
                    finally
                    {
                        // wait a little bit so we are not rate limited
                        await StrategyExecutionNextCycleDelayAsync(cancel);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                m_logger.LogInformation("Strategy execution cancelled.");
            }
        }
    }
}
}

// -----------------------------

// ==== FILE #3: ITradeStrategyManager.cs ====
// Uwaga: Ten plik zawiera interfejs – zadbaj o pełną dokumentację!
namespace CryptoBlade.Services
{
    public interface ITradeStrategyManager
    {
        DateTime LastExecution { get; }
        Task<ITradingStrategy[]> GetStrategiesAsync(CancellationToken cancel);
        Task StartStrategiesAsync(CancellationToken cancel);
        Task StopStrategiesAsync(CancellationToken cancel);
    }
}

// -----------------------------

// ==== FILE #4: NullTradeStrategyManager.cs ====
namespace CryptoBlade.Services
{
    public class NullTradeStrategyManager : ITradeStrategyManager
    {
        public DateTime LastExecution => DateTime.UtcNow;
        
        public Task<ITradingStrategy[]> GetStrategiesAsync(CancellationToken cancel)
        {
            return Task.FromResult(Array.Empty<ITradingStrategy>());
        }

        public Task StartStrategiesAsync(CancellationToken cancel)
        {
            return Task.CompletedTask;
        }

        public Task StopStrategiesAsync(CancellationToken cancel)
        {
            return Task.CompletedTask;
        }
    }
}

// -----------------------------

// ==== FILE #5: OptimizerHostedService.cs ====
namespace CryptoBlade.Services
{
    public class OptimizerHostedService : IHostedService
    {
        private readonly IOptimizer m_optimizer;

        public OptimizerHostedService(IOptimizer optimizer)
        {
            m_optimizer = optimizer;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await m_optimizer.RunAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await m_optimizer.StopAsync(cancellationToken);
        }
    }
}

// -----------------------------

// ==== FILE #6: TradeStrategyManagerBase.cs ====
namespace CryptoBlade.Services {
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using OrderStatus = CryptoBlade.Models.OrderStatus;
using PositionSide = CryptoBlade.Models.PositionSide;

namespace CryptoBlade.Services
{
    public abstract class TradeStrategyManagerBase : ITradeStrategyManager
    {
        protected readonly record struct SymbolCandle(string Symbol, Candle Candle);
        protected readonly record struct SymbolTicker(string Symbol, Ticker Ticker);
        private readonly ILogger<TradeStrategyManagerBase> m_logger;
        private readonly Dictionary<string, ITradingStrategy> m_strategies;
        private readonly ITradingStrategyFactory m_strategyFactory;
        private readonly ICbFuturesRestClient m_restClient;
        private readonly ICbFuturesSocketClient m_socketClient;
        private readonly IOptions<TradingBotOptions> m_options;
        private CancellationTokenSource? m_cancelSource;
        private readonly List<IUpdateSubscription> m_subscriptions;
        private readonly Channel<string> m_strategyExecutionChannel;
        private readonly AsyncLock m_lock;
        private Task? m_initTask;
        private Task? m_strategyExecutionTask;
        private readonly IWalletManager m_walletManager;
        protected long m_lastExecutionTimestamp;

        protected TradeStrategyManagerBase(IOptions<TradingBotOptions> options,
            ILogger<TradeStrategyManagerBase> logger,
            ITradingStrategyFactory strategyFactory,
            ICbFuturesRestClient restClient,
            ICbFuturesSocketClient socketClient, 
            IWalletManager walletManager)
        {
            m_lock = new AsyncLock();
            m_options = options;
            m_strategyFactory = strategyFactory;
            m_walletManager = walletManager;
            m_socketClient = socketClient;
            m_restClient = restClient;
            m_logger = logger;
            m_strategies = new Dictionary<string, ITradingStrategy>();
            m_subscriptions = new List<IUpdateSubscription>();
            m_strategyExecutionChannel = Channel.CreateUnbounded<string>();
            CandleChannel = Channel.CreateUnbounded<SymbolCandle>();
            TickerChannel = Channel.CreateUnbounded<SymbolTicker>();
            m_lastExecutionTimestamp = DateTime.UtcNow.Ticks;
        }

        protected Dictionary<string, ITradingStrategy> Strategies => m_strategies;
        protected Channel<string> StrategyExecutionChannel => m_strategyExecutionChannel;
        protected Channel<SymbolCandle> CandleChannel { get; }
        protected Channel<SymbolTicker> TickerChannel { get; }

        protected AsyncLock Lock => m_lock;

        public DateTime LastExecution
        {
            get
            {
                var last = Interlocked.Read(ref m_lastExecutionTimestamp);
                return new DateTime(last, DateTimeKind.Utc);
            }
        }

        public Task<ITradingStrategy[]> GetStrategiesAsync(CancellationToken cancel)
        {
            return Task.FromResult(m_strategies.Values.Select(x => x).ToArray());
        }

        public virtual async Task StartStrategiesAsync(CancellationToken cancel)
        {
            await CreateStrategiesAsync();
            m_cancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            CancellationToken ctsCancel = m_cancelSource.Token;
            m_initTask = Task.Run(async () => await InitStrategiesAsync(ctsCancel), ctsCancel);
        }

        protected virtual async Task StrategyExecutionDataDelayAsync(CancellationToken cancel)
        {
            int expectedUpdates = Strategies.Count;
            await StrategyExecutionChannel.Reader.WaitToReadAsync(cancel);
            TimeSpan totalWaitTime = TimeSpan.Zero;
            while (StrategyExecutionChannel.Reader.Count < expectedUpdates
                   && totalWaitTime < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(100, cancel);
                totalWaitTime += TimeSpan.FromMilliseconds(100);
            }
        }

        protected virtual async Task StrategyExecutionNextCycleDelayAsync(CancellationToken cancel)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancel);
        }

        protected virtual Task<bool> StrategyExecutionNextStepAsync(CancellationToken cancel)
        {
            return Task.FromResult(true);
        }

        protected async Task ProcessCandlesAsync(CancellationToken cancel)
        {
            List<SymbolCandle> candles = new List<SymbolCandle>();
            while (CandleChannel.Reader.TryRead(out var candle))
                candles.Add(candle);
            foreach (SymbolCandle symbolCandle in candles)
            {
                if (m_strategies.TryGetValue(symbolCandle.Symbol, out var strategy))
                    await strategy.AddCandleDataAsync(symbolCandle.Candle, cancel);
            }
        }

        protected async Task ProcessTickersAsync(CancellationToken cancel)
        {
            List<SymbolTicker> tickers = new List<SymbolTicker>();
            while (TickerChannel.Reader.TryRead(out var ticker))
                tickers.Add(ticker);
            foreach (SymbolTicker symbolTicker in tickers)
            {
                if (m_strategies.TryGetValue(symbolTicker.Symbol, out var strategy))
                    await strategy.UpdatePriceDataSync(symbolTicker.Ticker, cancel);
            }
        }

        protected async Task EvaluateSignalsAsync(CancellationToken cancel)
        {
            List<Task> evaluateTasks = new List<Task>();
            foreach (var strategy in m_strategies.Values)
            {
                Task evaluateTask = Task.Run(async () =>
                {
                    try
                    {
                        await strategy.EvaluateSignalsAsync(cancel);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception e)
                    {
                        m_logger.LogWarning(e, "Error while evaluating signals for strategy {Name} for symbol {Symbol}",
                                                       strategy.Name, strategy.Symbol);
                    }
                }, cancel);
                evaluateTasks.Add(evaluateTask);
            }
            await Task.WhenAll(evaluateTasks);
        }

        protected async Task<bool> ProcessStrategyDataAsync(CancellationToken cancel)
        {
            bool canContinue = await StrategyExecutionNextStepAsync(cancel);
            if (!canContinue)
                return canContinue;
            await StrategyExecutionDataDelayAsync(cancel);
            await ProcessTickersAsync(cancel);
            await ProcessCandlesAsync(cancel);
            await EvaluateSignalsAsync(cancel);
            return canContinue;
        }

        protected virtual Task PreInitializationPhaseAsync(CancellationToken cancel)
        {
            return Task.CompletedTask;
        }

        protected virtual async Task DelayBetweenEachSymbol(CancellationToken cancel)
        {
            TimeSpan delayBetweenEachSymbol = TimeSpan.FromMilliseconds(500);
            await Task.Delay(delayBetweenEachSymbol, cancel);
        }

        protected virtual async Task SymbolInitializationCallDelay(CancellationToken cancel)
        {
            TimeSpan callDelay = TimeSpan.FromMilliseconds(100);
            await Task.Delay(callDelay, cancel);
        }

        private async Task InitStrategiesAsync(CancellationToken cancel)
        {
            await PreInitializationPhaseAsync(cancel);
            var symbolInfo = await GetSymbolInfoAsync(cancel);
            Dictionary<string, SymbolInfo> symbolInfoDict = symbolInfo
                .DistinctBy(x => x.Name)
                .ToDictionary(x => x.Name, x => x);
            List<string> missingSymbols = m_strategies.Select(x => x.Key)
                .Where(x => !symbolInfoDict.ContainsKey(x))
                .ToList();
            // log missing symbols
            foreach (var symbol in missingSymbols)
                m_logger.LogWarning($"Symbol {symbol} is missing from the exchange.");

            foreach (string missingSymbol in missingSymbols)
                m_strategies.Remove(missingSymbol);

            foreach (ITradingStrategy strategy in m_strategies.Values)
            {
                await DelayBetweenEachSymbol(cancel);
                if (symbolInfoDict.TryGetValue(strategy.Symbol, out var info))
                    await strategy.SetupSymbolAsync(info, cancel);
            }
            
            var symbols = m_strategies.Values.Select(x => x.Symbol).ToArray();
            await UpdateTradingStatesAsync(cancel);

            var orderUpdateSubscription = await m_socketClient.SubscribeToOrderUpdatesAsync(OnOrderUpdate, cancel);
            orderUpdateSubscription.AutoReconnect(m_logger);
            m_subscriptions.Add(orderUpdateSubscription);

            var timeFrames = m_strategies.Values
                .SelectMany(x => x.RequiredTimeFrameWindows.Select(tfw => tfw.TimeFrame))
                .Distinct()
                .ToArray();

            foreach (TimeFrame timeFrame in timeFrames)
            {
                var klineUpdatesSubscription = await m_socketClient.SubscribeToKlineUpdatesAsync(
                    symbols,
                    timeFrame,
                    OnKlineUpdate,
                    cancel);
                klineUpdatesSubscription.AutoReconnect(m_logger);
                m_subscriptions.Add(klineUpdatesSubscription);
            }

            List<Task> initTasks = new();
            foreach (var strategy in m_strategies.Values)
            {
                await DelayBetweenEachSymbol(cancel);
                var initTask = InitializeStrategy(strategy, cancel);
                initTasks.Add(initTask);
            }

            foreach (ITradingStrategy strategiesValue in m_strategies.Values)
            {
                if (strategiesValue.IsInTrade)
                {
                    m_logger.LogInformation(
                        $"Strategy {strategiesValue.Name}:{strategiesValue.Symbol} is in trade after initialization. Scheduling trade execution.");
                    await m_strategyExecutionChannel.Writer.WriteAsync(strategiesValue.Symbol, cancel);
                }
            }

            var tickerSubscription = await m_socketClient.SubscribeToTickerUpdatesAsync(symbols, OnTicker, cancel);
            tickerSubscription.AutoReconnect(m_logger);
            m_subscriptions.Add(tickerSubscription);

            await Task.WhenAll(initTasks);
            m_strategyExecutionTask = Task.Run(async () => await StrategyExecutionAsync(cancel), cancel);
        }

        private void OnOrderUpdate(OrderUpdate orderUpdate)
        {
            if (orderUpdate.Status == OrderStatus.Filled)
            {
                // we want to schedule the strategy execution for the symbol after the order is filled
                m_logger.LogDebug($"Order {orderUpdate.OrderId} for symbol {orderUpdate.Symbol} is filled. Scheduling trade execution.");
                m_strategyExecutionChannel.Writer.TryWrite(orderUpdate.Symbol);
            }
        }

        private async void OnTicker(string symbol, Ticker ticker)
        {
            if (m_strategies.TryGetValue(symbol, out _))
                await TickerChannel.Writer.WriteAsync(new SymbolTicker(symbol, ticker));
        }

        private async void OnKlineUpdate(string symbol, Candle candle)
        {
            if (m_strategies.TryGetValue(symbol, out var strategy))
            {
                await CandleChannel.Writer.WriteAsync(new SymbolCandle(symbol, candle));
                bool isPrimaryCandle = strategy.RequiredTimeFrameWindows.Any(x => x.TimeFrame == candle.TimeFrame);
                if (isPrimaryCandle)
                {
                    m_logger.LogDebug(
                        $"Strategy {strategy.Name}:{strategy.Symbol} received primary candle. Scheduling trade execution.");
                    await m_strategyExecutionChannel.Writer.WriteAsync(strategy.Symbol, CancellationToken.None);
                }
            }
        }

        private async Task<SymbolInfo[]> GetSymbolInfoAsync(CancellationToken cancel)
        {
            var symbolData = await m_restClient.GetSymbolInfoAsync(cancel);

            return symbolData;
        }

        public async Task StopStrategiesAsync(CancellationToken cancel)
        {
            m_logger.LogInformation("Stopping strategies...");
            m_cancelSource?.Cancel();
            foreach (var subscription in m_subscriptions)
                await subscription.CloseAsync();
            var initTask  = m_initTask;
            try
            {
                if(initTask != null)
                    await initTask;
            }
            catch (OperationCanceledException)
            {
            }
            var executionTask = m_strategyExecutionTask;
            try
            {
                if (executionTask != null)
                    await executionTask;
            }
            catch (OperationCanceledException)
            {
            }

            m_cancelSource?.Dispose();
            m_strategies.Clear();
            m_subscriptions.Clear();
            m_logger.LogInformation("Strategies stopped.");
        }

        private Task CreateStrategiesAsync()
        {
            var config = m_options.Value;
            List<string> finalSymbolList = config.Whitelist
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Except(config.Blacklist.Where(x => !string.IsNullOrWhiteSpace(x)))
                .Distinct()
                .ToList();
            foreach (string symbol in finalSymbolList)
            {
                var strategy = m_strategyFactory.CreateStrategy(config, symbol);
                m_strategies[strategy.Symbol] = strategy;
            }

            return Task.CompletedTask;
        }

        protected async Task ReInitializeStrategies(CancellationToken cancel)
        {
            using (await m_lock.LockAsync())
            {
                List<Task> initTasks = new List<Task>();
                foreach (var tradingStrategy in m_strategies.Where(x => !x.Value.ConsistentData))
                {
                    await DelayBetweenEachSymbol(cancel);
                    var initTask = InitializeStrategy(tradingStrategy.Value, cancel);
                    initTasks.Add(initTask);
                }
                await Task.WhenAll(initTasks);
            }
        }

        private async Task InitializeStrategy(ITradingStrategy strategy, CancellationToken cancel)
        {
            var symbol = strategy.Symbol;
            var timeFrames = strategy.RequiredTimeFrameWindows;
            List<Candle> allCandles = new List<Candle>();
            foreach (var timeFrame in timeFrames)
            {
                await SymbolInitializationCallDelay(cancel);
                var candles = await m_restClient.GetKlinesAsync(symbol, timeFrame.TimeFrame, timeFrame.WindowSize + 1, cancel);
                
                allCandles.AddRange(candles);
            }

            await SymbolInitializationCallDelay(cancel);
            var ticker = await m_restClient.GetTickerAsync(symbol, cancel);
            await SymbolInitializationCallDelay(cancel);
            await strategy.InitializeAsync(allCandles.ToArray(), ticker, cancel);
        }

        private async Task<Order[]> GetOrdersAsync(CancellationToken cancel)
        {
            var orders = await m_restClient.GetOrdersAsync(cancel);

            return orders;
        }

        private async Task<Position[]> GetOpenPositions(CancellationToken cancel)
        {
            var positions = await m_restClient.GetPositionsAsync(cancel);

            return positions;
        }

        protected async Task<StrategyState> UpdateTradingStatesAsync(CancellationToken cancel)
        {
            m_logger.LogDebug("Updating trading state of all strategies.");
            var orders = await GetOrdersAsync(cancel);
            var symbolsFromOrders = orders.Where(x => x.Status != OrderStatus.Cancelled
                                                      && x.Status != OrderStatus.PartiallyFilledCanceled
                                                      && x.Status != OrderStatus.Deactivated
                                                      && x.Status != OrderStatus.Rejected)
                .Select(x => x.Symbol)
                .Distinct()
                .ToArray();
            var positions = await GetOpenPositions(cancel);

            var positionsPerSymbol = positions
                .DistinctBy(x => (x.Symbol, x.Side))
                .ToDictionary(x => (x.Symbol, x.Side));
            var symbols = symbolsFromOrders
                .Union(positionsPerSymbol.Select(x => x.Key.Symbol))
                .Distinct()
                .ToArray();
            using (await m_lock.LockAsync())
            {
                HashSet<string> activeStrategies = new HashSet<string>(symbols);
                foreach (KeyValuePair<string, ITradingStrategy> tradingStrategy in m_strategies)
                {
                    if(activeStrategies.Contains(tradingStrategy.Key))
                        continue;
                    await tradingStrategy.Value.UpdateTradingStateAsync(null, null, Array.Empty<Order>(), cancel);
                }

                foreach (string symbol in symbols)
                {
                    if (!m_strategies.TryGetValue(symbol, out var strategy))
                        continue;
                    var longPosition = positionsPerSymbol.TryGetValue((symbol, PositionSide.Buy), out var p) ? p : null;
                    var shortPosition = positionsPerSymbol.TryGetValue((symbol, PositionSide.Sell), out p) ? p : null;
                    var openOrders = orders
                        .Where(x => string.Equals(symbol, x.Symbol, StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    await strategy.UpdateTradingStateAsync(longPosition, shortPosition, openOrders, cancel);
                }
            }

            decimal longExposure = 0;
            decimal shortExposure = 0;
            if (positions.Any())
            {
                longExposure = positions.Where(x => x.Side == PositionSide.Buy).Sum(x => x.Quantity * x.AveragePrice);
                shortExposure = positions.Where(x => x.Side == PositionSide.Sell).Sum(x => x.Quantity * x.AveragePrice);
            }

            var wallet = m_walletManager.Contract;
            decimal? totalWalletLongExposure = null;
            decimal? totalWalletShortExposure = null;
            decimal? unrealizedPnlPercent = null;
            if (wallet.WalletBalance.HasValue && wallet.WalletBalance.Value > 0)
            {
                totalWalletLongExposure = longExposure / wallet.WalletBalance;
                totalWalletShortExposure = shortExposure / wallet.WalletBalance;
            }

            if (wallet.WalletBalance.HasValue && wallet.WalletBalance.Value > 0 && wallet.UnrealizedPnl.HasValue)
            {
                unrealizedPnlPercent = wallet.UnrealizedPnl / wallet.WalletBalance;
            }

            return new StrategyState(longExposure, shortExposure, totalWalletLongExposure, totalWalletShortExposure, unrealizedPnlPercent);
        }

        protected abstract Task StrategyExecutionAsync(CancellationToken cancel);

        protected void LogRemainingSlots(int remainingSlots)
        {
            if (remainingSlots < 0)
                remainingSlots = 0;
            m_logger.LogDebug("Remaining strategy slots: {RemainingSlots}", remainingSlots);
        }

        protected void LogRemainingLongSlots(int remainingSlots)
        {
            if (remainingSlots < 0)
                remainingSlots = 0;
            m_logger.LogDebug("Remaining long strategy slots: {RemainingSlots}", remainingSlots);
        }

        protected void LogRemainingShortSlots(int remainingSlots)
        {
            if (remainingSlots < 0)
                remainingSlots = 0;
            m_logger.LogDebug("Remaining short strategy slots: {RemainingSlots}", remainingSlots);
        }

        protected Task PrepareStrategyExecutionAsync(List<Task> strategyExecutionTasks, 
            string[] symbols, 
            Dictionary<string, ExecuteParams> executionParams, 
            CancellationToken cancel)
        {
            foreach (var symbol in symbols)
            {
                if (!m_strategies.TryGetValue(symbol, out var strategy))
                    continue;
                Task strategyExecutionTask = Task.Run(async () =>
                {
                    try
                    {
                        executionParams.TryGetValue(symbol, out var execParam);
                        await strategy.ExecuteAsync(execParam, cancel);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e, "Error while executing strategy {Name} for symbol {Symbol}",
                            strategy.Name, symbol);
                    }
                }, cancel);
                strategyExecutionTasks.Add(strategyExecutionTask);
            }
            return Task.CompletedTask;
        }

        protected Task PrepareStrategyUnstuckExecutionAsync(List<Task> strategyExecutionTasks,
            string[] symbols,
            Dictionary<string, ExecuteUnstuckParams> executionParams,
            CancellationToken cancel)
        {
            foreach (var symbol in symbols)
            {
                if (!m_strategies.TryGetValue(symbol, out var strategy))
                    continue;
                Task strategyExecutionTask = Task.Run(async () =>
                {
                    try
                    {
                        executionParams.TryGetValue(symbol, out var execParam);
                        await strategy.ExecuteUnstuckAsync(execParam.UnstuckLong, 
                            execParam.UnstuckShort, 
                            execParam.ForceUnstuckLong, 
                            execParam.ForceUnstuckShort, 
                            execParam.ForceKill,
                            cancel);
                    }
                    catch (Exception e)
                    {
                        m_logger.LogError(e, "Error while executing strategy {Name} for symbol {Symbol}",
                            strategy.Name, symbol);
                    }
                }, cancel);
                strategyExecutionTasks.Add(strategyExecutionTask);
            }
            return Task.CompletedTask;
        }

        protected readonly record struct StrategyState(
            decimal TotalLongExposure, 
            decimal TotalShortExposure, 
            decimal? TotalWalletLongExposure, 
            decimal? TotalWalletShortExposure,
            decimal? UnrealizedPnlPercent);

        protected record struct ExecuteUnstuckParams(bool UnstuckLong, bool UnstuckShort, bool ForceUnstuckLong, bool ForceUnstuckShort, bool ForceKill);
    }
}
}

// -----------------------------

// ==== FILE #7: TradingHostedService.cs ====
namespace CryptoBlade.Services
{
    public class TradingHostedService : IHostedService
    {
        private readonly ITradeStrategyManager m_strategyManager;
        private readonly IWalletManager m_walletManager;

        public TradingHostedService(ITradeStrategyManager strategyManager, IWalletManager walletManager)
        {
            m_strategyManager = strategyManager;
            m_walletManager = walletManager;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await m_walletManager.StartAsync(cancellationToken);
            await m_strategyManager.StartStrategiesAsync(cancellationToken);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await m_walletManager.StopAsync(cancellationToken);
            await m_strategyManager.StopStrategiesAsync(cancellationToken);
        }
    }
}

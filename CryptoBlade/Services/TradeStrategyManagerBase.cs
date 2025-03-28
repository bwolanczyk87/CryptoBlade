﻿using System.Threading.Channels;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Symbols;
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
        private readonly ITradingSymbolsManager m_symbolsManager;
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
            ITradingSymbolsManager symbolsManager,
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
            m_symbolsManager = symbolsManager;
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
            m_cancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            CancellationToken ctsCancel = m_cancelSource.Token;
            m_initTask = Task.Run(async () => await InitStrategiesAsync(ctsCancel), ctsCancel);
            await m_initTask;
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
            var tadingSymbolsInfo = await m_symbolsManager.GetTradingSymbolsAsync(
                m_restClient,
                m_options.Value.Whitelist.ToList(), 
                m_options.Value.Blacklist.ToList(),
                new SymbolPreferences
                {
                    Maturity = m_options.Value.SymbolMaturityPreference,
                    Volume = m_options.Value.SymbolVolumePreference,
                    Volatility = m_options.Value.SymbolVolatilityPreference
                },
                ConfigPaths.DefaultHistoricalDataDirectory,
                cancel);    

            await PreInitializationPhaseAsync(cancel);
            await CreateStrategiesAsync(tadingSymbolsInfo);

            foreach (ITradingStrategy strategy in m_strategies.Values)
            {
                await DelayBetweenEachSymbol(cancel);
                var symbol = tadingSymbolsInfo.Where(x => x.Name == strategy.Symbol).First();
                await strategy.SetupSymbolAsync(symbol, cancel);
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

        private Task CreateStrategiesAsync(List<SymbolInfo> symbols)
        {
            foreach (var symbol in symbols)
            {
                var strategy = m_strategyFactory.CreateStrategy(m_options.Value, symbol.Name);
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
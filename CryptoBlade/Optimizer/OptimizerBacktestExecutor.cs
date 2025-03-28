﻿using System.Text.Json;
using Bybit.Net.Clients;
using CryptoBlade.BackTesting;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Services;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Symbols;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Optimizer
{
    public class OptimizerBacktestExecutor : IBacktestExecutor
    {
        private readonly IHistoricalDataStorage m_historicalDataStorage;
        private readonly ITradingSymbolsManager m_tradingSymbolsManager;

        public OptimizerBacktestExecutor(IHistoricalDataStorage historicalDataStorage, ITradingSymbolsManager symbolsManager)
        {
            m_historicalDataStorage = historicalDataStorage;
            m_tradingSymbolsManager = symbolsManager;
        }

        public async Task<BacktestPerformanceResult> ExecuteAsync(IOptions<TradingBotOptions> options, CancellationToken cancel)
        {
            const string historicalDataDirectory = ConfigPaths.DefaultHistoricalDataDirectory;
            IOptions<BackTestExchangeOptions> backTestExchangeOptions = Options.Create(new BackTestExchangeOptions
            {
                Whitelist = options.Value.Whitelist,
                Start = options.Value.BackTest.Start,
                End = options.Value.BackTest.End,
                InitialBalance = options.Value.BackTest.InitialBalance,
                StartupCandleData = options.Value.BackTest.StartupCandleData,
                MakerFeeRate = options.Value.MakerFeeRate,
                TakerFeeRate = options.Value.TakerFeeRate,
                HistoricalDataDirectory = historicalDataDirectory,
            });
            IBackTestDataDownloader backTestDataDownloader = new OptimizerBacktestDataDownloader();
            IOptions<BybitCbFuturesRestClientOptions> bybitCbFuturesRestClientOptions = Options.Create(new BybitCbFuturesRestClientOptions
            {
                PlaceOrderAttempts = options.Value.PlaceOrderAttempts,
            });
            BybitCbFuturesRestClient bybitCbFuturesRestClient = new BybitCbFuturesRestClient(bybitCbFuturesRestClientOptions,
                options,
                new BybitRestClient(),
                ApplicationLogging.CreateLogger<BybitCbFuturesRestClient>());
            BackTestExchange backTestExchange = new BackTestExchange(
                backTestExchangeOptions,
                backTestDataDownloader,
                m_historicalDataStorage,
                bybitCbFuturesRestClient,
                m_tradingSymbolsManager);
            WalletManager walletManager = new WalletManager(ApplicationLogging.CreateLogger<WalletManager>(), backTestExchange, backTestExchange);
            TradingStrategyFactory tradingStrategyFactory = new TradingStrategyFactory(walletManager, backTestExchange, options);
            OptimizerApplicationHostApplicationLifetime backtestLifeTime = new OptimizerApplicationHostApplicationLifetime(cancel);
            TaskCompletionSource<bool> backtestDone = new TaskCompletionSource<bool>();
            backtestLifeTime.ApplicationStoppedEvent += _ => backtestDone.TrySetResult(true);
            BackTestDynamicTradingStrategyManager dynamicTradingStrategyManager = new BackTestDynamicTradingStrategyManager(
                options,
                ApplicationLogging.CreateLogger<DynamicTradingStrategyManager>(),
                backTestExchange,
                m_tradingSymbolsManager,
                tradingStrategyFactory,
                walletManager,
                backtestLifeTime);
            ExternalBackTestIdProvider externalBackTestIdProvider = new ExternalBackTestIdProvider(options.Value.CalculateMd5());
            BackTestPerformanceTracker backTestPerformanceTracker = new BackTestPerformanceTracker(
                options,
                backTestExchange,
                externalBackTestIdProvider,
                ApplicationLogging.CreateLogger<BackTestPerformanceTracker>());
            TradingHostedService tradingHostedService =
                new TradingHostedService(dynamicTradingStrategyManager, walletManager);
            await backTestPerformanceTracker.StartAsync(cancel);
            await tradingHostedService.StartAsync(cancel);
            await backtestDone.Task;
            cancel.ThrowIfCancellationRequested(); // we don't want to save the result if the backtest was cancelled
            await backTestPerformanceTracker.StopAsync(cancel);
            await tradingHostedService.StopAsync(cancel);
            var result = backTestPerformanceTracker.Result;
            return result;
        }
    }
}
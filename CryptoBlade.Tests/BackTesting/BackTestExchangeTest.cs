﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bybit.Net.Clients;
using CryptoBlade.BackTesting;
using CryptoBlade.BackTesting.Bybit;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Symbols;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace CryptoBlade.Tests.BackTesting
{
    public class BackTestExchangeTest : TestBase
    {
        public BackTestExchangeTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        [Fact]
        public async Task BackTestExchangeTestAsync()
        {
            var start = new DateTime(2023, 8, 8);
            var end = new DateTime(2023, 8, 10);
            var symbols = new[]
            {
                "SOLUSDT",
            };

            var exchangeOptions = Options.Create(new BackTestExchangeOptions
            {
                Start = start,
                End = end,
                Whitelist = symbols,
            });

            var options = Options.Create(new ProtoHistoricalDataStorageOptions
            {
                Directory = ConfigPaths.DefaultHistoricalDataDirectory,
            });
            var bybit = new BybitRestClient();
            var cbRestClientOptions = Options.Create(new BybitCbFuturesRestClientOptions
            {
                PlaceOrderAttempts = 5
            });
            var tradingBotOptions = Options.Create(new TradingBotOptions());
            var cbRestClient = new BybitCbFuturesRestClient(cbRestClientOptions,
                tradingBotOptions,
                bybit,
                ApplicationLogging.CreateLogger<BybitCbFuturesRestClient>());
            var storage = new ProtoHistoricalDataStorage(options);
            var downloader = new BybitHistoricalDataDownloader(
                storage,
                ApplicationLogging.CreateLogger<BybitHistoricalDataDownloader>(),
                cbRestClient);
            BackTestDataDownloader backTestDataDownloader =
                new BackTestDataDownloader(downloader);
            TradingSymbolsManager tradingSymbolsManager = new TradingSymbolsManager(
                ApplicationLogging.CreateLogger<TradingSymbolsManager>());
            var exchange = new BackTestExchange(exchangeOptions, backTestDataDownloader, storage, cbRestClient, tradingSymbolsManager);
            await exchange.PrepareDataAsync();
            List<IUpdateSubscription> subscriptions = new List<IUpdateSubscription>();
            
            List<Candle> oneMinuteCandles = new List<Candle>();
            List<Candle> dailyCandles = new List<Candle>();
            var candleSubscription = await exchange.SubscribeToKlineUpdatesAsync(symbols, TimeFrame.OneMinute,
                (_, candle) => { oneMinuteCandles.Add(candle); }, CancellationToken.None);
            subscriptions.Add(candleSubscription);
            var candleSubscription2 = await exchange.SubscribeToKlineUpdatesAsync(symbols, TimeFrame.OneDay,
                (_, candle) => { dailyCandles.Add(candle); }, CancellationToken.None);
            subscriptions.Add(candleSubscription2);

            List<Ticker> tickers = new List<Ticker>();
            var tickerSubscription = await exchange.SubscribeToTickerUpdatesAsync(symbols,
                               (_, ticker) => { tickers.Add(ticker); }, CancellationToken.None);
            subscriptions.Add(tickerSubscription);

            var oneMinuteInitialCandles = await exchange.GetKlinesAsync(symbols[0], TimeFrame.OneMinute, 100);
            Assert.NotEmpty(oneMinuteInitialCandles);
            var fiveMinutesInitialInitialCandles = await exchange.GetKlinesAsync(symbols[0], TimeFrame.FiveMinutes, 100);
            Assert.NotEmpty(fiveMinutesInitialInitialCandles);
            var ticker = await exchange.GetTickerAsync(symbols[0]);
            Assert.NotNull(ticker);

            while (await exchange.AdvanceTimeAsync())
            {
            }
            Assert.NotEmpty(oneMinuteCandles);
            Assert.NotEmpty(dailyCandles);
            Assert.NotEmpty(tickers);
            foreach (var updateSubscription in subscriptions)
                await updateSubscription.CloseAsync();
        }
    }
}

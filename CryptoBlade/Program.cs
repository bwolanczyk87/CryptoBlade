using System.Reflection;
using Binance.Net.Clients;
using Bybit.Net;
using CryptoBlade.BackTesting;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.HealthChecks;
using CryptoBlade.Helpers;
using CryptoBlade.Services;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Wallet;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using Microsoft.Extensions.Options;
using Bybit.Net.Clients;
using CryptoBlade.BackTesting.Binance;
using CryptoBlade.BackTesting.Bybit;
using CryptoBlade.Optimizer;
using Bybit.Net.Interfaces.Clients;
using CryptoBlade.Strategies.Symbols;

namespace CryptoBlade
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
            {
                builder.Configuration
                    .AddJsonFile("appsettings.Development.json", optional: false, reloadOnChange: true);
            }
            builder.Configuration
                .AddJsonFile("appsettings.Accounts.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables("CB_");
            var debugView = builder.Configuration.GetDebugView();
            string[] debugViewLines = [.. debugView.Split(Environment.NewLine)
                .Where(x => !x.Contains("ApiKey", StringComparison.OrdinalIgnoreCase)
                && !x.Contains("ApiSecret", StringComparison.OrdinalIgnoreCase))];
            debugView = string.Join(Environment.NewLine, debugViewLines);

            var tradingBotOptions = builder.Configuration.GetSection("TradingBot").Get<TradingBotOptions>();

            // Add services to the container.
            builder.Services.AddRazorPages();
            var healthChecksBuilder = builder.Services.AddHealthChecks();
            builder.Services.AddHostedService<TradingHostedService>();
            builder.Services.Configure<TradingBotOptions>(builder.Configuration.GetSection("TradingBot"));
            builder.Services.AddLogging(options =>
            {
                options.AddSimpleConsole(o =>
                {
                    o.UseUtcTimestamp = true;
                    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                });
            });

            if(tradingBotOptions == null)
            {
                Console.WriteLine("No configuration found.");
                return;
            }
            switch (tradingBotOptions.BotMode)
            {
                case BotMode.Live:
                    AddLiveDependencies(builder, healthChecksBuilder);
                    break;
                case BotMode.Backtest:
                    AddBackTestDependencies(builder, healthChecksBuilder);
                    break;
                case BotMode.Optimizer:
                    AddOptimizerDependencies(builder, healthChecksBuilder);
                    break;
                default:
                    Console.WriteLine("Unsupported bot mode.");
                    return;
            }

            var app = builder.Build();
            var lf = app.Services.GetRequiredService<ILoggerFactory>();
            ApplicationLogging.LoggerFactory = lf;
            LogVersionAndConfiguration(debugView);

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();
            app.MapHealthChecks("/healthz");
            app.MapRazorPages();
            app.Run();
        }

        private static void LogVersionAndConfiguration(string configuration)
        {
            var logger = ApplicationLogging.LoggerFactory.CreateLogger("Startup");
            var assembly = Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            logger.LogInformation($"CryptoBlade v{version}");
            logger.LogInformation(configuration);
        }

        private static void AddOptimizerDependencies(WebApplicationBuilder builder,
            IHealthChecksBuilder healthChecksBuilder)
        {
            healthChecksBuilder.AddCheck<BacktestExecutionHealthCheck>("Backtest");
            builder.Services.AddHostedService<OptimizerHostedService>();
            builder.Services.AddSingleton<IOptimizer, GeneticAlgorithmOptimizer>();
            builder.Services.AddSingleton<IWalletManager, NullWalletManager>();
            builder.Services.AddSingleton<ITradingSymbolsManager, TradingSymbolsManager>();
            builder.Services.AddSingleton<ITradeStrategyManager, NullTradeStrategyManager>();
        }

        private static void AddBackTestDependencies(WebApplicationBuilder builder, IHealthChecksBuilder healthChecksBuilder)
        { 
            builder.Services.AddSingleton<ITradingSymbolsManager, TradingSymbolsManager>();
            builder.Services.AddSingleton<ITradingStrategyFactory, TradingStrategyFactory>();
            builder.Services.AddSingleton<IBackTestIdProvider, BackTestIdProvider>();
            healthChecksBuilder.AddCheck<BacktestExecutionHealthCheck>("Backtest");
            var tradingBotOptions = builder.Configuration.GetSection("TradingBot").Get<TradingBotOptions>();
            TradingMode tradingMode = tradingBotOptions!.TradingMode;

            if (tradingMode == TradingMode.DynamicBackTest)
                builder.Services.AddSingleton<ITradeStrategyManager, BackTestDynamicTradingStrategyManager>();

            builder.Services.AddSingleton<IWalletManager, WalletManager>();
            builder.Services.AddSingleton(sp =>
            {
                var options = sp.GetRequiredService<IOptions<BackTestExchangeOptions>>();
                var backtestDownloader = sp.GetRequiredService<IBackTestDataDownloader>();
                var historicalDataStorage = sp.GetRequiredService<IHistoricalDataStorage>();
                var cbRestClient = CreateUnauthorizedBybitClient(Options.Create(tradingBotOptions));
                var symbolManager = sp.GetRequiredService<ITradingSymbolsManager>();

                var exchange = new BackTestExchange(
                    options, 
                    backtestDownloader, 
                    historicalDataStorage,
                    cbRestClient,
                    symbolManager);
                return exchange;
            });
            const string historicalDataDirectory = ConfigPaths.DefaultHistoricalDataDirectory;
            builder.Services.AddOptions<BackTestExchangeOptions>().Configure(x =>
            {
                x.Start = tradingBotOptions.BackTest.Start;
                x.End = tradingBotOptions.BackTest.End;
                x.InitialBalance = tradingBotOptions.BackTest.InitialBalance;
                x.StartupCandleData = tradingBotOptions.BackTest.StartupCandleData;
                x.Whitelist = tradingBotOptions.Whitelist;
                x.Blacklist = tradingBotOptions.Blacklist;
                x.MakerFeeRate = tradingBotOptions.MakerFeeRate;
                x.TakerFeeRate = tradingBotOptions.TakerFeeRate;
                x.HistoricalDataDirectory = historicalDataDirectory;
                x.SymbolMaturityPreference = tradingBotOptions.SymbolMaturityPreference;
                x.SymbolVolumePreference = tradingBotOptions.SymbolVolumePreference;
                x.SymbolVolatilityPreference = tradingBotOptions.SymbolVolatilityPreference;
            });
            builder.Services.AddOptions<TradingBotOptions>().Configure(x =>
                x = tradingBotOptions
            );
            builder.Services.AddSingleton<IBackTestDataDownloader, BackTestDataDownloader>();
            builder.Services.AddSingleton(provider =>
            {
                var historicalDataStorage = provider.GetRequiredService<IHistoricalDataStorage>();
                IHistoricalDataDownloader downloader;
                switch (tradingBotOptions.BackTest.DataSource)
                {
                    case DataSource.Bybit:
                        var bybitLogger = ApplicationLogging.CreateLogger<BybitHistoricalDataDownloader>();
                        var bybitClient = CreateUnauthorizedBybitClient(Options.Create(tradingBotOptions));
                        downloader = new BybitHistoricalDataDownloader(
                            historicalDataStorage,
                            bybitLogger,
                            bybitClient);
                        break;
                    case DataSource.Binance:
                        var binanceLogger = ApplicationLogging.CreateLogger<BinanceHistoricalDataDownloader>();
                        var binanceClient = CreateUnauthorizedBinanceClient();
                        downloader = new BinanceHistoricalDataDownloader(
                            historicalDataStorage,
                            binanceLogger,
                            binanceClient);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                
                return downloader;
            });
            builder.Services.AddSingleton<IHistoricalDataStorage, ProtoHistoricalDataStorage>();
            builder.Services.AddOptions<ProtoHistoricalDataStorageOptions>().Configure(x =>
            {
                x.Directory = historicalDataDirectory;
            });
            builder.Services.AddSingleton<ICbFuturesRestClient>(sp => sp.GetRequiredService<BackTestExchange>());
            builder.Services.AddSingleton<ICbFuturesSocketClient>(sp => sp.GetRequiredService<BackTestExchange>());
            builder.Services.AddSingleton<IBackTestRunner>(sp => sp.GetRequiredService<BackTestExchange>());
            builder.Services.AddHostedService<BackTestPerformanceTracker>();
        }

        private static BybitCbFuturesRestClient CreateUnauthorizedBybitClient(IOptions<TradingBotOptions> tradingBotOptions)
        {
            var bybit = new BybitRestClient();
            var cbRestClientOptions = Options.Create(new BybitCbFuturesRestClientOptions
            {
                PlaceOrderAttempts = 5
            });
            var cbRestClient = new BybitCbFuturesRestClient(cbRestClientOptions, 
                tradingBotOptions,
                bybit,
                ApplicationLogging.CreateLogger<BybitCbFuturesRestClient>());

            return cbRestClient;
        }

        private static BinanceCbFuturesRestClient CreateUnauthorizedBinanceClient()
        {
            var binance = new BinanceRestClient();
            var cbRestClient = new BinanceCbFuturesRestClient(
                ApplicationLogging.CreateLogger<BinanceCbFuturesRestClient>(),
                binance);

            return cbRestClient;
        }

        private static void AddLiveDependencies(WebApplicationBuilder builder, IHealthChecksBuilder healthChecksBuilder)
        {
            builder.Services.AddSingleton<ITradingSymbolsManager, TradingSymbolsManager>();
            builder.Services.AddSingleton<ITradingStrategyFactory, TradingStrategyFactory>();
            healthChecksBuilder.AddCheck<TradeExecutionHealthCheck>("TradeExecution");
            var tradingBotOptions = builder.Configuration.GetSection("TradingBot").Get<TradingBotOptions>();
            TradingMode tradingMode = tradingBotOptions!.TradingMode;

            if (tradingMode == TradingMode.Readonly || tradingMode == TradingMode.Dynamic)
            {
                builder.Services.AddSingleton<ITradeStrategyManager, DynamicTradingStrategyManager>();
            }
            else if (tradingMode == TradingMode.Normal)
            {
                builder.Services.AddSingleton<ITradeStrategyManager, DefaultTradingStrategyManager>();
            }

            var mainAccount = tradingBotOptions.Accounts.FirstOrDefault(x => string.Equals(x.Name, tradingBotOptions.AccountName, StringComparison.Ordinal)) ?? 
                throw new InvalidOperationException("No account found with the name specified in the configuration.");

            if (mainAccount.HasApiCredentials())
                builder.Services.AddSingleton<IWalletManager, WalletManager>();
            else
                builder.Services.AddSingleton<IWalletManager, NullWalletManager>();

            builder.Services.AddBybit(
                restOptions =>
                {
                    restOptions.RateLimitingBehaviour = RateLimitingBehaviour.Wait;

                    if (mainAccount.HasApiCredentials())
                        restOptions.V5Options.ApiCredentials = new ApiCredentials(mainAccount.ApiKey, mainAccount.ApiSecret);

                    if (mainAccount.IsDemo)
                    {
                        restOptions.Environment = (BybitEnvironment)BybitEnvironment.CreateCustom("BybitEnvironment.Demo", "https://api-demo.bybit.com", "wss://stream-demo.bybit.com");
                    }
                    else
                    {
                        restOptions.ReceiveWindow = TimeSpan.FromSeconds(10);
                        restOptions.AutoTimestamp = true;
                        restOptions.TimestampRecalculationInterval = TimeSpan.FromSeconds(10);
                    }
                });

            builder.Services.AddSingleton<IBybitSocketClientMain>(provider =>
            {
                return new BybitSocketClientMain(socketClientOptions =>
                {
                    if (mainAccount.HasApiCredentials())
                    {
                        socketClientOptions.V5Options.ApiCredentials = new ApiCredentials(mainAccount.ApiKey, mainAccount.ApiSecret);
                    }
                    if(mainAccount.IsDemo)
                    {
                        socketClientOptions.Environment = (BybitEnvironment)BybitEnvironment.CreateCustom("BybitEnvironment.Demo", "https://api-demo.bybit.com", "wss://stream-demo.bybit.com");
                    }
                });
            });

            if (mainAccount.IsDemo)
            {
                var secondaryAccount = tradingBotOptions.Accounts.FirstOrDefault(x => !x.IsDemo) ??
                    throw new InvalidOperationException("No secondary account found with the name specified in the configuration.");

                builder.Services.AddSingleton<IBybitSocketClientSecondary>(provider =>
                {
                    return new BybitSocketClientSecondary(socketClientOptions =>
                    {
                        if (secondaryAccount.HasApiCredentials())
                        {
                            socketClientOptions.V5Options.ApiCredentials = new ApiCredentials(secondaryAccount.ApiKey, secondaryAccount.ApiSecret);
                        }
                    });
                });

                builder.Services.AddSingleton<ICbFuturesSocketClient>(provider =>
                {
                    IBybitSocketClient bybitSocketClientMain = (IBybitSocketClient)provider.GetRequiredService<IBybitSocketClientMain>();
                    IBybitSocketClient bybitSocketClientSecondary = (IBybitSocketClient)provider.GetRequiredService<IBybitSocketClientSecondary>();
                    return new BybitCbFuturesSocketClient(bybitSocketClientMain, bybitSocketClientSecondary, Options.Create(tradingBotOptions));
                });
            }
            else
            {
                builder.Services.AddSingleton<ICbFuturesSocketClient>(provider =>
                {
                    IBybitSocketClient bybitSocketClientMain = (IBybitSocketClient)provider.GetRequiredService<IBybitSocketClientMain>();
                    return new BybitCbFuturesSocketClient(bybitSocketClientMain, null, Options.Create(tradingBotOptions));
                });
            }

            builder.Services.AddSingleton<ICbFuturesRestClient, BybitCbFuturesRestClient>();
            builder.Services.AddOptions<BybitCbFuturesRestClientOptions>().Configure(options =>
            {
                options.PlaceOrderAttempts = tradingBotOptions.PlaceOrderAttempts;
            });
        }
    }
}
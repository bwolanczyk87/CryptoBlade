using Binance.Net.Clients;
using Bybit.Net;
using Bybit.Net.Clients;
using Bybit.Net.Interfaces.Clients;
using CryptoBlade.Authentication;
using CryptoBlade.BackTesting;
using CryptoBlade.BackTesting.Bybit;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Exchanges.Interfaces;
using CryptoBlade.HealthChecks;
using CryptoBlade.Helpers;
using CryptoBlade.Services;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Symbols;
using CryptoBlade.Strategies.Wallet;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Objects;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text.Json.Serialization;

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

            var tradingBotOptions = builder.Configuration.GetSection("TradingBot").Get<TradingBotOptions>();

            builder.Services.AddRazorPages();
            var healthChecksBuilder = builder.Services.AddHealthChecks();
            builder.Services.AddHostedService<TradingHostedService>();
            builder.Services.Configure<TradingBotOptions>(builder.Configuration.GetSection("TradingBot"));
            builder.Services.AddLogging(logging =>
            {
                logging.AddSimpleConsole(o =>
                {
                    o.UseUtcTimestamp = true;
                    o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                    o.ColorBehavior = LoggerColorBehavior.Enabled;
                    o.SingleLine = true;
                });

                logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                logging.AddFilter("System.Net.Http.HttpClient.IBybitRestClient", LogLevel.Warning);
                logging.AddFilter("System.Net.Http.HttpClient.IBybitRestClient.ClientHandler", LogLevel.Warning);
                logging.AddFilter("System.Net.Http.HttpClient.IBybitRestClient.LogicalHandler", LogLevel.Warning);
            });
            builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new() { Title = "CryptoBlade API", Version = "v1" });
                c.AddSecurityDefinition("ApiKey", new()
                {
                    In = ParameterLocation.Header,
                    Name = "X-API-TOKEN",
                    Type = SecuritySchemeType.ApiKey,
                    Description = "Constant token from appsettings"
                });
                c.AddSecurityRequirement(new()
                {
                   {
                        new() { Reference = new() { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } },
                        Array.Empty<string>()
                   }
                });
                c.CustomSchemaIds(t => t.FullName);
                c.SchemaFilter<StringEnumSchemaFilter>();
            });

            builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.Scheme)
                 .AddScheme<ApiKeySchemeOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationHandler.Scheme, _ => { });

            builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ApiKey"));

            if (tradingBotOptions == null)
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
            ApplicationLogging.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
            }

            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "CryptoBlade API");
                c.RoutePrefix = "swagger";
            });
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapHealthChecks("/healthz");
            app.MapControllers();
            app.MapGet("/", context =>
            {
                context.Response.Redirect("/swagger");
                return Task.CompletedTask;
            }).AllowAnonymous();
            app.Run();
        }

        private static void AddOptimizerDependencies(WebApplicationBuilder builder,
            IHealthChecksBuilder healthChecksBuilder)
        {
            healthChecksBuilder.AddCheck<BacktestExecutionHealthCheck>("Backtest");
            builder.Services.AddHostedService<OptimizerHostedService>();
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
                var bybitLogger = ApplicationLogging.CreateLogger<BybitHistoricalDataDownloader>();
                var bybitClient = CreateUnauthorizedBybitClient(Options.Create(tradingBotOptions));

                return new BybitHistoricalDataDownloader(
                    historicalDataStorage,
                    bybitLogger,
                    bybitClient);
            });
            builder.Services.AddSingleton<IHistoricalDataStorage, ProtoHistoricalDataStorage>();
            builder.Services.AddOptions<ProtoHistoricalDataStorageOptions>().Configure(x =>
            {
                x.Directory = historicalDataDirectory;
            });
            builder.Services.AddSingleton<IFuturesRestClient>(sp => sp.GetRequiredService<BackTestExchange>());
            builder.Services.AddSingleton<IFuturesSocketClient>(sp => sp.GetRequiredService<BackTestExchange>());
            builder.Services.AddSingleton<IBackTestRunner>(sp => sp.GetRequiredService<BackTestExchange>());
            builder.Services.AddHostedService<BackTestPerformanceTracker>();
        }

        private static BybitFuturesRestClient CreateUnauthorizedBybitClient(IOptions<TradingBotOptions> tradingBotOptions)
        {
            var bybit = new BybitRestClient();
            var cbRestClientOptions = Options.Create(new BybitFuturesRestClientOptions
            {
                PlaceOrderAttempts = 5
            });
            var cbRestClient = new BybitFuturesRestClient(cbRestClientOptions,
                tradingBotOptions,
                bybit,
                ApplicationLogging.CreateLogger<BybitFuturesRestClient>());

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
                    if (mainAccount.IsDemo)
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

                builder.Services.AddSingleton<IFuturesSocketClient>(provider =>
                {
                    IBybitSocketClient bybitSocketClientMain = (IBybitSocketClient)provider.GetRequiredService<IBybitSocketClientMain>();
                    IBybitSocketClient bybitSocketClientSecondary = (IBybitSocketClient)provider.GetRequiredService<IBybitSocketClientSecondary>();
                    return new BybitFuturesSocketClient(bybitSocketClientMain, bybitSocketClientSecondary, Options.Create(tradingBotOptions));
                });
            }
            else
            {
                builder.Services.AddSingleton<IFuturesSocketClient>(provider =>
                {
                    IBybitSocketClient bybitSocketClientMain = (IBybitSocketClient)provider.GetRequiredService<IBybitSocketClientMain>();
                    return new BybitFuturesSocketClient(bybitSocketClientMain, null, Options.Create(tradingBotOptions));
                });
            }

            builder.Services.AddSingleton<IFuturesRestClient, BybitFuturesRestClient>();
            builder.Services.AddSingleton<IBybitCBRestClient, BybitCBRestClient>();
            builder.Services.AddOptions<BybitFuturesRestClientOptions>().Configure(options =>
            {
                options.PlaceOrderAttempts = tradingBotOptions.PlaceOrderAttempts;
            });
        }
    }
}
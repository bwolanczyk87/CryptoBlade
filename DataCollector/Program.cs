using CryptoBlade.Authentication;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using System.Text.Json.Serialization;

namespace DataCollector
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
                .AddEnvironmentVariables("DC_");

            var tradingBotOptions = builder.Configuration.GetSection("Collector").Get<CollectorOptions>();

            builder.Services.AddRazorPages();
            var healthChecksBuilder = builder.Services.AddHealthChecks();

            builder.Services.AddHostedService<CollectorHostedService>();
            builder.Services.Configure<CollectorOptions>(builder.Configuration.GetSection("Collector"));
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
            });

            builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.Scheme).AddScheme<ApiKeySchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.Scheme, _ => { }
            );
            builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ApiKey"));

            builder.Services.AddSingleton<ICollectorManager, TradingSymbolsManager>();
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
    }
}
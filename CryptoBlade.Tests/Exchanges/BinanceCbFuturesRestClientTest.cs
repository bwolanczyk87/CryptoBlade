using Binance.Net.Clients;
using CryptoBlade.Exchanges;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CryptoBlade.Tests.Exchanges
{
    public class BinanceCbFuturesRestClientTest : TestBase
    {
        private readonly ILoggerFactory m_loggerFactory;

        public BinanceCbFuturesRestClientTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
            m_loggerFactory = LoggerFactory
                .Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    builder.AddXunit(testOutputHelper);
                    builder.AddSimpleConsole(o =>
                    {
                        o.UseUtcTimestamp = true;
                        o.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                    });
                });
        }
    }
}

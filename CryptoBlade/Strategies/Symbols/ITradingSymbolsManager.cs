using CryptoBlade.Exchanges;
using CryptoBlade.Models;

namespace CryptoBlade.Strategies.Symbols
{
    public interface ITradingSymbolsManager
    {
        public Task<List<SymbolInfo>> GetTradingSymbolsAsync(ICbFuturesRestClient restClient, List<string> whitelist, List<string> blacklist, SymbolPreferences symbolPreferences, string historicalDataDirectory, CancellationToken cancel);
    }
}

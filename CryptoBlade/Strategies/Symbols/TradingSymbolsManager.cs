using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoExchange.Net.Interfaces;
using System.Text.Json;

namespace CryptoBlade.Strategies.Symbols
{
    public class TradingSymbolsManager : ITradingSymbolsManager
    {
        private readonly ILogger<TradingSymbolsManager> m_logger;

        public TradingSymbolsManager(ILogger<TradingSymbolsManager> logger)
        {
            m_logger = logger;
        }

        public async Task<List<SymbolInfo>> GetTradingSymbolsAsync(
            ICbFuturesRestClient restClient,
            List<string> whitelist, 
            List<string> blacklist, 
            SymbolPreferences symbolPreferences,
            string historicalDataDirectory,
            CancellationToken cancel)
        {
            var symbolsInfo = await GetSymbolsRealOrHistoricalInfoAsync(restClient, historicalDataDirectory, cancel);
            List<string> preferedSymbols = new();

            if (whitelist == null || !whitelist.Any())
            {
                preferedSymbols = GetPreferredSymbols(symbolsInfo, symbolPreferences);
            }
            else
            {
                preferedSymbols = whitelist;

                Dictionary<string, SymbolInfo> symbolInfoDict = symbolsInfo
                   .DistinctBy(x => x.Name)
                   .ToDictionary(x => x.Name, x => x);

                List<string> missingSymbols = preferedSymbols
                .Where(x => !symbolInfoDict.ContainsKey(x))
                .ToList();

                foreach (var symbol in missingSymbols)
                    m_logger.LogWarning($"Symbol {symbol} is missing from the exchange.");

                foreach (string missingSymbol in missingSymbols)
                    preferedSymbols.Remove(missingSymbol);
            }

            preferedSymbols = preferedSymbols
                .Except(blacklist.Where(x => !string.IsNullOrWhiteSpace(x)))
                .Distinct()
                .ToList();

            return symbolsInfo
                .Where(s => preferedSymbols.Contains(s.Name)).ToList();
        }

        private async Task<SymbolInfo[]> GetSymbolsRealOrHistoricalInfoAsync(ICbFuturesRestClient restClient, string historicalDataDirectory, CancellationToken cancel = default)
        {
            string jsonFile = Path.Combine(historicalDataDirectory, "symbolinfo.json");

            if (File.Exists(jsonFile))
            {
                var fileInfo = new FileInfo(jsonFile);
                if ((DateTime.UtcNow - fileInfo.CreationTimeUtc).TotalDays <= 5)
                {
                    try
                    {
                        var existingJson = await File.ReadAllTextAsync(jsonFile, cancel);
                        return JsonSerializer.Deserialize<SymbolInfo[]>(existingJson);
                    }
                    catch (Exception)
                    {
                        File.Delete(jsonFile);
                    }
                }
                else
                {
                    File.Delete(jsonFile);
                }
            }

            SymbolInfo[] symbolInfo = await restClient.GetSymbolInfoAsync(cancel);
            var json = JsonSerializer.Serialize(symbolInfo);
            if (File.Exists(jsonFile))
                File.Delete(jsonFile);

            await File.WriteAllTextAsync(jsonFile, json, cancel);   
            return symbolInfo;
        }

        private static List<string> GetPreferredSymbols(SymbolInfo[] symbols, SymbolPreferences symbolPreferences)
        {
            var classifiedSymbols = ClassifySymbols(symbols);
            return FilterSymbolsForStrategies(classifiedSymbols, symbolPreferences);
        }

        private static Dictionary<string, SymbolClassification> ClassifySymbols(SymbolInfo[] symbols)
        {
            if (!symbols.Any())
                throw new ArgumentException("No symbols to classify");

            var launchDesc = symbols
                .OrderByDescending(s => s.LaunchTime)
                .ToList();
            var launchMap = ClassifyIntoFourSegments(launchDesc);

            var volumeAsc = symbols
                .Where(s => s.Volume.HasValue && s.Volume.Value > 0)
                .OrderBy(s => s.Volume.Value)
                .ToList();
            var volumeMap = ClassifyIntoFourSegments(volumeAsc);

            var volAsc = symbols
                .Where(s => s.Volatility.HasValue && s.Volatility.Value > 0)
                .OrderBy(s => s.Volatility.Value)
                .ToList();
            var volatilityMap = ClassifyIntoFourSegments(volAsc);


            var result = new Dictionary<string, SymbolClassification>();
            foreach (var si in symbols)
            {
                var sc = new SymbolClassification();

                if (launchMap.TryGetValue(si.Name, out var lcat))
                    sc.MaturityLevel = lcat;
                else
                    sc.MaturityLevel = SymbolClassificationLevel.MEDIUM;

                if (volumeMap.TryGetValue(si.Name, out var vcat))
                    sc.VolumeLevel = vcat;
                else
                    sc.VolumeLevel = SymbolClassificationLevel.MEDIUM;

                if (volatilityMap.TryGetValue(si.Name, out var volcat))
                    sc.VolatilityLevel = volcat;
                else
                    sc.VolatilityLevel = SymbolClassificationLevel.MEDIUM;

                result[si.Name] = sc;
            }

            return result;
        }

        private static Dictionary<string, SymbolClassificationLevel> ClassifyIntoFourSegments(List<SymbolInfo> sortedList)
        {
            var dict = new Dictionary<string, SymbolClassificationLevel>();

            int n = sortedList.Count;
            if (n == 0)
                return dict;

            int groupSize = n / 4;

            var lowGroup = sortedList.Take(groupSize).ToList();
            var mediumGroup = sortedList.Skip(groupSize).Take(groupSize).ToList();
            var highGroup = sortedList.Skip(2 * groupSize).Take(groupSize).ToList();
            var largeGroup = sortedList.Skip(3 * groupSize).ToList();

            foreach (var item in lowGroup)
                dict[item.Name] = SymbolClassificationLevel.LOW;

            foreach (var item in mediumGroup)
                dict[item.Name] = SymbolClassificationLevel.MEDIUM;

            foreach (var item in highGroup)
                dict[item.Name] = SymbolClassificationLevel.HIGH;

            foreach (var item in largeGroup)
                dict[item.Name] = SymbolClassificationLevel.LARGE;

            return dict;
        }

        private static List<string> FilterSymbolsForStrategies(Dictionary<string, SymbolClassification> classifiedSymbols, SymbolPreferences symbolPreferences)
        {
            var filteredSymbols = classifiedSymbols
                .Where(s =>
                    (symbolPreferences.Maturity?.Contains(s.Value.MaturityLevel) ?? false) &&
                    (symbolPreferences.Volume?.Contains(s.Value.VolumeLevel) ?? false) &&
                    (symbolPreferences.Volatility?.Contains(s.Value.VolatilityLevel) ?? false)
                )
                .Select(kv => kv.Key)
                .ToList();

            return filteredSymbols;
        }
    }
}

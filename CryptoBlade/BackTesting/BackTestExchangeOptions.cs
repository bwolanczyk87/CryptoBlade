using CryptoBlade.Configuration;
using CryptoBlade.Strategies.Symbols;

namespace CryptoBlade.BackTesting
{
    public class BackTestExchangeOptions
    {
        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public TimeSpan StartupCandleData { get; set; } = TimeSpan.FromDays(1);

        public string[] Whitelist { get; set; } = Array.Empty<string>();

        public string[] Blacklist { get; set; } = Array.Empty<string>();

        public decimal InitialBalance { get; set; } = 5000;

        public decimal MakerFeeRate { get; set; } = 0.0002m;

        public decimal TakerFeeRate { get; set; } = 0.00055m;

        public string HistoricalDataDirectory { get; set; } = ConfigPaths.DefaultHistoricalDataDirectory;

        public SymbolClassificationLevel[] SymbolMaturityPreference { get; set; } = Array.Empty<SymbolClassificationLevel>();

        public SymbolClassificationLevel[] SymbolVolumePreference { get; set; } = Array.Empty<SymbolClassificationLevel>();

        public SymbolClassificationLevel[] SymbolVolatilityPreference { get; set; } = Array.Empty<SymbolClassificationLevel>();
    }
}
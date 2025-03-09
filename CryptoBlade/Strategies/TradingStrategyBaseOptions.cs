using CryptoBlade.Configuration;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Symbols;

namespace CryptoBlade.Strategies
{
    public class TradingStrategyBaseOptions : TradingStrategyCommonBaseOptions
    {
        public int DcaOrdersCount { get; set; }

        public bool ForceMinQty { get; set; }

        public decimal MinProfitRate { get; set; } = 0.0006m;

        public decimal QtyFactorLong { get; set; } = 1.0m;
        
        public decimal QtyFactorShort { get; set; } = 1.0m;
        
        public bool EnableRecursiveQtyFactorLong { get; set; }
        
        public bool EnableRecursiveQtyFactorShort { get; set; }

        public SymbolClassificationLevel[]? SymbolMaturityPreference { get; set; }

        public SymbolClassificationLevel[]? SymbolVolumePreference { get; set; }

        public SymbolClassificationLevel[]? SymbolVolatilityPreference { get; set; }
    }
}
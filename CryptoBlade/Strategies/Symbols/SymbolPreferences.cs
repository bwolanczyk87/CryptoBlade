namespace CryptoBlade.Strategies.Symbols
{
    public class SymbolPreferences
    {
        public SymbolClassificationLevel[] Maturity { get; set; } = Array.Empty<SymbolClassificationLevel>();
        public SymbolClassificationLevel[] Volume { get; set; } = Array.Empty<SymbolClassificationLevel>();
        public SymbolClassificationLevel[] Volatility { get; set; } = Array.Empty<SymbolClassificationLevel>();
    }
}

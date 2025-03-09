namespace CryptoBlade.Strategies.Symbols
{
    public class SymbolClassification
    {
        public DateTime LunchTime { get; set; }

        public decimal? Volume24H { get; set; }

        public decimal? Volatility { get; set; }

        public SymbolClassificationLevel Maturity { get; set; }

        public SymbolClassificationLevel VolumeCategory { get; set; }

        public SymbolClassificationLevel VolatilityCategory { get; set; }
    }
}

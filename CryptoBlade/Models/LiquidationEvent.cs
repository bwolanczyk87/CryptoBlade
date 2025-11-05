namespace CryptoBlade.Models
{
    public sealed class LiquidationEvent
    {
        public DateTime Timestamp { get; init; }
        public decimal Price { get; init; }
        public decimal Quantity { get; init; }   // nominal/qty
        public string Side { get; init; } = "Sell";
    }
}

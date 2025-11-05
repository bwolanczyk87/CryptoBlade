namespace CryptoBlade.Models
{
    public sealed class PublicTrade
    {
        public DateTime Timestamp { get; init; }
        public decimal Price { get; init; }
        public decimal Quantity { get; init; }     // w kontraktach/coinach
        public string Side { get; init; } = "Buy"; // "Buy" = taker buy, "Sell" = taker sell
    }
}

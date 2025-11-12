namespace CryptoBlade.Models
{
    public sealed class PublicTrade
    {
        public DateTime Timestamp { get; init; }
        public decimal Price { get; init; }
        public decimal Quantity { get; init; }
        public OrderSide Side { get; init; }
    }
}

namespace CryptoBlade.Models
{
    public sealed class OrderBookLevel
    {
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
    }

    public sealed class OrderBook
    {
        public List<OrderBookLevel> Bids { get; set; } = [];
        public List<OrderBookLevel> Asks { get; set; } = [];

        public decimal BestBid => Bids.Count > 0 ? Bids[0].Price : 0m;
        public decimal BestAsk => Asks.Count > 0 ? Asks[0].Price : 0m;
    }
}

using Bybit.Net.Enums;

namespace CryptoBlade.Models
{
    public enum OrderKind
    {
        Unknown = 0,
        Entry = 1,
        StopLoss = 2,
        TakeProfit = 3
    }

    public sealed class OrderUpdate
    {
        public string Symbol { get; init; } = string.Empty;
        public string OrderId { get; init; } = string.Empty;
        public string? ClientOrderId { get; init; }

        public OrderStatus Status { get; init; }
        public OrderSide Side { get; init; }
        public OrderType Type { get; init; }

        public decimal? Price { get; init; }
        public decimal? AverageFillPrice { get; init; }
        public decimal? Quantity { get; init; }
        public decimal? FilledQuantity { get; init; }

        public PositionIdx? PositionIdx { get; init; }
        public bool? ReduceOnly { get; init; }

        public DateTime? UpdateTime { get; init; }

        public OrderKind SigmaKind { get; init; } = OrderKind.Unknown;
    }
}
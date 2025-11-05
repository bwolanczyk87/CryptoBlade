namespace CryptoBlade.Models
{
    public sealed class MarkIndexPair
    {
        public DateTime Timestamp { get; init; }
        public decimal MarkPrice { get; init; }
        public decimal IndexPrice { get; init; }
    }
}

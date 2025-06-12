namespace CryptoBlade.Strategies.AI
{
    public class SignalResponseAI
    {
        public string Signal { get; set; } = "NONE";
        public int Confidence { get; set; }
        public decimal? EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int DataDelay { get; set; }
        public List<string>? RequestedIndicators { get; set; }
        public List<string>? RequestedCandles { get; set; }
    }
}

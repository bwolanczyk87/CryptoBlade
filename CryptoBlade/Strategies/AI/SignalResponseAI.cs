namespace CryptoBlade.Strategies.AI
{
    public class SignalResponseAI
    {
        public string Signal { get; set; } = "NONE";
        public int Confidence { get; set; }
        public decimal? EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public string Reason { get; set; } = string.Empty;
        public int Delay { get; set; }
        public List<string>? NeedIndicators { get; set; }
        public List<string>? NeedCandles { get; set; }
        public List<string>? NeedPivots { get; set; }
    }
}

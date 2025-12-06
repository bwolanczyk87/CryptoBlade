using DataCollector.Bybit;

namespace DataCollector
{
    public class CollectorOptions
    {
        public string AccountName { get; set; } = string.Empty;
        public ExchangeAccount[] Accounts { get; set; } = [];
        public DateTime DataSince { get; set; } = DateTime.MinValue;
        public string[] Symbols { get; set; } = [];
    }
}
using CryptoBlade.Models;
using Skender.Stock.Indicators;
using System.Globalization;
using System.Text;

namespace CryptoBlade.Strategies.AI
{
    public class CandlesAI
    {
        public TimeFrame TimeFrame { get; }
        public int Count { get; }

        public CandlesAI(TimeFrame timeframe, int count)
        {
            TimeFrame = timeframe;
            Count = count;
        }

        public static CandlesAI Parse(string input)
        {
            var parts = input.Split('|');
            var timeframe = TimeFrameHelper.Parse(parts[0]);
            var count = int.Parse(parts[1]);
            return new CandlesAI(timeframe, count);
        }

        public string FormatForBot(Dictionary<TimeFrame, QuoteQueue> quotes, int priceScale)
        {
            var sb = new StringBuilder();
            sb.Append($"{TimeFrameHelper.GetAbbreviation(TimeFrame)}|{Count}=");

            if (quotes.TryGetValue(TimeFrame, out var tfQuotes))
            {
                var quoteList = tfQuotes.GetQuotes().TakeLast(Count).ToList();
                for (int i = 0; i < quoteList.Count; i++)
                {
                    var quote = quoteList[i];
                    sb.Append($"{quote.Date:yyyyMMddHHmm}|");
                    sb.Append($"{quote.Open.ToString($"F{priceScale}", CultureInfo.InvariantCulture)},");
                    sb.Append($"{quote.High.ToString($"F{priceScale}", CultureInfo.InvariantCulture)},");
                    sb.Append($"{quote.Low.ToString($"F{priceScale}", CultureInfo.InvariantCulture)},");
                    sb.Append($"{quote.Close.ToString($"F{priceScale}", CultureInfo.InvariantCulture)},");
                    sb.Append($"{quote.Volume.ToString($"F{priceScale}", CultureInfo.InvariantCulture)}");

                    if (i < quoteList.Count - 1)
                    {
                        sb.Append(';');
                    }
                }
            }

            return sb.ToString();
        }
    }
}

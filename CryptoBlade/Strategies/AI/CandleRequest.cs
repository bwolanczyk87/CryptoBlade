using CryptoBlade.Models;
using Skender.Stock.Indicators;
using System.Globalization;
using System.Text;

namespace CryptoBlade.Strategies.AI
{
    public class CandleRequest
    {
        public TimeFrame Tf { get; }
        public int Count { get; }

        public CandleRequest(TimeFrame tf, int count) => (Tf, Count) = (tf, count);

        public static CandleRequest Parse(string s)
        {
            var p = s.Split('|');
            return new CandleRequest(
                TimeFrameHelper.Parse(p[0]),
                int.Parse(p[1]));
        }

        public string ToBotPayload(Dictionary<TimeFrame, QuoteQueue> quotes, int priceScale)
        {
            var sb = new StringBuilder();
            sb.Append($"{TimeFrameHelper.GetAbbreviation(Tf)}|{Count}=");

            if (quotes.TryGetValue(Tf, out var tfQuotes))
            {
                var quoteList = tfQuotes.GetQuotes().TakeLast(Count).ToList();
                for (int i = 0; i < quoteList.Count; i++)
                {
                    var quote = quoteList[i];
                    sb.Append($"{quote.Date:MMddHHmm}|");
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

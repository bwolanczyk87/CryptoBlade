using CryptoBlade.Models;
using Skender.Stock.Indicators;
using System.Globalization;
using System.Text;

namespace CryptoBlade.Strategies.AI
{
    /// <summary>
    /// Builds a payload in *plain OHLCV* format (absolute prices) so the AI no longer has to
    /// perform tick‑delta reconstruction. The previous delta logic has been removed.
    ///
    /// Header pattern:  {TFAbbr}|{Count}|{MMDD}=  
    /// Rows:            HHmm,Open,High,Low,Close,Volume  (semicolon‑separated)
    /// Prices are rounded to <paramref name="priceScale"/> decimal places using the invariant culture.
    /// Example:
    ///     1m|30|0624=1305,0.60450,0.60460,0.60440,0.60450,501;1306,0.60450,0.60460,0.60440,0.60450,13 667;...
    /// </summary>
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

        /// <summary>
        /// Converts the queued <see cref="Quote"/> objects for the requested timeframe to the
        /// simplified OHLCV string expected by the bot.
        /// </summary>
        /// <remarks>
        /// ‑ If <paramref name="quotes"/> has no data for the timeframe an empty string is returned.
        /// ‑ Only the <paramref name="Count"/> most‑recent candles are included.
        /// ‑ A new header block is started whenever the calendar day changes so the consumer can
        ///   reset its state naturally.
        /// </remarks>
        public string ToBotPayload(Dictionary<TimeFrame, QuoteQueue> quotes, int priceScale)
        {
            if (!quotes.TryGetValue(Tf, out var tfQuotes))
                return string.Empty;

            var list = tfQuotes.GetQuotes().TakeLast(Count).ToList();
            if (list.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            string tfAbbr = TimeFrameHelper.GetAbbreviation(Tf);
            string curDay = null!;
            string priceFmt = $"F{priceScale}";
            var inv = CultureInfo.InvariantCulture;

            foreach (var q in list)
            {
                string day = q.Date.ToString("MMdd");

                // ――― start a new block when the calendar day changes ―――
                if (day != curDay)
                {
                    if (sb.Length > 0)
                        sb.Append(';');

                    sb.Append($"{tfAbbr}|{Count}|{day}=");
                    curDay = day;
                }
                else
                {
                    sb.Append(';');
                }

                // ――― write the OHLCV row (absolute prices) ―――
                sb.Append($"{q.Date:HHmm},");
                sb.Append($"{Math.Round(q.Open, priceScale).ToString(priceFmt, inv)},");
                sb.Append($"{Math.Round(q.High, priceScale).ToString(priceFmt, inv)},");
                sb.Append($"{Math.Round(q.Low, priceScale).ToString(priceFmt, inv)},");
                sb.Append($"{Math.Round(q.Close, priceScale).ToString(priceFmt, inv)},");
                sb.Append($"{q.Volume.ToString("F0", inv)}");
            }

            return sb.ToString();
        }
    }
}

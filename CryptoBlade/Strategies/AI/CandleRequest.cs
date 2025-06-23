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

        public string ToBotPayload(Dictionary<TimeFrame, QuoteQueue> quotes,int priceScale)
        {
            if (!quotes.TryGetValue(Tf, out var tfQuotes))
                return string.Empty;

            var list = tfQuotes.GetQuotes().TakeLast(Count).ToList();
            if (list.Count == 0) return string.Empty;

            // 10^priceScale  → konwersja float → tick-int
            int pow = (int)Math.Pow(10, priceScale);

            var sb = new StringBuilder();

            string tfAbbr = TimeFrameHelper.GetAbbreviation(Tf);
            string curDay = null!;
            int prevCloseTicks = 0;   // zainicjujemy przy 1-szej świecy

            for (int i = 0; i < list.Count; i++)
            {
                var q = list[i];
                var day = q.Date.ToString("MMdd");

                // --- nowy nagłówek gdy zmiana dnia -----------------------
                if (day != curDay)
                {
                    // absolutny pierwszy close danego dnia
                    prevCloseTicks = (int)Math.Round(q.Close * pow);
                    if (sb.Length > 0) sb.Append(';');      // odetnij poprzedni blok

                    sb.Append($"{tfAbbr}|{Count}|{day}|{prevCloseTicks}=");
                    curDay = day;
                    // aktualna świeca będzie zakodowana niżej (delta = 0,0,0,0)
                }
                else
                {
                    sb.Append(';');   // separator kolejnej świecy tego samego dnia
                }

                // --- tick-int wartości -----------------------------------
                int o = (int)Math.Round(q.Open * pow);
                int h = (int)Math.Round(q.High * pow);
                int l = (int)Math.Round(q.Low * pow);
                int c = (int)Math.Round(q.Close * pow);

                // różnice względem poprzedniego CLOSE
                sb.Append($"{q.Date:HHmm},");
                sb.Append($"{o - prevCloseTicks},");
                sb.Append($"{h - prevCloseTicks},");
                sb.Append($"{l - prevCloseTicks},");
                sb.Append($"{c - prevCloseTicks},");
                sb.Append($"{q.Volume.ToString("F0", CultureInfo.InvariantCulture)}");

                prevCloseTicks = c; // update na następną świecę
            }

            return sb.ToString();
        }

    }
}

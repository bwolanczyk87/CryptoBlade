using CryptoBlade.Models;
using System.Globalization;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.AI
{
    public sealed record PivotRequest(TimeFrame Tf, decimal PercentChange, EndType EndType = EndType.Close)
    {
        //  "1H|1.3"  lub  "1H|H|1.3"  (H = HighLow, C = Close)
        public static PivotRequest Parse(string s)
        {
            var p = s.Split('|');                     // 1 lub 3 pola
            var tf = TimeFrameHelper.Parse(p[0]);
            EndType et;
            decimal pc;

            if (p.Length == 2)                       // "TF|percent"
            {
                et = EndType.Close;
                pc = decimal.Parse(p[1], CultureInfo.InvariantCulture);
            }
            else                                     // "TF|H|percent"
            {
                et = p[1].Equals("H", StringComparison.OrdinalIgnoreCase)
                        ? EndType.HighLow : EndType.Close;
                pc = decimal.Parse(p[2], CultureInfo.InvariantCulture);
            }
            return new PivotRequest(tf, pc, et);
        }

        public override string ToString()
            => $"{TimeFrameHelper.GetAbbreviation(Tf)}|{(EndType == EndType.HighLow ? "H" : "C")}|{PercentChange}";
    }

    public static class PivotEngine
    {
        public static IEnumerable<ZigZagResult> Compute(PivotRequest req, IEnumerable<Quote> q)
            => q.GetZigZag(req.EndType, req.PercentChange);
    }
}

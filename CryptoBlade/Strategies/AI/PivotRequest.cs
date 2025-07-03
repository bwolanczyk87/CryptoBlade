using CryptoBlade.Models;
using Skender.Stock.Indicators;
using System;
using System.Globalization;

namespace CryptoBlade.Strategies.AI
{
    public sealed record PivotRequest(decimal PercentChange, EndType EndType = EndType.Close)
    {
        public static PivotRequest Parse(string s)
        {
            var p = s.Split('|');

            EndType et = p[0].Equals("H", StringComparison.OrdinalIgnoreCase) ? EndType.HighLow : EndType.Close;
            decimal pc = decimal.Parse(p[1], CultureInfo.InvariantCulture);
            return new PivotRequest(pc, et);
        }

        public override string ToString()
            => $"{(EndType == EndType.HighLow ? "H" : "C")}|{PercentChange}";
    }

    public static class PivotEngine
    {
        public static IEnumerable<ZigZagResult> Compute(PivotRequest req, IEnumerable<Quote> q)
            => q.GetZigZag(req.EndType, req.PercentChange);
    }
}

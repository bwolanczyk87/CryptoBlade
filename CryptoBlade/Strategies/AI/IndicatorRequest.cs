using CryptoBlade.Models;
using System.Text.RegularExpressions;

namespace CryptoBlade.Strategies.AI
{
    public sealed class IndicatorRequest
    {
        public string Name { get; }
        public TimeFrame Tf { get; }
        public int[] Params { get; }

        private IndicatorRequest(string n, TimeFrame tf, int[] pr) =>
            (Name, Tf, Params) = (n, tf, pr);

        /// <summary>
        ///  Parses **TF|Name(params)** - e.g.  "15M|Macd(12,26,9)"  or  "1M|Obv()"
        /// </summary>
        public static IndicatorRequest Parse(string s)
        {
            var parts = s.Split('|');
            if (parts.Length != 2)
                throw new FormatException($"Bad indicator format: {s}");

            // part[0]  → TF   |   part[1] → Name(params)
            var tf = TimeFrameHelper.Parse(parts[0]);

            var m = Regex.Match(parts[1], @"(\w+)\((.*?)\)");
            if (!m.Success)
                throw new FormatException($"Bad indicator format: {s}");

            var name = m.Groups[1].Value;
            var prms = string.IsNullOrWhiteSpace(m.Groups[2].Value)
                       ? Array.Empty<int>()
                       : m.Groups[2].Value.Split(',')
                                           .Select(int.Parse)
                                           .ToArray();

            return new IndicatorRequest(name, tf, prms);
        }

        /// <summary>
        ///  Serialises back to **TF|Name(params)**
        /// </summary>
        public override string ToString() =>
            $"{TimeFrameHelper.GetAbbreviation(Tf)}|{Name}({string.Join(',', Params)})";
    }
}

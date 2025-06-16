using CryptoBlade.Models;
using System.Text.RegularExpressions;

namespace CryptoBlade.Strategies.AI
{
    public sealed class IndicatorRequest
    {
        public string Name { get; }
        public TimeFrame Tf { get; }
        public int[] Params { get; }

        private IndicatorRequest(string n, TimeFrame tf, int[] par) =>
            (Name, Tf, Params) = (n, tf, par);

        public static IndicatorRequest Parse(string s)
        {
            var parts = s.Split('|');
            var match = Regex.Match(parts[0], @"(\w+)\((.*?)\)");
            var name = match.Groups[1].Value;
            var prms = string.IsNullOrWhiteSpace(match.Groups[2].Value)
                       ? []
                       : match.Groups[2].Value.Split(',').Select(int.Parse).ToArray();
            return new IndicatorRequest(name, TimeFrameHelper.Parse(parts[1]), prms);
        }

        public override string ToString() =>
            $"{Name}({string.Join(',', Params)})|{TimeFrameHelper.GetAbbreviation(Tf)}";
    }
}
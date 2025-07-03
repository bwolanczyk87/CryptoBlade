using CryptoBlade.Models;
using System.Text.RegularExpressions;

namespace CryptoBlade.Strategies.AI
{
    public sealed class IndicatorRequest
    {
        public string Name { get; }
        public int[] Params { get; }

        private IndicatorRequest(string n, int[] pr) =>
            (Name, Params) = (n, pr);

        public static IndicatorRequest Parse(string indicator)
        {
            var m = Regex.Match(indicator, @"(\w+)\((.*?)\)");
            if (!m.Success)
                throw new FormatException($"Bad indicator format: {indicator}");

            var name = m.Groups[1].Value;
            var prms = string.IsNullOrWhiteSpace(m.Groups[2].Value)
                       ? []
                       : m.Groups[2].Value.Split(',')
                                           .Select(int.Parse)
                                           .ToArray();

            return new IndicatorRequest(name, prms);
        }
    }
}

using CryptoBlade.Models;

namespace CryptoBlade.Strategies.AI
{
    public class IndicatorAI
    {
        public string Name { get; }
        public string Abbreviation { get; }
        public int[] Parameters { get; }
        public object Value { get; set; }
        public TimeFrame TimeFrame { get; set; }

        public IndicatorAI(string name, string abbreviation, int[] parameters)
        {
            Name = name;
            Abbreviation = abbreviation;
            Parameters = parameters;
        }

        public static Dictionary<string, string> Abbreviations => new()
        {
            ["EMA"] = "E",
            ["MACD"] = "M",
            ["RSI"] = "R",
            ["ATR"] = "A",
            ["ADX"] = "D",
            ["BollingerBands"] = "BB",
            ["Stochastic"] = "STO",
            ["Ichimoku"] = "ICH",
            ["VWAP"] = "VW",
            ["Volume"] = "VOL"
        };

        public static IndicatorAI Parse(string input)
        {
            var parts = input.Split('|');
            var name = parts[0];
            var timeframe = ParseTimeFrame(parts[1]);
            var parameters = parts[2].Split(',').Select(int.Parse).ToArray();

            return new IndicatorAI(
                name,
                Abbreviations.TryGetValue(name, out var abbr) ? abbr : name,
                parameters
            )
            {
                TimeFrame = timeframe
            };
        }

        public string FormatForBot() => $"{Name}|{TimeFrameHelper.GetAbbreviation(TimeFrame)}|{string.Join(",", Parameters)}";

        public static string GetName(string abbreviation) =>
            Abbreviations.FirstOrDefault(x => x.Value == abbreviation).Key ?? abbreviation;

        public static string GetAbbreviation(string name) =>
            Abbreviations.TryGetValue(name, out var abbr) ? abbr : name;

        private static TimeFrame ParseTimeFrame(string tf) => tf.ToUpper() switch
        {
            "1D" => TimeFrame.OneDay,
            "4H" => TimeFrame.FourHours,
            "1H" => TimeFrame.OneHour,
            "15M" => TimeFrame.FifteenMinutes,
            "5M" => TimeFrame.FiveMinutes,
            "1M" => TimeFrame.OneMinute,
            _ => throw new ArgumentException($"Unknown timeframe: {tf}")
        };
    }
}

using CryptoBlade.Models;

namespace CryptoBlade.Strategies.AI
{
    public static class TimeFrameHelper
    {
        public static string GetAbbreviation(TimeFrame tf) => tf switch
        {
            TimeFrame.OneDay => "1D",
            TimeFrame.FourHours => "4H",
            TimeFrame.OneHour => "1H",
            TimeFrame.FifteenMinutes => "15m",
            TimeFrame.FiveMinutes => "5m",
            TimeFrame.OneMinute => "1m",
            _ => tf.ToString()
        };

        public static TimeFrame Parse(string tf) => tf.ToUpper() switch
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

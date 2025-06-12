using Skender.Stock.Indicators;
using System.Globalization;

namespace CryptoBlade.Strategies.AI
{
    public class IndicatorManager
    {
        private readonly Dictionary<string, Func<IEnumerable<Quote>, int[], object>> _calculators = new()
        {
            ["EMA"] = (q, p) => q.GetEma(p.Length > 0 ? p[0] : 100).Last(),
            ["MACD"] = (q, p) => q.GetMacd(
                p.Length > 0 ? p[0] : 8,
                p.Length > 1 ? p[1] : 21,
                p.Length > 2 ? p[2] : 5).Last(),
            ["RSI"] = (q, p) => q.GetRsi(p.Length > 0 ? p[0] : 14).Last(),
            ["Volume"] = (q, p) => q.Use(CandlePart.Volume).GetSma(p.Length > 0 ? p[0] : 20).Last(),
            ["ATR"] = (q, p) => q.GetAtr(p.Length > 0 ? p[0] : 14).Last(),
            ["ADX"] = (q, p) => q.GetAdx(p.Length > 0 ? p[0] : 14).Last(),
            ["BollingerBands"] = (q, p) => q.GetBollingerBands(
                p.Length > 0 ? p[0] : 20,
                p.Length > 1 ? p[1] : 2).Last(),
            ["Stochastic"] = (q, p) => q.GetStoch(
                p.Length > 0 ? p[0] : 14,
                p.Length > 1 ? p[1] : 3,
                p.Length > 2 ? p[2] : 3).Last(),
            ["Ichimoku"] = (q, p) => q.GetIchimoku(
                p.Length > 0 ? p[0] : 9,
                p.Length > 1 ? p[1] : 26,
                p.Length > 2 ? p[2] : 52,
                p.Length > 3 ? p[3] : 26).Last(),
            ["VWAP"] = (q, _) => q.GetVwap().Last()
        };

        public object Calculate(string name, IEnumerable<Quote> quotes, int[] parameters)
        {
            if (_calculators.TryGetValue(name, out var calculator))
            {
                return calculator(quotes, parameters);
            }
            throw new Exception($"Indicator '{name}' not supported");
        }

        public static string Format(object value)
        {
            return value switch
            {
                EmaResult ema => ema.Ema?.ToString("F0") ?? "0",
                MacdResult macd => $"{macd.Macd?.ToString("F1", CultureInfo.InvariantCulture)},{macd.FastEma?.ToString("F1", CultureInfo.InvariantCulture)},{macd.SlowEma?.ToString("F1", CultureInfo.InvariantCulture)},{macd.Signal?.ToString("F1", CultureInfo.InvariantCulture)}",
                RsiResult rsi => rsi.Rsi?.ToString("F0") ?? "0",
                SmaResult sma => sma.Sma?.ToString("F0") ?? "0",
                AtrResult atr => atr.Atr?.ToString("F1", CultureInfo.InvariantCulture) ?? "0",
                AdxResult adx => adx.Adx?.ToString("F0") ?? "0",
                BollingerBandsResult bb => $"{bb.UpperBand?.ToString("F0")},{bb.LowerBand?.ToString("F0")}",
                StochResult stoch => $"{stoch.K?.ToString("F0")},{stoch.D?.ToString("F0")}",
                IchimokuResult ichi => $"{ichi.TenkanSen?.ToString("F0")},{ichi.KijunSen?.ToString("F0")}",
                VwapResult vwap => vwap.Vwap?.ToString("F0") ?? "0",
                _ => value.ToString()?[..Math.Min(10, value.ToString()?.Length ?? 0)] ?? "N/A"
            };
        }
    }
}

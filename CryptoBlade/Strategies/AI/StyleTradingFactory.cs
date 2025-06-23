namespace CryptoBlade.Strategies.AI
{
    public enum TradingStyle { Scalping, Intraday, Swing }

    public record StyleProfile(
        string[] DefaultIndicators,
        string[] DefaultCandles,
        string[] DefaultPivots);

    public static class StyleProfileFactory
    {
        public static StyleProfile Create(TradingStyle style) => style switch
        {
            TradingStyle.Scalping => new StyleProfile
            (
                DefaultIndicators:
                [
                    "Ema(20)|1M",
                    "Ema(50)|5M",
                    "Vwap()|1M",
                    "Rsi(7)|5M",
                    "StochRsi(14,14,3)|1M",
                    "Atr(14)|5M",
                    "BollingerBands(20,2)|1M"
                ],
                DefaultCandles:
                [
                    "15M|8",
                    "5M|20",
                    "1M|15"
                ],
                DefaultPivots:
                [
                    "15M|H|0.8",
                    "5M|H|0.4",
                    "1M|L|0.3"
                ]
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
    }
}

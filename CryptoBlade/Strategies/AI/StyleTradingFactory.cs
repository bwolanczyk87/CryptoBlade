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
                DefaultCandles:
                [  
                   "1H|4",
                   "15M|16",
                   "5M|24",    
                   "1M|30"
                ],

                DefaultPivots:
                [
                  "1H|H|0.8",       // higher-timeframe S/R
                  "15M|H|0.6",     // intraday bias
                  "5M|H|0.35",     // trade setup structure
                  "1M|H|0.20"      // scalp wicks / liquidity
                ],

                DefaultIndicators:
                [
                   "1H|Ema(100)","15M|Ema(50)","5M|Ema(20)",
                  "1M|Rsi(7)","1M|StochRsi(14,14,3)","5M|Macd(12,26,9)",
                  "1M|Atr(14)","5M|Atr(14)",
                  "1M|Keltner(20,2)","1M|BollingerBands(20,2)",
                  "1M|Vwap()","5M|Vwap()","1M|Obv()",
                  "5M|Donchian(55)","1M|Donchian(20)"
                ]
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
    }
}

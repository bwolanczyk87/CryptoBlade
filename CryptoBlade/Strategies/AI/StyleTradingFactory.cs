//  File:  StyleTradingFactory.cs   (UPDATED)
// ============================================================================
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using System;
using System.Collections.Generic;

namespace CryptoBlade.Strategies.AI
{
    /// <summary>
    /// Trading styles supported by the AI‑driven momentum engine.
    /// New styles can be plugged‑in in a single location (below) without touching
    /// the rest of the pipeline.
    /// </summary>
    public enum TradingStyle { Scalping, Intraday, Swing }

    /// <summary>
    /// Immutable definition of the data each style needs.
    /// </summary>
    /// <param name="DefaultCandles">TimeFrame|BarsCount strings – define how many candles are sent.</param>
    /// <param name="DefaultIndicators">TimeFrame|Indicator(params) strings – one per indicator.</param>
    /// <param name="DefaultPivots">TimeFrame|H|pct (High/Low ZigZag) strings – multi‑TF confluence map.</param>
    public sealed record StyleProfile(
        string StyleName,
        IReadOnlyList<TimeFrameWindow> DefaultTimeFrameWindows,
        IReadOnlyList<string> DefaultCandles,
        IReadOnlyList<string> DefaultIndicators,
        IReadOnlyList<string> DefaultPivots);

    /// <summary>
    /// Central factory – returns a ready profile for a given <see cref="TradingStyle"/>.
    /// </summary>
    public static class StyleProfileFactory
    {
        public static StyleProfile Create(TradingStyle style) => style switch
        {
            /* ------------------------------------------------------------------ */
            /*  SCALPING  (legacy – unchanged)                                   */
            /* ------------------------------------------------------------------ */
            TradingStyle.Scalping => new StyleProfile(
                StyleName: style.ToString(),
                // candles (short history, very granular)
                DefaultTimeFrameWindows:
                [
                    new TimeFrameWindow(TimeFrame.OneHour, 100, false),
                    new(TimeFrame.FifteenMinutes, 128, false),
                    new(TimeFrame.FiveMinutes,     96, true ), 
                    new(TimeFrame.OneMinute,      120, false)  
                ],
                DefaultCandles:
                [
                    "15M|16",
                    "5M|24",
                    "1M|30"
                ],
                // indicators
                DefaultIndicators: new[]
                {
                    "1H|Ema(100)", "15M|Ema(50)", "5M|Ema(20)",
                    "1M|Rsi(7)", "1M|StochRsi(14,14,3)", "5M|Macd(12,26,9)",
                    "1M|Atr(14)", "5M|Atr(14)",
                    "1M|Keltner(20,2)", "1M|BollingerBands(20,2)",
                    "1M|Vwap()", "5M|Vwap()", "1M|Obv()",
                    "5M|Donchian(55)", "1M|Donchian(20)"
                },
                // pivots
                DefaultPivots: new[]
                {
                    "1H|H|0.8",
                    "15M|H|0.6",
                    "5M|H|0.35",
                    "1M|H|0.20"
                }),

        /* ------------------------------------------------------------------ */
        /*  INTRADAY – fast but respects higher‑TF structure                 */
        /* ------------------------------------------------------------------ */
        TradingStyle.Intraday => new StyleProfile(
                StyleName: style.ToString(),
                // candles (short history, very g
                DefaultTimeFrameWindows: new[]
                {
                    new TimeFrameWindow(TimeFrame.FourHours,      64,  false), // ≈11-day context
                    new(TimeFrame.OneHour,        192, false),                // 8 days
                    new(TimeFrame.FifteenMinutes, 256, true ),                // working TF (primary)
                    new(TimeFrame.FiveMinutes,    150, false)                 // execution granularity
                },

                DefaultCandles: new[]
                {
                    "4H|32",   // trend context (~5 days)
                    "1H|48",   // 2‑day view
                    "15M|64",  // working timeframe
                    "5M|96"    // execution
                },
                DefaultIndicators: new[]
                {
                    // trend & momentum
                    "4H|Ema(200)", "1H|Ema(50)", "15M|Ema(20)",
                    "1H|Rsi(14)", "15M|Macd(12,26,9)",
                    // vol & risk
                    "1H|Atr(14)", "15M|BollingerBands(20,2)",
                    // market profile / value area
                    "15M|Vwap()", "5M|Vwap()",
                    // support / breakout tools
                    "1H|Donchian(20)",
                    // confluence helpers
                    "4H|FibRetrace(23,38,50,62,78)",
                    "1H|Corr(BTCUSDT,1440)"
                },
                DefaultPivots: new[]
                {
                    "4H|H|1.2",
                    "1H|H|0.8",
                    "15M|H|0.6",
                    "5M|H|0.35"
                }),

            /* ------------------------------------------------------------------ */
            /*  SWING – slow, position trade                                      */
            /* ------------------------------------------------------------------ */
            TradingStyle.Swing => new StyleProfile(
                StyleName: style.ToString(),
                // candles (short history, very g
                DefaultTimeFrameWindows: new[]
                {
                    new TimeFrameWindow(TimeFrame.OneDay,   180, false), // 6‑month context
                    new(TimeFrame.FourHours,  240, true ),              // position‑management TF (primary)
                    new(TimeFrame.OneHour,    192, false)               // entry timing
                },
                DefaultCandles: new[]
                {
                    "1D|60",  // quarter
                    "4H|60",  // month
                    "1H|48"   // two days – timing entries
                },
                DefaultIndicators: new[]
                {
                    // trend filters
                    "1D|Ema(200)", "1D|Ema(50)", "4H|Sma(100)",
                    // momentum / strength
                    "1D|Rsi(14)", "4H|Stoch(14,3,3)", "1D|Macd(12,26,9)",
                    // volatility & strength
                    "1D|Adx(14)", "4H|Cci(20)",
                    // confluence helpers
                    "1D|FibRetrace(23,38,50,62)",
                    "1D|Corr(DXY,1440)"
                },
                DefaultPivots: new[]
                {
                    "1D|H|1.5",
                    "4H|H|1.0",
                    "1H|H|0.8"
                }),

            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
    }
}

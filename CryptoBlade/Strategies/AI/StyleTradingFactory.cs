using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using System;
using System.Collections.Generic;

namespace CryptoBlade.Strategies.AI
{
    /// <summary>
    /// Trading styles supported by the AI‑driven momentum engine.
    /// </summary>
    public enum TradingStyle { Scalping, Intraday, Swing }

    /// <summary>
    /// Encapsulates the full dataset required for a single timeframe:
    ///   • <see cref="TimeFrameWindow"/> – raw candles delivered to the model
    ///   • Candles – subsampled «n‑bars» snapshots sent as prompts
    ///   • Indicators – technical indicator descriptors (without TF prefix)
    ///   • Pivots – market‑structure levels (without TF prefix)
    /// </summary>
    public sealed record TimeFrameProfile(
        TimeFrameWindow Window,
        int CandlesCount,
        IReadOnlyList<string> Indicators,
        IReadOnlyList<string> Pivots);

    /// <summary>
    /// Definition of a trading style – simply a collection of <see cref="TimeFrameProfile"/>s.
    /// </summary>
    public sealed record TradingStyleProfile(
        string StyleName,
        IReadOnlyList<TimeFrameProfile> Frames);

    /// <summary>
    /// Central factory – returns a ready profile for a given <see cref="TradingStyle"/>.
    /// </summary>
    public static class StyleProfileFactory
    {
        public static TradingStyleProfile Create(TradingStyle style) => style switch
        {
            /* ================================================================== */
            /*  SCALPING                                                         */
            /* ================================================================== */
            TradingStyle.Scalping => new TradingStyleProfile(
                StyleName: style.ToString(),
                Frames: new List<TimeFrameProfile>
                {
                    // 1‑hour context
                    new(
                        new TimeFrameWindow(TimeFrame.OneHour, 100, false),
                        CandlesCount: 0,
                        Indicators: new[] { "Ema(100)" },
                        Pivots:      new[] { "H|0.8" }),

                    // 15‑minute context
                    new(
                        new TimeFrameWindow(TimeFrame.FifteenMinutes, 50, false),
                        CandlesCount: 0 ,
                        Indicators: new[] { "Ema(50)" },
                        Pivots:      new[] { "H|0.6" }),

                    // 5‑minute working TF
                    new(
                        new TimeFrameWindow(TimeFrame.FiveMinutes, 96, true),
                        CandlesCount: 24,
                        Indicators: new[] { "Ema(20)", "Macd(12,26,9)", "Atr(14)", "Vwap()", "Donchian(55)" },
                        Pivots: []),

                    // 1‑minute execution granularity
                    new(
                        new TimeFrameWindow(TimeFrame.OneMinute, 120, false),
                        CandlesCount : 30,
                        Indicators: new[]
                        {
                            "Rsi(7)", "StochRsi(14,14,3)", "Atr(14)",
                            "Keltner(20,2)", "BollingerBands(20,2)",
                            "Vwap()", "Obv()", "Donchian(20)"
                        },
                        Pivots: [])
                }),

            /* ================================================================== */
            /*  INTRADAY                                                         */
            /* ================================================================== */
            TradingStyle.Intraday => new TradingStyleProfile(
                StyleName: style.ToString(),
                Frames: new List<TimeFrameProfile>
                {
                    // 4‑hour trend context
                    new(
                        new TimeFrameWindow(TimeFrame.FourHours, 200, false),
                        CandlesCount : 0,
                        Indicators: new[] { "Ema(200)", "Donchian(55)" },
                        Pivots:      new[] { "H|1.0" }),

                    // 1‑hour structure
                    new(
                        new TimeFrameWindow(TimeFrame.OneHour, 50, false),
                        CandlesCount : 0,
                        Indicators: new[] { "Ema(50)", "Rsi(14)", "Atr(14)", "Donchian(20)" },
                        Pivots:      new[] { "H|0.8" }),

                    // 15‑minute working TF (primary)
                    new(
                        new TimeFrameWindow(TimeFrame.FifteenMinutes, 16, true),
                        CandlesCount : 16,
                        Indicators: new[] { "Ema(16)", "Macd(12,26,9)", "BollingerBands(20,2)", "Vwap()" },
                        Pivots: new[] { "H|0.6" }),

                    // 5‑minute execution granularity
                    new(
                        new TimeFrameWindow(TimeFrame.FiveMinutes, 48, false),
                        CandlesCount : 48,
                        Indicators: new[]
                        {
                            "SuperTrend(10,3)",
                            "Rsi(7)", "StochRsi(14,14,3)", "Atr(14)",
                            "BollingerBands(20,2)",
                            "Vwap()", "Obv()", "Donchian(20)", "Ema(8)", "Ema(21)"
                        },
                        Pivots: [])
                }),

            /* ================================================================== */
            /*  SWING                                                            */
            /* ================================================================== */
            TradingStyle.Swing => new TradingStyleProfile(
                StyleName: style.ToString(),
                Frames: new List<TimeFrameProfile>
                {
                    // Daily chart – macro trend filter
                    new(
                        new TimeFrameWindow(TimeFrame.OneDay, 180, false),
                        CandlesCount : 60,
                        Indicators: new[]
                        {
                            "Ema(200)", "Ema(50)", "Rsi(14)", "Macd(12,26,9)",
                            "Adx(14)", "FibRetrace(23,38,50,62)", "Corr(DXY,1440)"
                        },
                        Pivots: new[] { "H|1.5" }),

                    // 4‑hour – position‑management TF
                    new(
                        new TimeFrameWindow(TimeFrame.FourHours, 240, true),
                        CandlesCount : 60,
                        Indicators: new[] { "Sma(100)", "Stoch(14,3,3)", "Cci(20)" },
                        Pivots:      new[] { "H|1.0" }),

                    // 1‑hour – timing entries
                    new(
                        new TimeFrameWindow(TimeFrame.OneHour, 192, false),
                        CandlesCount : 48,
                        Indicators: Array.Empty<string>(),
                        Pivots:      new[] { "H|0.8" })
                }),

            _ => throw new ArgumentOutOfRangeException(nameof(style))
        };
    }
}

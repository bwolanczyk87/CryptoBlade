// *** METADATA ***
// Version: 1.0.0
// Generated: 2025-02-28 23:16:54 UTC
// Module: CryptoBlade.Helpers
// ****************

// *** INDEX OF INCLUDED FILES ***
1. ApplicationLogging.cs
2. Assets.cs
3. ConfigHelpers.cs
4. GridHelpers.cs
5. GridPosition.cs
6. LinearChannelPrice.cs
7. SubscriptionReconnectHelper.cs
8. TradeSignalHelpers.cs
9. TradingHelpers.cs
// *******************************

using Accord.Statistics.Models.Regression.Linear;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Policies;
using System.Security.Cryptography;

// ==== FILE #1: ApplicationLogging.cs ====
namespace CryptoBlade.Helpers
{
    /// <summary>
    /// Shared logger
    /// </summary>
    public static class ApplicationLogging
    {
        public static ILoggerFactory LoggerFactory { get; set; } = null!;
        public static ILogger<T> CreateLogger<T>() => LoggerFactory.CreateLogger<T>();
        public static ILogger CreateLogger(string categoryName) => LoggerFactory.CreateLogger(categoryName);
    }
}

// -----------------------------

// ==== FILE #2: Assets.cs ====
namespace CryptoBlade.Helpers
{
    public class Assets
    {
        public const string QuoteAsset = "USDT";
    }
}

// -----------------------------

// ==== FILE #3: ConfigHelpers.cs ====
namespace CryptoBlade.Helpers {
using System.Text;
using System.Text.Json;
using CryptoBlade.Configuration;

namespace CryptoBlade.Helpers
{
    public static class ConfigHelpers
    {
        public static bool IsBackTest(this TradingBotOptions options)
        {
            return options.TradingMode == TradingMode.DynamicBackTest;
        }

        public static TradingBotOptions Clone(this TradingBotOptions options)
        {
            var serializedOptions = JsonSerializer.Serialize(options);
            var clonedOptions = JsonSerializer.Deserialize<TradingBotOptions>(serializedOptions);
            if (clonedOptions == null)
                throw new InvalidOperationException("Failed to deserialize options");
            return clonedOptions;
        }

        public static string CalculateMd5(this TradingBotOptions options)
        {
            var serializedOptions = JsonSerializer.Serialize(options);
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(serializedOptions));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
}

// -----------------------------

// ==== FILE #4: GridHelpers.cs ====
namespace CryptoBlade.Helpers
{
    /// <summary>
    /// Helper methods taken from https://github.com/enarjord/passivbot
    /// </summary>
    public static class GridHelpers
    {
        public static double CalcMinEntryQty(double price, bool inverse, double qtyStep, double minQty, double minCost)
        {
            if (inverse)
            {
                return minQty;
            }
            else
            {
                double calculatedQty =
                    (price > 0.0f) ? Math.Ceiling(minCost / price / qtyStep) * qtyStep : 0.0f;
                return Math.Max(minQty, calculatedQty);
            }
        }

        public static double QtyToCost(double qty, double price, bool inverse, double cMultiplier)
        {
            double cost;
            if (price > 0.0f)
            {
                cost = (inverse ? Math.Abs(qty / price) : Math.Abs(qty * price)) * cMultiplier;
            }
            else
            {
                cost = 0.0f;
            }

            return cost;
        }

        public static (double, double) CalcNewPSizePPrice(double positionSize, double positionPrice, double qty,
            double price, double qtyStep)
        {
            if (qty == 0.0f)
            {
                return (positionSize, positionPrice);
            }

            double newPSize = Round(positionSize + qty, qtyStep);

            if (newPSize == 0.0f)
            {
                return (0.0f, 0.0f);
            }

            return (newPSize, NaNToZero(positionPrice) * (positionSize / newPSize) + price * (qty / newPSize));
        }

        public static double Round(double value, double step)
        {
            return Math.Round(value / step) * step;
        }

        public static double NaNToZero(double value)
        {
            return double.IsNaN(value) ? 0.0f : value;
        }

        public static double CalcWalletExposureIfFilled(double balance, double positionSize, double positionPrice,
            double qty,
            double price, bool inverse, double cMultiplier, double qtyStep)
        {
            positionSize = Round(Math.Abs(positionSize), qtyStep);
            qty = Round(Math.Abs(qty), qtyStep);

            (double newPSize, double newPPrice) = CalcNewPSizePPrice(positionSize, positionPrice, qty, price, qtyStep);

            return QtyToCost(newPSize, newPPrice, inverse, cMultiplier) / balance;
        }

        public static double CalcRecursiveReentryQty(
            double balance,
            double positionSize,
            double entryPrice,
            bool inverse,
            double qtyStep,
            double minQty,
            double minCost,
            double cMultiplier,
            double initialQtyPct,
            double ddownFactor,
            double walletExposureLimit)
        {
            double minEntryQty = CalcMinEntryQty(entryPrice, inverse, qtyStep, minQty, minCost);
            double costToQtyResult = CostToQty(balance, entryPrice, inverse, cMultiplier);

            double reentryQty = Math.Max(positionSize * ddownFactor,
                CustomRound(Math.Max(costToQtyResult * walletExposureLimit * initialQtyPct, minEntryQty), qtyStep));

            return reentryQty;
        }


        public static double CostToQty(double cost, double price, bool inverse, double cMultiplier)
        {
            if (inverse)
            {
                return (price > 0.0) ? (cost * price) / cMultiplier : 0.0;
            }
            else
            {
                return (price > 0.0) ? (cost / price) / cMultiplier : 0.0;
            }
        }

        public static double CustomRound(double value, double step)
        {
            if (step == 0.0)
            {
                return value;
            }
            else
            {
                return Math.Round(value / step) * step;
            }
        }

        public static double FindEntryQtyBringingWalletExposureToTarget(
            double balance,
            double positionSize,
            double positionPrice,
            double walletExposureTarget,
            double entryPrice,
            bool inverse,
            double qtyStep,
            double cMultiplier)
        {
            if (walletExposureTarget == 0.0)
            {
                return 0.0;
            }

            double walletExposure = QtyToCost(positionSize, positionPrice, inverse, cMultiplier) / balance;

            if (walletExposure >= walletExposureTarget * 0.99)
            {
                // Return zero if walletExposure is already within 1% of the target
                return 0.0;
            }

            List<double> guesses = new List<double>();
            List<double> values = new List<double>();
            List<double> evaluations = new List<double>();

            guesses.Add(Round(Math.Abs(positionSize) * walletExposureTarget / Math.Max(0.01, walletExposure), qtyStep));
            values.Add(CalcWalletExposureIfFilled(balance, positionSize, positionPrice, guesses.Last(), entryPrice,
                inverse, cMultiplier,
                qtyStep));
            evaluations.Add(Math.Abs(values.Last() - walletExposureTarget) / walletExposureTarget);

            guesses.Add(Math.Max(0.0, Round(Math.Max(guesses.Last() * 1.2, guesses.Last() + qtyStep), qtyStep)));
            values.Add(CalcWalletExposureIfFilled(balance, positionSize, positionPrice, guesses.Last(), entryPrice,
                inverse, cMultiplier,
                qtyStep));
            evaluations.Add(Math.Abs(values.Last() - walletExposureTarget) / walletExposureTarget);

            for (int i = 0; i < 15; i++)
            {
                // ReSharper disable CompareOfFloatsByEqualityOperator
                if (guesses.Last() == guesses[^2])
                    // ReSharper restore CompareOfFloatsByEqualityOperator
                {
                    guesses[^1] =
                        Math.Abs(Round(Math.Max(guesses[^2] * 1.1, guesses[^2] + qtyStep),
                            qtyStep));
                    values[^1] = CalcWalletExposureIfFilled(balance, positionSize, positionPrice,
                        guesses[^1], entryPrice, inverse, cMultiplier, qtyStep);
                }

                guesses.Add(Math.Max(0.0,
                    Round(
                        Interpolate(walletExposureTarget, values.GetRange(values.Count - 2, 2).ToArray(),
                            guesses.GetRange(guesses.Count - 2, 2).ToArray()), qtyStep)));
                values.Add(CalcWalletExposureIfFilled(balance, positionSize, positionPrice, guesses.Last(), entryPrice,
                    inverse, cMultiplier,
                    qtyStep));
                evaluations.Add(Math.Abs(values.Last() - walletExposureTarget) / walletExposureTarget);

                if (evaluations.Last() < 0.01)
                {
                    // Close enough
                    break;
                }
            }

            List<(double, double)> evaluationGuesses = evaluations.Zip(guesses, (e, g) => (e, g)).ToList();
            evaluationGuesses.Sort();

            return evaluationGuesses[0].Item2;
        }

        public static double Interpolate(double target, double[] values, double[] guesses)
        {
            return guesses[0] + (target - values[0]) * (guesses[1] - guesses[0]) / (values[1] - values[0]);
        }

        public static GridPosition CalcRecursiveEntryLong(
            double balance,
            double positionSize,
            double positionPrice,
            double highestBid,
            bool inverse,
            double qtyStep,
            double priceStep,
            double minQty,
            double minCost,
            double cMultiplier,
            double initialQtyPct,
            double ddownFactor,
            double reentryPositionPriceDistance,
            double reentryPositionPriceDistanceWalletExposureWeighting,
            double walletExposureLimit)
        {
            if (walletExposureLimit == 0.0)
                return new GridPosition(0.0, 0.0);

            double initialEntryPrice = Math.Max(
                priceStep,
                highestBid);

            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (initialEntryPrice == priceStep)
                // ReSharper restore CompareOfFloatsByEqualityOperator
            {
                return new GridPosition(0.0, initialEntryPrice);
            }

            double minEntryQty = CalcMinEntryQty(initialEntryPrice, inverse, qtyStep, minQty, minCost);
            double initialEntryQty = Math.Max(
                minEntryQty,
                Round(
                    CostToQty(balance, initialEntryPrice, inverse, cMultiplier)
                    * walletExposureLimit
                    * initialQtyPct,
                    qtyStep
                )
            );

            if (positionSize == 0.0)
            {
                // Normal initial entry
                return new GridPosition(initialEntryQty, initialEntryPrice);
            }
            else if (positionSize < initialEntryQty * 0.8)
            {
                // Partial initial entry
                double entryQty = Math.Max(minEntryQty, Round(initialEntryQty - positionSize, qtyStep));
                return new GridPosition(entryQty, initialEntryPrice);
            }
            else
            {
                double walletExposure = QtyToCost(positionSize, positionPrice, inverse, cMultiplier) / balance;

                if (walletExposure >= walletExposureLimit * 0.999)
                {
                    // No entry if walletExposure is within 0.1% of the limit
                    return new GridPosition(0.0, 0.0);
                }

                // Normal reentry
                double multiplier = (walletExposure / walletExposureLimit) *
                                    reentryPositionPriceDistanceWalletExposureWeighting;
                double entryPrice = RoundDn(positionPrice * (1 - reentryPositionPriceDistance * (1 + multiplier)),
                    priceStep);

                if (entryPrice <= priceStep)
                {
                    return new GridPosition(0.0, priceStep);
                }

                entryPrice = Math.Min(highestBid, entryPrice);
                double entryQty = CalcRecursiveReentryQty(
                    balance,
                    positionSize,
                    entryPrice,
                    inverse,
                    qtyStep,
                    minQty,
                    minCost,
                    cMultiplier,
                    initialQtyPct,
                    ddownFactor,
                    walletExposureLimit
                );

                double walletExposureIfFilled = CalcWalletExposureIfFilled(
                    balance,
                    positionSize,
                    positionPrice,
                    entryQty,
                    entryPrice,
                    inverse,
                    cMultiplier,
                    qtyStep
                );

                bool adjust = false;

                if (walletExposureIfFilled > walletExposureLimit * 1.01)
                {
                    // Reentry too big
                    adjust = true;
                }
                else
                {
                    // Preview next reentry
                    (double newPSize, double newPPrice) =
                        CalcNewPSizePPrice(positionSize, positionPrice, entryQty, entryPrice, qtyStep);
                    double newWalletExposure = QtyToCost(newPSize, newPPrice, inverse, cMultiplier) / balance;
                    double newMultiplier = (newWalletExposure / walletExposureLimit) *
                                           reentryPositionPriceDistanceWalletExposureWeighting;
                    double newEntryPrice = RoundDn(newPPrice * (1 - reentryPositionPriceDistance * (1 + newMultiplier)),
                        priceStep);
                    double newEntryQty = CalcRecursiveReentryQty(
                        balance,
                        newPSize,
                        newEntryPrice,
                        inverse,
                        qtyStep,
                        minQty,
                        minCost,
                        cMultiplier,
                        initialQtyPct,
                        ddownFactor,
                        walletExposureLimit
                    );
                    double walletExposureIfNextFilled = CalcWalletExposureIfFilled(
                        balance,
                        newPSize,
                        newPPrice,
                        newEntryQty,
                        newEntryPrice,
                        inverse,
                        cMultiplier,
                        qtyStep
                    );

                    if (walletExposureIfNextFilled > walletExposureLimit * 1.2)
                    {
                        // Reentry too small
                        adjust = true;
                    }
                }

                if (adjust)
                {
                    // Increase qty if next reentry is too big
                    // Decrease qty if current reentry is too big
                    entryQty = FindEntryQtyBringingWalletExposureToTarget(
                        balance,
                        positionSize,
                        positionPrice,
                        walletExposureLimit,
                        entryPrice,
                        inverse,
                        qtyStep,
                        cMultiplier
                    );
                    entryQty = Math.Max(
                        entryQty,
                        CalcMinEntryQty(entryPrice, inverse, qtyStep, minQty, minCost)
                    );
                }

                return new GridPosition(entryQty, entryPrice);
            }
        }

        public static GridPosition CalcRecursiveEntryShort(
            double balance,
            double positionSize,
            double positionPrice,
            double lowestAsk,
            bool inverse,
            double qtyStep,
            double priceStep,
            double minQty,
            double minCost,
            double cMultiplier,
            double initialQtyPct,
            double ddownFactor,
            double reentryPPriceDist,
            double reentryPPriceDistWalletExposureWeighting,
            double walletExposureLimit)
        {
            if (walletExposureLimit == 0.0)
            {
                return new GridPosition(0.0, 0.0);
            }

            positionSize = Math.Abs(positionSize);

            double initialEntryPrice = Math.Max(
                priceStep,
                lowestAsk);

            double minEntryQty = CalcMinEntryQty(initialEntryPrice, inverse, qtyStep, minQty, minCost);

            double initialEntryQty = Math.Max(
                minEntryQty,
                Round(
                    CostToQty(balance, initialEntryPrice, inverse, cMultiplier)
                    * walletExposureLimit
                    * initialQtyPct,
                    qtyStep
                )
            );

            if (positionSize == 0.0)
            {
                // Normal initial entry
                return new GridPosition(initialEntryQty, initialEntryPrice);
            }
            else if (positionSize < initialEntryQty * 0.8)
            {
                // Partial initial entry
                double entryQty = Math.Max(minEntryQty, Round(initialEntryQty - positionSize, qtyStep));
                return new GridPosition(entryQty, initialEntryPrice);
            }
            else
            {
                double walletExposure = QtyToCost(positionSize, positionPrice, inverse, cMultiplier) / balance;

                if (walletExposure >= walletExposureLimit * 0.999)
                {
                    // No entry if walletExposure is within 0.1% of the limit
                    return new GridPosition(0.0, 0.0);
                }

                // Normal reentry
                double multiplier = (walletExposure / walletExposureLimit) * reentryPPriceDistWalletExposureWeighting;

                double entryPrice = RoundDn(positionPrice * (1 + reentryPPriceDist * (1 + multiplier)),
                    priceStep);
                entryPrice = Math.Max(lowestAsk, entryPrice);

                double entryQty = CalcRecursiveReentryQty(
                    balance,
                    positionSize,
                    entryPrice,
                    inverse,
                    qtyStep,
                    minQty,
                    minCost,
                    cMultiplier,
                    initialQtyPct,
                    ddownFactor,
                    walletExposureLimit
                );

                double walletExposureIfFilled = CalcWalletExposureIfFilled(
                    balance,
                    positionSize,
                    positionPrice,
                    entryQty,
                    entryPrice,
                    inverse,
                    cMultiplier,
                    qtyStep
                );

                bool adjust = false;

                if (walletExposureIfFilled > walletExposureLimit * 1.01)
                {
                    // Reentry too big
                    adjust = true;
                }
                else
                {
                    // Preview next reentry
                    (double newPSize, double newPPrice) =
                        CalcNewPSizePPrice(positionSize, positionPrice, entryQty, entryPrice, qtyStep);
                    double newWalletExposure = QtyToCost(newPSize, newPPrice, inverse, cMultiplier) / balance;
                    double newMultiplier = (newWalletExposure / walletExposureLimit) *
                                           reentryPPriceDistWalletExposureWeighting;
                    double newEntryPrice = RoundUp(newPPrice * (1 + reentryPPriceDist * (1 + newMultiplier)), priceStep);
                    double newEntryQty = CalcRecursiveReentryQty(
                        balance,
                        newPSize,
                        newEntryPrice,
                        inverse,
                        qtyStep,
                        minQty,
                        minCost,
                        cMultiplier,
                        initialQtyPct,
                        ddownFactor,
                        walletExposureLimit
                    );
                    double walletExposureIfNextFilled = CalcWalletExposureIfFilled(
                        balance,
                        newPSize,
                        newPPrice,
                        newEntryQty,
                        newEntryPrice,
                        inverse,
                        cMultiplier,
                        qtyStep
                    );

                    if (walletExposureIfNextFilled > walletExposureLimit * 1.2)
                    {
                        // Reentry too small
                        adjust = true;
                    }
                }

                if (adjust)
                {
                    // Increase qty if next reentry is too big
                    // Decrease qty if current reentry is too big
                    entryQty = FindEntryQtyBringingWalletExposureToTarget(
                        balance,
                        positionSize,
                        positionPrice,
                        walletExposureLimit,
                        entryPrice,
                        inverse,
                        qtyStep,
                        cMultiplier
                    );
                    entryQty = Math.Max(
                        entryQty,
                        CalcMinEntryQty(entryPrice, inverse, qtyStep, minQty, minCost)
                    );
                }

                return new GridPosition(entryQty, entryPrice);
            }
        }


        public static double RoundDn(double value, double step)
        {
            return Math.Floor(value / step) * step;
        }

        public static double RoundUp(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}

// -----------------------------

// ==== FILE #5: GridPosition.cs ====
namespace CryptoBlade.Helpers
{
    public readonly record struct GridPosition(double Qty, double Price);
}

// -----------------------------

// ==== FILE #6: LinearChannelPrice.cs ====
namespace CryptoBlade.Helpers
{
    public readonly record struct LinearChannelPrice(double? ExpectedPrice, double? StandardDeviation);
}

// -----------------------------

// ==== FILE #7: SubscriptionReconnectHelper.cs ====
namespace CryptoBlade.Helpers {
using CryptoExchange.Net.Sockets;

namespace CryptoBlade.Helpers
{
    public static class SubscriptionReconnectHelper
    {
        public static void AutoReconnect(this UpdateSubscription subscription, ILogger logger)
        {
            subscription.ConnectionRestored += _ =>
            {
                logger.LogWarning("Connection restored...");
            };

            subscription.ConnectionLost += async () =>
            {
                logger.LogWarning("Connection lost. Reconnecting...");
                await ExchangePolicies.RetryForever.ExecuteAsync(async () => await subscription.ReconnectAsync());
            };
            subscription.Exception += async (ex) =>
            {
                logger.LogWarning(ex, "Error in subscription to wallet updates. Retrying...");
                await ExchangePolicies.RetryForever.ExecuteAsync(async () => await subscription.ReconnectAsync());
            };
        }
    }
}
}

// -----------------------------

// ==== FILE #8: TradeSignalHelpers.cs ====
namespace CryptoBlade.Helpers {
using CryptoBlade.Strategies.Common;
using Microsoft.VisualBasic;
using ScottPlot;
using Skender.Stock.Indicators;

namespace CryptoBlade.Helpers
{
    public static class TradeSignalHelpers
    {
        public static decimal VolumeInQuoteCurrency(Quote quote)
        {
            var typicalPrice = (quote.High + quote.Low + quote.Close) / 3.0m;
            var volume = (quote.Volume * typicalPrice);
            return volume;
        }

        public static decimal? GetTrendPercent(SmaResult? sma, Quote quote)
        {
            if (sma == null || !sma.Sma.HasValue)
                return null;
            var lastClosePrice = quote.Close;
            var smaValue = (decimal)sma.Sma.Value;
            var trendPercent = Math.Round((lastClosePrice - smaValue) / lastClosePrice * 100.0m, 4);
            return trendPercent;
        }

        public static bool ShortCounterTradeCondition(decimal bestAskPrice, decimal maHigh)
        {
            return bestAskPrice > maHigh;
        }

        public static bool LongCounterTradeCondition(decimal bestBidPrice, decimal maLow)
        {
            return bestBidPrice < maLow;
        }

        public static Trend GetTrend(decimal? trendPercent)
        {
            if(trendPercent == null)
                return Trend.Neutral;
            if (trendPercent > 0)
                return Trend.Short;
            if (trendPercent < 0)
                return Trend.Long;
            return Trend.Neutral;
        }

        public static bool IsMfiRsiBuy(MfiResult? mfi, RsiResult? rsi, Quote quote)
        {
            if (mfi == null || rsi == null)
                return false;
            if (!mfi.Mfi.HasValue || !rsi.Rsi.HasValue)
                return false;
            bool buy = mfi.Mfi < 20 && rsi.Rsi < 35 && quote.Close < quote.Open;
            return buy;
        }

        public static bool IsMfiRsiSell(MfiResult? mfi, RsiResult? rsi, Quote quote)
        {
            if (mfi == null || rsi == null)
                return false;
            if (!mfi.Mfi.HasValue || !rsi.Rsi.HasValue)
                return false;
            bool sell = mfi.Mfi > 80 && rsi.Rsi > 65 && quote.Close > quote.Open;
            return sell;
        }

        public static decimal Get5MinSpread(Quote[] quotes)
        {
            var last5 = quotes.Reverse().Take(5).ToArray();
            var highestHigh = last5.Max(x => x.High);
            var lowestLow = last5.Min(x => x.Low);
            var spread = Math.Round((highestHigh - lowestLow) / highestHigh * 100, 4);
            return spread;
        }

        public static Trend GetModifiedEriTrend(Quote[] quotes, int slowEmaPeriod = 64)
        {
            try
            {
                var vwma = quotes.GetVwma(slowEmaPeriod).ToArray();
                if(vwma.All(x => !x.Vwma.HasValue))
                    return Trend.Neutral;
                var slowMovingAverage = vwma.GetEma(slowEmaPeriod);
                var lastAverage = slowMovingAverage.LastOrDefault();
                var lastQuote = quotes.LastOrDefault();
                if(lastAverage == null || lastQuote == null || !lastAverage.Ema.HasValue)
                    return Trend.Neutral;
                return lastQuote.Close > (decimal)lastAverage.Ema.Value ? Trend.Short : Trend.Long;
            }
            catch (ArgumentOutOfRangeException)
            {
                // there could be some shitty symbol that has no volume
                // it should be already handled, this is just a safety net
                return Trend.Neutral;
            }
        }

        public static Trend GetMfiTrend(Quote[] quotes, int lookbackPeriod = 100)
        {
            int requiredQuotes = 14 + lookbackPeriod;
            int skip = quotes.Length - requiredQuotes;
            var quotesToUse = quotes.Skip(skip).ToArray();
            var mfi = quotesToUse.GetMfi().ToArray();
            var rsi = quotesToUse.GetRsi().ToArray();
            int lookback = Math.Min(Math.Min(mfi.Length, rsi.Length), lookbackPeriod);
            for (int i = 1; i < (lookback + 1); i++)
            {
                var quote = quotes[^i];
                var mfiResult = mfi[^i];
                var rsiResult = rsi[^i];
                if (IsMfiRsiBuy(mfiResult, rsiResult, quote))
                    return Trend.Long;
                if (IsMfiRsiSell(mfiResult, rsiResult, quote))
                    return Trend.Short;
            }

            return Trend.Neutral;
        }

        public static double?[] CalculateQflBuyBases(Quote[] quotes, int volumeSmaLength = 6)
        {
            if (quotes.Length < volumeSmaLength + 3)
                return Array.Empty<double?>();

            var smaLength = quotes.Use(CandlePart.Volume).GetSma(volumeSmaLength).ToArray();
            bool[] downArr = new bool[quotes.Length];
            for (int i = 0; i < quotes.Length; i++)
            {
                bool down = i >= 5 &&
                            quotes[i - 3].Low < quotes[i - 4].Low
                            && quotes[i - 4].Low < quotes[i - 5].Low
                            && quotes[i - 2].Low > quotes[i - 3].Low
                            && quotes[i - 1].Low > quotes[i - 2].Low
                            && smaLength[i - 3].Sma.HasValue
                            && (double)quotes[i - 3].Volume > smaLength[i - 3].Sma!.Value;
                downArr[i] = down;
            }

            double?[] fractalDown = new double?[quotes.Length];

            for (int i = 0; i < quotes.Length; i++)
            {
                if (downArr[i] && i > 2)
                {
                    fractalDown[i] = (double)quotes[i - 3].Low;
                }
                else if (i > 0)
                {
                    fractalDown[i] = fractalDown[i - 1];
                }
            }

            return fractalDown;
        }

        public static double?[] CalculateQflSellBases(Quote[] quotes, int volumeSmaLength = 6)
        {
            if (quotes.Length < volumeSmaLength + 3)
                return Array.Empty<double?>();

            var smaLength = quotes.Use(CandlePart.Volume).GetSma(volumeSmaLength).ToArray();
            bool[] upArr = new bool[quotes.Length];
            for (int i = 0; i < quotes.Length; i++)
            {
                bool up = i >= 5 &&
                            quotes[i - 3].High > quotes[i - 4].High
                            && quotes[i - 4].High > quotes[i - 5].High
                            && quotes[i - 2].High < quotes[i - 3].High
                            && quotes[i - 1].High < quotes[i - 2].High
                            && smaLength[i - 3].Sma.HasValue
                            && (double)quotes[i - 3].Volume > smaLength[i - 3].Sma!.Value;

                upArr[i] = up;
            }

            double?[] fractalUp = new double?[quotes.Length];

            for (int i = 0; i < quotes.Length; i++)
            {
                if (upArr[i] && i > 2)
                {
                    fractalUp[i] = (double)quotes[i - 3].High;
                }
                else if (i > 0)
                {
                    fractalUp[i] = fractalUp[i - 1];
                }
            }

            return fractalUp;
        }
    }
}
}

// -----------------------------

// ==== FILE #9: TradingHelpers.cs ====
namespace CryptoBlade.Helpers {
using CryptoBlade.Models;
using CryptoBlade.Strategies.Wallet;
using Skender.Stock.Indicators;

namespace CryptoBlade.Helpers
{
    public static class TradingHelpers
    {
        public static decimal? CalculateQuantity(this SymbolInfo symbolInfo,
            IWalletManager walletManager,
            decimal price,
            decimal walletExposure,
            decimal dcaMultiplier)
        {
            if (price == 0)
                return null;
            if (walletExposure == 0)
                return null;
            decimal? walletBalance = walletManager.Contract.WalletBalance;
            if (!walletBalance.HasValue)
                return null;
            if (!symbolInfo.MaxLeverage.HasValue)
                return null;    
            if (!symbolInfo.QtyStep.HasValue)
                return null;
            decimal maxTradeQty =
                walletBalance.Value * walletExposure / price / (100.0m / symbolInfo.MaxLeverage.Value);
            decimal dynamicQty = maxTradeQty / dcaMultiplier;
            dynamicQty -= (dynamicQty % symbolInfo.QtyStep.Value);
            return dynamicQty;
        }

        public static decimal? CalculateMinBalance(this SymbolInfo symbolInfo,
            decimal price,
            decimal walletExposure,
            decimal dcaMultiplier)
        {
            if (!symbolInfo.MaxLeverage.HasValue)
                return null;
            if (!symbolInfo.QtyStep.HasValue)
                return null;
            if (walletExposure == 0)
                return null;
            decimal minBalance =
                symbolInfo.QtyStep.Value * dcaMultiplier * (100.0m / symbolInfo.MaxLeverage.Value) * price /
                walletExposure;
            return minBalance;
        }

        public static decimal? CalculateShortTakeProfit(Position position, SymbolInfo symbolInfo, Quote[] quotes,
            decimal increasePercentage, Ticker currentPrice, decimal feeRate, decimal minProfitRate)
        {
            try
            {
                var ma6High = quotes.Use(CandlePart.High).GetSma(6);
                var ma6Low = quotes.Use(CandlePart.Low).GetSma(6);
                var ma6HighLast = ma6High.LastOrDefault();
                var ma6LowLast = ma6Low.LastOrDefault();
                if (ma6LowLast == null || !ma6LowLast.Sma.HasValue)
                    return null;
                if (ma6HighLast == null || !ma6HighLast.Sma.HasValue)
                    return null;
                decimal shortTargetPrice =
                    position.AveragePrice - ((decimal)ma6HighLast.Sma.Value - (decimal)ma6LowLast.Sma.Value);
                decimal shortTakeProfit = shortTargetPrice * (1.0m - increasePercentage / 100.0m);
                decimal entryFee = position.AveragePrice * position.Quantity * feeRate;
                decimal exitFee = shortTakeProfit * position.Quantity * feeRate;
                decimal totalFee = entryFee + exitFee;
                decimal feeInPrice = totalFee / position.Quantity;
                shortTakeProfit -= feeInPrice;
                shortTakeProfit = Math.Round(shortTakeProfit, (int)symbolInfo.PriceScale,
                    MidpointRounding.AwayFromZero);
                if (minProfitRate >= 1.0m)
                    minProfitRate = 0.99m;
                decimal shortMinTakeProfit = position.AveragePrice * (1.0m - minProfitRate);
                shortMinTakeProfit -= feeInPrice;
                shortMinTakeProfit = Math.Round(shortMinTakeProfit, (int)symbolInfo.PriceScale,
                    MidpointRounding.AwayFromZero);

                if (shortTakeProfit > shortMinTakeProfit)
                    shortTakeProfit = shortMinTakeProfit;

                if (currentPrice.BestBidPrice < shortTakeProfit)
                    shortTakeProfit = currentPrice.BestBidPrice;

                if (shortTakeProfit <= 0)
                    shortTakeProfit = (decimal)Math.Pow(10, -(int)symbolInfo.PriceScale);

                return shortTakeProfit;
            }
            catch
            {
                return null;
            }
        }

        public static decimal? CalculateLongTakeProfit(Position position, SymbolInfo symbolInfo, Quote[] quotes,
            decimal increasePercentage, Ticker currentPrice, decimal feeRate, decimal minProfitRate)
        {
            try
            {
                var ma6High = quotes.Use(CandlePart.High).GetSma(6);
                var ma6Low = quotes.Use(CandlePart.Low).GetSma(6);
                var ma6HighLast = ma6High.LastOrDefault();
                var ma6LowLast = ma6Low.LastOrDefault();
                if (ma6LowLast == null || !ma6LowLast.Sma.HasValue)
                    return null;
                if (ma6HighLast == null || !ma6HighLast.Sma.HasValue)
                    return null;
                decimal longTargetPrice =
                    position.AveragePrice + ((decimal)ma6HighLast.Sma.Value - (decimal)ma6LowLast.Sma.Value);
                decimal longTakeProfit = longTargetPrice * (1.0m + increasePercentage / 100.0m);
                decimal entryFee = position.AveragePrice * position.Quantity * feeRate;
                decimal exitFee = longTakeProfit * position.Quantity * feeRate;
                decimal totalFee = entryFee + exitFee;
                decimal feeInPrice = totalFee / position.Quantity;
                longTakeProfit += feeInPrice;
                longTakeProfit = Math.Round(longTakeProfit, (int)symbolInfo.PriceScale, MidpointRounding.AwayFromZero);

                decimal longMinTakeProfit = position.AveragePrice * (1.0m + minProfitRate);
                longMinTakeProfit += feeInPrice;
                longMinTakeProfit = Math.Round(longMinTakeProfit, (int)symbolInfo.PriceScale,
                    MidpointRounding.AwayFromZero);

                if (longTakeProfit < longMinTakeProfit)
                    longTakeProfit = longMinTakeProfit;

                if (currentPrice.BestAskPrice > longTakeProfit)
                    longTakeProfit = currentPrice.BestAskPrice;
                return longTakeProfit;
            }
            catch
            {
                return null;
            }
        }

        public static bool CrossesBellow(this Quote quote, double priceLevel)
        {
            return (double)quote.High > priceLevel && (double)quote.Low < priceLevel;
        }

        public static bool CrossesBellow(double previousValue, double currentValue, double value)
        {
            return previousValue > value && currentValue <= value;
        }

        public static bool CrossesAbove(this Quote quote, double priceLevel)
        {
            return (double)quote.Low < priceLevel && (double)quote.High > priceLevel;
        }

        public static bool CrossesAbove(double previousValue, double currentValue, double value)
        {
            return previousValue < value && currentValue >= value;
        }

        public static LinearChannelPrice CalculateExpectedPrice(Quote[] quotes, int channelLength)
        {
            int quotesLength = quotes.Length;
            int skip = quotesLength - channelLength;
            if (skip < 0)
                return new LinearChannelPrice(null, null); // not enough quotes
            OrdinaryLeastSquares ols = new OrdinaryLeastSquares();
            double[] priceData = new double[channelLength];
            double[] xAxis = new double[channelLength];
            for (int i = 0; i < channelLength; i++)
            {
                var averagePrice = (quotes[i + skip].Open + quotes[i + skip].Close) / 2.0m;
                priceData[i] = (double)averagePrice;
                xAxis[i] = i;
            }

            var lr = ols.Learn(xAxis, priceData.ToArray());
            var intercept = lr.Intercept;
            var slope = lr.Slope;
            var expectedPrice = intercept + slope * quotes.Length;
            var standardDeviation = StandardDeviation(priceData);

            return new LinearChannelPrice(expectedPrice, standardDeviation);
        }

        public static double StandardDeviation(double[] data)
        {
            double mean = data.Sum() / data.Length;
            double sumOfSquares = 0;
            for (int i = 0; i < data.Length; i++)
            {
                var x = data[i];
                sumOfSquares += (x - mean) * (x - mean);
            }

            return Math.Sqrt(sumOfSquares / data.Length);
        }

        public static double RootMeanSquareError(IReadOnlyList<double> estimated, IReadOnlyList<double> measured)
        {
            double sum = 0;
            for (int i = 0; i < estimated.Count; i++)
            {
                var error = estimated[i] - measured[i];
                sum += error * error;
            }

            return Math.Sqrt(sum / estimated.Count);
        }

        public static double NormalizedRootMeanSquareError(IReadOnlyList<double> estimated, IReadOnlyList<double> measured)
        {
            double sum = 0;
            for (int i = 0; i < estimated.Count; i++)
            {
                var error = estimated[i] - measured[i];
                sum += error * error;
            }
            var average = measured.Average();
            var nrmse = Math.Sqrt(sum / estimated.Count) / average;
            if (nrmse > 1)
                nrmse = 1;
            return nrmse;
        }
    }
}
}

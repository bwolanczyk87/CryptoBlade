using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;  // używamy tylko do obliczania RSI
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy : TradingStrategyBase
    {
        private readonly IOptions<MomentumStrategyOptions> m_options;

        public MomentumStrategy(
            IOptions<MomentumStrategyOptions> options,
            IOptions<TradingBotOptions> botOptions,
            string symbol,
            IWalletManager walletManager,
            ICbFuturesRestClient restClient)
            : base(
                  options,
                  botOptions,
                  symbol,
                  BuildTimeFrameWindows(options.Value),
                  walletManager,
                  restClient)
        {
            m_options = options;
        }

        public override string Name => "Momentum";

        protected override decimal WalletExposureLong => m_options.Value.WalletExposureLong;
        protected override decimal WalletExposureShort => m_options.Value.WalletExposureShort;
        protected override int DcaOrdersCount => m_options.Value.DcaOrdersCount;
        protected override bool ForceMinQty => m_options.Value.ForceMinQty;

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            // Odczyt parametrów
            var opts = m_options.Value;

            // Pobranie danych świec z głównego interwału
            var primaryQuotes = QuoteQueues[opts.PrimaryTimeFrame].GetQuotes();

            // Opcjonalne pobranie świec z interwału pomocniczego (jeśli włączone)
            var secondaryQuotes = opts.UseSecondaryTimeFrameFilter
                ? QuoteQueues[opts.SecondaryTimeFrame].GetQuotes()
                : Array.Empty<Quote>();

            // Jeśli brak danych w głównym interwale, nie ma co oceniać
            if (primaryQuotes.Length == 0)
            {
                return Task.FromResult(new SignalEvaluation(
                    false, false, false, false, Array.Empty<StrategyIndicator>()));
            }

            // Konwersja Candle->(DateTime, double) do obliczania zero-lag MACD
            var primaryCloseList = primaryQuotes
                .Select(q => (q.Date, (double)q.Close))
                .ToList();

            // Obliczenie zero-lag MACD na głównym interwale
            var (macdZL, signalZL, histZL) = ComputeZeroLagMacd(
                primaryCloseList,
                opts.MacdFastPeriod,
                opts.MacdSlowPeriod,
                opts.MacdSignalPeriod);

            // RSI z biblioteki Skender
            var rsiList = primaryQuotes.GetRsi(opts.RsiPeriod);
            var rsi = rsiList.LastOrDefault();

            bool okAdx = true;
            double? adxValue = null;
            if (opts.UseAdxFilter)
            {
                var adxList = primaryQuotes.GetAdx(opts.AdxPeriod);
                var lastAdx = adxList.LastOrDefault();
                if (lastAdx != null && lastAdx.Adx.HasValue)
                {
                    adxValue = lastAdx.Adx.Value;
                    okAdx = (decimal)adxValue >= opts.MinAdxThreshold;
                }
                else
                {
                    okAdx = false;
                }
            }

            // Filtrowanie trendu na interwale pomocniczym
            bool isBullishTrendSecondary = false;
            bool isBearishTrendSecondary = false;
            if (opts.UseSecondaryTimeFrameFilter && secondaryQuotes.Length > 0)
            {
                var secondaryCloseList = secondaryQuotes
                    .Select(q => (q.Date, (double)q.Close))
                    .ToList();

                var (macdSec, sigSec, histSec) = ComputeZeroLagMacd(
                    secondaryCloseList,
                    opts.MacdFastPeriod,
                    opts.MacdSlowPeriod,
                    opts.MacdSignalPeriod);

                isBullishTrendSecondary = (histSec.HasValue && histSec.Value > 0) &&
                                          (macdSec.HasValue && sigSec.HasValue && macdSec.Value > sigSec.Value);
                isBearishTrendSecondary = (histSec.HasValue && histSec.Value < 0) &&
                                          (macdSec.HasValue && sigSec.HasValue && macdSec.Value < sigSec.Value);
            }

            // Ocena sygnałów: buy / sell
            bool hasBuySignal = false;
            bool hasSellSignal = false;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;

            // Sprawdzamy warunek buy/sell na podstawie MACD, RSI, ADX i (opcjonalnie) filtra secondary
            if (macdZL.HasValue && signalZL.HasValue && histZL.HasValue && rsi != null && okAdx)
            {
                // Warunek kupna
                if (histZL.Value > 0 &&
                    macdZL.Value > signalZL.Value &&
                    (decimal)rsi.Rsi < opts.RsiUpperThreshold &&
                    (!opts.UseSecondaryTimeFrameFilter || isBullishTrendSecondary))
                {
                    hasBuySignal = true;
                }

                // Warunek sprzedaży
                if (histZL.Value < 0 &&
                    macdZL.Value < signalZL.Value &&
                    (decimal)rsi.Rsi > opts.RsiLowerThreshold &&
                    (!opts.UseSecondaryTimeFrameFilter || isBearishTrendSecondary))
                {
                    hasSellSignal = true;
                }
            }

            // Re-entry logic: jeżeli mamy już Long / Short i wciąż jest sygnał w tym samym kierunku
            if (LongPosition != null && hasBuySignal)
            {
                var thresholdLong = (1.0m - opts.MinReentryPositionDistanceLong) * LongPosition.AveragePrice;
                if (Ticker != null && Ticker.BestBidPrice < thresholdLong)
                {
                    hasBuyExtraSignal = true;
                }
            }
            if (ShortPosition != null && hasSellSignal)
            {
                var thresholdShort = (1.0m + opts.MinReentryPositionDistanceShort) * ShortPosition.AveragePrice;
                if (Ticker != null && Ticker.BestAskPrice > thresholdShort)
                {
                    hasSellExtraSignal = true;
                }
            }

            // Zapis wskaźników do debugowania
            List<StrategyIndicator> indicators = new()
            {
                new StrategyIndicator(IndicatorType.Macd.ToString(), macdZL ?? 0),
                new StrategyIndicator(IndicatorType.MacdSignal.ToString(), signalZL ?? 0),
                new StrategyIndicator(IndicatorType.MacdHistogram.ToString(), histZL ?? 0)
            };
            if (rsi != null)
            {
                indicators.Add(new StrategyIndicator(IndicatorType.Rsi.ToString(), rsi.Rsi ?? 0));
            }
            if (opts.UseAdxFilter && adxValue.HasValue)
            {
                indicators.Add(new StrategyIndicator(IndicatorType.Adx.ToString(), adxValue ?? 0));
            }

            var evaluation = new SignalEvaluation(
                hasBuySignal,
                hasSellSignal,
                hasBuyExtraSignal,
                hasSellExtraSignal,
                indicators.ToArray());

            return Task.FromResult(evaluation);
        }

        /// <summary>
        /// Budowa listy TimeFrameWindow na podstawie configu – ewentualny interwał wtórny
        /// </summary>
        private static TimeFrameWindow[] BuildTimeFrameWindows(MomentumStrategyOptions opts)
        {
            if (opts.UseSecondaryTimeFrameFilter)
            {
                return new[]
                {
                    new TimeFrameWindow(opts.PrimaryTimeFrame, opts.PrimaryTimeFrameWindowSize, true),
                    new TimeFrameWindow(opts.SecondaryTimeFrame, opts.SecondaryTimeFrameWindowSize, false)
                };
            }
            return new[]
            {
                new TimeFrameWindow(opts.PrimaryTimeFrame, opts.PrimaryTimeFrameWindowSize, true)
            };
        }

        /// <summary>
        /// Implementacja Zero-Lag MACD w stylu artykułu:
        /// 1) obliczamy Zero-Lag (fast i slow) – double-smooth,
        /// 2) macdLine = ZLfast - ZLslow,
        /// 3) sygnał = zero-lag ema z macdLine,
        /// 4) histogram = macdLine - signal
        /// Zwraca (macdLine, signalLine, histogram).
        /// </summary>
        private static (double?, double?, double?) ComputeZeroLagMacd(
            List<(DateTime DateTime, double Close)> prices,
            int fastPeriods,
            int slowPeriods,
            int signalPeriods)
        {
            // Potrzebujemy wystarczająco świec, by liczyć slow i potem signal
            // np. ~ slowPeriods + signalPeriods
            int needed = Math.Max(slowPeriods, signalPeriods) * 2;
            if (prices.Count < needed)
                return (null, null, null);

            // Zero-lag fast
            var zlemaFast = ZeroLagMa(prices, fastPeriods);

            // Zero-lag slow
            var zlemaSlow = ZeroLagMa(prices, slowPeriods);

            if (zlemaFast.Count == 0 || zlemaSlow.Count == 0)
                return (null, null, null);

            // Weźmy ostatni element (zakładając, że zlemaFast i slow mają zbliżoną długość)
            double fastVal = zlemaFast[^1].Value;
            double slowVal = zlemaSlow[^1].Value;

            double macdLine = fastVal - slowVal;

            // Sygnał zero-lag
            var macdList = new List<(DateTime, double)>();
            int minCount = Math.Min(zlemaFast.Count, zlemaSlow.Count);
            for (int i = 0; i < minCount; i++)
            {
                double mVal = zlemaFast[i].Value - zlemaSlow[i].Value;
                macdList.Add((zlemaFast[i].DateTime, mVal));
            }

            var zlemaSignal = ZeroLagMa(macdList, signalPeriods);
            if (zlemaSignal.Count == 0)
                return (null, null, null);

            double signalLine = zlemaSignal[^1].Value;
            double hist = macdLine - signalLine;

            return (macdLine, signalLine, hist);
        }

        /// <summary>
        /// Zero-lag MA:
        /// 1) oblicz Ema(...) z inputu (okres = length),
        /// 2) oblicz Ema(...) z poprzednich wyników,
        /// 3) ZL = 2*ema1 - ema2
        /// </summary>
        private static List<(DateTime DateTime, double Value)> ZeroLagMa(
            List<(DateTime DateTime, double Value)> data,
            int length)
        {
            var ema1 = Ema(data, length);
            var ema2 = Ema(ema1, length);

            int count = Math.Min(ema1.Count, ema2.Count);
            var zl = new List<(DateTime, double)>(count);
            for (int i = 0; i < count; i++)
            {
                double zVal = 2.0 * ema1[i].Value - ema2[i].Value;
                zl.Add((ema1[i].DateTime, zVal));
            }
            return zl;
        }

        /// <summary>
        /// Klasyczna EMA – pętla, zwraca listę o tej samej długości co 'data'.
        /// Pierwsza wartość = data[0].Value,
        /// Ema(i) = Ema(i-1) + k*(price - Ema(i-1)),
        /// k = 2/(length+1).
        /// </summary>
        private static List<(DateTime DateTime, double Value)> Ema(
            List<(DateTime DateTime, double Value)> data,
            int length)
        {
            var output = new List<(DateTime DateTime, double Value)>(data.Count);
            if (data.Count == 0 || length < 1)
                return output;

            double k = 2.0 / (length + 1.0);

            double prevEma = data[0].Value;
            output.Add((data[0].DateTime, prevEma));

            for (int i = 1; i < data.Count; i++)
            {
                double price = data[i].Value;
                double currentEma = prevEma + k * (price - prevEma);
                output.Add((data[i].DateTime, currentEma));
                prevEma = currentEma;
            }

            return output;
        }
    }
}

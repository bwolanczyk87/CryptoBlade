using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy : TradingStrategyBase
    {
        private readonly MomentumStrategyOptions m_strategyOptions;
        private DateTime m_lastSignalTime;
        private decimal? m_entryPrice;

        public MomentumStrategy(
            IOptions<MomentumStrategyOptions> strategyOptions,
            IOptions<TradingBotOptions> botOptions,
            string symbol,
            IWalletManager walletManager,
            ICbFuturesRestClient restClient)
            : base(strategyOptions, botOptions, symbol, BuildTimeFrameWindows(), walletManager, restClient)
        {
            m_strategyOptions = strategyOptions.Value;
            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }

        public override string Name => "Momentum";

        private decimal? EntryPrice
        {
            get => IsInLongTrade ? LongPosition?.AveragePrice
                   : IsInShortTrade ? ShortPosition?.AveragePrice
                   : m_entryPrice;
            set => m_entryPrice = value;
        }

        protected override Task CalculateTakeProfitAsync(IList<StrategyIndicator> indicators)
            => Task.CompletedTask;

        protected override Task CalculateStopLossTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            try
            {
                if (!IsInTrade || Ticker == null || !EntryPrice.HasValue)
                    return Task.CompletedTask;

                var quotes = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
                var atr = quotes.GetAtr(m_strategyOptions.VolatilityPeriod).Last();
                double fallbackAtr = (double)(SymbolInfo.QtyStep ?? 1m);
                decimal atrValue = (decimal)(atr.Atr ?? fallbackAtr);

                if (HasBuySignal)
                {
                    StopLossPrice = Math.Round(EntryPrice.Value - atrValue * m_strategyOptions.AtrMultiplierSl,
                                             (int)SymbolInfo.PriceScale);
                    TakeProfitPrice = Math.Round(EntryPrice.Value + atrValue * m_strategyOptions.AtrMultiplierTp,
                                              (int)SymbolInfo.PriceScale);
                }
                else if (HasSellSignal)
                {
                    StopLossPrice = Math.Round(EntryPrice.Value + atrValue * m_strategyOptions.AtrMultiplierSl,
                                             (int)SymbolInfo.PriceScale);
                    TakeProfitPrice = Math.Round(EntryPrice.Value - atrValue * m_strategyOptions.AtrMultiplierTp,
                                              (int)SymbolInfo.PriceScale);
                }
            }
            catch (Exception ex)
            {
                indicators.Add(new StrategyIndicator("SL/TP Error", ex.Message));
            }
            return Task.CompletedTask;
        }

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var indicators = new List<StrategyIndicator>();

            try
            {
                if (DateTime.UtcNow - m_lastSignalTime < m_strategyOptions.CooldownPeriod)
                    return Task.FromResult(NoSignal(indicators, "Cooldown"));

                var primaryQuotes = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
                var secondaryQuotes = QuoteQueues[TimeFrame.FiveMinutes].GetQuotes();

                if (!ValidateData(primaryQuotes, secondaryQuotes, indicators))
                    return Task.FromResult(NoSignal(indicators, "InvalidData"));

                var (bollingerBands, rsi, adx, volumeSma) = CalculatePrimaryIndicators(primaryQuotes);
                var (emaTrend, contextRsi, volatility) = CalculateSecondaryIndicators(secondaryQuotes);

                if (!ValidateIndicators(bollingerBands, rsi, adx, emaTrend, contextRsi, indicators))
                    return Task.FromResult(NoSignal(indicators, "InvalidIndicators"));

                bool isSqueeze = DetectBollingerSqueeze(bollingerBands);
                bool volumeSpike = DetectVolumeSpike(primaryQuotes, volumeSma);
                bool adxValid = (decimal)(adx.Last().Adx ?? 0) >= m_strategyOptions.AdxTrendThreshold;
                bool trendContextValid = ValidateTrendContext(secondaryQuotes, emaTrend, contextRsi, volatility);

                indicators.AddRange(new[]
                {
                    new StrategyIndicator("Squeeze", isSqueeze),
                    new StrategyIndicator("VolumeSpike", volumeSpike),
                    new StrategyIndicator("ADX", (decimal)adx.Last().Adx),
                    new StrategyIndicator("TrendContext", trendContextValid)
                });

                if (!isSqueeze || !volumeSpike || !adxValid || !trendContextValid)
                    return Task.FromResult(NoSignal(indicators, "ConditionsNotMet"));

                var lastPrimary = primaryQuotes.Last();
                var lastBB = bollingerBands.Last();
                var lastRSI = rsi.Last();

                bool breakoutLong = DetectBreakout(lastPrimary, lastBB, lastRSI, true);
                bool breakoutShort = DetectBreakout(lastPrimary, lastBB, lastRSI, false);

                //&& ValidateBreakoutCandles(primaryQuotes, true)
                if (breakoutLong)
                {
                    decimal allowedSlippage = lastPrimary.Close * m_strategyOptions.MaxSlippagePercent;
                    EntryPrice = Math.Min(Ticker.BestAskPrice, lastPrimary.Close + allowedSlippage);
                    return Task.FromResult(GenerateSignal(indicators, true, "LongBreakout"));
                }

                // && ValidateBreakoutCandles(primaryQuotes, false)
                if (breakoutShort)
                {
                    decimal allowedSlippage = lastPrimary.Close * m_strategyOptions.MaxSlippagePercent;
                    EntryPrice = Math.Max(Ticker.BestBidPrice, lastPrimary.Close - allowedSlippage);
                    return Task.FromResult(GenerateSignal(indicators, false, "ShortBreakout"));
                }
            }
            catch (Exception ex)
            {
                indicators.Add(new StrategyIndicator("Error", ex.Message));
            }

            return Task.FromResult(NoSignal(indicators, "NoBreakout"));
        }

        private bool ValidateTrendContext(
            IEnumerable<Quote> quotes,
            IEnumerable<EmaResult> emaTrend,
            IEnumerable<RsiResult> rsi,
            decimal volatility)
        {
            var lastQuote = quotes.Last();
            decimal emaValue = (decimal)emaTrend.Last().Ema;
            decimal rsiValue = (decimal)rsi.Last().Rsi;

            decimal dynamicLongThreshold = Math.Min(65, m_strategyOptions.RsiContextLongThresholdBase
                + (volatility * m_strategyOptions.RsiVolatilityFactor));

            decimal dynamicShortThreshold = Math.Max(35, m_strategyOptions.RsiContextShortThresholdBase
                - (volatility * m_strategyOptions.RsiVolatilityFactor));

            bool isUptrend = lastQuote.Close > emaValue &&
                             rsiValue > dynamicLongThreshold &&
                             emaTrend.IsRising();

            bool isDowntrend = lastQuote.Close < emaValue &&
                               rsiValue < dynamicShortThreshold &&
                               emaTrend.IsFalling();

            return isUptrend || isDowntrend;
        }

        private (IEnumerable<EmaResult>, IEnumerable<RsiResult>, decimal)
            CalculateSecondaryIndicators(IEnumerable<Quote> quotes)
        {
            var atr = quotes.GetAtr(m_strategyOptions.VolatilityPeriod);
            decimal volatility = (decimal)(atr.Last().Atr ?? 0);

            return (
                quotes.GetEma(m_strategyOptions.TrendEmaPeriod),
                quotes.GetRsi(m_strategyOptions.RsiPeriod),
                volatility
            );
        }

        private bool ValidateBreakoutCandles(IEnumerable<Quote> quotes, bool isLong)
        {
            int lookback = m_strategyOptions.BreakoutConfirmationCandles;
            var candles = quotes.TakeLast(lookback + 1).ToArray();

            if (candles.Length < lookback + 1)
                return false;

            decimal minBodySize = SymbolInfo.QtyStep.Value * 1;

            return isLong
                ? candles.Take(lookback).All(q => q.Close > q.Open + minBodySize)
                : candles.Take(lookback).All(q => q.Close < q.Open - minBodySize);
        }

        private bool DetectBreakout(Quote quote, BollingerBandsResult bb, RsiResult rsi, bool isLong)
        {
            decimal upperBand = (decimal)bb.UpperBand;
            decimal lowerBand = (decimal)bb.LowerBand;
            decimal rsiValue = (decimal)rsi.Rsi;

            return isLong
                ? quote.Close > upperBand && rsiValue > m_strategyOptions.RsiLongThreshold
                : quote.Close < lowerBand && rsiValue < m_strategyOptions.RsiShortThreshold;
        }

        private bool DetectVolumeSpike(IEnumerable<Quote> quotes, IEnumerable<SmaResult> volumeSma)
        {
            decimal lastVolume = quotes.Last().Volume;
            decimal avgVolume = (decimal)(volumeSma.Last().Sma ?? 1);
            return lastVolume > avgVolume * m_strategyOptions.VolumeSpikeMultiplier;
        }

        private bool DetectBollingerSqueeze(IEnumerable<BollingerBandsResult> bbResults)
        {
            var bbWidths = bbResults
                .TakeLast(m_strategyOptions.SqueezeLookback)
                .Select(b => (decimal)b.Width.GetValueOrDefault())
                .ToList();

            if (bbWidths.Count < 2) return false;

            decimal currentWidth = bbWidths.Last();
            decimal avgWidth = bbWidths.Average();
            decimal widthRatio = currentWidth / avgWidth;

            return widthRatio < m_strategyOptions.SqueezeStdRatioThreshold;
        }

        private (IEnumerable<BollingerBandsResult>, IEnumerable<RsiResult>, IEnumerable<AdxResult>, IEnumerable<SmaResult>)
            CalculatePrimaryIndicators(IEnumerable<Quote> quotes)
        {
            var volumeSma = quotes
                .Use(CandlePart.Volume)
                .GetSma(m_strategyOptions.VolumeLookbackPeriod);

            return (
                quotes.GetBollingerBands(m_strategyOptions.BollingerBandsPeriod, m_strategyOptions.BollingerBandsStdDev),
                quotes.GetRsi(m_strategyOptions.RsiPeriod),
                quotes.GetAdx(m_strategyOptions.AdxPeriod),
                volumeSma
            );
        }

        private SignalEvaluation GenerateSignal(List<StrategyIndicator> indicators, bool isLong, string condition)
        {
            m_lastSignalTime = DateTime.UtcNow;
            indicators.Add(new StrategyIndicator("Condition", condition));
            return new SignalEvaluation(
                isLong,
                !isLong,
                false,
                false,
                indicators.ToArray());
        }

        private SignalEvaluation NoSignal(List<StrategyIndicator> indicators, string reason)
        {
            indicators.Add(new StrategyIndicator("Condition", reason));
            if (!IsInTrade) EntryPrice = null;
            return new SignalEvaluation(false, false, false, false, indicators.ToArray());
        }

        private bool ValidateData(
            IReadOnlyList<Quote> primaryQuotes,
            IReadOnlyList<Quote> secondaryQuotes,
            List<StrategyIndicator> indicators)
        {
            bool isValid = primaryQuotes.Count >= m_strategyOptions.BollingerBandsPeriod + m_strategyOptions.SqueezeLookback
                          && secondaryQuotes.Count >= Math.Max(m_strategyOptions.TrendEmaPeriod, m_strategyOptions.RsiPeriod)
                          && Ticker != null
                          && SymbolInfo != null;

            if (!isValid) indicators.Add(new StrategyIndicator("Error", "InsufficientData"));
            return isValid;
        }

        private bool ValidateIndicators(
            IEnumerable<BollingerBandsResult> bb,
            IEnumerable<RsiResult> rsi,
            IEnumerable<AdxResult> adx,
            IEnumerable<EmaResult> ema,
            IEnumerable<RsiResult> contextRsi,
            List<StrategyIndicator> indicators)
        {
            bool isValid = bb.Any() && rsi.Any() && adx.Any() && ema.Any() && contextRsi.Any()
                           && bb.Last().UpperBand.HasValue
                           && rsi.Last().Rsi.HasValue
                           && adx.Last().Adx.HasValue
                           && ema.Last().Ema.HasValue
                           && contextRsi.Last().Rsi.HasValue;

            if (!isValid) indicators.Add(new StrategyIndicator("Error", "InvalidIndicatorValues"));
            return isValid;
        }

        private static TimeFrameWindow[] BuildTimeFrameWindows()
        {
            return new[]
            {
                new TimeFrameWindow(TimeFrame.OneMinute, 200, true),
                new TimeFrameWindow(TimeFrame.FiveMinutes, 100, false)
            };
        }
    }

    public static class EmaExtensions
    {
        public static bool IsRising(this IEnumerable<EmaResult> emaResults, int lookback = 2)
        {
            var lastValues = emaResults.TakeLast(lookback).Select(x => x.Ema).ToList();
            return lastValues.Count == lookback && lastValues[^1] > lastValues[^2];
        }

        public static bool IsFalling(this IEnumerable<EmaResult> emaResults, int lookback = 2)
        {
            var lastValues = emaResults.TakeLast(lookback).Select(x => x.Ema).ToList();
            return lastValues.Count == lookback && lastValues[^1] < lastValues[^2];
        }
    }

    public class MomentumStrategyOptions : TradingStrategyBaseOptions
    {
        // Core Parameters
        public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(1);  // Zmniejszony czas cooldown
        public decimal RiskRewardRatio { get; set; } = 1.5m;
        public decimal MaxSlippagePercent { get; set; } = 0.1m;

        // Bollinger Bands
        public int BollingerBandsPeriod { get; set; } = 10;  // Krótszy okres dla szybszej reakcji
        public double BollingerBandsStdDev { get; set; } = 1.4;  // Węższe pasma
        public int SqueezeLookback { get; set; } = 10;  // Krótszy lookback
        public decimal SqueezeStdRatioThreshold { get; set; } = 0.8m;  // Łatwiejsza detekcja squeeze

        // Volume Analysis
        public int VolumeLookbackPeriod { get; set; } = 12;
        public decimal VolumeSpikeMultiplier { get; set; } = 2.0m;  // Niższy próg spików

        // Momentum Indicators
        public int RsiPeriod { get; set; } = 5;  // Bardziej responsywny RSI
        public decimal RsiLongThreshold { get; set; } = 60m;  // Obniżony próg
        public decimal RsiShortThreshold { get; set; } = 40m;  // Podniesiony próg
        public int AdxPeriod { get; set; } = 12;
        public decimal AdxTrendThreshold { get; set; } = 25m;  // Niższy próg trendu

        // Trend Context
        public int TrendEmaPeriod { get; set; } = 15;
        public decimal RsiContextLongThresholdBase { get; set; } = 55m;  // Łagodniejsze warunki trendu
        public decimal RsiContextShortThresholdBase { get; set; } = 45m;
        public decimal RsiVolatilityFactor { get; set; } = 0.3m;  // Mniejszy wpływ zmienności

        // Risk Management
        public int VolatilityPeriod { get; set; } = 8;
        public decimal AtrMultiplierSl { get; set; } = 1.0m;  // Mniejszy SL
        public decimal AtrMultiplierTp { get; set; } = 1.5m;  // Mniejszy TP

        // Execution
        public int BreakoutConfirmationCandles { get; set; } = 1;  // Mniej świec potwierdzenia
    }
}
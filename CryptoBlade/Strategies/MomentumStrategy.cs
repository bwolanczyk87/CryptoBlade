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
using BybitNetEnums = Bybit.Net.Enums;

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
        }

        public override string Name => "Momentum";
        
        private decimal? EntryPrice 
        {
            get => IsInLongTrade ? LongPosition?.AveragePrice 
                   : IsInShortTrade ? ShortPosition?.AveragePrice 
                   : m_entryPrice;
            set => m_entryPrice = value;
        }

        protected override async Task CalculateTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            await base.CalculateTakeProfitAsync(indicators);
            CalculateDynamicStopLossAndTakeProfit();
        }

        protected override Task CalculateStopLossTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            return Task.CompletedTask;
        }

        private void CalculateDynamicStopLossAndTakeProfit()
        {
            try
            {
                if (!IsInTrade || Ticker == null || !EntryPrice.HasValue)
                    return;

                decimal currentPrice = Ticker.LastPrice;
                decimal priceDelta = Math.Abs(currentPrice - EntryPrice.Value);
                decimal minPriceMovement = SymbolInfo.QtyStep ?? 0.0001m;

                if (HasBuySignal)
                {
                    decimal dynamicSl = Math.Round(
                        EntryPrice.Value * (1 - m_strategyOptions.FixedStopLossPercentage),
                        (int)SymbolInfo.PriceScale);
                        
                    decimal dynamicTp = Math.Round(
                        EntryPrice.Value + (priceDelta * m_strategyOptions.RiskRewardRatio),
                        (int)SymbolInfo.PriceScale);

                    StopLossPrice = dynamicSl > minPriceMovement ? dynamicSl : null;
                    TakeProfitPrice = dynamicTp > currentPrice ? dynamicTp : null;
                }
                else if (HasSellSignal)
                {
                    decimal dynamicSl = Math.Round(
                        EntryPrice.Value * (1 + m_strategyOptions.FixedStopLossPercentage),
                        (int)SymbolInfo.PriceScale);
                        
                    decimal dynamicTp = Math.Round(
                        EntryPrice.Value - (priceDelta * m_strategyOptions.RiskRewardRatio),
                        (int)SymbolInfo.PriceScale);

                    StopLossPrice = dynamicSl > minPriceMovement ? dynamicSl : null;
                    TakeProfitPrice = dynamicTp < currentPrice ? dynamicTp : null;
                }
            }
            catch (Exception ex)
            {
                // Logowanie błędów
                Indicators.ToList().Add(new StrategyIndicator("Error", ex.Message));
            }
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
                var (emaTrend, contextRsi) = CalculateSecondaryIndicators(secondaryQuotes);

                if (!ValidateIndicators(bollingerBands, rsi, adx, emaTrend, contextRsi, indicators))
                    return Task.FromResult(NoSignal(indicators, "InvalidIndicators"));

                bool isSqueeze = DetectBollingerSqueeze(bollingerBands, primaryQuotes);
                bool volumeSpike = DetectVolumeSpike(primaryQuotes, volumeSma);
                bool adxValid = (decimal)(adx.Last().Adx ?? 0) >= m_strategyOptions.AdxTrendThreshold;
                bool trendContextValid = ValidateTrendContext(secondaryQuotes.Last(), emaTrend, contextRsi);

                if (!isSqueeze || !volumeSpike || !adxValid || !trendContextValid)
                    return Task.FromResult(NoSignal(indicators, "ConditionsNotMet"));

                var lastPrimary = primaryQuotes.Last();
                var lastBB = bollingerBands.Last();
                var lastRSI = rsi.Last();

                bool breakoutLong = DetectBreakout(lastPrimary, lastBB, lastRSI, true);
                bool breakoutShort = DetectBreakout(lastPrimary, lastBB, lastRSI, false);

                if (breakoutLong && ValidateBreakoutCandles(primaryQuotes, true))
                {
                    EntryPrice = Ticker?.BestAskPrice;
                    return Task.FromResult(GenerateSignal(indicators, true, "LongBreakout"));
                }
                
                if (breakoutShort && ValidateBreakoutCandles(primaryQuotes, false))
                {
                    EntryPrice = Ticker?.BestBidPrice;
                    return Task.FromResult(GenerateSignal(indicators, false, "ShortBreakout"));
                }
            }
            catch (Exception ex)
            {
                indicators.Add(new StrategyIndicator("Error", ex.Message));
            }
            
            return Task.FromResult(NoSignal(indicators, "NoBreakout"));
        }

        private bool ValidateBreakoutCandles(IEnumerable<Quote> quotes, bool isLong)
        {
            int lookback = m_strategyOptions.BreakoutConfirmationCandles;
            var candles = quotes.TakeLast(lookback + 1).ToArray();
            
            return candles.Length >= lookback + 1 && 
                   (isLong 
                       ? candles.Take(lookback).All(q => q.Close > q.Open)
                       : candles.Take(lookback).All(q => q.Close < q.Open));
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

        private bool ValidateTrendContext(Quote quote, IEnumerable<EmaResult> emaTrend, IEnumerable<RsiResult> rsi)
        {
            decimal emaValue = (decimal)emaTrend.Last().Ema;
            decimal rsiValue = (decimal)rsi.Last().Rsi;
            
            return quote.Close > emaValue 
                ? rsiValue > m_strategyOptions.RsiContextLongThreshold
                : rsiValue < m_strategyOptions.RsiContextShortThreshold;
        }

        private bool DetectVolumeSpike(IEnumerable<Quote> quotes, IEnumerable<SmaResult> volumeSma)
        {
            decimal lastVolume = quotes.Last().Volume;
            decimal avgVolume = (decimal)(volumeSma.Last().Sma ?? 1.0);
            return lastVolume > avgVolume * m_strategyOptions.VolumeSpikeMultiplier;
        }

        private bool DetectBollingerSqueeze(IEnumerable<BollingerBandsResult> bbResults, IEnumerable<Quote> quotes)
        {
            var bbWidths = bbResults
                .Select(b => (decimal)b.Width.GetValueOrDefault())
                .ToList();

            if (bbWidths.Count < m_strategyOptions.SqueezeLookback)
                return false;

            var syntheticQuotes = bbWidths
                .TakeLast(m_strategyOptions.SqueezeLookback)
                .Select((v, i) => new Quote 
                { 
                    Date = quotes.ElementAt(i).Date, 
                    Close = v 
                });

            var stdDevResults = syntheticQuotes
                .GetStdDev(m_strategyOptions.SqueezeLookback)
                .ToList();

            decimal lastWidth = bbWidths.Last();
            decimal stdDev = (decimal)(stdDevResults.Last().StdDev ?? 0.0001);
            
            return lastWidth > 0 && 
                   stdDev > 0 && 
                   (lastWidth / stdDev) < m_strategyOptions.SqueezeStdRatioThreshold;
        }

        private (IEnumerable<BollingerBandsResult>, IEnumerable<RsiResult>, IEnumerable<AdxResult>, IEnumerable<SmaResult>)
            CalculatePrimaryIndicators(IEnumerable<Quote> quotes)
        {
            return (
                quotes.GetBollingerBands(m_strategyOptions.BollingerBandsPeriod, m_strategyOptions.BollingerBandsStdDev),
                quotes.GetRsi(m_strategyOptions.RsiPeriod),
                quotes.GetAdx(m_strategyOptions.AdxPeriod),
                quotes.GetSma(m_strategyOptions.VolumeLookbackPeriod)
            );
        }

        private (IEnumerable<EmaResult>, IEnumerable<RsiResult>) 
            CalculateSecondaryIndicators(IEnumerable<Quote> quotes)
        {
            return (
                quotes.GetEma(m_strategyOptions.EmaTrendPeriod),
                quotes.GetRsi(m_strategyOptions.RsiPeriod)
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
                          && secondaryQuotes.Count >= Math.Max(m_strategyOptions.EmaTrendPeriod, m_strategyOptions.RsiPeriod)
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
}
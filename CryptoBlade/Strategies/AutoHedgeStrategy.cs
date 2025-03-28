﻿using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies
{
    public class AutoHedgeStrategy : TradingStrategyBase
    {
        private readonly IOptions<AutoHedgeStrategyOptions> m_options;
        private const int c_candlePeriod = 15;

        public AutoHedgeStrategy(IOptions<AutoHedgeStrategyOptions> options, IOptions<TradingBotOptions> botOptions,
            string symbol, IWalletManager walletManager, ICbFuturesRestClient restClient) 
            : base(options, botOptions, symbol, GetRequiredTimeFrames(), walletManager, restClient)
        {
            m_options = options;
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames()
        {
            return new[]
            {
                new TimeFrameWindow(TimeFrame.OneMinute, c_candlePeriod, true),
                new TimeFrameWindow(TimeFrame.FiveMinutes, c_candlePeriod, false),
            };
        }

        public override string Name
        {
            get { return "AutoHedge"; }
        }

        protected override decimal WalletExposureLong
        {
            get { return m_options.Value.WalletExposureLong; }
        }

        protected override decimal WalletExposureShort
        {
            get { return m_options.Value.WalletExposureShort; }
        }

        protected override int DcaOrdersCount
        {
            get { return m_options.Value.DcaOrdersCount; }
        }

        protected override bool ForceMinQty
        {
            get {return m_options.Value.ForceMinQty; }
        }

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var quotes = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            List<StrategyIndicator> indicators = new();
            var lastQuote = quotes.LastOrDefault();
            bool hasBuySignal = false;
            bool hasSellSignal = false;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;
            
            if (lastQuote != null)
            {
                bool canBeTraded = (lastQuote.Date - SymbolInfo.LaunchTime).TotalDays > m_options.Value.InitialUntradableDays;
                var spread5Min = TradeSignalHelpers.Get5MinSpread(quotes);
                var volume = TradeSignalHelpers.VolumeInQuoteCurrency(lastQuote);
                var sma = quotes.GetSma(14);
                var lastSma = sma.LastOrDefault();
                var trendPercent = TradeSignalHelpers.GetTrendPercent(lastSma, lastQuote);
                var trend = TradeSignalHelpers.GetTrend(trendPercent);
                var ma3High = quotes.Use(CandlePart.High).GetSma(3);
                var ma3Low = quotes.Use(CandlePart.Low).GetSma(3);
                var ma6High = quotes.Use(CandlePart.High).GetSma(6);
                var ma6Low = quotes.Use(CandlePart.Low).GetSma(6);
                
                var ma3HighLast = ma3High.LastOrDefault();
                var ma3LowLast = ma3Low.LastOrDefault();
                var ma6HighLast = ma6High.LastOrDefault();
                var ma6LowLast = ma6Low.LastOrDefault();

                bool hasAllRequiredMa = ma6HighLast != null 
                    && ma6HighLast.Sma.HasValue
                    && ma6LowLast != null
                    && ma6LowLast.Sma.HasValue
                    && ma3HighLast != null
                    && ma3HighLast.Sma.HasValue
                    && ma3LowLast != null
                    && ma3LowLast.Sma.HasValue;

                var ticker = Ticker;

                bool hasMinSpread = spread5Min > m_options.Value.MinimumPriceDistance;
                bool hasMinVolume = volume >= m_options.Value.MinimumVolume;
                bool shouldShort = false;
                bool shouldLong = false;
                bool shouldAddToShort = false;
                bool shouldAddToLong = false;
                bool hasMinShortPriceDistance = false;
                bool hasMinLongPriceDistance = false;
                if (ticker != null 
                    && ma3HighLast != null && ma3HighLast.Sma.HasValue
                    && ma3LowLast != null && ma3LowLast.Sma.HasValue
                    && ma6HighLast != null && ma6HighLast.Sma.HasValue
                    && ma6LowLast != null && ma6LowLast.Sma.HasValue)
                {
                    shouldShort = TradeSignalHelpers.ShortCounterTradeCondition(ticker.BestAskPrice, (decimal)ma3HighLast.Sma!.Value);
                    shouldLong = TradeSignalHelpers.LongCounterTradeCondition(ticker.BestBidPrice, (decimal)ma3LowLast.Sma!.Value);
                    shouldAddToShort = TradeSignalHelpers.ShortCounterTradeCondition(ticker.BestAskPrice, (decimal)ma6HighLast.Sma!.Value);
                    shouldAddToLong = TradeSignalHelpers.LongCounterTradeCondition(ticker.BestBidPrice, (decimal)ma6LowLast.Sma!.Value);
                }

                Position? longPosition = LongPosition;
                Position? shortPosition = ShortPosition;
                hasBuySignal = hasMinVolume
                               && shouldLong
                               && hasAllRequiredMa
                               && trend == Trend.Long
                               && hasMinSpread
                               && canBeTraded;

                hasSellSignal = hasMinVolume 
                                && shouldShort 
                                && hasAllRequiredMa 
                                && trend == Trend.Short 
                                && hasMinSpread
                                && canBeTraded;

                if (ticker != null && longPosition != null)
                {
                    var rebuyPrice = longPosition.AveragePrice * (1.0m - m_options.Value.MinReentryPositionDistanceLong);
                    if (ticker.BestBidPrice < rebuyPrice)
                        hasMinLongPriceDistance = hasBuySignal;
                }

                if (ticker != null && shortPosition != null)
                {
                    var resellPrice = shortPosition.AveragePrice * (1.0m + m_options.Value.MinReentryPositionDistanceShort);
                    if (ticker.BestAskPrice > resellPrice)
                        hasMinShortPriceDistance = hasSellSignal;
                }

                hasBuyExtraSignal = hasMinVolume 
                                    && shouldAddToLong 
                                    && hasAllRequiredMa 
                                    && trend == Trend.Long 
                                    && hasMinSpread 
                                    && longPosition != null
                                    && ticker != null
                                    && ticker.BestBidPrice < longPosition.AveragePrice
                                    && hasMinLongPriceDistance
                                    && canBeTraded;

                hasSellExtraSignal = hasMinVolume 
                                     && shouldAddToShort 
                                     && hasAllRequiredMa 
                                     && trend == Trend.Short 
                                     && hasMinSpread
                                     && shortPosition != null
                                     && ticker != null
                                     && ticker.BestAskPrice > shortPosition.AveragePrice 
                                     && hasMinShortPriceDistance
                                     && canBeTraded;

                if (hasAllRequiredMa)
                {
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma3High), ma3HighLast!.Sma!.Value));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma3Low), ma3LowLast!.Sma!.Value));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma6High), ma6HighLast!.Sma!.Value));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Ma6Low), ma6LowLast!.Sma!.Value));
                }

                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Volume1Min), volume));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.MainTimeFrameVolume), volume));
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.Spread5Min), spread5Min));
                if (trendPercent != null)
                {
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.TrendPercent), trendPercent));
                    indicators.Add(new StrategyIndicator(nameof(IndicatorType.Trend), trend));
                }
            }

            return Task.FromResult(new SignalEvaluation(hasBuySignal, hasSellSignal, hasBuyExtraSignal, hasSellExtraSignal, indicators.ToArray()));
        }
    }
}

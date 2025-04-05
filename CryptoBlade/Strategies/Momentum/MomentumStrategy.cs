using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Momentum
{
    public class MomentumStrategy(
        IOptions<MomentumStrategyOptions> options,
        IOptions<TradingBotOptions> botOptions,
        string symbol,
        IWalletManager walletManager,
        ICbFuturesRestClient restClient) : TradingStrategyBase(
            options,
            botOptions,
            symbol,
            BuildTimeFrameWindows(options.Value),
            walletManager,
            restClient)
    {
        private readonly IOptions<MomentumStrategyOptions> m_options = options;
        public override string Name => "Momentum";
        protected override decimal WalletExposureLong => m_options.Value.WalletExposureLong;
        protected override decimal WalletExposureShort => m_options.Value.WalletExposureShort;
        protected override int DcaOrdersCount => m_options.Value.DcaOrdersCount;
        protected override bool ForceMinQty => m_options.Value.ForceMinQty;

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var opts = m_options.Value;
            var primaryQuotes = QuoteQueues[opts.PrimaryTimeFrame].GetQuotes();
            var secondaryQuotes = opts.UseSecondaryTimeFrameFilter
                ? QuoteQueues[opts.SecondaryTimeFrame].GetQuotes()
                : [];

            if (primaryQuotes.Length == 0)
            {
                return Task.FromResult(new SignalEvaluation(false, false, false, false, []));
            }

            var macdList = primaryQuotes.GetMacd(
                opts.MacdFastPeriod,
                opts.MacdSlowPeriod,
                opts.MacdSignalPeriod);
            var macd = macdList.LastOrDefault();
            var rsiList = primaryQuotes.GetRsi(opts.RsiPeriod);
            var rsi = rsiList.LastOrDefault();

            bool isBullishTrendSecondary = false;
            bool isBearishTrendSecondary = false;
            if (opts.UseSecondaryTimeFrameFilter && secondaryQuotes.Length > 0)
            {
                var macdSecList = secondaryQuotes.GetMacd(
                    opts.MacdFastPeriod,
                    opts.MacdSlowPeriod,
                    opts.MacdSignalPeriod);

                var macdSec = macdSecList.LastOrDefault();
                if (macdSec != null)
                {
                    isBullishTrendSecondary = macdSec.Histogram > 0 && macdSec.Macd > macdSec.Signal;
                    isBearishTrendSecondary = macdSec.Histogram < 0 && macdSec.Macd < macdSec.Signal;
                }
            }

            bool hasBuySignal = false;
            bool hasSellSignal = false;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;

            if (macd != null && rsi != null)
            {
                if (macd.Histogram > 0 &&
                    macd.Macd > macd.Signal &&
                    (decimal)rsi.Rsi < opts.RsiUpperThreshold &&
                    (!opts.UseSecondaryTimeFrameFilter || isBullishTrendSecondary))
                {
                    hasBuySignal = true;
                }

                if (macd.Histogram < 0 &&
                    macd.Macd < macd.Signal &&
                    (decimal)rsi.Rsi > opts.RsiLowerThreshold &&
                    (!opts.UseSecondaryTimeFrameFilter || isBearishTrendSecondary))
                {
                    hasSellSignal = true;
                }
            }

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

            var indicators = new StrategyIndicator[]
            {
                new(IndicatorType.Macd.ToString(), macd?.Macd ?? 0),
                new(IndicatorType.MacdSignal.ToString(), macd?.Signal ?? 0),
                new(IndicatorType.MacdHistogram.ToString(), macd?.Histogram ?? 0),
                new(IndicatorType.Rsi.ToString(), rsi?.Rsi ?? 0),
        };

        var evaluation = new SignalEvaluation(
            hasBuySignal,
            hasSellSignal,
            hasBuyExtraSignal,
            hasSellExtraSignal,
            indicators);

        return Task.FromResult(evaluation);
        }

        private static TimeFrameWindow[] BuildTimeFrameWindows(MomentumStrategyOptions opts)
        {
            if (opts.UseSecondaryTimeFrameFilter)
            {
                return
                [
                    new TimeFrameWindow(opts.PrimaryTimeFrame, opts.PrimaryTimeFrameWindowSize, true),
                    new TimeFrameWindow(opts.SecondaryTimeFrame, opts.SecondaryTimeFrameWindowSize, false)
                ];
            }

            return
            [
                new TimeFrameWindow(opts.PrimaryTimeFrame, opts.PrimaryTimeFrameWindowSize, true)
            ];
        }
    }
}

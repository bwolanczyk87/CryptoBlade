using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Sigma.Regimes;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Strategies.Sigma
{
    public class SigmaStrategy : TradingStrategyBase
    {
        private readonly IOptions<SigmaStrategyOptions> _options;
        private readonly ISigmaDataProvider _data;

        private DateTime _lastRegimeDecisionUtc = DateTime.MinValue;
        private RegimeState _regimeState = new(Regime.None, DateTime.MinValue, RegimeScores.Zero);

        protected override bool UseMarketOrdersForEntries => false;

        public SigmaStrategy(
            IOptions<SigmaStrategyOptions> options,
            IOptions<TradingBotOptions> botOptions,
            string symbol,
            IWalletManager walletManager,
            ICbFuturesRestClient restClient)
            : base(options, botOptions, symbol, GetRequiredTimeFrames(options.Value), walletManager, restClient)
        {
            _options = options;
            _data = new BybitSigmaDataProvider(restClient);
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames(SigmaStrategyOptions o)
            => new[]
            {
                new TimeFrameWindow(TimeFrame.OneMinute,       o.OneMinuteWindow,  true),
                new TimeFrameWindow(TimeFrame.FiveMinutes,     o.FiveMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.FifteenMinutes,  o.FifteenMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.OneHour,         o.OneHourWindow, false),
            };

        public override string Name => "Sigma";

        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var indicators = new List<StrategyIndicator>();

            var ticker = Ticker;
            var quotes1m = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            var quotes5m = QuoteQueues[TimeFrame.FiveMinutes].GetQuotes();
            var quotes15m = QuoteQueues[TimeFrame.FifteenMinutes].GetQuotes();
            var quotes1h = QuoteQueues[TimeFrame.OneHour].GetQuotes();

            if (ticker == null || quotes1m.Length == 0 || quotes5m.Length == 0 || quotes15m.Length == 0 || quotes1h.Length == 0)
                return new SignalEvaluation(false, false, false, false, []);

            indicators.Add(new StrategyIndicator(nameof(IndicatorType.MainTimeFrameVolume),
                TradeSignalHelpers.VolumeInQuoteCurrency(quotes1m[^1])));

            // 1) Cechy
            var f = await FeatureSnapshot.BuildAsync(Symbol, quotes1m, quotes5m, quotes15m, quotes1h, ticker, _data, cancel);


            indicators.Add(new StrategyIndicator("Sigma.Spread.Bps", (decimal)Math.Round(f.SpreadBps, 4)));
            indicators.Add(new StrategyIndicator("Sigma.ATR1h.Pct", (decimal)Math.Round(f.AtrPct1h, 4)));

            // 2) Global gates + scoring na KAŻDYM wywołaniu
            var nowUtc = DateTime.UtcNow;
            var eval = RegimeEngine.Evaluate(f, _regimeState, nowUtc, _options.Value);

            // Aktualizuj wskaźniki score'ów ZAWSZE (telemetria świeża)
            indicators.Add(new StrategyIndicator("MM.Score", (decimal)eval.Decision.State.Scores.Momentum));
            indicators.Add(new StrategyIndicator("MR.Score", (decimal)eval.Decision.State.Scores.MeanReversion));
            indicators.Add(new StrategyIndicator("BO.Score", (decimal)eval.Decision.State.Scores.Breakout));

            // 3) Przełączenie reżimu tylko co RecalcMinutes (lepkość)
            bool timeToDecide = (nowUtc - _lastRegimeDecisionUtc) >= TimeSpan.FromMinutes(_options.Value.RecalcMinutes);
            if (timeToDecide)
            {
                _regimeState = eval.Decision.State;
                _lastRegimeDecisionUtc = nowUtc;
            }

            indicators.Add(new StrategyIndicator("Active", _regimeState.Mode.ToString()));

            if (!eval.Tradable)
            {
                indicators.Add(new StrategyIndicator("NoTrade.Reason", eval.Reason));
                Indicators = [.. indicators];
                return new SignalEvaluation(false, false, false, false, Indicators);
            }

            // 4) Tryby
            ModeDecision decision = _regimeState.Mode switch
            {
                Regime.Momentum => MomentumController.Evaluate(f, _options.Value),
                Regime.MeanReversion => MeanReversionController.Evaluate(f, _options.Value),
                Regime.Breakout => BreakoutController.Evaluate(f, _options.Value),
                _ => ModeDecision.None
            };

            Indicators = [.. indicators];
            return new SignalEvaluation(
                decision.HasBuy,
                decision.HasSell,
                decision.HasBuyExtra,
                decision.HasSellExtra,
                Indicators);
        }
    }
}

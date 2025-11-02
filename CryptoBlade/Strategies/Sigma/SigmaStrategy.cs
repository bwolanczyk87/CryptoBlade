using System;
using System.Collections.Generic;
using System.Linq;
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
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma
{
    public class SigmaStrategy : TradingStrategyBase
    {
        private readonly IOptions<SigmaStrategyOptions> _options;
        private readonly ISigmaDataProvider _data;

        private DateTime _lastRegimeDecisionUtc = DateTime.MinValue;
        private RegimeState _regimeState = new(Regime.None, DateTime.MinValue, RegimeScores.Zero);

        protected override bool UseMarketOrdersForEntries => true;

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
        {
            return
            [
                new TimeFrameWindow(TimeFrame.OneMinute, o.OneMinuteWindow,  true),
                new TimeFrameWindow(TimeFrame.FiveMinutes, o.FiveMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.FifteenMinutes, o.FifteenMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.OneHour, o.OneHourWindow, false),
            ];
        }

        public override string Name => "Sigma";

        // --- Główna ocena sygnałów: global gates -> scoring -> decyzja reżimu -> tryby ---
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

            // Telemetria wolumenu (przykładowa)
            indicators.Add(new StrategyIndicator(nameof(IndicatorType.MainTimeFrameVolume),
                TradeSignalHelpers.VolumeInQuoteCurrency(quotes1m.Last())));

            // 1) Zbuduj cechy (w tym SpreadBps z providera)
            var features = await FeatureSnapshot.BuildAsync(Symbol, quotes1m, quotes5m, quotes15m, quotes1h, ticker, _data, cancel);

            // 1a) Jeśli mamy realny bid-ask w tickerze, nadpisz/uzupełnij SpreadBps lokalnie (bardziej wiarygodne)
            if (ticker.BestAskPrice > 0 && ticker.BestBidPrice > 0 && ticker.LastPrice > 0)
            {
                var localSpread = ticker.BestAskPrice - ticker.BestBidPrice;
                var localBps = (double)((localSpread / ticker.LastPrice) * 10_000m);
                if (localBps > 0) features.GetType().GetProperty(nameof(FeatureSnapshot.SpreadBps))!
                    .SetValue(features, localBps);
            }

            // Telemetria kosztów i zmienności
            indicators.Add(new StrategyIndicator("Sigma.Spread.Bps", (decimal)Math.Round(features.SpreadBps, 4)));
            if (ticker.BestAskPrice > 0 && ticker.BestBidPrice > 0)
                indicators.Add(new StrategyIndicator("Sigma.Spread.Raw", Math.Round(ticker.BestAskPrice - ticker.BestBidPrice, 8)));
            indicators.Add(new StrategyIndicator("Sigma.ATR1h.Pct", (decimal)Math.Round(features.AtrPct1h, 4)));

            // 2) Decyzja reżimu – tylko co RecalcMinutes (lepkość), ale globalne bramki sprawdzamy zawsze
            var nowUtc = DateTime.UtcNow;

            // Global gates + scoring + decide
            (bool tradable, string reason, (bool Changed, RegimeState State) decision) eval;

            bool timeToDecide = (nowUtc - _lastRegimeDecisionUtc) >= TimeSpan.FromMinutes(_options.Value.RecalcMinutes);
            if (timeToDecide)
            {
                eval = RegimeEngine.Evaluate(features, _regimeState, nowUtc, _options.Value);
                _lastRegimeDecisionUtc = nowUtc;

                // Uaktualnij stan reżimu (zmiana lub brak)
                _regimeState = eval.decision.State;

                // Telemetria score'ów przy decyzji
                indicators.Add(new StrategyIndicator("MM.Score", (decimal)_regimeState.Scores.Momentum));
                indicators.Add(new StrategyIndicator("MR.Score", (decimal)_regimeState.Scores.MeanReversion));
                indicators.Add(new StrategyIndicator("BO.Score", (decimal)_regimeState.Scores.Breakout));
            }
            else
            {
                // Nie czas na przełączenie – ale globalne gate'y wciąż obowiązują
                eval = RegimeEngine.Evaluate(features, _regimeState, _regimeState.SinceUtc, _options.Value);
                // Stan pozostaje jak był (brak odświeżenia dwell)
            }

            indicators.Add(new StrategyIndicator("Active", _regimeState.Mode.ToString()));

            if (!eval.tradable)
            {
                indicators.Add(new StrategyIndicator("NoTrade.Reason", eval.reason));
                Indicators = [.. indicators];
                return new SignalEvaluation(false, false, false, false, Indicators);
            }

            // 3) Wywołaj kontroler trybu (wydmuszki) – tylko gdy aktywny reżim != None
            ModeDecision decision = _regimeState.Mode switch
            {
                Regime.Momentum => MomentumController.Evaluate(features, _options.Value),
                Regime.MeanReversion => MeanReversionController.Evaluate(features, _options.Value),
                Regime.Breakout => BreakoutController.Evaluate(features, _options.Value),
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

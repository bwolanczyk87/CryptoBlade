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
    public sealed class SigmaStrategy : TradingStrategyBase
    {
        private readonly IOptions<SigmaStrategyOptions> _options;
        private readonly ISigmaDataProvider _data;
        private DateTime _lastRegimeDecisionUtc = DateTime.MinValue;
        private RegimeState _regimeState = new(Regime.None, DateTime.MinValue, new RegimeScores());
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
            _data = new BybitSigmaDataProvider(restClient); // adapter danych (wydmuszka/ToDo)
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames(SigmaStrategyOptions o)
        {
            // Wzorowane na Mona/LinearRegression: główny TF 1m + pomocnicze 5m/15m oraz 1h
            return new[]
            {
                new TimeFrameWindow(TimeFrame.OneMinute, o.OneMinuteWindow,  true),
                new TimeFrameWindow(TimeFrame.FiveMinutes, o.FiveMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.FifteenMinutes, o.FifteenMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.OneHour, o.OneHourWindow, false),
            };
        }

        public override string Name => "Sigma";

        // --- Kluczowe: ocena sygnałów. Tu sterujemy reżimami i odpalamy odpowiednie kontrolery (wydmuszki). ---
        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            // 0) Zbierz dane lokalne z kolejek i tickera
            var ticker = Ticker;
            var quotes1m = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            var quotes5m = QuoteQueues[TimeFrame.FiveMinutes].GetQuotes();
            var quotes15m = QuoteQueues[TimeFrame.FifteenMinutes].GetQuotes();
            var quotes1h = QuoteQueues[TimeFrame.OneHour].GetQuotes();

            List<StrategyIndicator> indicators = new();
            bool hasBuy = false, hasSell = false, hasBuyExtra = false, hasSellExtra = false;

            if (ticker == null || quotes1m.Length == 0 || quotes5m.Length == 0 || quotes15m.Length == 0 || quotes1h.Length == 0)
            {
                Indicators = indicators.ToArray();
                return new SignalEvaluation(hasBuy, hasSell, hasBuyExtra, hasSellExtra, Indicators);
            }

            // 1) Filtry jakości (spread, ATR%_1H)
            var spread5Min = TradeSignalHelpers.Get5MinSpread(quotes1m); // wzór jak w innych strategiach
            var lastAtr1h = quotes1h.GetAtr(14).LastOrDefault()?.Atr;
            double? atrPct1h = (lastAtr1h.HasValue && ticker.BestAskPrice > 0)
                ? (double)(lastAtr1h.Value / (double)ticker.BestAskPrice) * 100.0
                : null;

            bool spreadOk = (spread5Min / ticker.LastPrice) * 10000m <= _options.Value.MaxSpreadBps;
            bool atrOk = atrPct1h.HasValue &&
                         atrPct1h.Value >= (double)_options.Value.MinAtr1hPct &&
                         atrPct1h.Value <= (double)_options.Value.MaxAtr1hPct;

            indicators.Add(new StrategyIndicator(nameof(IndicatorType.MainTimeFrameVolume),
                TradeSignalHelpers.VolumeInQuoteCurrency(quotes1m.Last())));
            if (atrPct1h.HasValue)
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.NormalizedAverageTrueRange),
                    (decimal)Math.Round(atrPct1h.Value, 6)));

            if (!spreadOk || !atrOk)
            {
                Indicators = indicators.ToArray();
                return new SignalEvaluation(false, false, false, false, Indicators);
            }

            // 2) Feature snapshot (pierwotne cechy + placeholdery ΔOI, funding, basis, CVD, liq)
            var features = await FeatureSnapshot.BuildAsync(Symbol, quotes1m, quotes5m, quotes15m, quotes1h, ticker, _data, cancel);

            // 3) Decyzja reżimu (co 5m, z histerezą/persistencją)
            var nowUtc = DateTime.UtcNow;
            bool timeToDecide = (nowUtc - _lastRegimeDecisionUtc) >= TimeSpan.FromMinutes(_options.Value.RecalcMinutes);

            if (timeToDecide)
            {
                var scores = RegimeClassifier.Score(features, _options.Value);
                var decided = RegimeClassifier.Decide(scores, _regimeState, nowUtc, _options.Value);
                if (decided.Changed)
                    _regimeState = decided.NewState;
                _lastRegimeDecisionUtc = nowUtc;

                indicators.Add(new StrategyIndicator("MM.Score", (decimal)scores.Momentum));
                indicators.Add(new StrategyIndicator("MR.Score", (decimal)scores.MeanReversion));
                indicators.Add(new StrategyIndicator("BO.Score", (decimal)scores.Breakout));
                indicators.Add(new StrategyIndicator("Active", _regimeState.Mode.ToString()));
            }

            // 4) Wywołanie kontrolera trybu (wydmuszki – tylko interfejsy i zwrot flag)
            ModeDecision decision = _regimeState.Mode switch
            {
                Regime.Momentum => MomentumController.Evaluate(features, _options.Value),
                Regime.MeanReversion => MeanReversionController.Evaluate(features, _options.Value),
                Regime.Breakout => BreakoutController.Evaluate(features, _options.Value),
                _ => ModeDecision.None
            };

            hasBuy = decision.HasBuy;
            hasSell = decision.HasSell;
            hasBuyExtra = decision.HasBuyExtra;
            hasSellExtra = decision.HasSellExtra;

            // 5) Zapisz wskaźniki i wyjdź
            Indicators = indicators.ToArray();
            return new SignalEvaluation(hasBuy, hasSell, hasBuyExtra, hasSellExtra, Indicators);
        }

        // Wartości z TradingStrategyBaseOptions (wallet exposure, DCA, ForceMinQty) są już obsługiwane w bazie. :contentReference[oaicite:3]{index=3}
    }

}

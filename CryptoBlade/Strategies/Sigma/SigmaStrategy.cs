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

        // --- Kluczowe: ocena sygnałów. Tu sterujemy reżimami i odpalamy odpowiednie kontrolery (wydmuszki). ---
        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            List<StrategyIndicator> indicators = [];
            var ticker = Ticker;
            var quotes1m = QuoteQueues[TimeFrame.OneMinute].GetQuotes();
            var quotes5m = QuoteQueues[TimeFrame.FiveMinutes].GetQuotes();
            var quotes15m = QuoteQueues[TimeFrame.FifteenMinutes].GetQuotes();
            var quotes1h = QuoteQueues[TimeFrame.OneHour].GetQuotes();

            if (ticker == null || quotes1m.Length == 0 || quotes5m.Length == 0 || quotes15m.Length == 0 || quotes1h.Length == 0)
            {
                return new SignalEvaluation(false, false, false, false, [.. indicators]);
            }

            // 1) Filtry jakości (spread, ATR%_1H)
            var fallbackSpread5m = TradeSignalHelpers.Get5MinSpread(quotes1m); // tylko fallback/telemetria
            var (spreadOk, spreadBps, rawSpread) = CheckSpreadOk(ticker, _options.Value, fallbackSpread5m);
            var (atrOk, atrPct1h, atrAbs1h) = CheckAtrOk(quotes1h, ticker, _options.Value);

            // Telemetria/indikatory
            indicators.Add(new StrategyIndicator(nameof(IndicatorType.MainTimeFrameVolume),
                TradeSignalHelpers.VolumeInQuoteCurrency(quotes1m.Last())));

            indicators.Add(new StrategyIndicator("Sigma.Spread.Bps", Math.Round(spreadBps, 4)));
            if (rawSpread > 0)
                indicators.Add(new StrategyIndicator("Sigma.Spread.Raw", Math.Round(rawSpread, 8)));

            if (atrPct1h.HasValue)
                indicators.Add(new StrategyIndicator(nameof(IndicatorType.NormalizedAverageTrueRange),
                    (decimal)Math.Round(atrPct1h.Value, 6)));
            if (atrAbs1h.HasValue)
                indicators.Add(new StrategyIndicator("Sigma.ATR1h.Abs", Math.Round(atrAbs1h.Value, 8)));

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

            return new SignalEvaluation(
                decision.HasBuy, 
                decision.HasSell, 
                decision.HasBuyExtra, 
                decision.HasSellExtra,
                [.. indicators]);
        }

        // === Helpers: Spread & ATR gates ===
        private static (bool ok, decimal spreadBps, decimal rawSpread) CheckSpreadOk(
            Ticker ticker,
            SigmaStrategyOptions opt,
            decimal? fallbackSpread = null)
        {
            // Priorytet: realny bid-ask z tickera
            if (ticker == null || ticker.LastPrice <= 0 || ticker.BestAskPrice <= 0 || ticker.BestBidPrice <= 0)
            {
                // Fallback: użyj preliczonego 5m spreadu jeśli podany
                if (fallbackSpread is null || fallbackSpread <= 0)
                    return (false, 0m, 0m);

                var fbBps = (fallbackSpread.Value / (ticker?.LastPrice ?? 1m)) * 10_000m;
                return (fbBps <= opt.MaxSpreadBps, fbBps, fallbackSpread.Value);
            }

            var spread = ticker.BestAskPrice - ticker.BestBidPrice;
            var spreadBps = (spread / ticker.LastPrice) * 10_000m;
            return (spreadBps <= opt.MaxSpreadBps, spreadBps, spread);
        }

        private static (bool ok, double? atrPct1h, decimal? atrAbs) CheckAtrOk(
            Quote[] quotes1h,
            Ticker ticker,
            SigmaStrategyOptions opt)
        {
            if (quotes1h == null || quotes1h.Length == 0)
                return (false, null, null);

            var atrRes = quotes1h.GetAtr(14).LastOrDefault();
            if (atrRes?.Atr is null || atrRes.Atr <= 0)
                return (false, null, null);

            // Odniesienie do ceny: preferuj ticker, fallback na last close 1h
            var refPx = ticker?.BestAskPrice > 0 ? ticker.BestAskPrice : quotes1h.Last().Close;
            if (refPx <= 0) return (false, null, (decimal)atrRes.Atr);

            var atrPct = (double)((decimal)atrRes.Atr / refPx) * 100.0;
            var ok = atrPct >= (double)opt.MinAtr1hPct && atrPct <= (double)opt.MaxAtr1hPct;
            return (ok, atrPct, (decimal)atrRes.Atr);
        }

    }
}

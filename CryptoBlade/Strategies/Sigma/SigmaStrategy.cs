using System;
using System.Collections.Generic;
using System.IO;
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
    public class SigmaStrategyOptions : TradingStrategyBaseOptions
    {
        // Okna buforów
        public int RecalcMinutes { get; init; } = 5;
        public int HysteresisLockMinutes { get; init; } = 30;
        public int OneMinuteWindow { get; init; } = 500;
        public int FiveMinuteWindow { get; init; } = 200;
        public int FifteenMinuteWindow { get; init; } = 200;
        public int OneHourWindow { get; init; } = 200;

        // Progi reżimów
        public decimal AdxEnableMomentum { get; init; } = 22m;
        public decimal AdxDisableMomentum { get; init; } = 18m;
        public decimal BbWidthBreakoutPct { get; init; } = 30m;
        public decimal BbWidthExitBreakoutPct { get; init; } = 45m;
        public decimal ZVwapEnableMR { get; init; } = 1.8m;
        public decimal ZVwapExitMR { get; init; } = 1.0m;
        public decimal MinScore { get; init; } = 45m;
        public decimal MinMargin { get; init; } = 10m;

        // Globalne gate’y
        public decimal MaxSpreadBps { get; init; } = 2m;

        // ATR gates per-mode
        public decimal MmAtrMinPct { get; init; } = 1.2m;
        public decimal MmAtrMaxPct { get; init; } = 4.0m;
        public decimal MrAtrMinPct { get; init; } = 1.0m;
        public decimal MrAtrMaxPct { get; init; } = 3.5m;
        public decimal BoAtrMaxPct { get; init; } = 7.0m;

        // Egzekucja
        public decimal DefaultQuoteSize { get; init; } = 500m;
        public decimal MinAtr5mFloor { get; init; } = 0.5m;

        // Makro
        public int MacroFreezeMinutesBefore { get; init; } = 10;
        public int MacroFreezeMinutesAfter { get; init; } = 30;
        public IReadOnlyList<DateTime> MacroEventsUtc { get; init; } =
        [
            new DateTime(2025, 12, 05, 13, 30, 00, DateTimeKind.Utc), // NFP
            new DateTime(2025, 12, 10, 13, 30, 00, DateTimeKind.Utc), // CPI
            new DateTime(2025, 12, 10, 19, 00, 00, DateTimeKind.Utc), // FOMC statement
            new DateTime(2025, 12, 18, 13, 15, 00, DateTimeKind.Utc), // ECB
        ];

        // Founding Rate
        public int FundingFreezeMinutesBefore { get; init; } = 3;   // freeze ±3 min wokół cyklu
        public int FundingFreezeMinutesAfter { get; init; } = 1;
        public decimal CorrOppositeBlock { get; init; } = 0.85m;
    }

    public class SigmaStrategy : TradingStrategyBase
    {
        private readonly IOptions<SigmaStrategyOptions> _options;
        private readonly BybitSigmaDataProvider _data;
        private readonly IModeController _mm;
        private readonly IModeController _mr;
        private readonly IModeController _bo;
        private readonly IRegimeAuditSink _audit;

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
            _mm = new MomentumController(options.Value);
            _mr = new MeanReversionController();
            _bo = new BreakoutController();

            var relDir = Path.Combine("Data", "Strategies", "Sigma", "Audit", symbol);
            var relFile = Path.Combine(relDir, $"regime_audit_{DateTime.UtcNow:yyyyMMdd}.csv");
            _audit = new RegimeAuditSink(relFile);
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames(SigmaStrategyOptions o)
            =>
            [
                new TimeFrameWindow(TimeFrame.OneMinute,       o.OneMinuteWindow,  true),
                new TimeFrameWindow(TimeFrame.FiveMinutes,     o.FiveMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.FifteenMinutes,  o.FifteenMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.OneHour,         o.OneHourWindow,    false),
            ];

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

            // 1) Features
            var f = await FeatureSnapshot.BuildAsync(
                Symbol, quotes1m, quotes5m, quotes15m, quotes1h,
                ticker, _data, cancel, sessionStartHourUtc: 0, vwapSlopeWindow: 60);

            indicators.Add(new StrategyIndicator("Sigma.Spread.Bps", (decimal)Math.Round(f.SpreadBps, 4)));
            indicators.Add(new StrategyIndicator("Sigma.ATR1h.Pct", (decimal)Math.Round(f.AtrPct1h, 4)));

            var nowUtc = DateTime.UtcNow;
            bool isTimeToDecide = (nowUtc - _lastRegimeDecisionUtc) >= TimeSpan.FromMinutes(_options.Value.RecalcMinutes);

            // 2) Klasyfikacja reżimu (bez handlu; RegimeEngine nie dotyka trade’ów)
            var decision = RegimeEngine.Classify(f, _regimeState, nowUtc, _options.Value, _audit);

            if (isTimeToDecide)
            {
                // aktualizujemy aktywny reżim zgodnie z histerezą/dwell
                _regimeState = decision.State;
                _lastRegimeDecisionUtc = nowUtc;
            }
            // jeśli nie jest czas na decyzję, _regimeState zostaje bez zmian,
            // ale decision niesie Proposed/Score/Margin do audytu i wskaźników

            // 3) Globalne gate’y (poza RegimeEngine)
            var (tradable, reason) = GlobalGates.Evaluate(f, nowUtc, _options.Value);

            // 4) Szybka egzekucja kontrolera aktywnego reżimu (co 1m)
            ModeDecision tradeDecision = ModeDecision.None;
            if (tradable)
            {
                var ctrl = _regimeState.Mode switch
                {
                    Regime.MM => _mm,
                    Regime.MR => _mr,
                    Regime.BO => _bo,
                    _ => null
                };
                if (ctrl != null) tradeDecision = ctrl.Evaluate(f, nowUtc, cancel);
            }

            _audit.Add(RegimeAudit.MakeRecord(
                f, _regimeState, nowUtc, _options.Value,
                tradable: tradable,            // wynik z GlobalGates
                reason: reason,              // wynik z GlobalGates
                decision: decision,            // pełny RegimeDecision
                lastDecisionUtc: _lastRegimeDecisionUtc
            ));

            // 5) Telemetria
            indicators.Add(new StrategyIndicator("MM.Score", (decimal)decision.State.Scores.Momentum));
            indicators.Add(new StrategyIndicator("MR.Score", (decimal)decision.State.Scores.MeanReversion));
            indicators.Add(new StrategyIndicator("BO.Score", (decimal)decision.State.Scores.Breakout));
            indicators.Add(new StrategyIndicator("Regime.Active", _regimeState.Mode.ToString()));
            indicators.Add(new StrategyIndicator("Regime.ActiveSinceUtc", _regimeState.SinceUtc));
            indicators.Add(new StrategyIndicator("Regime.Proposed", decision.ProposedMode.ToString()));
            indicators.Add(new StrategyIndicator("Regime.ProposedScore", (decimal)decision.ProposedScore));
            indicators.Add(new StrategyIndicator("Regime.Margin", (decimal)decision.Margin));
            indicators.Add(new StrategyIndicator("Regime.Tradable", tradable.ToString()));
            indicators.Add(new StrategyIndicator("Regime.Reason", reason));
            indicators.Add(new StrategyIndicator("Regime.LastDecisionUtc", _lastRegimeDecisionUtc));

            Indicators = [.. indicators];

            var d = tradeDecision;
            return new SignalEvaluation(d.HasBuy, d.HasSell, d.HasBuyExtra, d.HasSellExtra, Indicators);
        }
    }
}

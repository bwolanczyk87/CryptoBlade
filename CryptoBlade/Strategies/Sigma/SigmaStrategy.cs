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
        // Okna buforów (informacyjne; zarządza tym warstwa danych)
        public int RecalcMinutes { get; init; } = 5;          // decyzja co 5m
        public int HysteresisLockMinutes { get; init; } = 30; // minimalny dwell reżimu
        public int OneMinuteWindow { get; init; } = 500;
        public int FiveMinuteWindow { get; init; } = 200;
        public int FifteenMinuteWindow { get; init; } = 200;
        public int OneHourWindow { get; init; } = 200;

        // Progi reżimów (kalibrowalne, pair-aware docelowo)
        public decimal AdxEnableMomentum { get; init; } = 22m;
        public decimal AdxDisableMomentum { get; init; } = 18m;
        public decimal BbWidthBreakoutPct { get; init; } = 30m;   // percentyl
        public decimal BbWidthExitBreakoutPct { get; init; } = 45m;
        public decimal ZVwapEnableMR { get; init; } = 1.8m;       // |z| od D-VWAP
        public decimal ZVwapExitMR { get; init; } = 1.0m;
        public decimal MinScore { get; init; } = 55m;
        public decimal MinMargin { get; init; } = 10m;            // przewaga nad 2. trybem

        // Globalne gate’y koszt/zmienność (twarde)
        public decimal MaxSpreadBps { get; init; } = 2m;

        // ATR gates per-mode (domyślne; kalibrowalne)
        public decimal MmAtrMinPct { get; init; } = 1.2m;
        public decimal MmAtrMaxPct { get; init; } = 4.0m;
        public decimal MrAtrMinPct { get; init; } = 1.0m;
        public decimal MrAtrMaxPct { get; init; } = 3.5m;

        // BO: brak dolnego progu; tylko górny bezpiecznik
        public decimal BoAtrMaxPct { get; init; } = 7.0m;
        public decimal DefaultQuoteSize { get; init; } = 500m; // kwota per trade (USDT)
        public decimal MinAtr5mFloor { get; init; } = 0.5m;    // minimalny „floor” ATR5m w USD, by SL nie był zbyt blisko

        // Harmonogramy makro wydarzeń (czas UTC)
        public int MacroFreezeMinutesBefore { get; init; } = 10;
        public int MacroFreezeMinutesAfter { get; init; } = 30;
        public IReadOnlyList<DateTime> MacroEventsUtc = [];
    }

    public class SigmaStrategy : TradingStrategyBase
    {
        private readonly IOptions<SigmaStrategyOptions> _options;
        private readonly IBybitSigmaDataProvider _data;
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

            var relDir = Path.Combine("Data", "Strategies", "Sigma", "audit", symbol);
            var relFile = Path.Combine(relDir, $"regime_audit_{DateTime.UtcNow:yyyyMMdd}.csv");
            _audit = new RegimeAuditSink(relFile);
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames(SigmaStrategyOptions o)
            =>
            [
                new TimeFrameWindow(TimeFrame.OneMinute,       o.OneMinuteWindow,  true),
                new TimeFrameWindow(TimeFrame.FiveMinutes,     o.FiveMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.FifteenMinutes,  o.FifteenMinuteWindow, false),
                new TimeFrameWindow(TimeFrame.OneHour,         o.OneHourWindow, false),
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

            // 1) Cechy (korelacja BTC liczy się w providerze)
            var f = await FeatureSnapshot.BuildAsync(
                Symbol, quotes1m, quotes5m, quotes15m, quotes1h,
                ticker, _data, cancel, sessionStartHourUtc: 0, vwapSlopeWindow: 60);

            indicators.Add(new StrategyIndicator("Sigma.Spread.Bps", (decimal)Math.Round(f.SpreadBps, 4)));
            indicators.Add(new StrategyIndicator("Sigma.ATR1h.Pct", (decimal)Math.Round(f.AtrPct1h, 4)));

            // 2) Jedno wywołanie silnika: reżim + kontroler
            var nowUtc = DateTime.UtcNow;
            var (tradable, reason, decision, tradeDecision) =
                RegimeEngine.EvaluateTrade(
                    f, _regimeState, nowUtc, _options.Value,
                    momentumCtrl: _mm, meanReversionCtrl: _mr, breakoutCtrl: _bo,
                    cancel: cancel, audit: _audit);

            // 3) Telemetria score'ów i aktywnego reżimu
            indicators.Add(new StrategyIndicator("MM.Score", (decimal)decision.State.Scores.Momentum));
            indicators.Add(new StrategyIndicator("MR.Score", (decimal)decision.State.Scores.MeanReversion));
            indicators.Add(new StrategyIndicator("BO.Score", (decimal)decision.State.Scores.Breakout));

            // 4) Lepkość (harmonogram) + aktualizacja stanu tylko przy realnej zmianie
            bool timeToDecide = (nowUtc - _lastRegimeDecisionUtc) >= TimeSpan.FromMinutes(_options.Value.RecalcMinutes);
            if (timeToDecide)
            {
                _regimeState = decision.State;     // może być ta sama wartość – OK
                _lastRegimeDecisionUtc = nowUtc;   // bijemy heartbeat => prawdziwy throttling
            }

            indicators.Add(new StrategyIndicator("Regime.Active", _regimeState.Mode.ToString()));
            indicators.Add(new StrategyIndicator("Regime.Proposed", decision.State.Mode.ToString()));
            indicators.Add(new StrategyIndicator("Regime.Tradable", tradable.ToString()));
            indicators.Add(new StrategyIndicator("Regime.Reason", reason));

            Indicators = [.. indicators];

            var d = tradeDecision;
            return new SignalEvaluation(
                d.HasBuy, d.HasSell, d.HasBuyExtra, d.HasSellExtra, Indicators);
        }
    }
}

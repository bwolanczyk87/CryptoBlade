using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Strategies.Sigma
{
    public class SigmaStrategy : TradingStrategyBase
    {
        private readonly IOptions<SigmaStrategyOptions> _options;

        private readonly IMode _mm;
        private readonly IMode _mr;
        private readonly IMode _bo;

        private readonly ISigmaAuditSink _audit;
        private readonly ModeEngine _modeEngine;

        // Stan trybu (MM/MR/BO/None) + score'y – utrzymywany przez strategię na potrzeby audytu
        private ModeState _modeState = new(Mode.None, DateTime.MinValue, ModeScores.Zero);

        protected override bool UseMarketOrdersForEntries => false;

        public SigmaStrategy(
            IOptions<SigmaStrategyOptions> options,
            IOptions<TradingBotOptions> botOptions,
            string symbol,
            IWalletManager walletManager,
            ICbFuturesRestClient restClient)
            : base(options, botOptions, symbol, GetRequiredTimeFrames(options.Value), walletManager, restClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

            _mm = new MomentumMode(options.Value);
            _mr = new MeanReversionMode(options.Value);
            _bo = new BreakoutMode(options.Value);

            var relDir = Path.Combine("Data", "Strategies", "Sigma", "Audit", symbol);
            var relFile = Path.Combine(relDir, $"regime_audit_{DateTime.UtcNow:yyyyMMdd}.csv");
            _audit = new SigmaAuditSink(relFile);

            // ModeEngine jest stanowy – tworzymy go raz
            _modeEngine = new ModeEngine(options.Value, _mm, _mr, _bo, _audit);
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
            var nowUtc = DateTime.UtcNow;

            // SigmaData agreguje wszystkie cechy z helperów
            var sigmaData = new SigmaData(Symbol);

            // BTC 15m – do korelacji i biasu
            var btcQuotes15m = await GetQuotesAsync(
                "BTCUSDT",
                TimeFrame.FifteenMinutes,
                _options.Value.FifteenMinuteWindow,
                cancel);

            // Open Interest – 1h history (limit=100, ale SigmaData użyje ile trzeba)
            var oiPoints = await GetOpenInterestAsync(
                TimeFrame.OneHour,
                100,
                cancel);

            // Funding rates – ostatnie parę minut (czas okna możesz potem doprecyzować)
            var fundingRates = await m_cbFuturesRestClient.GetFundingRatesAsync(
                Symbol,
                nowUtc - TimeSpan.FromMinutes(5),
                nowUtc,
                cancel);

            // Build SigmaData – wszystkie obliczenia lecą w środku
            sigmaData.Build(
                nowUtc,
                QuoteQueues,
                btcQuotes15m,
                Ticker,
                PublicTrades,
                Liquidations,
                oiPoints,
                fundingRates);

            // Zachowujemy poprzedni stan trybu do audytu
            var prevState = _modeState;

            // 1) Globalne bramki + klasyfikacja reżimu
            var (mode, modeDecision, gateReason) = _modeEngine.Evaluate(
                sigmaData,
                nowUtc,
                cancel);

            // Aktualizujemy stan trybu zgodnie z decyzją ModeEngine
            _modeState = modeDecision.State;

            // 2) Sygnał z aktywnego trybu (jeśli globalne bramki pozwalają i istnieje aktywny tryb)
            var modeSignal = mode?.Execute(sigmaData, nowUtc, cancel) ?? ModeSignal.None;

            // 3) Audyt – pełny snapshot cech + scores + wybór trybu + gating
            var tradable = mode is not null;

            _audit.Add(SigmaAudit.MakeRecord(
                sigmaData,
                prevState,
                nowUtc,
                _options.Value,
                tradable: tradable,
                reason: gateReason,
                decision: modeDecision,
                lastDecisionUtc: nowUtc));

            // 4) Zwracamy sygnał bez wskaźników (pusta tablica)
            return new SignalEvaluation(
                modeSignal.HasBuy,
                modeSignal.HasSell,
                modeSignal.HasBuyExtra,
                modeSignal.HasSellExtra,
                []);
        }

        public override Task ExecuteAsync(ExecuteParams executeParams, CancellationToken cancel)
        {
            // Sigma nie korzysta z domyślnego engine’u wejść/wyjść.
            // Wszystkie decyzje o orderach idą przez SigmaPositionManager.
            return Task.CompletedTask;
        }
    }
}

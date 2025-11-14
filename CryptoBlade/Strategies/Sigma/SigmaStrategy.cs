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

        // Stan trybu (MM/MR/BO/None) + score'y – utrzymywany przez strategię
        private ModeState _modeState = new(Mode.None, DateTime.MinValue, ModeScores.Zero);

        private readonly RollingSignedQty _cvd5m = new(TimeSpan.FromMinutes(5));
        private readonly LiquidationBuffer _liq20m = new(TimeSpan.FromMinutes(20));

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

            _mm = new MomentumMode(options.Value);
            _mr = new MeanReversionMode(options.Value);
            _bo = new BreakoutMode();

            var relDir = Path.Combine("Data", "Strategies", "Sigma", "Audit", symbol);
            var relFile = Path.Combine(relDir, $"regime_audit_{DateTime.UtcNow:yyyyMMdd}.csv");
            _audit = new SigmaAuditSink(relFile);
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

            // Snapshot likwidacji (20m rolling)
            var liqs = _liq20m.Snapshot(nowUtc);

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
            var fundingRates = await GetFundingRatesAsync(
                Symbol,
                nowUtc - TimeSpan.FromMinutes(5),
                nowUtc,
                cancel);

            // Build SigmaData – wszystkie obliczenia lecą w środku
            sigmaData.Build(
                QuoteQueues,
                btcQuotes15m,
                Ticker,
                PublicTrades,
                liqs,
                oiPoints,
                fundingRates);

            var modeEngine = new ModeEngine(_options.Value, _mm, _mr, _bo, _audit);
            var decision = modeEngine.Evaluate(
                sigmaData,
                nowUtc,
                cancel);

            var signal = decision.Mode.Execute(sigmaData, nowUtc, cancel);

            // 4) Audyt – pełny snapshot cech + scores + wybór trybu + gating
            _audit.Add(SigmaAudit.MakeRecord(
                sigmaData,
                _modeState,
                nowUtc,
                _options.Value,
                tradable: true,
                reason: "OK",
                decision: decision.ModeDecision,
                lastDecisionUtc: nowUtc));

            // 5) Zwracamy sygnał bez wskaźników (pusta tablica)
            return new SignalEvaluation(signal.HasBuy, signal.HasSell, signal.HasBuyExtra, signal.HasSellExtra, []);
        }

        public override Task AddPublicTradeAsync(PublicTrade trade, CancellationToken cancel)
        {
            var signed = trade.Side == OrderSide.Buy ? trade.Quantity : -trade.Quantity;
            _cvd5m.Add(trade.Timestamp, signed);
            return Task.CompletedTask;
        }

        public override Task AddLiquidationAsync(LiquidationEvent liq, CancellationToken cancel)
        {
            _liq20m.Add(liq);
            return Task.CompletedTask;
        }
    }
}

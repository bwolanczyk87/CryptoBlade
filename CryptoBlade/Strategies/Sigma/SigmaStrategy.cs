using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
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
        private readonly SigmaAuditSink _audit;
        private readonly ModeEngine _modeEngine;
        private ModeState _modeState = new(Mode.None, DateTime.MinValue, ModeScores.Zero);
        private readonly SigmaPositionManager _positionManager;

        protected override bool UseMarketOrdersForEntries => false;

        public SigmaStrategy(IOptions<SigmaStrategyOptions> options, IOptions<TradingBotOptions> botOptions, string symbol, IWalletManager walletManager, ICbFuturesRestClient restClient)
            : base(options, botOptions, symbol, GetRequiredTimeFrames(options.Value), walletManager, restClient)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _mm = new MomentumMode(options.Value);
            _mr = new MeanReversionMode(options.Value);
            _bo = new BreakoutMode(options.Value);

            var relDir = Path.Combine("Data", "Strategies", "Sigma", "Audit", symbol);
            var relFile = Path.Combine(relDir, $"sigma_audit_{DateTime.UtcNow:yyyyMMdd}.csv");

            _audit = new SigmaAuditSink(relFile);
            _modeEngine = new ModeEngine(options.Value, _mm, _mr, _bo, _audit);
            _positionManager = new SigmaPositionManager(options.Value, SymbolInfo);
        }

        private static TimeFrameWindow[] GetRequiredTimeFrames(SigmaStrategyOptions o) =>
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
            var sigmaData = new SigmaData(Symbol);
            var btcQuotes15m = await GetQuotesAsync("BTCUSDT", TimeFrame.FifteenMinutes, _options.Value.FifteenMinuteWindow, cancel);
            var oiPoints = await GetOpenInterestAsync(TimeFrame.FiveMinutes, 60, cancel);
            var fundingRates = await m_cbFuturesRestClient.GetFundingRatesAsync(Symbol,nowUtc - TimeSpan.FromDays(1), nowUtc, cancel);

            sigmaData.Build(nowUtc, QuoteQueues, btcQuotes15m, Ticker, PublicTrades, Liquidations, oiPoints, fundingRates);

            var prevState = _modeState;
            var (mode, modeDecision, gateReason) = _modeEngine.Evaluate(sigmaData, nowUtc, cancel);
            _modeState = modeDecision.State;

            var modeSignal = mode?.Execute(sigmaData, nowUtc, cancel) ?? ModeSignal.None;
            var tradable = mode is not null;
            _audit.Add(SigmaAudit.MakeRecord(sigmaData, prevState, nowUtc, _options.Value, tradable, gateReason, modeDecision, nowUtc));

            await _positionManager.OnSignalAsync(Symbol, sigmaData, modeDecision.ProposedMode, modeSignal, tradable,nowUtc, m_cbFuturesRestClient, WalletManager, cancel);
            return new SignalEvaluation(modeSignal.HasBuy, modeSignal.HasSell, false, false, []);
        }

        public override async Task OrderUpdatedAsync(OrderUpdate orderUpdate, CancellationToken cancel)
        {
            await _positionManager.OnOrderUpdateAsync(Symbol, orderUpdate, m_cbFuturesRestClient, cancel);
        }

        public async Task RecoverSigmaStateAsync(CancellationToken cancel)
        {
            await _positionManager.RecoverFromOpenOrdersAsync([.. BuyOrders, .. SellOrders], Symbol, SymbolInfo, m_cbFuturesRestClient, DateTime.UtcNow, cancel);
        }

        public override Task ExecuteAsync(ExecuteParams executeParams, CancellationToken cancel) => Task.CompletedTask;
    }
}

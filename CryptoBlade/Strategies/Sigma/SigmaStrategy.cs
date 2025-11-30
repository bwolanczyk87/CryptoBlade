using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Sigma.Audit;
using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;

namespace CryptoBlade.Strategies.Sigma
{
    public class SigmaStrategy : TradingStrategyBase
    {
        private readonly SigmaStrategyOptions _options;
        private readonly SigmaAuditSink _audit;
        private readonly ModeEngine _modeEngine;
        private readonly SigmaPositionManager _positionManager;
        private readonly bool _enableTestSignal = false;
        public SigmaData Data { get; set; } = new();

        protected override bool UseMarketOrdersForEntries => false;

        public SigmaStrategy(IOptions<SigmaStrategyOptions> options, IOptions<TradingBotOptions> botOptions, string symbol, IWalletManager walletManager, ICbFuturesRestClient restClient)
            : base(options, botOptions, symbol, GetRequiredTimeFrames(options.Value), walletManager, restClient)
        {
            _options = options.Value;

            var relDir = Path.Combine("Data", "Strategies", "Sigma", "Audit", symbol);
            var relFile = Path.Combine(relDir, $"sigma_audit_{DateTime.UtcNow:yyyyMMdd}.csv");

            _audit = new SigmaAuditSink(relFile);
            _modeEngine = new ModeEngine(options.Value);
            _positionManager = new SigmaPositionManager(options.Value, restClient);
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
            DateTime nowUtc = DateTime.UtcNow;
            ModeSignal modeSignal = ModeSignal.None;
            IMode? mode = null;
            ModeScores scores = new(0, 0, 0);

            await CancelStaleEntryOrdersAsync(nowUtc, cancel);

            Data = new SigmaData();
            var btcQuotes15m = await GetQuotesAsync("BTCUSDT", TimeFrame.FifteenMinutes, _options.FifteenMinuteWindow, cancel);
            var oiPoints = await GetOpenInterestAsync(TimeFrame.FiveMinutes, 60, cancel);
            var fundingRates = await m_cbFuturesRestClient.GetFundingRatesAsync(Symbol,nowUtc - TimeSpan.FromDays(1), nowUtc, cancel);
            var fullTicker = await m_cbFuturesRestClient.GetTickerAsync(Symbol, cancel);

            Data.Build(nowUtc, QuoteQueues, btcQuotes15m, fullTicker, PublicTrades, Liquidations, oiPoints, fundingRates);

            (bool gateOk, string gateReason) = _modeEngine.CheckGlobalGates(Data, nowUtc);
            if (gateOk)
            {
                (mode, scores) = _modeEngine.SelectModeAndScores(nowUtc, Data);
                if(mode != null)
                    modeSignal = mode.GenerateSignal(Data, _enableTestSignal);
            }

            _audit.Add(SigmaAudit.MakeRecord(nowUtc, Symbol, Data, _options, gateReason, mode, scores));

            if(!IsInTrade)
                await _positionManager.OnSignalAsync(nowUtc, SymbolInfo, Data, mode, modeSignal, WalletManager, m_logger, cancel);
            
            return new SignalEvaluation(modeSignal.HasBuy, modeSignal.HasSell, false, false, []);
        }

        public override Task ExecuteAsync(ExecuteParams executeParams, CancellationToken cancel) => Task.CompletedTask;

        private async Task CancelStaleEntryOrdersAsync(DateTime nowUtc, CancellationToken cancel)
        {
            if (_options.PendingEntryTimeoutMinutes <= 0)
                return;

            if (IsInTrade)
                return;

            var maxAge = TimeSpan.FromMinutes(_options.PendingEntryTimeoutMinutes);
            var allOrders = (BuyOrders ?? []).Concat(SellOrders ?? []);

            foreach (var order in allOrders)
            {
                if (order is null)
                    continue;

                if (order.Status is OrderStatus.Filled or OrderStatus.Cancelled)
                    continue;

                if (!SigmaClientOrderId.IsSigmaOrderId(order.ClientOrderId))
                    continue;

                var kind = SigmaClientOrderId.TryParseKind(order.ClientOrderId);
                if (kind != SigmaOrderKind.Entry)
                    continue;

                var age = nowUtc - order.CreateTime;
                if (age < maxAge)
                    continue;

                m_logger.LogInformation(
                    "Sigma time-stop ENTRY: cancel stale orderId={OrderId} clientOrderId={ClientOrderId} age={Age} sym={Symbol}",
                    order.OrderId,
                    order.ClientOrderId,
                    age,
                    Symbol);

                await m_cbFuturesRestClient.CancelOrderAsync(Symbol, order.OrderId, cancel);
            }
        }
    }
}

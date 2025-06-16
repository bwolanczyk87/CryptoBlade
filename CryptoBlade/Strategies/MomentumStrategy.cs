using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Services;
using CryptoBlade.Strategies.AI;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CryptoBlade.Strategies
{
    /// <summary>
    /// Momentum strategy powered by AI‑Crypto‑Ultimate‑Bot prompt v2 (Candles | Indicators | Pivots).
    /// </summary>
    public sealed class MomentumStrategy : TradingStrategyBase
    {
        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;
        private const int MaxCandlesPerTimeframe = 100;

        /* === AI runtime === */
        private readonly ChatAI _chatAI;
        private readonly List<IndicatorRequest> _activeIndicators = new();
        private List<CandleRequest> _activeCandles = new();
        private List<PivotRequest> _activePivots = new();

        /* === misc === */
        private bool _isInitialized;
        private DateTime _lastEvalUtc = DateTime.MinValue;
        private readonly object _evalLock = new();
        private readonly ILogger<MomentumStrategy> _log;

        /* === ctor === */
        public MomentumStrategy(IOptions<MomentumStrategyOptions> strategyOpt,
                                IOptions<TradingBotOptions> botOpt,
                                string symbol,
                                IWalletManager walletMgr,
                                ICbFuturesRestClient restClient,
                                DeepSeekAccountConfig deepSeekCfg)
            : base(strategyOpt, botOpt, symbol, BuildTfWindows(), walletMgr, restClient)
        {
            _log = ApplicationLogging.CreateLogger<MomentumStrategy>();
            _chatAI = new ChatAI(deepSeekCfg, symbol, ApplicationLogging.CreateLogger<ChatAI>());

            InitIndicators();
            InitCandles();
            InitPivots();

            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }

        /* === helpers === */
        private static TimeFrameWindow[] BuildTfWindows() =>
        [
            new(TimeFrame.OneDay,          MaxCandlesPerTimeframe, true),
            new(TimeFrame.FourHours,       MaxCandlesPerTimeframe, true),
            new(TimeFrame.OneHour,         MaxCandlesPerTimeframe, true),
            new(TimeFrame.FifteenMinutes,  MaxCandlesPerTimeframe, false),
            new(TimeFrame.FiveMinutes,     MaxCandlesPerTimeframe, false)
        ];

        private void InitIndicators()
        {
            foreach (var name in new[] { "Ema(100)|1H", "Macd(8,21,5)|1H", "Rsi(14)|1H", "Atr(14)|1H" })
                _activeIndicators.Add(IndicatorRequest.Parse(name));
        }

        private void InitCandles() => _activeCandles =
        [
            CandleRequest.Parse("1H|12"),
            CandleRequest.Parse("15M|16"),
            CandleRequest.Parse("5M|60")
        ];

        private void InitPivots() => _activePivots =
        [
            PivotRequest.Parse("4H|1.2"),
            PivotRequest.Parse("1H|H|0.8")
        ];

        private void LogCycleDuration()
        {
            lock (_evalLock)
            {
                var now = DateTime.UtcNow;
                if (_lastEvalUtc != DateTime.MinValue)
                {
                    var span = now - _lastEvalUtc;
                    _log.LogInformation($"⏱️ Cycle gap: {span.TotalMinutes:0.00} min ({span.Minutes}m {span.Seconds}s)");
                }
                _lastEvalUtc = now;
            }
        }

        /* === main loop === */
        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken ct)
        {
            LogCycleDuration();
            var indics = new List<StrategyIndicator>();

            try
            {
                if (IsInTrade)
                    return NoSignal(indics, "Already in trade");

                if (!_isInitialized)
                {
                    _chatAI.InitializeConversation(SymbolInfo.MaxLeverage, WalletManager.Contract.WalletBalance.Value);
                    _isInitialized = true;
                }

                /* collect quotes */
                var quotesDict = QuoteQueues.ToDictionary(kv => kv.Key, kv => kv.Value.GetQuotes().TakeLast(MaxCandlesPerTimeframe));

                /* build user payload */
                string userMsg = BuildUserMessage(quotesDict);
                string aiJson = await _chatAI.GetAIResponseAsync(userMsg, ct);

                return ParseAiJson(aiJson, quotesDict, indics);
            }
            catch (Exception ex)
            {
                indics.Add(new("Error", ex.Message));
                return NoSignal(indics, "AI error");
            }
        }

        /* === build prompt data === */
        private string BuildUserMessage(Dictionary<TimeFrame, IEnumerable<Quote>> quotes)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"SYM:{Symbol} | BAL:{WalletManager.Contract.WalletBalance.Value:F2} | PRC:{Ticker?.LastPrice.ToString("F2", CultureInfo.InvariantCulture) ?? "0"}");
            sb.AppendLine();

            // indicators
            foreach (var req in _activeIndicators)
            {
                string tag = req.Name.Substring(0, Math.Min(3, req.Name.Length)).ToUpper();
                if (quotes.TryGetValue(req.Tf, out var tfQuotes))
                {
                    var val = IndicatorEngine.Compute(req, tfQuotes);
                    string fv = IndicatorEngine.Format(val);
                    sb.AppendLine($"{tag}:{fv}");
                }
                else sb.AppendLine($"{tag}:N/A");
            }
            sb.AppendLine();

            // candles
            sb.AppendLine("CANDLES:");
            foreach (var cr in _activeCandles)
                sb.AppendLine(cr.ToBotPayload(QuoteQueues, (int)SymbolInfo.PriceScale));

            sb.AppendLine("PIVOTS:");
            foreach (var pr in _activePivots)
            {
                if (!quotes.TryGetValue(pr.Tf, out var tfQ)) continue;

                var pivots = PivotEngine.Compute(pr, tfQ)
                    .Where(z => !string.IsNullOrWhiteSpace(z.PointType))
                    .TakeLast(10)
                    .Select(z => $"{z.Date:yyyyMMddHHmm}|{z.ZigZag:F0}|{z.PointType}");

                sb.AppendLine($"{pr}={string.Join(';', pivots)}");
            }

            sb.AppendLine();
            sb.AppendLine("Analyze indicators, candles and pivots. Request only data that will materially increase Confidence.");
            return sb.ToString();
        }

        /* === parse AI json === */
        private SignalEvaluation ParseAiJson(string json, Dictionary<TimeFrame, IEnumerable<Quote>> quotes, List<StrategyIndicator> indics)
        {
            SignalResponseAI? ai;
            try { ai = JsonSerializer.Deserialize<SignalResponseAI>(json); }
            catch (Exception ex) { return NoSignal(indics, $"JSON parse error: {ex.Message}"); }
            if (ai == null) return NoSignal(indics, "Null AI");

            indics.Add(new("AI-Conf", $"{ai.Confidence}%"));
            indics.Add(new("AI-Note", ai.Reason));

            UpdateActiveIndicators(ai.RequestedIndicators);
            UpdateActiveCandles(ai.RequestedCandles);
            UpdateActivePivots(ai.RequestedPivots);

            if (ai.Confidence < 70 || ai.Signal == "NONE")
                return NoSignal(indics, "Low confidence or NONE");

            bool isLong = ai.Signal == "LONG";
            decimal entry = ai.EntryPrice ?? (isLong ? Ticker?.BestAskPrice ?? 0 : Ticker?.BestBidPrice ?? 0);

            return GenerateSignal(isLong, entry, ai.StopLoss, ai.TakeProfit, ai.Quantity, indics);
        }

        private void UpdateActiveIndicators(List<string>? list)
        {
            if (list == null) return;
            foreach (var s in list)
            {
                try
                {
                    var req = IndicatorRequest.Parse(s);
                    if (!_activeIndicators.Any(i => i.Name == req.Name && i.Tf == req.Tf))
                        _activeIndicators.Add(req);
                }
                catch { /* ignore */ }
            }
        }

        private void UpdateActiveCandles(List<string>? list)
        {
            if (list == null) return;
            var tmp = new List<CandleRequest>();
            foreach (var s in list)
            {
                try { tmp.Add(CandleRequest.Parse(s)); }
                catch { }
            }
            if (tmp.Any()) _activeCandles = tmp;
        }

        private void UpdateActivePivots(List<string>? list)
        {
            if (list == null) return;
            var tmp = new List<PivotRequest>();
            foreach (var s in list)
            {
                try { tmp.Add(PivotRequest.Parse(s)); }
                catch { }
            }
            if (tmp.Any()) _activePivots = tmp;
        }

        /* === helpers === */
        private SignalEvaluation GenerateSignal(bool isLong, decimal entry, decimal stop, decimal tp, decimal qty, List<StrategyIndicator> indics)
        {
            TakeProfitPrice = tp;
            StopLossPrice = stop;

            if (isLong) { DynamicQtyLong = qty; indics.Add(new("Signal", "LONG")); }
            else { DynamicQtyShort = qty; indics.Add(new("Signal", "SHORT")); }

            return new SignalEvaluation(isLong, !isLong, false, false, [.. indics]);
        }

        private static SignalEvaluation NoSignal(List<StrategyIndicator> indics, string reason)
        {
            indics.Add(new("Reason", reason));
            return new SignalEvaluation(false, false, false, false, [.. indics]);
        }
    }
}

using Accord;
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
using System.Threading.Tasks;

namespace CryptoBlade.Strategies
{
    public sealed class MomentumStrategy : TradingStrategyBase
    {
        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;
        private const int MaxCandlesPerTimeframe = 100;

        private readonly StyleProfile _profile;
        private readonly ChatAI _chatAI;
        private readonly List<IndicatorRequest> _activeIndicators = [];
        private List<CandleRequest> _activeCandles = [];
        private List<PivotRequest> _activePivots = [];
        private int _confidence = 0;

        private bool _isInitialized;
        private DateTime _lastEvalUtc = DateTime.MinValue;
        private int _dataDelay = 0;
        private readonly object _evalLock = new();
        private readonly ILogger<MomentumStrategy> _log;

        public MomentumStrategy(IOptions<MomentumStrategyOptions> strategyOpt,
                                IOptions<TradingBotOptions> botOpt,
                                string symbol,
                                IWalletManager walletMgr,
                                ICbFuturesRestClient restClient,
                                DeepSeekAccountConfig deepSeekCfg)
            : base(strategyOpt, botOpt, symbol, [], walletMgr, restClient)
        {
            _log = ApplicationLogging.CreateLogger<MomentumStrategy>();
            _chatAI = new ChatAI(deepSeekCfg, "deepseek-chat", symbol, ApplicationLogging.CreateLogger<ChatAI>());

            _profile = StyleProfileFactory.Create(TradingStyle.Scalping);
            var requiredTimeFrames = _profile.DefaultTimeFrameWindows.ToArray();

            RequiredTimeFrameWindows = requiredTimeFrames;
            foreach (TimeFrameWindow requiredTimeFrame in requiredTimeFrames)
                QuoteQueues[requiredTimeFrame.TimeFrame] = new(requiredTimeFrame.WindowSize, requiredTimeFrame.TimeFrame);
            foreach (TimeFrame timeFrame in Enum.GetValues<TimeFrame>())
            {
                if (!QuoteQueues.ContainsKey(timeFrame))
                    QuoteQueues[timeFrame] = new(c_defaultCandleBufferSize, timeFrame);
            }

            _activeIndicators = [.. _profile.DefaultIndicators.Select(IndicatorRequest.Parse)];
            _activeCandles = [.. _profile.DefaultCandles.Select(CandleRequest.Parse)];
            _activePivots = [.. _profile.DefaultPivots.Select(PivotRequest.Parse)];

            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }

        private void LogCycleDuration()
        {
            lock (_evalLock)
            {
                var now = DateTime.UtcNow;

                if (_lastEvalUtc != DateTime.MinValue)
                {
                    var span = now - _lastEvalUtc;
                    _log.LogInformation($"Cycle gap: {span.TotalMinutes:0.00} min ({span.Minutes}m {span.Seconds}s)");
                }
                _lastEvalUtc = now;
            }
        }

        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken ct)
        {
            LogCycleDuration();
            var indics = new List<StrategyIndicator>
            {
                new(nameof(IndicatorType.MainTimeFrameVolume), 10m)
            };

            try
            {
                if (IsInTrade)
                    return NoSignal(indics);

                if (_dataDelay > 0) 
                { 
                    _dataDelay--;
                    indics.Add(new("AI-DataDelay", _dataDelay));
                    return NoSignal(indics);
                }

                if (!_isInitialized)
                {
                    _chatAI.InitializeConversation(_profile.StyleName, SymbolInfo, WalletManager.Contract.WalletBalance.Value);
                    _isInitialized = true;
                }

                var quotesDict = QuoteQueues.ToDictionary(kv => kv.Key, kv => kv.Value.GetQuotes().TakeLast(MaxCandlesPerTimeframe));
                string userMsg = BuildUserMessage(quotesDict);
                _chatAI.TrimConversationHistory();

                _log.LogInformation($"[{Symbol}]Wait for AI respose...");
                string aiJson = await _chatAI.GetAIResponseAsync(userMsg, ct);

                return await ParseAiJson(aiJson, quotesDict, indics);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private string BuildUserMessage(Dictionary<TimeFrame, IEnumerable<Quote>> quotes)
        {
            var sb = new StringBuilder();
            int scale = (int)SymbolInfo.PriceScale;

            /* ------------------  INDICATORS  ------------------ */
            foreach (var req in _activeIndicators)
            {
                string paramStr = req.Params.Length > 0
                                  ? $"({string.Join(',', req.Params)})"
                                  : "()";
                string tag = $"{TimeFrameHelper.GetAbbreviation(req.Tf)}|{req.Name}{paramStr}";

                if (quotes.TryGetValue(req.Tf, out var tfQuotes))
                {
                    var val = IndicatorEngine.Compute(req, tfQuotes);
                    string fv = IndicatorEngine.Format(val, scale);

                    if (string.IsNullOrWhiteSpace(fv) ||
                        fv.Trim('0', '.', '-') == string.Empty)
                        fv = "N/A";

                    sb.AppendLine($"{tag}={fv}");
                }
                else
                    sb.AppendLine($"{tag}=N/A");
            }
            sb.AppendLine();

            /* ------------------  CANDLES  ------------------ */
            foreach (var cr in _activeCandles)
                sb.AppendLine(cr.ToBotPayload(QuoteQueues, scale));

            /* ------------------  PIVOTS  ------------------- */
            foreach (var pr in _activePivots)
            {
                if (!quotes.TryGetValue(pr.Tf, out var tfQ))
                    continue;

                var pivots = PivotEngine.Compute(pr, tfQ)
                    .Where(z => !string.IsNullOrWhiteSpace(z.PointType))
                    .TakeLast(10)
                    .Select(z =>
                        $"{z.Date:MMddHHmm}," +
                        $"{Convert.ToDecimal(z.ZigZag ?? 0).ToString($"F{scale}", CultureInfo.InvariantCulture)}," +
                        $"{z.PointType}");

                sb.AppendLine($"{pr}={string.Join(';', pivots)}");
            }
            return sb.ToString();
        }

        private async Task<SignalEvaluation> ParseAiJson(string json, Dictionary<TimeFrame, IEnumerable<Quote>> quotes, List<StrategyIndicator> indics)
        {
            SignalResponseAI? ai;
            try { ai = JsonSerializer.Deserialize<SignalResponseAI>(json); }
            catch (Exception ex) { return NoSignal(indics, $"JSON parse error: {ex.Message}"); }
            if (ai == null) return NoSignal(indics, "Null AI");
            _dataDelay = ai.Delay;

            indics.Add(new("AI-DataDelay", ai.Delay));
            indics.Add(new("AI-Conf", $"{ai.Confidence}%"));
            indics.Add(new("AI-Note", ai.Reason));

            _confidence = ai.Confidence;
            if (ai.Confidence < 70 || ai.Signal == "NONE")
                return NoSignal(indics, $"Low confidence {ai.Confidence}");

            bool isLong = ai.Signal == "LONG";
            return await GenerateSignal(isLong, ai.StopLoss, indics);
        }

        private async Task<SignalEvaluation> GenerateSignal(bool isLong, decimal stop, List<StrategyIndicator> indics)
        {
            var ticker = await m_cbFuturesRestClient.GetTickerAsync(Symbol, CancellationToken.None);
            if (ticker == null)
                return NoSignal(indics, "Ticker not found");

            decimal entry = ticker.LastPrice;

            // odległość ryzyka w punktach
            decimal risk = isLong ? entry - stop           // LONG: SL poniżej
                                  : stop - entry;         // SHORT: SL powyżej

            if (risk <= 0)                // SL na złej stronie?
                return NoSignal(indics, "SL invalid vs entry");

            decimal tp = isLong ? entry + risk/2             // LONG: TP powyżej
                                : entry - risk/2;            // SHORT: TP poniżej

            // zapisz do pól bazowej klasy
            StopLossPrice = stop;
            TakeProfitPrice = tp;

            // (obliczenie wielkości pozycji patrzy już na StopLossPrice)
            await CalculateDynamicQtyAsync();

            string priceFmt = $"F{SymbolInfo.PriceScale}";

            indics.Add(new("Entry", entry.ToString(priceFmt, CultureInfo.InvariantCulture)));
            indics.Add(new("TP", tp.ToString(priceFmt, CultureInfo.InvariantCulture)));

            return new SignalEvaluation(isLong, !isLong, false, false, [.. indics]);
        }

        private static SignalEvaluation NoSignal(List<StrategyIndicator> indics, string? reason = null)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                indics.Add(new("Reason", reason));

            return new SignalEvaluation(false, false, false, false, [.. indics]);
        }

        //protected override async Task CalculateDynamicQtyAsync()
        //{
        //    var ticker = await m_cbFuturesRestClient.GetTickerAsync(Symbol, CancellationToken.None);
        //    if(StopLossPrice == null || ticker == null)
        //    {
        //        return;
        //    }

        //    var quantity = CalculateQtyRiskBased(SymbolInfo, WalletManager, ticker.LastPrice, StopLossPrice.Value, _confidence, 0.01m, 0.05m);
        //    DynamicQtyLong = quantity;
        //    DynamicQtyShort = quantity;
        //}

        protected override Task CalculateTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            return Task.CompletedTask;
        }
    }
}

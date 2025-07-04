using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Strategies.AI;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CryptoBlade.Strategies
{
    public sealed class MomentumStrategy : TradingStrategyBase
    {
        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;
        private readonly TradingStyleProfile _profile;
        private readonly ChatAI _chatAI;

        private bool _isInitialized;
        private int _dataDelay = 0;
        private readonly ILogger<MomentumStrategy> _log;

        public MomentumStrategy(IOptions<MomentumStrategyOptions> strategyOpt,
                                IOptions<TradingBotOptions> botOpt,
                                string symbol,
                                IWalletManager walletMgr,
                                ICbFuturesRestClient restClient,
                                AiAccountsRoot aiAccounts)
            : base(strategyOpt, botOpt, symbol, [], walletMgr, restClient)
        {
            var account = aiAccounts.AiAccounts.FirstOrDefault(a => a.ModelId == "deepseek-chat")
                ?? throw new Exception($"AI account not found for symbol {Symbol}");

            _chatAI = new ChatAI(account, ApplicationLogging.CreateLogger<ChatAI>());
            _log = ApplicationLogging.CreateLogger<MomentumStrategy>();
            _profile = StyleProfileFactory.Create(TradingStyle.Intraday);
            var requiredTimeFrames = _profile.Frames.Select(f => f.Window).ToArray();

            foreach (TimeFrameWindow requiredTimeFrame in requiredTimeFrames)
                QuoteQueues[requiredTimeFrame.TimeFrame] = new(requiredTimeFrame.WindowSize, requiredTimeFrame.TimeFrame);

            foreach (TimeFrame timeFrame in Enum.GetValues<TimeFrame>())
            {
                if (!QuoteQueues.ContainsKey(timeFrame))
                    QuoteQueues[timeFrame] = new(c_defaultCandleBufferSize, timeFrame);
            }

            RequiredTimeFrameWindows = requiredTimeFrames;
            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }

        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken ct)
        {
            var indics = new List<StrategyIndicator>
            {
                new(nameof(IndicatorType.MainTimeFrameVolume), 1000m)
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
                    _chatAI.AddSystemMessage(_profile.StyleName, Symbol);
                    _isInitialized = true;
                }

                _chatAI.AddUserMessage(_profile, QuoteQueues, SymbolInfo);

                _log.LogInformation($"[{Symbol}]Wait for AI respose...");
                string aiJson = await _chatAI.GetAIResponseAsync(Symbol, ct);

                _chatAI.TrimConversationHistory();
                return await ParseAiJson(aiJson, indics);
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        private async Task<SignalEvaluation> ParseAiJson(string json, List<StrategyIndicator> indics)
        {
            SignalResponseAI? ai;
            try
            {
                ai = JsonSerializer.Deserialize<SignalResponseAI>(json);
            }
            catch (Exception ex)
            {
                return NoSignal(indics, $"JSON parse error: {ex.Message}");
            }
            if (ai is null)
                return NoSignal(indics, "Null AI response");

            _dataDelay = ai.Delay;
            indics.Add(new("AI-DataDelay", ai.Delay));
            indics.Add(new("AI-Conf", $"{ai.Confidence}%"));
            indics.Add(new("AI-Note", ai.Reason));

            if (ai.Confidence < 90 || ai.Signal == "NONE")
                return NoSignal(indics, $"Confidence {ai.Confidence} < 90");

            bool isLong = ai.Signal == "LONG";
            return await GenerateSignal(isLong, ai.EntryPrice, ai.StopLoss, ai.TakeProfit, indics);
        }


        private async Task<SignalEvaluation> GenerateSignal(bool isLong,
                                                            decimal entry,
                                                            decimal stop,
                                                            decimal tp,
                                                            List<StrategyIndicator> indics)
        {
            EntryPrice = entry;
            StopLossPrice = stop;

            decimal distance = Math.Abs(entry - stop);
            TakeProfitPrice = isLong
                ? entry + distance
                : entry - distance;

            await CalculateDynamicQtyAsync();

            return new SignalEvaluation(isLong, !isLong, false, false, [.. indics]);
        }

        private static SignalEvaluation NoSignal(List<StrategyIndicator> indics, string? reason = null)
        {
            if (!string.IsNullOrWhiteSpace(reason))
                indics.Add(new("Reason", reason));

            return new SignalEvaluation(false, false, false, false, [.. indics]);
        }

        protected override Task CalculateTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            return Task.CompletedTask;
        }
    }
}

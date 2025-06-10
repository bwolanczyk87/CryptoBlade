using System.Text;
using System.Text.Json;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Services;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using Microsoft.Extensions.ObjectPool;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy : TradingStrategyBase
    {
        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;
        private const int MaxConversationHistory = 5;
        private const int MaxCandlesPerTimeframe = 100;
        private readonly ChatClient _chatClient;
        private readonly List<ChatMessage> _conversationHistory = new();
        private readonly Dictionary<string, Func<IEnumerable<Quote>, object>> _indicatorCalculators;
        private List<string> _activeIndicators = ["EMA", "MACD", "RSI", "Volume", "ATR"];
        private List<string> _activeCandles = ["1H|12", "15M|16", "5M|60"];
        private bool _isInitialized = false;

        public MomentumStrategy(IOptions<MomentumStrategyOptions> strategyOptions, IOptions<TradingBotOptions> botOptions, string symbol,
            IWalletManager walletManager, ICbFuturesRestClient restClient, DeepSeekAccountConfig deepSeekConfig)
            : base(strategyOptions, botOptions, symbol, BuildTimeFrameWindows(), walletManager, restClient)
        {
            var account = deepSeekConfig.Accounts.FirstOrDefault(a => a.ApiName == symbol.ToLower())
                ?? throw new Exception($"DeepSeek account not found for symbol {symbol}");

            var deepSeekClient = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
            );

            _chatClient = deepSeekClient.GetChatClient("deepseek-chat");

            _indicatorCalculators = new()
            {
                ["EMA"] = q => q.GetEma(100).Last(),
                ["MACD"] = q => q.GetMacd(8, 21, 5).Last(),
                ["RSI"] = q => q.GetRsi(14).Last(),
                ["Volume"] = q => q.Use(CandlePart.Volume).GetSma(20).Last(),
                ["ATR"] = q => q.GetAtr(14).Last(),
                ["ADX"] = q => q.GetAdx(14).Last(),
                ["BollingerBands"] = q => q.GetBollingerBands(20, 2).Last(),
                ["Stochastic"] = q => q.GetStoch(14, 3, 3).Last(),
                ["Ichimoku"] = q => q.GetIchimoku(9, 26, 52, 26).Last(),
                ["VWAP"] = q => q.GetVwap().Last()
            };

            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }


        private static TimeFrameWindow[] BuildTimeFrameWindows()
        {
            return
            [
                new TimeFrameWindow(TimeFrame.OneDay, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.FourHours, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.OneHour, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.FifteenMinutes, MaxCandlesPerTimeframe, false),
                new TimeFrameWindow(TimeFrame.FiveMinutes, MaxCandlesPerTimeframe, false),
                new TimeFrameWindow(TimeFrame.OneMinute, MaxCandlesPerTimeframe, false)
            ];
        }

        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var indicators = new List<StrategyIndicator>();

            try
            {
                if (IsInTrade)
                    return NoSignal(indicators, "Already in trade");

                if (!_isInitialized)
                {
                    await InitializeConversationAsync();
                    _isInitialized = true;
                }

                var quotes = new Dictionary<TimeFrame, IEnumerable<Quote>>();
                foreach (var timeframe in QuoteQueues.Keys)
                {
                    quotes[timeframe] = QuoteQueues[timeframe].GetQuotes().TakeLast(MaxCandlesPerTimeframe);
                }

                var userMessage = BuildUserMessage(quotes);
                _conversationHistory.Add(new UserChatMessage(userMessage));
                TrimConversationHistory();
                var aiResponse = await GetAIResponseAsync(cancel);
                _conversationHistory.Add(new AssistantChatMessage(aiResponse));
                var signal = ParseAIResponse(aiResponse, indicators);
                return signal;
            }
            catch (Exception ex)
            {
                indicators.Add(new StrategyIndicator("Error", ex.Message));
                return NoSignal(indicators, "AI Error");
            }
        }

        private Task InitializeConversationAsync()
        {
            const string initMessage = """
                You are AI trading ultimate instace. Rules:
                - Produce ONLY JSON response in the following format: 
                {
                    "signal": "LONG|SHORT|NONE",
                    "confidence": 0-100,
                    "entry_price": number,
                    "stop_loss": number,
                    "take_profit": number,
                    "quantity": number,
                    "reason": "string",
                    "requested_indicators": ["i1","i2",...],
                    "requested_candles": ["1D|10","4H|20",...]
                }
                - Signal only when confidence >= 70
                - Risk: max 5% of balance {{balance}} USDT
                - Choose indicators: {{indicators}}
                - Available timeframes: 1D,4H,1H,15M,5M,1M
                - Candles format {timeframe}|{number}
                """;

            var compressedIndicators = string.Join(",", _indicatorCalculators.Keys
                .Select(abbr => IndicatorAbbreviations.TryGetValue(abbr, out string? value) ? $"{abbr}({value})" : abbr));

            var finalMessage = initMessage
                .Replace("{{balance}}", WalletManager.Contract.WalletBalance.ToString())
                .Replace("{{indicators}}", compressedIndicators);

            _conversationHistory.Add(new SystemChatMessage(finalMessage));
            return Task.CompletedTask;
        }

        private static readonly Dictionary<string, string> IndicatorAbbreviations = new()
        {
            ["EMA"] = "E",
            ["MACD"] = "M",
            ["RSI"] = "R",
            ["ATR"] = "A",
            ["ADX"] = "D",
            ["BollingerBands"] = "BB",
            ["Stochastic"] = "S",
            ["Ichimoku"] = "I",
            ["VWAP"] = "V"
        };

        private void TrimConversationHistory()
        {
            // Keep system message and last 4 messages
            if (_conversationHistory.Count > MaxConversationHistory)
            {
                var systemMessage = _conversationHistory[0];
                _conversationHistory.Clear();
                _conversationHistory.Add(systemMessage);
                _conversationHistory.AddRange(_conversationHistory
                    .Where(m => m is UserChatMessage || m is AssistantChatMessage)
                    .TakeLast(MaxConversationHistory - 1));
            }
        }

        private string BuildUserMessage(Dictionary<TimeFrame, IEnumerable<Quote>> quotes)
        {
            var request = new StringBuilder();
            request.AppendLine($"SYM:{Symbol} | BAL:{WalletManager.Contract.WalletBalance:F0} | PRC:{Ticker?.LastPrice ?? 0:F2}");
            request.AppendLine();

            request.AppendLine("IND:");
            foreach (var indicator in _activeIndicators)
            {
                try
                {
                    var value = CalculateIndicator(indicator, quotes[TimeFrame.OneHour]);
                    request.AppendLine($"{GetIndicatorAbbreviation(indicator)}:{FormatIndicatorValue(value)}");
                }
                catch
                {
                    request.AppendLine($"{GetIndicatorAbbreviation(indicator)}:ERR");
                }
            }
            request.AppendLine();

            request.AppendLine("CANDLES:");
            var priorityTimeframes = new Dictionary<TimeFrame, int>();

            foreach (var candleRequest in _activeCandles)
            {
                try
                {
                    var parts = candleRequest.Split('|');
                    var timeframe = ParseTimeFrame(parts[0]);
                    var count = int.Parse(parts[1]);

                    // Walidacja ilości świec
                    count = Math.Clamp(count, 1, MaxCandlesPerTimeframe);
                    priorityTimeframes[timeframe] = count;
                }
                catch
                {
                    // Ignoruj błędne formaty
                }
            }

            // Domyślna konfiguracja jeśli AI nie podało
            if (priorityTimeframes.Count == 0)
            {
                priorityTimeframes = new Dictionary<TimeFrame, int>
                {
                    [TimeFrame.OneDay] = 10,
                    [TimeFrame.OneHour] = 20,
                    [TimeFrame.FiveMinutes] = 50
                };
            }

            // Generowanie danych świecowych
            foreach (var (timeframe, count) in priorityTimeframes)
            {
                if (quotes.TryGetValue(timeframe, out var tfQuotes))
                {
                    var candles = tfQuotes.TakeLast(count);
                    request.Append($"{GetTimeframeAbbreviation(timeframe)}|{count}=");

                    foreach (var quote in candles)
                    {
                        // Format: timestamp|O,H,L,C,V
                        request.Append($"{quote.Date:yyyyMMddHHmm}|");
                        request.Append($"{quote.Open:F0},");
                        request.Append($"{quote.High:F0},");
                        request.Append($"{quote.Low:F0},");
                        request.Append($"{quote.Close:F0},");
                        request.Append($"{quote.Volume:F0};");
                    }
                    request.AppendLine();
                }
            }

            // Instrukcja końcowa
            request.AppendLine("GENERATE NEXT SIGNAL");

            return request.ToString();
        }

        // Metody pomocnicze
        private static string FormatIndicatorValue(object value)
        {
            return value switch
            {
                EmaResult ema => ema.Ema?.ToString("F0") ?? "0",
                MacdResult macd => $"{macd.Macd?.ToString("F1")},{macd.Signal?.ToString("F1")}",
                RsiResult rsi => rsi.Rsi?.ToString("F0") ?? "0",
                SmaResult sma => sma.Sma?.ToString("F0") ?? "0",
                AtrResult atr => atr.Atr?.ToString("F1") ?? "0",
                AdxResult adx => adx.Adx?.ToString("F0") ?? "0",
                BollingerBandsResult bb => $"{bb.UpperBand?.ToString("F0")},{bb.LowerBand?.ToString("F0")}",
                StochResult stoch => $"{stoch.K?.ToString("F0")},{stoch.D?.ToString("F0")}",
                IchimokuResult ichi => $"{ichi.TenkanSen?.ToString("F0")},{ichi.KijunSen?.ToString("F0")}",
                VwapResult vwap => vwap.Vwap?.ToString("F0") ?? "0",
                _ => value.ToString()?[..Math.Min(10, value.ToString()?.Length ?? 0)] ?? "N/A"
            };
        }

        private static string GetTimeframeAbbreviation(TimeFrame tf) => tf switch
        {
            TimeFrame.OneDay => "1D",
            TimeFrame.FourHours => "4H",
            TimeFrame.OneHour => "1H",
            TimeFrame.FifteenMinutes => "15m",
            TimeFrame.FiveMinutes => "5m",
            TimeFrame.OneMinute => "1m",
            _ => tf.ToString()
        };

        private static string GetIndicatorAbbreviation(string indicator) => indicator switch
        {
            "EMA" => "E",
            "MACD" => "M",
            "RSI" => "R",
            "ATR" => "A",
            "ADX" => "D",
            "BollingerBands" => "BB",
            "Stochastic" => "STO",
            "Ichimoku" => "ICH",
            "VWAP" => "VW",
            "Volume" => "VOL",
            _ => indicator
        };

        private object CalculateIndicator(string name, IEnumerable<Quote> quotes)
        {
            if (_indicatorCalculators.TryGetValue(name, out var calculator))
            {
                return calculator.Invoke(quotes);
            }
            return $"Indicator '{name}' not available";
        }

        private async Task<string> GetAIResponseAsync(CancellationToken cancel)
        {
            var options = new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500,
                FrequencyPenalty = 0.2f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                StopSequences = { "\n```", "```json", "}\n" }
            };

            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            return response.Value.Content[0].Text.Trim();
        }

        private static TimeFrame ParseTimeFrame(string tf) => tf.ToUpper() switch
        {
            "1D" => TimeFrame.OneDay,
            "4H" => TimeFrame.FourHours,
            "1H" => TimeFrame.OneHour,
            "15M" => TimeFrame.FifteenMinutes,
            "5M" => TimeFrame.FiveMinutes,
            "1M" => TimeFrame.OneMinute,
            _ => throw new ArgumentException($"Unknown timeframe: {tf}")
        };
        private SignalEvaluation ParseAIResponse(string response, List<StrategyIndicator> indicators)
        {
            try
            {
                var result = JsonSerializer.Deserialize<AISignalResponse>(response);
                if (result == null)
                    return NoSignal(indicators, "Failed to parse AI response");

                indicators.Add(new StrategyIndicator("AI-Confidence", $"{result.Confidence}%"));
                indicators.Add(new StrategyIndicator("AI-Reason", result.Reason));
                UpdateActiveIndicators(result.RequestedIndicators);
                _activeCandles = result.RequestedCandles ?? _activeCandles;

                if (result.Confidence < 70)
                    return NoSignal(indicators, $"Low confidence: {result.Confidence}%");

                if (result.Signal == "LONG")
                {
                    return GenerateSignal(
                        isLong: true,
                        entryPrice: result.EntryPrice ?? Ticker?.BestAskPrice ?? 0,
                        stopLoss: result.StopLoss,
                        takeProfit: result.TakeProfit,
                        quantity: result.Quantity,
                        indicators: indicators);
                }

                if (result.Signal == "SHORT")
                {
                    return GenerateSignal(
                        isLong: false,
                        entryPrice: result.EntryPrice ?? Ticker?.BestBidPrice ?? 0,
                        stopLoss: result.StopLoss,
                        takeProfit: result.TakeProfit,
                        quantity: result.Quantity,
                        indicators: indicators);
                }

                return NoSignal(indicators, "No signal from AI");
            }
            catch (Exception ex)
            {
                return NoSignal(indicators, $"Invalid AI response: {ex.Message}");
            }
        }

        private void UpdateActiveIndicators(List<string>? requestedIndicators)
        {
            if (requestedIndicators != null)
            {
                foreach (var indicator in requestedIndicators)
                {
                    if (_indicatorCalculators.ContainsKey(indicator)
                        && !_activeIndicators.Contains(indicator))
                    {
                        _activeIndicators.Add(indicator);
                    }
                }
                _activeIndicators = [.. _activeIndicators.Distinct()];
            }
        }

        private SignalEvaluation GenerateSignal(bool isLong, decimal entryPrice, decimal stopLoss, decimal takeProfit, decimal quantity, List<StrategyIndicator> indicators)
        {
            TakeProfitPrice = takeProfit;
            StopLossPrice = stopLoss;
            if (isLong)
            {
                DynamicQtyLong = quantity;
                indicators.Add(new StrategyIndicator("Signal", "LONG"));
            }
            else
            {
                DynamicQtyShort = quantity;
                indicators.Add(new StrategyIndicator("Signal", "SHORT"));
            }

            return new SignalEvaluation(
                isLong,
                !isLong,
                false,
                false,
                [.. indicators]);
        }

        private static SignalEvaluation NoSignal(List<StrategyIndicator> indicators, string reason)
        {
            indicators.Add(new StrategyIndicator("Reason", reason));
            return new SignalEvaluation(false, false, false, false, [.. indicators]);
        }
    }

    public class AISignalResponse
    {
        public string Signal { get; set; } = "NONE";
        public int Confidence { get; set; }
        public decimal? EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<string>? RequestedIndicators { get; set; }
        public List<string>? RequestedCandles { get; set; }
    }
}
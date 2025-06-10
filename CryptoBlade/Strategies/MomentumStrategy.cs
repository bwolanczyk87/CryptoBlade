using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy : TradingStrategyBase
    {
        private const int MaxConversationHistory = 5;
        private const int MaxCandlesPerTimeframe = 100;
        private readonly OpenAIClient _deepSeekClient;
        private readonly List<ChatMessage> _conversationHistory = new();
        private readonly Dictionary<string, Func<IEnumerable<Quote>, object>> _indicatorCalculators;
        private List<string> _activeIndicators = new() { "EMA", "MACD", "RSI", "Volume", "ATR" };
        private bool _isInitialized = false;

        public MomentumStrategy(
            IOptions<MomentumStrategyOptions> strategyOptions,
            IOptions<TradingBotOptions> botOptions,
            string symbol,
            IWalletManager walletManager,
            ICbFuturesRestClient restClient,
            DeepSeekAccountConfig deepSeekConfig)
            : base(strategyOptions as IOptions<TradingStrategyBaseOptions>, botOptions, symbol, BuildTimeFrameWindows(), walletManager, restClient)
        {
            // Initialize DeepSeek client
            var account = deepSeekConfig.Accounts.FirstOrDefault(a => a.ApiName == symbol.ToLower());
            if (account == null)
                throw new Exception($"DeepSeek account not found for symbol {symbol}");

            var clientOptions = new OpenAIClientOptions
            {
                // Ustawienie endpointa DeepSeek
                Endpoint = new Uri("https://api.deepseek.com")
            };
            
            _deepSeekClient = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                clientOptions
            );
            
            // Register available indicators
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

        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;

        private static TimeFrameWindow[] BuildTimeFrameWindows()
        {
            return new[]
            {
                new TimeFrameWindow(TimeFrame.OneDay, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.FourHours, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.OneHour, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.FifteenMinutes, MaxCandlesPerTimeframe, false),
                new TimeFrameWindow(TimeFrame.FiveMinutes, MaxCandlesPerTimeframe, false),
                new TimeFrameWindow(TimeFrame.OneMinute, MaxCandlesPerTimeframe, false)
            };
        }

        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var indicators = new List<StrategyIndicator>();

            try
            {
                if (IsInTrade)
                    return NoSignal(indicators, "Already in trade");

                // Initialize conversation on first run
                if (!_isInitialized)
                {
                    await InitializeConversationAsync();
                    _isInitialized = true;
                }

                // Get data from multiple timeframes
                var quotes = new Dictionary<TimeFrame, IEnumerable<Quote>>();
                foreach (var timeframe in QuoteQueues.Keys)
                {
                    quotes[timeframe] = QuoteQueues[timeframe].GetQuotes().TakeLast(MaxCandlesPerTimeframe);
                }

                // Prepare data for AI
                var userMessage = BuildUserMessage(quotes);

                // Add user message to history
                _conversationHistory.Add(new UserChatMessage(userMessage));

                // Trim conversation history
                TrimConversationHistory();

                // Get AI response
                var aiResponse = await GetAIResponseAsync(cancel);

                // Add AI response to history
                _conversationHistory.Add(new AssistantChatMessage(aiResponse));

                // Parse the response
                var signal = ParseAIResponse(aiResponse, indicators);

                // Update active indicators based on AI request
                UpdateActiveIndicators(aiResponse);

                return signal;
            }
            catch (Exception ex)
            {
                indicators.Add(new StrategyIndicator("Error", ex.Message));
                return NoSignal(indicators, "AI Error");
            }
        }

        private async Task InitializeConversationAsync()
        {
            var initMessage = new StringBuilder();
            initMessage.AppendLine("Jesteś ekspertem tradingowym specjalizującym się w kryptowalutach. Twoje zadanie:");
            initMessage.AppendLine("- Analizuj dane rynkowe w czasie rzeczywistym");
            initMessage.AppendLine("- Generuj sygnały w formacie JSON");
            initMessage.AppendLine("- Możesz żądać dodatkowych wskaźników");
            initMessage.AppendLine();
            initMessage.AppendLine("Zasady:");
            initMessage.AppendLine("- Tylko 1 aktywny sygnał (LONG lub SHORT)");
            initMessage.AppendLine("- Confidence < 70 = brak sygnału");
            initMessage.AppendLine("- Ryzyko: max 2% kapitału na transakcję");
            initMessage.AppendLine();
            initMessage.AppendLine("Dostępne timeframe:");
            initMessage.AppendLine("- 1D, 4H, 1H, 15M, 5M, 1M");
            initMessage.AppendLine();
            initMessage.AppendLine("Dostępne wskaźniki:");
            foreach (var indicator in _indicatorCalculators.Keys)
            {
                initMessage.AppendLine($"- {indicator}");
            }
            initMessage.AppendLine();
            initMessage.AppendLine("Format odpowiedzi:");
            initMessage.AppendLine("{");
            initMessage.AppendLine("  \"signal\": \"LONG|SHORT|NONE\",");
            initMessage.AppendLine("  \"confidence\": 0-100,");
            initMessage.AppendLine("  \"entry_price\": number,");
            initMessage.AppendLine("  \"stop_loss\": number,");
            initMessage.AppendLine("  \"take_profit\": number,");
            initMessage.AppendLine("  \"quantity\": number,");
            initMessage.AppendLine("  \"reason\": \"string\",");
            initMessage.AppendLine("  \"requested_indicators\": [\"indicator1\", \"indicator2\"]");
            initMessage.AppendLine("}");

            _conversationHistory.Add(new UserChatMessage(initMessage.ToString()));
        }

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
            request.AppendLine("### Kontekst");
            request.AppendLine($"- Symbol: {Symbol}");
            request.AppendLine($"- Balance: {WalletManager.Contract.WalletBalance:F2} USDT");
            request.AppendLine($"- Aktualna cena: {Ticker?.LastPrice ?? 0:F2}");
            request.AppendLine();

            request.AppendLine("### Aktywne wskaźniki");
            foreach (var indicator in _activeIndicators)
            {
                try
                {
                    var latestValue = CalculateIndicator(indicator, quotes[TimeFrame.OneHour]);
                    request.AppendLine($"- {indicator}: {JsonSerializer.Serialize(latestValue)}");
                }
                catch (Exception ex)
                {
                    request.AppendLine($"- {indicator}: Error - {ex.Message}");
                }
            }
            request.AppendLine();

            request.AppendLine("### Dane historyczne (ostatnie 5 świec)");
            foreach (var tf in new[] { TimeFrame.OneDay, TimeFrame.OneHour, TimeFrame.FiveMinutes })
            {
                if (quotes.TryGetValue(tf, out var tfQuotes))
                {
                    request.AppendLine($"#### {tf} Timeframe");
                    foreach (var quote in tfQuotes.TakeLast(5))
                    {
                        request.AppendLine($"- {quote.Date:yyyy-MM-dd HH:mm} O:{quote.Open} H:{quote.High} L:{quote.Low} C:{quote.Close} V:{quote.Volume}");
                    }
                }
            }

            request.AppendLine();
            request.AppendLine("### Instrukcja");
            request.AppendLine("Wygeneruj sygnał handlowy w wymaganym formacie JSON. Jeśli potrzebujesz dodatkowych wskaźników, dodaj je do 'requested_indicators'.");

            return request.ToString();
        }

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
                FrequencyPenalty = 0.2f
            };

            // Konwersja historyi konwersacji na format SDK OpenAI
            var messages = _conversationHistory
                .Select(m => m.Role switch
                {
                    ChatMessageRole.User => new UserChatMessage(m.Content),
                    ChatMessageRole.Assistant => new AssistantChatMessage(m.Content),
                    _ => throw new NotSupportedException($"Role {m.Role} not supported")
                })
                .ToList();

            // Wykonanie zapytania do modelu
            var response = await _deepSeekClient.CompleteChatAsync(
                messages,
                "deepseek-chat", // Użyj najnowszego modelu DeepSeek
                options,
                cancel);

            var completion = response.Value;
            
            // Obsługa różnych scenariuszy zakończenia
            switch (completion.FinishReason)
            {
                case ChatFinishReason.Stop:
                    return completion.Content[0].Text;
                
                case ChatFinishReason.ToolCalls:
                    // W naszym przypadku nie oczekujemy wywołań funkcji
                    return "ERROR: Unexpected tool calls";
                
                case ChatFinishReason.Length:
                    return "ERROR: Response too long";
                
                case ChatFinishReason.ContentFilter:
                    return "ERROR: Content filtered";
                
                default:
                    return "ERROR: Unknown finish reason";
            }
        }

        private SignalEvaluation ParseAIResponse(string response, List<StrategyIndicator> indicators)
        {
            try
            {
                // Extract the JSON part
                int jsonStart = response.IndexOf('{');
                int jsonEnd = response.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < 0)
                    return NoSignal(indicators, "No JSON found in AI response");

                string jsonResponse = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

                var result = JsonSerializer.Deserialize<AISignalResponse>(jsonResponse);
                if (result == null)
                    return NoSignal(indicators, "Failed to parse AI response");

                indicators.Add(new StrategyIndicator("AI-Confidence", $"{result.Confidence}%"));
                indicators.Add(new StrategyIndicator("AI-Reason", result.Reason));

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

        private void UpdateActiveIndicators(string aiResponse)
        {
            try
            {
                int jsonStart = aiResponse.IndexOf('{');
                int jsonEnd = aiResponse.LastIndexOf('}');
                if (jsonStart < 0 || jsonEnd < 0)
                    return;

                string jsonResponse = aiResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var result = JsonSerializer.Deserialize<AISignalResponse>(jsonResponse);

                if (result?.RequestedIndicators != null)
                {
                    foreach (var indicator in result.RequestedIndicators)
                    {
                        if (_indicatorCalculators.ContainsKey(indicator)
                            && !_activeIndicators.Contains(indicator))
                        {
                            _activeIndicators.Add(indicator);
                        }
                    }

                    // Limit to 15 indicators to avoid too many
                    _activeIndicators = _activeIndicators
                        .Distinct()
                        .Take(15)
                        .ToList();
                }
            }
            catch
            {
                // Ignore
            }
        }

        private SignalEvaluation GenerateSignal(
            bool isLong,
            decimal entryPrice,
            decimal stopLoss,
            decimal takeProfit,
            decimal quantity,
            List<StrategyIndicator> indicators)
        {
            indicators.Add(new StrategyIndicator("Signal", isLong ? "LONG" : "SHORT"));
            return new SignalEvaluation(
                isLong,
                !isLong,
                false,
                false,
                indicators.ToArray());
        }

        private SignalEvaluation NoSignal(List<StrategyIndicator> indicators, string reason)
        {
            indicators.Add(new StrategyIndicator("Reason", reason));
            return new SignalEvaluation(false, false, false, false, indicators.ToArray());
        }
    }

    public class AISignalResponse
    {
        public string Signal { get; set; } = "NONE"; // LONG, SHORT, NONE
        public int Confidence { get; set; }
        public decimal? EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal Quantity { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<string>? RequestedIndicators { get; set; }
    }
}
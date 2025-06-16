using CryptoBlade.Services;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CryptoBlade.Strategies.AI
{
    public class ChatAI
    {
        private const int MaxConversationHistory = 7;
        private readonly ChatClient _chatClient;
        private readonly List<ChatMessage> _conversationHistory = [];
        private readonly ILogger<ChatAI> _logger;

        public ChatAI(DeepSeekAccountConfig config, string symbol, ILogger<ChatAI> logger)
        {
            var account = config.Accounts.FirstOrDefault(a => a.ApiName == "btcusdt")
                ?? throw new Exception($"DeepSeek account not found for symbol {symbol}");

            var client = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
            );

            _chatClient = client.GetChatClient("deepseek-chat");
            _logger = logger;
        }

        public void InitializeConversation(decimal? leverage, decimal balance)
        {
            const string initMessage = """
                You are AI-Crypto-Ultimate-Bot.
                Primary goal: maximise net profit over time.
                Reply ONLY with one JSON object:

                {
                  "Signal": "LONG | SHORT | NONE", // if Confidence ≤ 70 then Signal=NONE
                  "Confidence": 0-100,
                  "EntryPrice": num,
                  "StopLoss": num,
                  "TakeProfit": num,
                  "Quantity": num, // Quantity × EntryPrice × (1/{LEVERAGE}) ≤ 0.05 (0.1 if Confidence ≥ 90) × {BALANCE}
                  "Reason": string, // ≤ 200 chars, if Signal≠NONE = trade rationale, else = brief note for next cycle
                  "Candles": ["TF|count", …], // choose ≥ 2 TF, total count ≤ 120
                  "Indicators": ["Name(params)|TF", …], // choose ≤ 10 indicators from Skender.Stock.Indicators v2.6.1 (GetXxx → Name)
                  "Pivots": ["TF|EndType?|percent", …]        // ZigZag, e.g. "4H|1.2"  or "1H|H|0.8"
                }

                • Allowed TF: 1M,5M,15M,1H,4H,1D.
                • Use dot as decimal separator.
                • “Name(params)” must match a public Get-method without the “Get” prefix (e.g. "Rsi(14)", "Macd(12,26,9)").
                • Optional EndType: add '|H|' for HighLow (default C = Close).
                • percent = positive decimal, dot separator (e.g. 0.8, 1.3).
                • Request Candles, Indicators and Pivots to increase Confidence and optimize trade.
                """;

            var message = initMessage
                .Replace("{LEVERAGE}", leverage?.ToString("F0") ?? "0")
                .Replace("{BALANCE}", balance.ToString("F2", CultureInfo.InvariantCulture));
            _conversationHistory.Add(new SystemChatMessage(message));
        }

        public void AddUserMessage(string message)
        {
            _conversationHistory.Add(new UserChatMessage(message));
            TrimConversationHistory();
        }

        public void AddAssistantMessage(string message) =>
            _conversationHistory.Add(new AssistantChatMessage(message));

        public async Task<string> GetAIResponseAsync(string userMessage, CancellationToken cancel)
        {
            _conversationHistory.Add(new UserChatMessage(userMessage));
            foreach (var item in _conversationHistory)
            {
                _logger.LogInformation(item.Content[0].Text);
            }

            TrimConversationHistory();


            var options = new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500,
                FrequencyPenalty = 0.2f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                StopSequences = { "}" }
            };

            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            var aiResponse = response.Value.Content[0].Text.Trim();
            _logger.LogInformation(aiResponse);

            _conversationHistory.Add(new AssistantChatMessage(aiResponse));
            return aiResponse;
        }

        private int CountChar(string str, char c) => str.Count(ch => ch == c);

        private void TrimConversationHistory()
        {
            if (_conversationHistory.Count <= MaxConversationHistory)
                return;

            var systemMessage = _conversationHistory[0];
            var recentMessages = _conversationHistory
                .Where(m => m is UserChatMessage || m is AssistantChatMessage)
                .TakeLast(MaxConversationHistory)
                .ToList();

            _conversationHistory.Clear();
            _conversationHistory.Add(systemMessage);
            _conversationHistory.AddRange(recentMessages);
        }
    }
}

using CryptoBlade.Services;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace CryptoBlade.Strategies.AI
{
    public class ChatAI
    {
        private const int MaxConversationHistory = 7;
        private readonly ChatClient _chatClient;
        private readonly List<ChatMessage> _conversationHistory = new();

        public ChatAI(DeepSeekAccountConfig config, string symbol)
        {
            var account = config.Accounts.FirstOrDefault(a => a.ApiName == symbol.ToLower())
                ?? throw new Exception($"DeepSeek account not found for symbol {symbol}");

            var client = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
            );

            _chatClient = client.GetChatClient("deepseek-chat");
        }

        public void InitializeConversation(decimal balance, string indicators, decimal? leverage)
        {
            const string initMessage = """
                You are AI trading ultimate instance. Rules:
                - Produce ONLY JSON response in the following format: 
                {{
                    "Signal": "LONG|SHORT|NONE",
                    "Confidence": 0-100,
                    "EntryPrice": number,
                    "StopLoss": number,
                    "TakeProfit": number,
                    "Quantity": number,
                    "Reason": "string",
                    "DataDelay": number,
                    "RequestedIndicators": ["i1|TF|p1,p2","i2|TF|p1",...],
                    "RequestedCandles": ["TF|count",...]
                }}
                - Signal only when confidence >= 70
                - Risk: max 5% of balance {0} USDT, Leverage: {1}
                - Choose indicators: {2}
                - Available timeframes (TF): 1D,4H,1H,15M,5M,1M
                - Data candle timeframe marker: TF|count (e.g. 1H|12)
                - Data candle format: yyyyMMddHHmm|open,high,low,close,volume (e.g. 1H|12=202506111000
                - RequestedCandles format: TF|count (e.g.5M|80, 1H|20)
                - Data indicatos format:
                    MACD: Macd,FastEma,SlowEma,Signal
                    BB: UpperBand,LowerBand
                    ICH: TenkanSen,KijunSen
                - RequestedIndicators format: name|TF|params (e.g. E|5M|80, BB|1H|8.3,2)
                - Data will arrive after the selected DataDelay period (1-60 m)
                """;

            var message = string.Format(initMessage, balance, leverage.Value, indicators);
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
            TrimConversationHistory();

            var options = new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500,
                FrequencyPenalty = 0.2f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
                StopSequences = { "\n```", "```json", "}\n" }
            };

            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            var aiResponse = response.Value.Content[0].Text.Trim();

            _conversationHistory.Add(new AssistantChatMessage(aiResponse));
            return aiResponse;
        }

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

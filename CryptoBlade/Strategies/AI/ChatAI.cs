using CryptoBlade.Services;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
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
            var account = config.Accounts.FirstOrDefault(a => a.ApiName == symbol.ToLower())
                ?? throw new Exception($"DeepSeek account not found for symbol {symbol}");

            var client = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
            );

            _chatClient = client.GetChatClient("deepseek-chat");
            _logger = logger;
        }

        public void InitializeConversation(decimal balance, string indicators, decimal? leverage)
        {
            const string initMessage = """
                You are AI trading ultimate instance. Goal: 100000 USDT. Rules:
                - Produce ONLY JSON response in the following format, add closing bracket): 
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

                - Available timeframes (TF): 1D,4H,1H,15M,5M
                - Data candle timeframe marker: TF,count (e.g. 1H,12)
                - Data candle format: yyyyMMddHHmm,open,high,low,close,volume (e.g. 202506111000,21,21.9,19.2, 21, 100)
                - RequestedCandles format: TF,count (e.g.5M,80)
                - Data indicatos format:
                    MACD: Macd,FastEma,SlowEma,Signal
                    BB: UpperBand,LowerBand
                    ICH: TenkanSen,KijunSen
                - RequestedIndicators format: name|TF|params (e.g. E|5M|80, BB|1H|8.3,2) [{2}]
                - Next Data pagkage after choose by you DataDelay (1-60m)
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
                StopSequences = { "\n```", "```json", "}\n" }
            };

            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            var aiResponse = response.Value.Content[0].Text.Trim();
            aiResponse = FixIncompleteJson(aiResponse);
            _logger.LogInformation(aiResponse);

            _conversationHistory.Add(new AssistantChatMessage(aiResponse));
            return aiResponse;
        }

        private int CountChar(string str, char c) => str.Count(ch => ch == c);

        private string FixIncompleteJson(string json)
        {
            // Proste przypadki: brakujący zamykający nawias
            if (!json.Trim().EndsWith("}") && json.Trim().StartsWith("{"))
            {
                json = json.Trim() + "}";
            }

            try
            {
                // Walidacja struktury przy użyciu System.Text.Json
                using var doc = JsonDocument.Parse(json);
                return json;
            }
            catch (JsonException)
            {
                // Zaawansowana naprawa przy użyciu regex
                var fixedJson = Regex.Replace(json, @"(""[^""]+""\s*:\s*)([^,{}\n]+)(?=[^\}]*$)", "$1\"$2\"");

                // Dodaj brakujące zamknięcia
                if (!fixedJson.Trim().EndsWith("}")) fixedJson += "}";
                if (CountChar(fixedJson, '{') > CountChar(fixedJson, '}'))
                {
                    fixedJson += new string('}', CountChar(fixedJson, '{') - CountChar(fixedJson, '}'));
                }

                // Walidacja po naprawie
                try
                {
                    using var doc = JsonDocument.Parse(fixedJson);
                    return fixedJson;
                }
                catch
                {
                    // Awaryjny JSON gdy naprawa niemożliwa
                    return @"{
                        ""Signal"": ""NONE"",
                        ""Confidence"": 0,
                        ""EntryPrice"": 0,
                        ""StopLoss"": 0,
                        ""TakeProfit"": 0,
                        ""Quantity"": 0,
                        ""Reason"": ""Invalid JSON format"",
                        ""DataDelay"": 5,
                        ""RequestedIndicators"": [],
                        ""RequestedCandles"": []
                    }";
                }
            }
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

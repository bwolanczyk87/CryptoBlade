using CryptoBlade.Models;
using CryptoBlade.Services;
using OpenAI;
using OpenAI.Chat;
using SharpToken;
using System;
using System.ClientModel;
using System.Globalization;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

namespace CryptoBlade.Strategies.AI
{
    public class ChatAI
    {
        private readonly ChatClient _chatClient;
        private readonly List<ChatMessage> _conversationHistory = [];
        private readonly ILogger<ChatAI> _logger;
        private readonly string _symbol;

        public ChatAI(DeepSeekAccountConfig config, string symbol, ILogger<ChatAI> logger)
        {
            var account = config.Accounts.FirstOrDefault(a => a.ApiName == "btcusdt")
                ?? throw new Exception($"DeepSeek account not found for symbol {symbol}");

            var client = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
            );

            _chatClient = client.GetChatClient("deepseek-reasoner");
            _logger = logger;
            _symbol = symbol;
        }

        public void InitializeConversation(string styleName, SymbolInfo symbolInfo, decimal balance)
        {
            const string initMessage = """
                Date {DATE}, symbol {SYMBOL}.
                You are {STYLE} AI-Crypto, a hyper-focused, chart-obsessed {STYLE} genius.
                Find momentum, spot reversals, price formations, volume spikes, and micro-trends.
                Wait for strong signals, ignore noise and avoid overtrading.
                Limit reasoning_content to max 600 tokens and final answer to max 200 tokens. Be concise.

                Return exactly one JSON:
                {
                "Signal":"LONG|SHORT|NONE",
                "Confidence":0-100,
                "StopLoss": decimal,
                "Reason":str,
                "Delay":minutes,
                }
                Rules:
                -Signal: LONG/SHORT if Confidence>80
                -TakeProfit is 0.5 of StopLoss
                -Reason: <300 chars
                -Place StopLoss at the nearest significant local support (for LONG) or resistance (for SHORT) level.
                -Candles header: TF|MMdd=  (e.g. 1M|0624=)
                -Candles body: HHmm,O,H,L,C,V; (e.g. 5M|0624|37280=6,7.2,5.8,6.1,800;)
                -Indicators: TF|ind(params)=  (e.g. 5M|Rsi(7)=) 
                -Pivots: HHmm,zigzag,pointType; (e.g. 1200,6.1,H;1210,6,L)
                -Delay: 1-15min, learn on previous signals and predict next best evaluation point
                """;

            var message = initMessage
                .Replace("{SYMBOL}", _symbol)
                .Replace("{STYLE}", styleName)
                .Replace("{DATE}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"));  
            _conversationHistory.Add(new SystemChatMessage(message));
        }

        public void AddUserMessage(string message)
        {
            _conversationHistory.Add(new UserChatMessage(message));
        }

        public void AddAssistantMessage(string message) =>
            _conversationHistory.Add(new AssistantChatMessage(message));

        public async Task<string> GetAIResponseAsync(string userMessage, CancellationToken cancel)
        {
            var totalTokens = 0;
            _conversationHistory.Add(new UserChatMessage(userMessage));

            var prompt = "**********************************************************************\n";
            prompt += $"PROMPT SENDED TO AI FROM {_symbol}:";


            foreach (var item in _conversationHistory)
            {
                var text = item.Content[0].Text;
                prompt += text + "\n\n";
                totalTokens += CountTokens(text);
            }
            prompt += $"TOTAL TOKENS OF PROMPT: {totalTokens}\n\n";

            var options = new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 1000,
                FrequencyPenalty = 0.2f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var aiResponse = new StringBuilder();
            await foreach (StreamingChatCompletionUpdate update
               in _chatClient.CompleteChatStreamingAsync(_conversationHistory, options, cancel))
            {
                if (update.ContentUpdate.Count > 0)
                    aiResponse.Append(update.ContentUpdate[0].Text);
            }
            var response = aiResponse.ToString().Trim();

            var aiResponseWithDate =$"SYMBOL: {_symbol} | Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm}\n{response}";
            _conversationHistory.Add(new AssistantChatMessage(aiResponseWithDate));

            var finalLog = prompt + aiResponseWithDate;
            finalLog += "\n\n**********************************************************************";
            _logger.LogInformation(finalLog);
            return response;
        }

        private static int CountTokens(string prompt)
        {
            var enc = GptEncoding.GetEncoding("o200k_base");
            return enc.CountTokens(prompt);
        }

        public void TrimConversationHistory()
        {
            var systemMessage = _conversationHistory[0];
            var assistanceMessages = _conversationHistory
                .Where(m => m is AssistantChatMessage)
                .TakeLast(3)
                .ToList();

            _conversationHistory.Clear();
            _conversationHistory.Add(systemMessage);
            _conversationHistory.AddRange(assistanceMessages);
        }
    }
}

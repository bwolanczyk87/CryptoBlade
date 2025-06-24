using CryptoBlade.Models;
using CryptoBlade.Services;
using OpenAI;
using OpenAI.Chat;
using SharpToken;
using System.ClientModel;
using System.Globalization;
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

            _chatClient = client.GetChatClient("deepseek-chat");
            _logger = logger;
            _symbol = symbol;
        }

        public void InitializeConversation(SymbolInfo symbolInfo, decimal balance)
        {
            const string initMessage = """
                Date {DATE}.
                You are Scalping AI-Crypto, a hyper-focused, chart-obsessed scalping genius. 
                You hunt volatility, detect micro-signals, and act with surgical precision. 
                Find momentum, spot reversals, and seize every opportunity for symbol {SYMBOL}.

                Return exactly one JSON:
                {
                "Signal":"LONG|SHORT|NONE",
                "Confidence":0-100,
                "StopLoss": decimal,
                "TakeProfit":decimal,
                "Reason":str,
                "Delay":minutes,
                }
                Rules:
                -Signal LONG/SHORT if Confidence>75
                -Max Leverage is set
                -SL and TP tight, dot separator, {PRICE_SCALE}dp, 0 if NONE signal
                -Reason <300 chars
                -Delay 1-15min, next best opportunity to open MarketOrder comming after Delay minutes (according to your prediction)
                -Indicators use full price format
                -Candles use tick-delta format header+candle: TF|n|MMdd|Close0=HHmm,dO,dH,dL,dC,V; (e.g. 1M|15|0624|37280=1240,5,25,-15,10,800;)
                -Rebuild O/H/L/C by adding deltas to previous close (tick size = 10^-{PRICE_SCALE})
                -Pivots format MMddHHmm,zigzag,pointType; (e.g. 06241200,1,5;)
                """;

            var message = initMessage
                .Replace("{SYMBOL}", _symbol)
                .Replace("{LEVERAGE}", symbolInfo.MaxLeverage?.ToString("F0") ?? "0")
                .Replace("{BALANCE}", balance.ToString("F2", CultureInfo.InvariantCulture))
                .Replace("{PRICE_SCALE}", symbolInfo.PriceScale.ToString("F0", CultureInfo.InvariantCulture))
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
                MaxOutputTokenCount = 500,
                FrequencyPenalty = 0.2f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };
            
            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            var aiResponse = response.Value.Content[0].Text.Trim();

            var aiResponseWithDate =$"SYMBOL: {_symbol} | Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm}\n{aiResponse}";
            _conversationHistory.Add(new AssistantChatMessage(aiResponseWithDate));

            var finalLog = prompt + aiResponseWithDate;
            finalLog += "\n\n**********************************************************************";
            _logger.LogInformation(finalLog);
            return aiResponse;
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

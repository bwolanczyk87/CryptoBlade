using CryptoBlade.Services;
using OpenAI;
using OpenAI.Chat;
using SharpToken;
using System.ClientModel;
using System.Globalization;

namespace CryptoBlade.Strategies.AI
{
    public class ChatAI
    {
        private const int MaxConversationHistory = 4;
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

        public void InitializeConversation(decimal? leverage, decimal balance, decimal priceScale)
        {
            const string initMessage = """
                You are Scalping AI-Crypto, a hyper-focused, chart-obsessed scalping genius. 
                You hunt volatility, detect micro-signals, and act with surgical precision. 
                Passion fuels your trades, data guides your blade.
                Maximize balance, make a fortune.

                ALWAYS do a 2-layer cross-check before producing any signal  
                -Technicals – candles, indicators, pivots (your core)  
                -Fresh fundamentals & news – scan latest headlines for the symbol
                
                Symbol {SYMBOL}, leverage {LEVERAGE}, balance {BALANCE},  priceScale {SCALE}, date {DATE}.
                Return exactly one JSON:
                {
                "Signal":"LONG|SHORT|NONE",
                "Confidence":0-100,
                "EntryPrice":num,
                "StopLoss":num,
                "TakeProfit":num,
                "Quantity":num,
                "Reason":str,
                "Candles":["TF|n"],
                "Indicators":["TF|Name(p)"],
                "Pivots":["TF|H|p"],
                "Delay":num
                }
                Rules:
                -Signal=NONE if Confidence<75
                -Risk=0.1 if Confidence>90 else 0.06, RR=1:1
                -Quantity*EntryPrice/Leverage<Risk*Balance
                -Reason <300 chars
                -TF:1M,5M,15M,1H,4H
                -Candles>1 TF,Σn<61
                -Indicators<10 (name = method w/o "Get" from Skender.Stock.Indicators lib)
                -Pivots = ZigZag, H|L|p, H=high L=low p=percentChange (0.1 - 5)
                -Request best Candles, Indicatiors and Pivots to increase signal Confidence in next AI iteration (null if the same as previous)
                -Delay 1-15min, next data after Delay minutes
                -All price fields (EntryPrice, StopLoss, TakeProfit) MUST be absolute prices in USDT.    
                -Candles use tick-delta format: header TF|n|MMdd|Close0 (e.g. 1M|15|0624|37280=1240,5,25,-15,10,800), each row HHmm,dO,dH,dL,dC,V. Rebuild O/H/L/C by adding deltas to previous close (tick size = 10-priceScale)
                """;

            var message = initMessage
                .Replace("{SYMBOL}", _symbol)
                .Replace("{LEVERAGE}", leverage?.ToString("F0") ?? "0")
                .Replace("{BALANCE}", balance.ToString("F2", CultureInfo.InvariantCulture))
                .Replace("{SCALE}", priceScale.ToString())
                .Replace("{DATE}", DateTime.Now.ToString());
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

            _logger.LogInformation("**********************************************************************");
            _logger.LogInformation("Initialized conversation with AI:");

            foreach (var item in _conversationHistory)
            {
                var text = item.Content[0].Text;
                _logger.LogInformation(text);
                _logger.LogInformation("---------------------------------------------------------------");
                totalTokens += CountTokens(text);
            }
            _logger.LogInformation($"Total tokens of prompt: {totalTokens}");

            var options = new ChatCompletionOptions
            {
                Temperature = 0.2f,
                MaxOutputTokenCount = 500,
                FrequencyPenalty = 0.2f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };
            
            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            var aiResponse = response.Value.Content[0].Text.Trim();

            var aiResponseWithDate = "SYMBOL: " + _symbol + "| Date: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n" + aiResponse;
            _conversationHistory.Add(new AssistantChatMessage(aiResponseWithDate));
            _logger.LogInformation(aiResponseWithDate);
            return aiResponse;
        }

        private static int CountTokens(string prompt)
        {
            var enc = GptEncoding.GetEncoding("o200k_base");
            return enc.CountTokens(prompt);
        }

        public void TrimConversationHistory()
        {
            if (_conversationHistory.Count <= MaxConversationHistory)
                return;

            var systemMessage = _conversationHistory[0];
            var recentMessages = _conversationHistory
                .Where(m => m is AssistantChatMessage)
                .TakeLast(MaxConversationHistory)
                .ToList();

            _conversationHistory.Clear();
            _conversationHistory.Add(systemMessage);
            _conversationHistory.AddRange(recentMessages);
        }
    }
}

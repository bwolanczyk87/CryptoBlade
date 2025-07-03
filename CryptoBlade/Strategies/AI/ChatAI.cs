// =========================================================
// 2.  ZMODYFIKOWANA KLASA ChatAI
// =========================================================
using CryptoBlade.Models;
using OpenAI;
using OpenAI.Chat;
using SharpToken;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;

namespace CryptoBlade.Strategies.AI
{
    public class ChatAI
    {
        private readonly ChatClient _chatClient;
        private readonly List<ChatMessage> _conversationHistory = [];
        private readonly ILogger<ChatAI> _logger;
        private readonly AiAccount _account;

        public ChatAI(AiAccount account, ILogger<ChatAI> logger)
        {
            _account = account;
            _logger = logger;

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(account.Url),
                Transport = new HttpClientPipelineTransport(new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(account.HttpTimeout)
                })
            };

            var openAi = new OpenAIClient(new ApiKeyCredential(account.ApiKey), clientOptions);
            _chatClient = openAi.GetChatClient(account.ModelId);
        }

        public void AddSystemMessage(string styleName, string symbol)
        {
            const string fullInitTemplate = """
                Date {DATE}. Symbol {SYMBOL}. Style: {STYLE}
                You are a hyper-focused crypto-trader AI. Hunt 5-15 min impulses, avoid noise.
                Trade directionally with RR>=1:1 and hit rate >=55%.

                ## DATA DICTIONARY  (all tags are placed in the USER block each cycle)
                OHLCV:           TF|MMDD=HHmm,O,H,L,C,V;...  (5m|0627=1300,0.6048,...)
                Indicators:      TF|Name(params)=values  (5m|Vwap()=0.6049)
                Pivots:          TF|H|p = MMDDHHmm,Price,Type;...  (5m|H|0.20=06271305,0.6047,L;...)
                OrderBook:       AvgSpread,Imb0.05pct,WallBid,WallAsk,TopTurnover60s
                VolumeFlow:      5mCVD, 1mBuyVol, 1mSellVol
                RiskMetrics:     5mATR, Funding8h, OIDelta5m
                BTCBias:         BTCDelta5m, Corr30d
                NewsImpact:      NONE|MEDIUM|HIGH
                Position:        Status(FLAT|LONG|SHORT), Last3PnL(+0.3,-0.1,+0.5)
                ------------------------------------------------------------------------------

                ## QUALITY GATES  – if ANY fails => Signal=NONE, Confidence<=70
                1. AvgSpread <= 0.00025   (about 0.04 pct)
                2. 5mATR    >= 0.0008     (min volatility)
                3. VolumeFlow: 1mBuyVol + 1mSellVol >= 8000 USDT
                4. NewsImpact must not be HIGH
                5. No LONG when BTCDelta5m <= -0.2 pct, no SHORT when BTCDelta5m >= +0.2 pct

                ## PATTERN LIBRARY  (trigger requires >=1 PRIMARY + >=2 CONFLUENCE)
                PRIMARY:
                  P1  VWAP-Bounce          | Price rejects VWAP +/-1 sigma with Buy/Sell spike
                  P2  EMA20-50 Cross       | Fresh cross on 5 m, slope >=30 deg
                  P3  Pivot-Retest         | Price retests 5 m ZigZag(0.35 pct) pivot after BO
                  P4  Momentum Burst       | 5 m body >=0.6*ATR AND CVD spike >=2 sigma
                CONFLUENCE:
                  C1  OrderBook Imbalance >= +15 pct with trade
                  C2  OIDelta5m >= +1 pct (long) or <= -1 pct (short)
                  C3  Funding aligns (neg->long, pos->short)
                  C4  BTCBias supports dir && Corr30d >=0.5

                ## SCORING (max 30)
                +10  PRIMARY present
                +5   each CONFLUENCE (max 3)
                +5   All Quality Gates pass AND AvgSpread <=0.00018
                +5   StopLoss behind wall (>=30 s) or pivot extreme
                +5   TakeProfit before opposite wall or +/-1 sigma VWAP

                Confidence = int((Score/30)*100)

                ## OUTPUT — return EXACTLY one JSON
                {
                 "Signal":"LONG|SHORT|NONE",
                 "Confidence":0-100,
                 "EntryPrice":decimal,
                 "StopLoss":decimal,
                 "TakeProfit":decimal,
                 "Reason":"<300 chars>",
                 "Delay":minutes(1-5)
                }

                ## RULES
                - Use LONG/SHORT only if Confidence >=90
                - Delay = next preferred evaluation time (usually 2)
                - Reason: pattern + key confluence (<=300 chars)
                """;

            const string initMessage = """
                Date {DATE}. Symbol {SYMBOL}. Style: {STYLE}
                You are a hyper-focused crypto-trader AI. Hunt 5-15 min impulses, avoid noise.
                Trade directionally with RR>=1:1 and hit rate >=55%. Wait for perfect conditions.

                Data dictionary divided by timeframes (TF):
                1. TimeFrame: TF 
                1.1. Candles:    MMDDHHmm,O,H,L,C,V;...  (06271300,0.6048,...)
                1.2. Indicators: Name(params)=values  (Vwap()=0.6049,...)
                1.3. Pivots:     H|p = MMDDHHmm,Price,Type;...  (H|0.20=06271305,0.6047,L;...)

                Return exactly one JSON:
                {
                "Signal":"LONG|SHORT|NONE",
                "Confidence":0-100,
                "EntryPrice":decimal,
                "StopLoss":decimal,
                "TakeProfit":decimal,
                "Reason":"<300 chars>",
                "Delay":5-30 minutes
                }
                Rules:
                -Signal: LONG/SHORT if Confidence>95
                -Reason: <300 chars
                -Delay: predict next best evaluation point
                """;

            var sysMsg = initMessage
                .Replace("{DATE}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"))
                .Replace("{SYMBOL}", symbol)
                .Replace("{STYLE}", styleName);

            _conversationHistory.Add(new SystemChatMessage(sysMsg));
        }      

        public void AddUserMessage(TradingStyleProfile styleProfile, Dictionary<TimeFrame, QuoteQueue> quoteQueues, SymbolInfo symbolInfo)
        {
            int numeration = 1;
            foreach (var frame in styleProfile.Frames)
            {
                TimeFramePrompt timeFramePrompt = new(symbolInfo, frame.Window.TimeFrame, numeration);
                var candles = quoteQueues[frame.Window.TimeFrame].GetQuotes();
                var message = timeFramePrompt.CreateUserMessage(candles, frame.CandlesCount, [.. frame.Indicators], [.. frame.Pivots]);

                _conversationHistory.Add(message);
                numeration++;
            }
        }
        public async Task<string> GetAIResponseAsync(string symbol, CancellationToken cancel)
        {
            var sbLog = new StringBuilder()
                .AppendLine("**********************************************************************")
                .AppendLine($"PROMPT SENT TO AI FROM {symbol}:");

            var promptTokens = 0;
            foreach (var msg in _conversationHistory)
            {
                var text = msg.Content[0].Text;
                sbLog.AppendLine(text).AppendLine();
                promptTokens += CountTokens(text);
            }
            sbLog.AppendLine($"TOTAL TOKENS IN PROMPT: {promptTokens}");

            var options = new ChatCompletionOptions
            {
                Temperature = _account.Temperature,
                MaxOutputTokenCount = _account.MaxOutputTokens,
                FrequencyPenalty = _account.FrequencyPenalty,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            var aiContent = response.Value.Content[0].Text.Trim();

            var datedReply = $"SYMBOL: {symbol} | Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm}\n{aiContent}";
            _conversationHistory.Add(new AssistantChatMessage(datedReply));


            sbLog.AppendLine(datedReply)
                 .AppendLine("**********************************************************************");
            _logger.LogInformation(sbLog.ToString());

            return aiContent;
        }

        private static int CountTokens(string text)
        {
            var enc = GptEncoding.GetEncoding("o200k_base");
            return enc.CountTokens(text);
        }

        public void TrimConversationHistory()
        {
            var system = _conversationHistory.First();
            var lastAi = _conversationHistory
                .Where(m => m is AssistantChatMessage)
                .TakeLast(3)
                .ToList();

            _conversationHistory.Clear();
            _conversationHistory.Add(system);
            _conversationHistory.AddRange(lastAi);
        }
    }
}

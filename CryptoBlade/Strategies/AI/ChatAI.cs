// =========================================================
// 2.  ZMODYFIKOWANA KLASA ChatAI
// =========================================================
using CryptoBlade.Models;
using CryptoBlade.Services;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using SharpToken;
using System;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CryptoBlade.Strategies.AI
{
    public class ChatAI
    {
        private readonly ChatClient _chatClient;
        private readonly List<ChatMessage> _conversationHistory = [];
        private readonly ILogger<ChatAI> _logger;
        private readonly string _symbol;
        private readonly AIModelOptions _modelOpts;

        /// <summary>
        /// Konstruuje ChatAI z wybranym modelem. 
        /// </summary>
        /// <param name="apiKey">Klucz do OpenAI / DeepSeek – zależnie od modelu.</param>
        /// <param name="modelId">Id modelu (deepseek-chat, deepseek-reasoner, gpt-4o-mini, gpt-4o).</param>
        /// <param name="symbol">Ticker (np. BTCUSDT) – tylko do logów / promptów.</param>
        /// <param name="logger">ILogger wstrzykiwany z DI.</param>
        /// <exception cref="ArgumentException">Gdy nieznany modelId.</exception>
        public ChatAI(DeepSeekAccountConfig config, string modelId, string symbol, ILogger<ChatAI> logger)
        {
            var account = config.Accounts.FirstOrDefault(a => a.ApiName == "btcusdt")
                ?? throw new Exception($"DeepSeek account not found for symbol {symbol}");

            var client = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri("https://api.deepseek.com") }
            );

            // 1. Pobierz preset.
            if (!AIModelCatalog.Defaults.TryGetValue(modelId, out _modelOpts))
                throw new ArgumentException($"Nieznany model LLM: {modelId}", nameof(modelId));

            // 2. Utwórz klienta OpenAI / DeepSeek (oba używają tej samej biblioteki).
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = _modelOpts.Endpoint,
                // wewnętrzny HttpClient jest dostępny przez właściwość Transport:
                Transport = new HttpClientPipelineTransport(new System.Net.Http.HttpClient
                {
                    Timeout = _modelOpts.HttpTimeout // ustaw timeout zgodnie z modelem
                })
            };

            var openAi = new OpenAIClient(
                new ApiKeyCredential(account.ApiKey),
                clientOptions);

            _chatClient = openAi.GetChatClient(_modelOpts.ModelId);

            _logger = logger;
            _symbol = symbol;
        }

        // =====================================================
        // 2.1  Metoda inicjująca system prompt
        // =====================================================
        public void InitializeConversation(string styleName, SymbolInfo symbolInfo, decimal balance)
        {
            const string initTemplate = """
                Date {DATE}. Symbol {SYMBOL}. Style: Intraday-Momentum-5m
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

            var sysMsg = initTemplate
                .Replace("{DATE}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"))
                .Replace("{SYMBOL}", _symbol)
                .Replace("{STYLE}", styleName);

            _conversationHistory.Add(new SystemChatMessage(sysMsg));
        }

        // =====================================================
        // 2.2  API dla kodu zewnętrznego
        // =====================================================
        public void AddUserMessage(string content) => _conversationHistory.Add(new UserChatMessage(content));
        public void AddAssistantMessage(string content) => _conversationHistory.Add(new AssistantChatMessage(content));

        // =====================================================
        // 2.3  Główna metoda wysyłająca prompt i odbierająca odp.
        // =====================================================
        public async Task<string> GetAIResponseAsync(string userMessage, CancellationToken cancel)
        {
            // 1) dodaj wiadomość użytkownika
            _conversationHistory.Add(new UserChatMessage(userMessage));

            // 2) przygotuj log promptu (dla czytelności logów, nie wysyłamy tego do modelu)
            var sbLog = new StringBuilder()
                .AppendLine("**********************************************************************")
                .AppendLine($"PROMPT SENT TO AI FROM {_symbol}:");

            var promptTokens = 0;
            foreach (var msg in _conversationHistory)
            {
                var text = msg.Content[0].Text;
                sbLog.AppendLine(text).AppendLine();
                promptTokens += CountTokens(text);
            }
            sbLog.AppendLine($"TOTAL TOKENS IN PROMPT: {promptTokens}");

            // 3) przygotuj options zgodnie z presetem modelu
            var options = new ChatCompletionOptions
            {
                Temperature = _modelOpts.Temperature,
                MaxOutputTokenCount = _modelOpts.MaxOutputTokens,
                FrequencyPenalty = 0.2f,
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            };

            // 4) wywołaj model
            var response = await _chatClient.CompleteChatAsync(_conversationHistory, options, cancel);
            var aiContent = response.Value.Content[0].Text.Trim();

            // 5) dopisz znacznik czasu i zachowaj historię
            var datedReply = $"SYMBOL: {_symbol} | Date: {DateTime.UtcNow:yyyy-MM-dd HH:mm}\n{aiContent}";
            _conversationHistory.Add(new AssistantChatMessage(datedReply));

            // 6) log
            sbLog.AppendLine(datedReply)
                 .AppendLine("**********************************************************************");
            _logger.LogInformation(sbLog.ToString());

            return aiContent;
        }

        /// <summary>
        /// Zwraca przybliżoną liczbę tokenów w tekście (o200k).
        /// </summary>
        private static int CountTokens(string text)
        {
            var enc = GptEncoding.GetEncoding("o200k_base");
            return enc.CountTokens(text);
        }

        /// <summary>
        /// Trzyma w historii maksymalnie 1 system + 3 ostatnie odpowiedzi AI.
        /// </summary>
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

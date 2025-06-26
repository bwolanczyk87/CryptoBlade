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
                Date {DATE}, symbol {SYMBOL}.
                You are {STYLE} AI-Crypto, a hyper-focused, chart-obsessed {STYLE} genius.
                Find momentum, spot reversals, price formations, volume spikes, and micro-trends.
                Wait for strong signals, ignore noise and avoid overtrading.

                Return exactly one JSON:
                {
                "Signal":"LONG|SHORT|NONE",
                "Confidence":0-100,
                "StopLoss": decimal,
                "Reason":str,
                "Delay":minutes,
                }
                Rules:
                -Signal: LONG/SHORT if Confidence>90
                -Reason: <300 chars
                -Place StopLoss at the nearest significant local support (for LONG) or resistance (for SHORT) level.
                -Candles header: TF|MMdd=  (e.g. 1M|0624=)
                -Candles body: HHmm,O,H,L,C,V; (e.g. 5M|0624|37280=6,7.2,5.8,6.1,800;)
                -Indicators: TF|ind(params)=  (e.g. 5M|Rsi(7)=) 
                -Pivots: HHmm,zigzag,pointType; (e.g. 1200,6.1,H;1210,6,L)
                -Delay: 1-15min, learn on previous signals and predict next best evaluation point
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

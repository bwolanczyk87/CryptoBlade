using System.Text;
using System.Text.Json;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Services;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy : TradingStrategyBase
    {
        private const int MaxCandlesPerTimeframe = 100;
        private const decimal MinAtrPercent = 0.4m;
        private readonly IDeepSeekClient _deepSeekClient;
        private readonly DeepSeekAccount? _deepSeekAccount;
        private readonly Random _random = new();
        private int _currentApiKeyIndex;

        public MomentumStrategy(
            IOptions<MomentumStrategyOptions> strategyOptions,
            IOptions<TradingBotOptions> botOptions,
            string symbol,
            IWalletManager walletManager,
            ICbFuturesRestClient restClient,
            IDeepSeekClient deepSeekClient,
            DeepSeekAccountConfig deepSeekConfig)
            : base(strategyOptions as IOptions<TradingStrategyBaseOptions>, botOptions, symbol, BuildTimeFrameWindows(), walletManager, restClient)
        {
            _deepSeekClient = deepSeekClient;
            _deepSeekAccount = deepSeekConfig.Accounts.Where(a => a.ApiName == symbol.ToLower()).FirstOrDefault();
            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }

        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;

        private static TimeFrameWindow[] BuildTimeFrameWindows()
        {
            return new[]
            {
                new TimeFrameWindow(TimeFrame.OneHour, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.FiveMinutes, MaxCandlesPerTimeframe, false)
            };
        }

        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            if (_deepSeekAccount != null)
            {
                var indicators = new List<StrategyIndicator>();
                try
                {
                    if (IsInTrade)
                        return NoSignal(indicators, "Already in trade");

                    // Pobierz dane historyczne
                    var htfQuotes = QuoteQueues[TimeFrame.OneHour].GetQuotes();
                    var ltfQuotes = QuoteQueues[TimeFrame.FiveMinutes].GetQuotes();

                    // Oblicz wskaźniki
                    var indicatorsData = CalculateIndicators(htfQuotes, ltfQuotes);

                    // // Waliduj zmienność
                    // if (!ValidateVolatility(ltfQuotes, indicatorsData.Atr, indicators))
                    //     return NoSignal(indicators, "Low volatility");

                    // Przygotuj dane dla AI
                    var aiRequest = BuildAIRequest(htfQuotes, ltfQuotes, indicatorsData);

                    var apiKey = _deepSeekAccount.ApiKey;
                    var aiResponse = await _deepSeekClient.GetTradingSignalAsync(aiRequest, apiKey, cancel);

                    // Przetwórz odpowiedź AI
                    return ProcessAIResponse(aiResponse, indicators);
                }
                catch (Exception ex)
                {
                    indicators.Add(new StrategyIndicator("Error", ex.Message));
                    return NoSignal(indicators, "AI Error");
                }
            }
            return NoSignal([], "DeepSeek account not configured");
        }

        private (IEnumerable<MacdResult> Macd, IEnumerable<EmaResult> Ema,
                 IEnumerable<SmaResult> VolumeSma, IEnumerable<AtrResult> Atr,
                 IEnumerable<AdxResult> Adx) CalculateIndicators(
            IEnumerable<Quote> htfQuotes,
            IEnumerable<Quote> ltfQuotes)
        {
            return (
                Macd: ltfQuotes.GetMacd(8, 21, 5),
                Ema: htfQuotes.GetEma(100),
                VolumeSma: ltfQuotes.Use(CandlePart.Volume).GetSma(12),
                Atr: ltfQuotes.GetAtr(14),
                Adx: htfQuotes.GetAdx(14)
            );
        }

        private bool ValidateVolatility(
            IEnumerable<Quote> quotes,
            IEnumerable<AtrResult> atrResults,
            List<StrategyIndicator> indicators)
        {
            var lastQuote = quotes.Last();
            var lastAtr = atrResults.Last().Atr ?? 0;
            decimal atrPercent = (decimal)(lastAtr / (double)lastQuote.Close) * 100;

            if (atrPercent < MinAtrPercent)
            {
                indicators.Add(new StrategyIndicator("ATR%", $"{atrPercent:F2}%"));
                return false;
            }
            return true;
        }

        private AISignalRequest BuildAIRequest(
            IReadOnlyList<Quote> htfQuotes,
            IReadOnlyList<Quote> ltfQuotes,
            (IEnumerable<MacdResult> Macd, IEnumerable<EmaResult> Ema,
             IEnumerable<SmaResult> VolumeSma, IEnumerable<AtrResult> Atr,
             IEnumerable<AdxResult> Adx) indicators)
        {
            var context = new TradingContext
            {
                Symbol = Symbol,
                Leverage = SymbolInfo.MaxLeverage.Value,
                Balance = WalletManager.Contract.WalletBalance.Value,
                CurrentPrice = Ticker?.LastPrice ?? 0,
                OpenPositions = this.LongPosition != null ? 1 : 0 + (this.ShortPosition != null ? 1 : 0)
            };

            return new AISignalRequest
            {
                Context = context,
                HtfQuotes = CompressQuotes(htfQuotes, TimeFrame.OneHour),
                LtfQuotes = CompressQuotes(ltfQuotes, TimeFrame.FiveMinutes),
                Indicators = new TradingIndicators
                {
                    Ema100 = indicators.Ema.Last().Ema ?? 0,
                    Adx = indicators.Adx.Last().Adx ?? 0,
                    MacdHistogram = indicators.Macd.Last().Histogram ?? 0,
                    VolumeSma = indicators.VolumeSma.Last().Sma ?? 0,
                    CurrentVolume = (double)ltfQuotes.Last().Volume,
                    Atr = indicators.Atr.Last().Atr ?? 0
                }
            };
        }

        private List<CompressedQuote> CompressQuotes(IEnumerable<Quote> quotes, TimeFrame timeframe)
        {
            return quotes
                .TakeLast(MaxCandlesPerTimeframe)
                .Select(q => new CompressedQuote(
                    q.Date,
                    (float)q.Open,
                    (float)q.High,
                    (float)q.Low,
                    (float)q.Close,
                    (float)q.Volume,
                    timeframe.ToString()))
                .ToList();
        }

        private SignalEvaluation ProcessAIResponse(AISignalResponse response, List<StrategyIndicator> indicators)
        {
            indicators.Add(new StrategyIndicator("AI-Confidence", $"{response.Confidence}%"));
            indicators.Add(new StrategyIndicator("AI-Reason", response.Reason));

            if (response.Confidence < 70)
                return NoSignal(indicators, $"Low confidence: {response.Confidence}%");

            if (response.BuySignal)
            {
                return GenerateSignal(
                    isLong: true,
                    entryPrice: response.EntryPrice ?? Ticker?.BestAskPrice ?? 0,
                    stopLoss: response.StopLoss,
                    takeProfit: response.TakeProfit,
                    quantity: response.Quantity,
                    indicators: indicators);
            }

            if (response.SellSignal)
            {
                return GenerateSignal(
                    isLong: false,
                    entryPrice: response.EntryPrice ?? Ticker?.BestBidPrice ?? 0,
                    stopLoss: response.StopLoss,
                    takeProfit: response.TakeProfit,
                    quantity: response.Quantity,
                    indicators: indicators);
            }

            return NoSignal(indicators, "No AI signal");
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

    public interface IDeepSeekClient
    {
        Task<AISignalResponse> GetTradingSignalAsync(AISignalRequest request, string apiKey, CancellationToken cancel);
    }

    public class DeepSeekClient : IDeepSeekClient
    {
        private readonly HttpClient _httpClient;

        public DeepSeekClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<AISignalResponse> GetTradingSignalAsync(
            AISignalRequest request,
            string apiKey,
            CancellationToken cancel)
        {
            // Budowanie promptu dla AI
            var prompt = BuildPrompt(request);

            // Wywołanie API DeepSeek
            var response = await SendDeepSeekRequest(prompt, apiKey, cancel);

            // Parsowanie odpowiedzi
            return ParseResponse(response);
        }

        private string BuildPrompt(AISignalRequest request)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Jesteś ekspertem tradingowym analizującym rynek kryptowalut. Wygeneruj sygnał w formacie JSON:");
            sb.AppendLine("{\"BuySignal\":bool,\"SellSignal\":bool,\"EntryPrice\":float,\"StopLoss\":float,");
            sb.AppendLine("\"TakeProfit\":float,\"Quantity\":float,\"Confidence\":0-100,\"Reason\":string}");
            sb.AppendLine("Zasady:");
            sb.AppendLine("- Tylko 1 aktywny sygnał (Buy LUB Sell)");
            sb.AppendLine("- Confidence < 70 = brak sygnału");
            sb.AppendLine("- Quantity: ryzykuj max 2% kapitału");
            sb.AppendLine();
            sb.AppendLine($"### Kontekst:");
            sb.AppendLine($"- Symbol: {request.Context.Symbol}");
            sb.AppendLine($"- Balance: {request.Context.Balance:F2} USDT");
            sb.AppendLine($"- Price: {request.Context.CurrentPrice:F2}");
            sb.AppendLine();
            sb.AppendLine("### Wskaźniki:");
            sb.AppendLine($"- EMA100(1h): {request.Indicators.Ema100:F2}");
            sb.AppendLine($"- ADX(1h): {request.Indicators.Adx:F2}");
            sb.AppendLine($"- MACD Hist(5m): {request.Indicators.MacdHistogram:F4}");
            sb.AppendLine($"- Volume(5m): {request.Indicators.CurrentVolume:F2} vs SMA: {request.Indicators.VolumeSma:F2}");
            sb.AppendLine($"- ATR(5m): {request.Indicators.Atr:F2}");
            sb.AppendLine();
            sb.AppendLine("### Dane historyczne (skompresowane):");
            sb.AppendLine(CompressToCsv(request.HtfQuotes, "HTF"));
            sb.AppendLine(CompressToCsv(request.LtfQuotes, "LTF"));

            return sb.ToString();
        }

        private string CompressToCsv(List<CompressedQuote> quotes, string prefix)
        {
            var sb = new StringBuilder();
            foreach (var q in quotes)
            {
                sb.AppendLine($"{prefix},{q.Timestamp:yyyy-MM-dd HH:mm:ss},{q.Open:F1},{q.High:F1},{q.Low:F1},{q.Close:F1},{q.Volume:F2}");
            }
            return sb.ToString();
        }

        private async Task<string> SendDeepSeekRequest(string prompt, string apiKey, CancellationToken cancel)
        {
            var request = new
            {
                model = "deepseek-reasoner",
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.3,
                max_tokens = 500
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
            httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(httpRequest, cancel);

            if ((int)response.StatusCode == 429)
                throw new ApiRateLimitException("Rate limit exceeded");

            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync(cancel);
            using var doc = JsonDocument.Parse(jsonResponse);
            return doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }

        private AISignalResponse ParseResponse(string aiResponse)
        {
            try
            {
                var start = aiResponse.IndexOf('{');
                var end = aiResponse.LastIndexOf('}') + 1;
                var json = aiResponse[start..end];

                return JsonSerializer.Deserialize<AISignalResponse>(json);
            }
            catch
            {
                // Fallback dla błędów parsowania
                return new AISignalResponse { Confidence = 0, Reason = "Invalid response format" };
            }
        }
    }

    public class ApiRateLimitException : Exception
    {
        public ApiRateLimitException(string message) : base(message) { }
    }

    public class AISignalRequest
    {
        public TradingContext Context { get; set; }
        public List<CompressedQuote> HtfQuotes { get; set; }
        public List<CompressedQuote> LtfQuotes { get; set; }
        public TradingIndicators Indicators { get; set; }
    }

    public class AISignalResponse
    {
        public bool BuySignal { get; set; }
        public bool SellSignal { get; set; }
        public decimal? EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        public decimal Quantity { get; set; }
        public int Confidence { get; set; }
        public string Reason { get; set; }
    }

    public class TradingContext
    {
        public string Symbol { get; set; }
        public decimal Leverage { get; set; }
        public decimal Balance { get; set; }
        public decimal CurrentPrice { get; set; }
        public int OpenPositions { get; set; }
    }

    public class TradingIndicators
    {
        public double Ema100 { get; set; }
        public double Adx { get; set; }
        public double MacdHistogram { get; set; }
        public double VolumeSma { get; set; }
        public double CurrentVolume { get; set; }
        public double Atr { get; set; }
    }

    public record CompressedQuote(
        DateTime Timestamp,
        float Open,
        float High,
        float Low,
        float Close,
        float Volume,
        string Timeframe);

    public class DeepSeekConfig
    {
        public List<string> ApiKeys { get; set; } = new();
    }
}
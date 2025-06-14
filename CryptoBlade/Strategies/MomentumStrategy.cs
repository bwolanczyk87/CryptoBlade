
using Accord.Statistics.Filters;
using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Models;
using CryptoBlade.Optimizer;
using CryptoBlade.Services;
using CryptoBlade.Strategies.AI;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using ScottPlot.Plottables;
using Skender.Stock.Indicators;
using System.Globalization;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy : TradingStrategyBase
    {
        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;
        private const int MaxCandlesPerTimeframe = 100;
        private readonly ChatAI _chatAI;
        private readonly IndicatorManager _indicatorManager;
        private readonly List<IndicatorAI> _activeIndicators = [];
        private List<CandlesAI> _activeCandles = [];
        private bool _isInitialized = false;

        public MomentumStrategy(IOptions<MomentumStrategyOptions> strategyOptions,
                                IOptions<TradingBotOptions> botOptions,
                                string symbol,
                                IWalletManager walletManager,
                                ICbFuturesRestClient restClient,
                                DeepSeekAccountConfig deepSeekConfig)
            : base(strategyOptions, botOptions, symbol, BuildTimeFrameWindows(), walletManager, restClient)
        {
            var logger = ApplicationLogging.CreateLogger<ChatAI>();
            _chatAI = new ChatAI(deepSeekConfig, symbol, logger);
            _indicatorManager = new IndicatorManager();

            InitializeIndicators();
            InitializeCandles();

            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }

        private static TimeFrameWindow[] BuildTimeFrameWindows()
        {
            return
            [
                new TimeFrameWindow(TimeFrame.OneDay, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.FourHours, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.OneHour, MaxCandlesPerTimeframe, true),
                new TimeFrameWindow(TimeFrame.FifteenMinutes, MaxCandlesPerTimeframe, false),
                new TimeFrameWindow(TimeFrame.FiveMinutes, MaxCandlesPerTimeframe, false)
            ];
        }

        private void InitializeIndicators()
        {
            var initialIndicators = new[] { "EMA", "MACD", "RSI", "Volume", "ATR" };
            foreach (var indicator in initialIndicators)
            {
                _activeIndicators.Add(new IndicatorAI(
                    indicator,
                    IndicatorAI.GetAbbreviation(indicator),
                    GetDefaultParameters(indicator))
                );
            }
        }

        private static int[] GetDefaultParameters(string indicator)
        {
            return indicator switch
            {
                "EMA" => [100],
                "MACD" => [8, 21, 5],
                "RSI" => [14],
                "Volume" => [20],
                "ATR" => [14],
                "ADX" => [14],
                "BollingerBands" => [20, 2],
                "Stochastic" => [14, 3, 3],
                "Ichimoku" => [9, 26, 52, 26],
                "VWAP" => [],
                _ => []
            };
        }

        private void InitializeCandles()
        {
            var initialCandles = new[] { "1H|12", "15M|16", "5M|60" };
            foreach (var candle in initialCandles)
            {
                _activeCandles.Add(CandlesAI.Parse(candle));
            }
        }

        protected override async Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var indicators = new List<StrategyIndicator>
            {
                new("MainTimeFrameVolume", (decimal)1000)
            };

            try
            {
                if (IsInTrade)
                    return NoSignal(indicators, "Already in trade");

                if (!_isInitialized)
                {
                    await InitializeAIAsync();
                    _isInitialized = true;
                }

                var quotes = new Dictionary<TimeFrame, IEnumerable<Quote>>();
                foreach (var timeframe in QuoteQueues.Keys)
                {
                    quotes[timeframe] = QuoteQueues[timeframe].GetQuotes().TakeLast(MaxCandlesPerTimeframe);
                }

                var userMessage = BuildUserMessage(quotes);

                var aiResponse = await _chatAI.GetAIResponseAsync(userMessage, cancel);
                var signal = ParseAIResponse(aiResponse, indicators);
                return signal;
            }
            catch (Exception ex)
            {
                indicators.Add(new StrategyIndicator("Error", ex.Message));
                return NoSignal(indicators, "AI Error");
            }
        }

        private Task InitializeAIAsync()
        {
            var indicators = string.Join(",", _activeIndicators
                .Select(i => $"{i.Abbreviation}({i.Name})"));

            _chatAI.InitializeConversation(
                WalletManager.Contract.WalletBalance.Value,
                indicators,
                SymbolInfo.MaxLeverage
            );
            return Task.CompletedTask;
        }

        private string BuildUserMessage(Dictionary<TimeFrame, IEnumerable<Quote>> quotes)
        {
            var request = new StringBuilder();
            request.AppendLine($"SYM:{Symbol} | BAL:{WalletManager.Contract.WalletBalance.Value.ToString("F2", CultureInfo.InvariantCulture)} | PRC:{Ticker?.LastPrice.ToString("F2", CultureInfo.InvariantCulture) ?? 0.ToString()}");
            request.AppendLine();

            // Build indicators section
            foreach (var indicator in _activeIndicators)
            {
                try
                {
                    if (quotes.TryGetValue(indicator.TimeFrame, out var tfQuotes))
                    {
                        var value = _indicatorManager.Calculate(
                            indicator.Name,
                            tfQuotes,
                            indicator.Parameters);

                        indicator.Value = value;
                        request.AppendLine($"{indicator.Abbreviation}:{IndicatorManager.Format(value)}");
                    }
                    else
                    {
                        request.AppendLine($"{indicator.Abbreviation}:N/A");
                    }
                }
                catch
                {
                    request.AppendLine($"{indicator.Abbreviation}:ERR");
                }
            }
            request.AppendLine();

            // Build candles section
            request.AppendLine("CANDLES:");
            foreach (var candle in _activeCandles)
            {
                request.AppendLine(candle.FormatForBot(QuoteQueues, (int)SymbolInfo.PriceScale));
            }

            request.AppendLine("Analyze indicators and candles and request for new ones to optimize best signals production");
            request.AppendLine("Goal: get rich, really reach");
 
            return request.ToString();
        }

        private SignalEvaluation ParseAIResponse(string response, List<StrategyIndicator> indicators)
        {
            try
            {
                var result = JsonSerializer.Deserialize<SignalResponseAI>(response);
                if (result == null)
                    return NoSignal(indicators, "Failed to parse AI response");

                indicators.Add(new StrategyIndicator("AI-Confidence", $"{result.Confidence}%"));
                indicators.Add(new StrategyIndicator("AI-Reason", result.Reason));

                UpdateActiveIndicators(result.RequestedIndicators);
                UpdateActiveCandles(result.RequestedCandles);

                if (result.Confidence < 70)
                    return NoSignal(indicators, $"Low confidence: {result.Confidence}%");

                if (result.Signal == "LONG")
                {
                    return GenerateSignal(
                        isLong: true,
                        entryPrice: result.EntryPrice ?? Ticker?.BestAskPrice ?? 0,
                        stopLoss: result.StopLoss,
                        takeProfit: result.TakeProfit,
                        quantity: result.Quantity,
                        indicators: indicators);
                }

                if (result.Signal == "SHORT")
                {
                    return GenerateSignal(
                        isLong: false,
                        entryPrice: result.EntryPrice ?? Ticker?.BestBidPrice ?? 0,
                        stopLoss: result.StopLoss,
                        takeProfit: result.TakeProfit,
                        quantity: result.Quantity,
                        indicators: indicators);
                }

                return NoSignal(indicators, "No signal from AI");
            }
            catch (Exception ex)
            {
                return NoSignal(indicators, $"Invalid AI response: {ex.Message}");
            }
        }

        private void UpdateActiveIndicators(List<string>? requestedIndicators)
        {
            if (requestedIndicators != null)
            {
                foreach (var indicatorStr in requestedIndicators)
                {
                    try
                    {
                        var indicator = IndicatorAI.Parse(indicatorStr);

                        if (!_activeIndicators.Any(i =>
                            i.Name == indicator.Name &&
                            i.TimeFrame == indicator.TimeFrame))
                        {
                            _activeIndicators.Add(indicator);
                        }
                    }
                    catch
                    {
                        // Ignore invalid formats
                    }
                }
            }
        }

        private void UpdateActiveCandles(List<string>? requestedCandles)
        {
            if (requestedCandles != null)
            {
                var newCandles = new List<CandlesAI>();
                foreach (var candleStr in requestedCandles)
                {
                    try
                    {
                        newCandles.Add(CandlesAI.Parse(candleStr));
                    }
                    catch
                    {
                        // Ignore invalid formats
                    }
                }

                if (newCandles.Any())
                {
                    _activeCandles = newCandles;
                }
            }
        }

        private SignalEvaluation GenerateSignal(bool isLong, decimal entryPrice, decimal stopLoss, decimal takeProfit, decimal quantity, List<StrategyIndicator> indicators)
        {
            TakeProfitPrice = takeProfit;
            StopLossPrice = stopLoss;
            if (isLong)
            {
                DynamicQtyLong = quantity;
                indicators.Add(new StrategyIndicator("Signal", "LONG"));
            }
            else
            {
                DynamicQtyShort = quantity;
                indicators.Add(new StrategyIndicator("Signal", "SHORT"));
            }

            return new SignalEvaluation(
                isLong,
                !isLong,
                false,
                false,
                [.. indicators]);
        }

        private static SignalEvaluation NoSignal(List<StrategyIndicator> indicators, string reason)
        {
            indicators.Add(new StrategyIndicator("Reason", reason));
            return new SignalEvaluation(false, false, false, false, [.. indicators]);
        }
    }
}
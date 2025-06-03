using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy : TradingStrategyBase
    {
        // Hardkodowane parametry zgodnie z taktyką
        private const int TrendEmaPeriod = 200;
        private const int MacdFastPeriod = 12;
        private const int MacdSlowPeriod = 26;
        private const int MacdSignalPeriod = 9;
        private const int VolumeLookback = 20;
        private const decimal VolumeSpikeMultiplier = 1.8m;
        private const int AtrPeriod = 14;
        private const decimal AtrMultiplierSl = 1.5m;
        private const decimal RiskRewardRatio = 1.5m;
        private const decimal MaxSlippagePercent = 0.05m;
        private const int SwingLookback = 30;

        private DateTime m_lastSignalTime;
        private decimal? m_entryPrice;

        public MomentumStrategy(
            IOptions<MomentumStrategyOptions> strategyOptions,
            IOptions<TradingBotOptions> botOptions,
            string symbol,
            IWalletManager walletManager,
            ICbFuturesRestClient restClient)
            : base(strategyOptions as IOptions<TradingStrategyBaseOptions>, botOptions, symbol, BuildTimeFrameWindows(), walletManager, restClient)
        {
            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
        }

        public override string Name => "Momentum";
        protected override bool UseMarketOrdersForEntries => true;

        private static TimeFrameWindow[] BuildTimeFrameWindows()
        {
            return new[]
            {
                new TimeFrameWindow(TimeFrame.FourHours, 250, true),   // HTF dla trendu i dywergencji
                new TimeFrameWindow(TimeFrame.FifteenMinutes, 50, false)  // LTF dla breakoutu
            };
        }

        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            var indicators = new List<StrategyIndicator>();
            try
            {
                if (!IsInTrade)
                {
                    // Pobierz dane z timeframe'ów
                    var htfQuotes = QuoteQueues[TimeFrame.FourHours].GetQuotes();
                    var ltfQuotes = QuoteQueues[TimeFrame.FifteenMinutes].GetQuotes();

                    // Walidacja danych
                    if (!ValidateData(htfQuotes, ltfQuotes, indicators))
                        return Task.FromResult(NoSignal(indicators, "InvalidData"));

                    // Oblicz wskaźniki dla HTF (4h)
                    var htfMacd = htfQuotes.GetMacd(MacdFastPeriod, MacdSlowPeriod, MacdSignalPeriod);
                    var htfEma = htfQuotes.GetEma(TrendEmaPeriod);

                    // Oblicz wskaźniki dla LTF (15m)
                    var volumeSma = ltfQuotes.Use(CandlePart.Volume).GetSma(VolumeLookback);
                    var atr = ltfQuotes.GetAtr(AtrPeriod);

                    // Walidacja wskaźników
                    if (!ValidateIndicators(htfMacd, htfEma, volumeSma, atr, indicators))
                        return Task.FromResult(NoSignal(indicators, "InvalidIndicators"));

                    // Detekcja trendu (główny filtr)
                    var lastHtfQuote = htfQuotes.Last();
                    decimal htfEmaValue = (decimal)htfEma.Last().Ema;
                    bool isBullTrend = lastHtfQuote.Close > htfEmaValue;
                    bool isBearTrend = lastHtfQuote.Close < htfEmaValue;

                    // Detekcja dywergencji MACD
                    bool bullishDivergence = DetectBullishDivergence(htfQuotes, htfMacd);
                    bool bearishDivergence = DetectBearishDivergence(htfQuotes, htfMacd);

                    // Detekcja spiku wolumenu na LTF
                    bool volumeSpike = ltfQuotes.Last().Volume > (decimal)volumeSma.Last().Sma * VolumeSpikeMultiplier;

                    // Warunki wejścia
                    bool longSignal = isBullTrend && bullishDivergence && volumeSpike;
                    bool shortSignal = isBearTrend && bearishDivergence && volumeSpike;

                    if (longSignal)
                    {
                        return Task.FromResult(EnterLong(ltfQuotes, atr, indicators));
                    }
                    else if (shortSignal)
                    {
                        return Task.FromResult(EnterShort(ltfQuotes, atr, indicators));
                    }
                }
            }
            catch (Exception ex)
            {
                indicators.Add(new StrategyIndicator("Error", ex.Message));
            }

            return Task.FromResult(NoSignal(indicators, "NoSignal"));
        }

        private bool DetectBullishDivergence(IEnumerable<Quote> quotes, IEnumerable<MacdResult> macdResults)
        {
            var quotesList = quotes.TakeLast(SwingLookback).ToList();
            var macdList = macdResults.TakeLast(SwingLookback).ToList();

            // Znajdź dwa ostatnie dołki cenowe
            var (lastLow, prevLow) = FindLastTwoLows(quotesList);

            if (!lastLow.HasValue || !prevLow.HasValue)
                return false;

            // Znajdź odpowiadające im wartości MACD
            int lastIndex = quotesList.Count - 1;
            int prevIndex = quotesList.FindIndex(q => q.Low == prevLow);

            decimal lastMacd = (decimal)macdList[lastIndex].Macd!;
            decimal prevMacd = (decimal)macdList[prevIndex].Macd!;

            // Warunek dywergencji: niższy dołek cenowy + wyższy dołek MACD
            return lastLow < prevLow && lastMacd > prevMacd;
        }

        private bool DetectBearishDivergence(IEnumerable<Quote> quotes, IEnumerable<MacdResult> macdResults)
        {
            var quotesList = quotes.TakeLast(SwingLookback).ToList();
            var macdList = macdResults.TakeLast(SwingLookback).ToList();

            // Znajdź dwa ostatnie szczyty cenowe
            var (lastHigh, prevHigh) = FindLastTwoHighs(quotesList);

            if (!lastHigh.HasValue || !prevHigh.HasValue)
                return false;

            // Znajdź odpowiadające im wartości MACD
            int lastIndex = quotesList.Count - 1;
            int prevIndex = quotesList.FindIndex(q => q.High == prevHigh);

            decimal lastMacd = (decimal)macdList[lastIndex].Macd!;
            decimal prevMacd = (decimal)macdList[prevIndex].Macd!;

            // Warunek dywergencji: wyższy szczyt cenowy + niższy szczyt MACD
            return lastHigh > prevHigh && lastMacd < prevMacd;
        }

        private (decimal?, decimal?) FindLastTwoLows(List<Quote> quotes)
        {
            decimal? lastLow = null;
            decimal? prevLow = null;

            for (int i = 1; i < quotes.Count - 1; i++)
            {
                if (quotes[i].Low < quotes[i - 1].Low && quotes[i].Low < quotes[i + 1].Low)
                {
                    prevLow = lastLow;
                    lastLow = quotes[i].Low;
                }
            }
            return (lastLow, prevLow);
        }

        private (decimal?, decimal?) FindLastTwoHighs(List<Quote> quotes)
        {
            decimal? lastHigh = null;
            decimal? prevHigh = null;

            for (int i = 1; i < quotes.Count - 1; i++)
            {
                if (quotes[i].High > quotes[i - 1].High && quotes[i].High > quotes[i + 1].High)
                {
                    prevHigh = lastHigh;
                    lastHigh = quotes[i].High;
                }
            }
            return (lastHigh, prevHigh);
        }

        private SignalEvaluation EnterLong(
            IEnumerable<Quote> ltfQuotes,
            IEnumerable<AtrResult> atr,
            List<StrategyIndicator> indicators)
        {
            if (Ticker == null || SymbolInfo == null)
                return NoSignal(indicators, "MissingData");

            var lastQuote = ltfQuotes.Last();
            decimal atrValue = (decimal)(atr.Last().Atr ?? 1);

            // Oblicz cenę wejścia z uwzględnieniem slippage'u
            decimal allowedSlippage = lastQuote.Close * MaxSlippagePercent;
            m_entryPrice = Math.Min(Ticker.BestAskPrice, lastQuote.Close + allowedSlippage);

            // Oblicz SL i TP
            StopLossPrice = Math.Round(m_entryPrice.Value - atrValue * AtrMultiplierSl,
                                    (int)SymbolInfo.PriceScale);
            TakeProfitPrice = Math.Round(m_entryPrice.Value + (m_entryPrice.Value - StopLossPrice.Value) * RiskRewardRatio,
                                    (int)SymbolInfo.PriceScale);

            return GenerateSignal(indicators, true, "Long");
        }

        private SignalEvaluation EnterShort(
            IEnumerable<Quote> ltfQuotes,
            IEnumerable<AtrResult> atr,
            List<StrategyIndicator> indicators)
        {
            if (Ticker == null || SymbolInfo == null)
                return NoSignal(indicators, "MissingData");

            var lastQuote = ltfQuotes.Last();
            decimal atrValue = (decimal)(atr.Last().Atr ?? 1);

            // Oblicz cenę wejścia z uwzględnieniem slippage'u
            decimal allowedSlippage = lastQuote.Close * MaxSlippagePercent;
            m_entryPrice = Math.Max(Ticker.BestBidPrice, lastQuote.Close - allowedSlippage);

            // Oblicz SL i TP
            StopLossPrice = Math.Round(m_entryPrice.Value + atrValue * AtrMultiplierSl,
                                    (int)SymbolInfo.PriceScale);
            TakeProfitPrice = Math.Round(m_entryPrice.Value - (StopLossPrice.Value - m_entryPrice.Value) * RiskRewardRatio,
                                    (int)SymbolInfo.PriceScale);

            return GenerateSignal(indicators, false, "Short");
        }

        private SignalEvaluation GenerateSignal(List<StrategyIndicator> indicators, bool isLong, string condition)
        {
            indicators.Add(new StrategyIndicator("Condition", condition));
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

        private bool ValidateData(
    IReadOnlyList<Quote> htfQuotes,
    IReadOnlyList<Quote> ltfQuotes,
    List<StrategyIndicator> indicators)
        {
            int minHtfQuotes = Math.Max(SwingLookback, TrendEmaPeriod); // 200
            bool isValid = htfQuotes.Count >= minHtfQuotes
                          && ltfQuotes.Count >= VolumeLookback
                          && Ticker != null
                          && SymbolInfo != null;

            if (!isValid)
            {
                indicators.Add(new StrategyIndicator("Error",
                    $"Insufficient data: HTF={htfQuotes.Count}/{minHtfQuotes}"));
            }
            return isValid;
        }

        private bool ValidateIndicators(
            IEnumerable<MacdResult> macd,
            IEnumerable<EmaResult> ema,
            IEnumerable<SmaResult> volumeSma,
            IEnumerable<AtrResult> atr,
            List<StrategyIndicator> indicators)
        {
            bool isValid = macd.Any() && ema.Any() && volumeSma.Any() && atr.Any() &&
                          macd.Last().Macd.HasValue &&
                          ema.Last().Ema.HasValue &&
                          volumeSma.Last().Sma.HasValue &&
                          atr.Last().Atr.HasValue;

            if (!isValid) indicators.Add(new StrategyIndicator("Error", "InvalidIndicatorValues"));
            return isValid;
        }
    }
}
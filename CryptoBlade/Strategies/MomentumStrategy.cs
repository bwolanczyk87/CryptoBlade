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
        // Hiperparametry zoptymalizowane pod 5min/1h
        private const int TrendEmaPeriod = 100;
        private const int MacdFastPeriod = 8;
        private const int MacdSlowPeriod = 21;
        private const int MacdSignalPeriod = 5;  // Szybszy sygnał
        private const int VolumeLookback = 12;
        private const decimal VolumeSpikeMultiplier = 1.5m;
        private const int AtrPeriod = 14;
        private const int AdxPeriod = 14;  // Filtr siły trendu
        private const decimal AdxThreshold = 25m;
        private const decimal AtrMultiplierSl = 1.5m;
        private const decimal RiskRewardRatio = 1.7m;  // Zwiększony RR
        private const decimal MaxSlippagePercent = 0.03m;
        private const decimal MinAtrPercent = 0.4m;

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
                new TimeFrameWindow(TimeFrame.OneHour, 150, true),    // HTF: trend + ADX
                new TimeFrameWindow(TimeFrame.FiveMinutes, 120, false) // LTF: entry signals
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
                    var htfQuotes = QuoteQueues[TimeFrame.OneHour].GetQuotes();
                    var ltfQuotes = QuoteQueues[TimeFrame.FiveMinutes].GetQuotes();

                    // Walidacja danych
                    if (!ValidateData(htfQuotes, ltfQuotes, indicators))
                        return Task.FromResult(NoSignal(indicators, "InvalidData"));

                    // Wskaźniki HTF (1h)
                    var htfEma = htfQuotes.GetEma(TrendEmaPeriod);
                    var htfAdx = htfQuotes.GetAdx(AdxPeriod);

                    // Wskaźniki LTF (5min)
                    var ltfMacd = ltfQuotes.GetMacd(MacdFastPeriod, MacdSlowPeriod, MacdSignalPeriod);
                    var volumeSma = ltfQuotes.Use(CandlePart.Volume).GetSma(VolumeLookback);
                    var atr = ltfQuotes.GetAtr(AtrPeriod);

                    // Walidacja
                    if (!ValidateIndicators(ltfMacd, htfEma, volumeSma, atr, htfAdx, indicators))
                        return Task.FromResult(NoSignal(indicators, "InvalidIndicators"));

                    // Ostatnie wartości
                    var lastHtfQuote = htfQuotes.Last();
                    var lastLtfQuote = ltfQuotes.Last();
                    decimal htfEmaValue = (decimal)htfEma.Last().Ema;
                    var lastMacd = ltfMacd.Last();
                    decimal volumeSmaValue = (decimal)volumeSma.Last().Sma;
                    decimal atrValue = (decimal)atr.Last().Atr;
                    decimal adxValue = (decimal)htfAdx.Last().Adx;

                    // Filtr zmienności
                    decimal atrPercent = (atrValue / lastLtfQuote.Close) * 100;
                    if (atrPercent < MinAtrPercent)
                        return Task.FromResult(NoSignal(indicators, $"LowVolatility({atrPercent:F2}%)"));

                    // Główny filtr trendu
                    bool isBullTrend = lastHtfQuote.Close > htfEmaValue;
                    bool isBearTrend = lastHtfQuote.Close < htfEmaValue;

                    // Filtr siły trendu (ADX)
                    bool strongTrend = adxValue >= AdxThreshold;

                    // Spik wolumenu
                    bool volumeSpike = lastLtfQuote.Volume > volumeSmaValue * VolumeSpikeMultiplier;

                    // Analiza histogramu MACD (szybsze sygnały)
                    var macdHistogram = ltfMacd.Select(x => x.Histogram ?? 0).ToList();
                    bool bullishMomentum = IsHistogramRising(macdHistogram);
                    bool bearishMomentum = IsHistogramFalling(macdHistogram);

                    // Warunki wejścia
                    bool longSignal = isBullTrend && strongTrend && bullishMomentum && volumeSpike;
                    bool shortSignal = isBearTrend && strongTrend && bearishMomentum && volumeSpike;

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

        private bool IsHistogramRising(List<double> histogram)
        {
            if (histogram.Count < 3) return false;
            
            // Obecna i poprzednia wartość
            double current = histogram[^1];
            double prev = histogram[^2];
            double prev2 = histogram[^3];

            // Warunek: histogram rośnie i jest powyżej zera
            return current > prev && prev > prev2 && current > 0;
        }

        private bool IsHistogramFalling(List<double> histogram)
        {
            if (histogram.Count < 3) return false;
            
            double current = histogram[^1];
            double prev = histogram[^2];
            double prev2 = histogram[^3];

            // Warunek: histogram spada i jest poniżej zera
            return current < prev && prev < prev2 && current < 0;
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

            // Wejście z minimalnym slippage'iem
            decimal allowedSlippage = lastQuote.Close * MaxSlippagePercent;
            m_entryPrice = Math.Min(Ticker.BestAskPrice, lastQuote.Close + allowedSlippage);

            // Dynamiczne SL/TP
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

            decimal allowedSlippage = lastQuote.Close * MaxSlippagePercent;
            m_entryPrice = Math.Max(Ticker.BestBidPrice, lastQuote.Close - allowedSlippage);

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
            int minHtfQuotes = Math.Max(TrendEmaPeriod, AdxPeriod) + 20; // 120
            int minLtfQuotes = MacdSlowPeriod + MacdSignalPeriod + 20; // 46
            
            bool isValid = htfQuotes.Count >= minHtfQuotes
                          && ltfQuotes.Count >= minLtfQuotes
                          && Ticker != null
                          && SymbolInfo != null;

            if (!isValid)
            {
                indicators.Add(new StrategyIndicator("Error",
                    $"Insufficient data: HTF={htfQuotes.Count}/{minHtfQuotes}, LTF={ltfQuotes.Count}/{minLtfQuotes}"));
            }
            return isValid;
        }

        private bool ValidateIndicators(
            IEnumerable<MacdResult> macd,
            IEnumerable<EmaResult> ema,
            IEnumerable<SmaResult> volumeSma,
            IEnumerable<AtrResult> atr,
            IEnumerable<AdxResult> adx,
            List<StrategyIndicator> indicators)
        {
            var macdList = macd.ToList();
            var emaList = ema.ToList();
            var volumeSmaList = volumeSma.ToList();
            var atrList = atr.ToList();
            var adxList = adx.ToList();

            bool isValid = 
                macdList.Count > 0 && 
                emaList.Count > 0 && 
                volumeSmaList.Count > 0 && 
                atrList.Count > 0 &&
                adxList.Count > 0 &&
                macdList.Last().Histogram.HasValue &&
                emaList.Last().Ema.HasValue &&
                volumeSmaList.Last().Sma.HasValue &&
                atrList.Last().Atr.HasValue &&
                adxList.Last().Adx.HasValue;

            if (!isValid) 
            {
                indicators.Add(new StrategyIndicator("Error", "InvalidIndicatorValues"));
                indicators.Add(new StrategyIndicator("Debug", 
                    $"MACD: {macdList.Count}, EMA: {emaList.Count}, VolSMA: {volumeSmaList.Count}, ATR: {atrList.Count}, ADX: {adxList.Count}"));
            }
            return isValid;
        }
    }
}
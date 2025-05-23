using CryptoBlade.Configuration;
using CryptoBlade.Exchanges;
using CryptoBlade.Strategies.Common;
using CryptoBlade.Strategies.Wallet;
using Microsoft.Extensions.Options;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategy(IOptions<MomentumStrategyOptions> options, IOptions<TradingBotOptions> botOptions, string symbol, IWalletManager walletManager, ICbFuturesRestClient restClient) : 
        TradingStrategyBase(options, botOptions, symbol, BuildTimeFrameWindows(options.Value), walletManager, restClient)
    {
        public override string Name => "Momentum";

        protected override async Task CalculateTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            //var opts = m_options.Value;
            //var ticker = Ticker;
            //if (ticker == null)
            //    return;

            //if (LongPosition != null)
            //{
            //    var sellSignal = (bool)indicators.First(i => i.Name == IndicatorType.Sell.ToString()).Value;
            //    if (sellSignal)
            //    {
            //        LongTakeProfitPrice = ticker.BestBidPrice;
            //        indicators.Add(new StrategyIndicator(nameof(IndicatorType.LongTakeProfit), LongTakeProfitPrice));
            //    }
            //    else
            //    {
            //        LongTakeProfitPrice = null;
            //    }
            //}
            //else
            //{
            //    m_longPeakPrice = null;
            //}

            //if (ShortPosition != null)
            //{
            //    var buySignal = (bool)indicators.First(i => i.Name == IndicatorType.Buy.ToString()).Value;
            //    if (buySignal)
            //    {
            //        ShortTakeProfitPrice = ticker.BestAskPrice;
            //        indicators.Add(new StrategyIndicator(nameof(IndicatorType.ShortTakeProfit), ShortTakeProfitPrice));
            //    }
            //    else
            //    {
            //        ShortTakeProfitPrice = null;
            //    }
            //}
            //else
            //{
            //    m_shortValleyPrice = null;
            //}

            await Task.CompletedTask;
        }

        protected override async Task CalculateStopLossTakeProfitAsync(IList<StrategyIndicator> indicators)
        {
            StopLossTakeProfitMode = Bybit.Net.Enums.StopLossTakeProfitMode.Full;
            TrailingStopActivePrice = null;
            TrailingStopPriceDistance = null;
            StopLossQuantity = null;
            TakeProfitQuantity = null;

            if (LongPosition == null && ShortPosition == null)
            {
                StopLossPrice = null;
                TakeProfitPrice = null;
                return;
            }

            var quotes = QuoteQueues[options.Value.PrimaryTimeFrame].GetQuotes();
            if (quotes.Length < 2)
            {
                StopLossPrice = null;
                TakeProfitPrice = null;
                return;
            }

            int lookback = Math.Min(10, quotes.Length);
            var recentQuotes = quotes[^lookback..];

            if (LongPosition != null)
            {
                var localLow = recentQuotes.Min(q => q.Low);
                StopLossPrice = localLow * 0.998m;

                if (StopLossPrice.HasValue && StopLossPrice < LongPosition.AveragePrice)
                {
                    decimal risk = LongPosition.AveragePrice - StopLossPrice.Value;
                    decimal targetReward = 2m * risk;
                    TakeProfitPrice = LongPosition.AveragePrice + targetReward;
                }
                else
                {
                    TakeProfitPrice = null;
                }
            }
            else if (ShortPosition != null)
            {
                var localHigh = recentQuotes.Max(q => q.High);
                StopLossPrice = localHigh * 1.002m;

                if (StopLossPrice.HasValue && StopLossPrice > ShortPosition.AveragePrice)
                {
                    decimal risk = StopLossPrice.Value - ShortPosition.AveragePrice;
                    decimal targetReward = 2m * risk;
                    TakeProfitPrice = ShortPosition.AveragePrice - targetReward;
                }
                else
                {
                    TakeProfitPrice = null;
                }
            }

            indicators.Add(new StrategyIndicator("StopLossPrice", StopLossPrice ?? 0m));
            indicators.Add(new StrategyIndicator("TakeProfitPrice", TakeProfitPrice ?? 0m));

            await Task.CompletedTask;
        }


        protected override Task<SignalEvaluation> EvaluateSignalsInnerAsync(CancellationToken cancel)
        {
            // 1) Pobieramy ustawienia strategii i świeczki
            var opts = options.Value;
            var primaryQuotes = QuoteQueues[opts.PrimaryTimeFrame].GetQuotes();
            if (primaryQuotes.Length < 30)
            {
               // Za mało danych, nie generujemy sygnału
               return Task.FromResult(
                   new SignalEvaluation(false, false, false, false, Array.Empty<StrategyIndicator>())
               );
            }

            // 2) Konwertujemy do listy (Skender) i liczymy HMA, CMF i OBV
            var quotesList = primaryQuotes.ToList();

            // Długości HMA (przykładowe)
            int periodShort = 9;
            int periodLong = 21;
            int cmfPeriod = 20;

            // Hull Moving Average (krótka i długa)
            var hmaShortResults = quotesList.GetHma(periodShort).ToList(); // .Sma / .Hma z biblioteki Skendera
            var hmaLongResults = quotesList.GetHma(periodLong).ToList();

            // Chaikin Money Flow
            var cmfResults = quotesList.GetCmf(cmfPeriod).ToList();

            // (Opcjonalnie) OBV do wglądu
            var obvResults = quotesList.GetObv().ToList();

            // 3) Bierzemy ostatnie wartości z hma i cmf
            //    Uwaga: hmaShortResults[i].Hma zwraca double? (może być null, trzeba sprawdzić)
            var lastHmaShort = hmaShortResults.LastOrDefault();
            var prevHmaShort = hmaShortResults.Count > 1 ? hmaShortResults[^2] : null;

            var lastHmaLong = hmaLongResults.LastOrDefault();
            var prevHmaLong = hmaLongResults.Count > 1 ? hmaLongResults[^2] : null;

            var lastCmf = cmfResults.LastOrDefault(); // .Cmf => double?

            // Sprawdzamy, czy mamy wszystkie dane niepuste
            if (!lastHmaShort.Hma.HasValue || !lastHmaLong.Hma.HasValue || lastCmf.Cmf == null)
            {
               return Task.FromResult(
                   new SignalEvaluation(false, false, false, false, Array.Empty<StrategyIndicator>())
               );
            }

            // 4) Logika przecięć: HMA short vs. HMA long
            // Sprawdzamy dwa ostatnie "punkty" w hmaShort i hmaLong, 
            // żeby wykryć, czy nastąpiło przecięcie z dołu/do góry
            double hmaShortNow = lastHmaShort.Hma.Value;
            double hmaLongNow = lastHmaLong.Hma.Value;

            double? hmaShortPrev = prevHmaShort?.Hma;
            double? hmaLongPrev = prevHmaLong?.Hma;

            bool hasBuySignal = false;
            bool hasSellSignal = false;

            // Dodatkowo filtr CMF > 0 => kupno, CMF < 0 => sprzedaż
            double cmfValue = lastCmf.Cmf.Value;

            if (hmaShortPrev.HasValue && hmaLongPrev.HasValue)
            {
               // CROSS UP:
               // warunek: wcześniej hmaShort < hmaLong, teraz hmaShort > hmaLong (i CMF > 0)
               bool crossUp = (hmaShortPrev < hmaLongPrev) && (hmaShortNow > hmaLongNow);
               bool bullishVolume = (cmfValue > 0);
               if (crossUp && bullishVolume)
                   hasBuySignal = true;

               // CROSS DOWN:
               // wcześniej hmaShort > hmaLong, teraz hmaShort < hmaLong (CMF < 0)
               bool crossDown = (hmaShortPrev > hmaLongPrev) && (hmaShortNow < hmaLongNow);
               bool bearishVolume = (cmfValue < 0);
               if (crossDown && bearishVolume)
                   hasSellSignal = true;
            }

            // Ewentualnie tu logika "extra signals" (dca) – np. re-entry
            // if (LongPosition != null && hasBuySignal) ...
            // if (ShortPosition != null && hasSellSignal) ...
            hasBuySignal = true;
            bool hasBuyExtraSignal = false;
            bool hasSellExtraSignal = false;

            // 5) Zbierzmy wskaźniki do debugowania
            var indicators = new List<StrategyIndicator>
    {
        //new StrategyIndicator("HmaShortNow", (decimal)hmaShortNow),
        //new StrategyIndicator("HmaLongNow", (decimal)hmaLongNow),
        //new StrategyIndicator("CMF", (decimal)cmfValue),
        // Jeśli chcesz – OBV:
        // new StrategyIndicator("OBV", obvResults.LastOrDefault()?.Obv ?? 0)
    };

            // Budujemy wynik
            var evaluation = new SignalEvaluation(
                hasBuySignal,
                hasSellSignal,
                hasBuyExtraSignal,
                hasSellExtraSignal,
                indicators.ToArray()
            );
            return Task.FromResult(evaluation);
        }

        private static TimeFrameWindow[] BuildTimeFrameWindows(MomentumStrategyOptions opts)
        {
            if (opts.UseSecondaryTimeFrameFilter)
            {
                return
                [
                    new TimeFrameWindow(opts.PrimaryTimeFrame, opts.PrimaryTimeFrameWindowSize, true),
                    new TimeFrameWindow(opts.SecondaryTimeFrame, opts.SecondaryTimeFrameWindowSize, false)
                ];
            }
            return
            [
                new TimeFrameWindow(opts.PrimaryTimeFrame, opts.PrimaryTimeFrameWindowSize, true)
            ];
        }
    }
}

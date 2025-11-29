using System;
using System.Threading.Tasks;
using CryptoBlade.Models;
using Skender.Stock.Indicators;

namespace CryptoBlade.Strategies.Sigma.Helpers
{
    public static class SessionHelpers
    {
        public static void EnsureSortedByDate(Quote[]? quotes)
        {
            if (quotes == null || quotes.Length < 2)
                return;

            for (int i = 1; i < quotes.Length; i++)
            {
                if (quotes[i].Date < quotes[i - 1].Date)
                {
                    Array.Sort(quotes, (a, b) => a.Date.CompareTo(b.Date));
                    break;
                }
            }
        }

        public static DateTime GetCurrentSessionAnchorUtc(
            Quote[]? oneMinuteQuotes,
            int sessionStartHourUtc)
        {
            DateTime last =
                (oneMinuteQuotes != null && oneMinuteQuotes.Length > 0)
                    ? oneMinuteQuotes[^1].Date
                    : DateTime.UtcNow;

            var dayAnchor = new DateTime(
                    last.Year,
                    last.Month,
                    last.Day,
                    0, 0, 0,
                    DateTimeKind.Utc)
                .AddHours(sessionStartHourUtc);

            // Jeśli ostatni bar jest "przed" kotwicą tego dnia,
            // to tak naprawdę jesteśmy jeszcze w sesji z poprzedniego dnia.
            if (last < dayAnchor)
                dayAnchor = dayAnchor.AddDays(-1);

            return dayAnchor;
        }

        public static double SelectReferencePrice(
            Ticker? ticker,
            Quote[]? referenceQuotes)
        {
            if (ticker != null && ticker.LastPrice > 0m)
                return (double)ticker.LastPrice;

            if (referenceQuotes != null && referenceQuotes.Length > 0)
                return (double)referenceQuotes[^1].Close;

            return double.NaN;
        }

        public static async Task<double> TryGetOrNaNAsync(
            Func<Task<double>> providerCall)
        {
            if (providerCall == null)
                throw new ArgumentNullException(nameof(providerCall));

            try
            {
                var v = await providerCall().ConfigureAwait(false);
                return (double.IsNaN(v) || double.IsInfinity(v)) ? double.NaN : v;
            }
            catch
            {
                return double.NaN;
            }
        }

        public static (decimal? Tp1, decimal? Tp2) ChooseTakeProfits(
    System.Collections.Generic.IReadOnlyList<(decimal Price, double RMultiple)> targets,
    OrderSide side,
    decimal entryPrice,
    decimal priceScale)
        {
            // Domyślne okno R dla trybów, które nie potrzebują osobnych progów.
            const double defaultMinR = 0.6;
            const double defaultMaxR = 3.0;
            const double defaultMinSpacing = 0.3;

            return ChooseTakeProfits(
                targets,
                side,
                entryPrice,
                priceScale,
                defaultMinR,
                defaultMaxR,
                defaultMinSpacing);
        }

        /// <summary>
        /// Wybór TP1/TP2 na podstawie listy kandydatów (Price, RMultiple).
        /// Parametry minR / maxR / minSpacing są podawane per-tryb (MM/MR/BO),
        /// tak aby dopasować okno zysku do charakteru reżimu.
        /// </summary>
        public static (decimal? Tp1, decimal? Tp2) ChooseTakeProfits(
            System.Collections.Generic.IReadOnlyList<(decimal Price, double RMultiple)> targets,
            OrderSide side,
            decimal entryPrice,
            decimal priceScale,
            double minR,
            double maxR,
            double minSpacing)
        {
            if (targets == null || targets.Count == 0)
                return (null, null);

            bool IsValid((decimal Price, double RMultiple) t)
            {
                if (t.Price <= 0m)
                    return false;

                if (!double.IsFinite(t.RMultiple))
                    return false;

                if (t.RMultiple < minR || t.RMultiple > maxR)
                    return false;

                return side switch
                {
                    OrderSide.Buy => t.Price > entryPrice,
                    OrderSide.Sell => t.Price < entryPrice,
                    _ => false
                };
            }

            // Filtrowanie po stronie / oknie R
            var buffer = new System.Collections.Generic.List<(decimal Price, double RMultiple)>(targets.Count);
            foreach (var t in targets)
            {
                if (IsValid(t))
                    buffer.Add(t);
            }

            if (buffer.Count == 0)
                return (null, null);

            // Sortujemy rosnąco po RMultiple
            buffer.Sort((a, b) => a.RMultiple.CompareTo(b.RMultiple));

            var tp1Candidate = buffer[0];
            decimal tp1 = MathHelpers.RoundPrice(priceScale, tp1Candidate.Price);

            if (tp1 <= 0m)
                return (null, null);

            // Po zaokrągleniu jeszcze raz upewniamy się, że TP1 leży po dobrej stronie
            if (side == OrderSide.Buy && tp1 <= entryPrice)
                return (null, null);
            if (side == OrderSide.Sell && tp1 >= entryPrice)
                return (null, null);

            decimal? tp2 = null;

            if (buffer.Count >= 2)
            {
                // Szukamy TP2 możliwie najdalej, ale zachowując minimalny odstęp w R
                for (int i = buffer.Count - 1; i >= 1; i--)
                {
                    var candidate = buffer[i];
                    if (candidate.RMultiple - tp1Candidate.RMultiple >= minSpacing)
                    {
                        var p = MathHelpers.RoundPrice(priceScale, candidate.Price);
                        if (p > 0m)
                        {
                            // Po zaokrągleniu kontrolujemy stronę względem entry
                            if (side == OrderSide.Buy && p > entryPrice)
                            {
                                tp2 = p;
                                break;
                            }

                            if (side == OrderSide.Sell && p < entryPrice)
                            {
                                tp2 = p;
                                break;
                            }
                        }
                    }
                }

                // Fallback – jeżeli nie znaleźliśmy kandydata spełniającego minSpacing,
                // bierzemy po prostu najdalszy sensowny target.
                if (tp2 is null)
                {
                    var last = buffer[^1];
                    var p = MathHelpers.RoundPrice(priceScale, last.Price);
                    if (p > 0m)
                    {
                        if (side == OrderSide.Buy && p > entryPrice)
                            tp2 = p;
                        else if (side == OrderSide.Sell && p < entryPrice)
                            tp2 = p;
                    }
                }
            }

            return (tp1, tp2);
        }


        public static decimal? GetRefPrice(SigmaData data)
        {
            var lastClose = data.Last1mClose ?? data.LastPrice;
            if (!lastClose.HasValue || lastClose.Value <= 0m)
                return null;
            return lastClose.Value;
        }

        public static decimal ComputeRiskUnit(
            SigmaData data,
            decimal refPrice,
            decimal floorPct,
            decimal capPct,
            decimal fallbackPct)
        {
            if (refPrice <= 0m)
                return 0m;

            var atr5m = double.IsFinite(data.Atr5mAbs) ? (decimal)data.Atr5mAbs : 0m;
            var floor = floorPct > 0m ? refPrice * floorPct / 100m : 0m;
            var cap = capPct > 0m ? refPrice * capPct / 100m : 0m;

            if (atr5m > 0m)
            {
                var risk = atr5m;

                if (floor > 0m)
                    risk = Math.Max(risk, floor);

                if (cap > 0m)
                    risk = Math.Min(risk, cap);

                return risk;
            }

            return refPrice * fallbackPct / 100m;
        }
    }
}

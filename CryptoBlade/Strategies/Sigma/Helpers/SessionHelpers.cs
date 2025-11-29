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
            IReadOnlyList<(decimal Price, double RMultiple)> targets, OrderSide side, decimal entryPrice, decimal priceScale)
        {
            if (targets == null || targets.Count == 0)
                return (null, null);

            const double minR = 0.6;
            const double maxR = 3.0;

            bool IsValid((decimal Price, double RMultiple) t)
            {
                if (t.Price <= 0m)
                    return false;

                if (side == OrderSide.Buy && t.Price <= entryPrice)
                    return false;

                if (side == OrderSide.Sell && t.Price >= entryPrice)
                    return false;

                if (!double.IsFinite(t.RMultiple))
                    return false;

                if (t.RMultiple < minR || t.RMultiple > maxR)
                    return false;

                return true;
            }

            var valid = targets
                .Where(IsValid)
                .OrderBy(t => t.RMultiple)
                .ToArray();

            if (valid.Length == 0)
                return (null, null);

            decimal tp1 = valid[0].Price;
            decimal? tp2 = null;
            const double minSpacing = 0.3;

            for (int i = valid.Length - 1; i >= 0; i--)
            {
                var (Price, RMultiple) = valid[i];
                if (RMultiple - valid[0].RMultiple >= minSpacing)
                {
                    tp2 = Price;
                    break;
                }
            }

            if (tp2 is null && valid.Length >= 2)
                tp2 = valid[^1].Price;

            tp1 = MathHelpers.RoundPrice(priceScale, tp1);
            if (tp2.HasValue)
                tp2 = MathHelpers.RoundPrice(priceScale, tp2.Value);

            if (tp1 <= 0m)
                return (null, null);

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

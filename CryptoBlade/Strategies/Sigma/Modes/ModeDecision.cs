using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public interface IModeController
    {
        /// <summary>
        /// Zwraca decyzję wejścia/zarządzania pozycją dla danego reżimu.
        /// </summary>
        ModeDecision Evaluate(FeatureSnapshot f, DateTime nowUtc, CancellationToken cancel);
    }
    public readonly struct ModeDecision(bool buy, bool sell, bool buyExtra, bool sellExtra)
    {
        public readonly bool HasBuy = buy, HasSell = sell, HasBuyExtra = buyExtra, HasSellExtra = sellExtra;
        public static ModeDecision None => new(false, false, false, false);
    }

    public sealed class EntryPlan
    {
        public decimal? EntryPrice { get; init; }           // limit na 50% świecy triggera / retest
        public decimal? StopLoss { get; init; }             // za ekstremum + 0.5–0.6*ATR5m (min floor)
        public decimal? TakeProfit1 { get; init; }          // 1R
        public decimal? TakeProfit2 { get; init; }          // 1.6–2.2R (MM/BO); dla MR opcjonalny
        public decimal? SizeQuote { get; init; }            // kwota w USDT
        public string? Reason { get; init; }
    }

    public static class GlobalGates
    {
        public static (bool ok, string reason) Evaluate(FeatureSnapshot f, DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (!double.IsFinite(f.SpreadBps) || f.SpreadBps > (double)o.MaxSpreadBps)
                return (false, $"Global gate: Spread {f.SpreadBps:F2} bps > {o.MaxSpreadBps}");

            if (IsMacroFreeze(nowUtc, o))
                return (false, "Global gate: Macro freeze window");

            // (opcjonalnie) Near-funding window ±3 min → blokada nowych wejść
            // if (f.NextFundingUtc.HasValue && Math.Abs((nowUtc - f.NextFundingUtc.Value).TotalMinutes) <= 3)
            //     return (false, "Global gate: Funding window");

            return (true, "OK");
        }

        internal static bool IsMacroFreeze(DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (o?.MacroEventsUtc == null || o.MacroEventsUtc.Count == 0) return false;
            var before = TimeSpan.FromMinutes(o.MacroFreezeMinutesBefore);
            var after = TimeSpan.FromMinutes(o.MacroFreezeMinutesAfter);
            foreach (var dt in o.MacroEventsUtc)
            {
                var t = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                if (nowUtc >= t - before && nowUtc <= t + after) return true;
            }
            return false;
        }
    }
}

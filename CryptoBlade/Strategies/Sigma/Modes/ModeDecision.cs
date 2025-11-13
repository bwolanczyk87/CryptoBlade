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
        /// <summary>
        /// Globalne bramki: Spread, Macro freeze, Funding window freeze, opcjonalnie hard-halt przy ekstremalnym fundingu.
        /// </summary>
        public static (bool ok, string reason) Evaluate(FeatureSnapshot f, DateTime nowUtc, SigmaStrategyOptions o)
        {
            // Spread (twardy)
            if (!double.IsFinite(f.SpreadBps) || f.SpreadBps > (double)o.MaxSpreadBps)
                return (false, $"Global gate: Spread {f.SpreadBps:F2} bps > {o.MaxSpreadBps}");

            // Macro freeze (twardy)
            if (IsMacroFreeze(nowUtc, o))
                return (false, "Global gate: Macro freeze window");

            // Funding window freeze (twardy) – wokół najbliższego cyklu
            if (IsFundingFreeze(f, nowUtc, o))
                return (false, "Global gate: Funding window");

            // Correlation gate (twardy): wysoka |ρ| z BTC + przeciwny bias BTC ⇒ blokada
            if (IsCorrOppositeBlocked(f, o))
                return (false, $"Global gate: Corr {f.CorrToBtc15m:F3} with opposite BTC bias");

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
                if (nowUtc >= t - before && nowUtc <= t + after)
                    return true;
            }
            return false;
        }

        internal static bool IsFundingFreeze(FeatureSnapshot f, DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (!f.NextFundingUtc.HasValue) return false;

            var dt = DateTime.SpecifyKind(f.NextFundingUtc.Value, DateTimeKind.Utc);
            var from = dt.AddMinutes(-o.FundingFreezeMinutesBefore);
            var to = dt.AddMinutes(o.FundingFreezeMinutesAfter);

            return nowUtc >= from && nowUtc <= to;
        }


        internal static bool IsCorrOppositeBlocked(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            if (!double.IsFinite(f.CorrToBtc15m)) return false;
            return Math.Abs(f.CorrToBtc15m) >= (double)o.CorrOppositeBlock
                   && f.BtcBiasOpposite;
        }
    }
}

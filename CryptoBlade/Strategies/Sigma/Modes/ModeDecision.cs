using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    /// <summary>
    /// Pojedynczy tryb (Momentum / MeanReversion / Breakout).
    /// Nie zna scoringu ani tego, czy jest „aktywny”.
    /// SigmaModeEngine wybiera tryb i tylko jego wywołuje.
    /// </summary>
    public interface IMode
    {
        /// <summary>
        /// Identyfikator trybu – mapuje się 1:1 na Regime.MM / MR / BO.
        /// </summary>
        Mode Kind { get; }

        /// <summary>
        /// Logika wejścia/zarządzania pozycją dla danego trybu.
        /// Implementacja:
        /// - jeśli Engine wybrał ten tryb, Execute zostanie wywołane
        ///   w bieżącym cyklu i może wygenerować sygnał.
        /// - jeśli Engine wybrał inny tryb, ten Mode nie jest wywoływany.
        /// </summary>
        ModeDecision Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel);
    }

    /// <summary>
    /// Decyzja trybu: flaga wejścia/wyjścia i „extra” (silniejszy setup).
    /// </summary>


    /// <summary>
    /// Globalne bramki (Spread / Macro / Funding / CorrOpposite).
    /// Używane przez SigmaModeEngine przed scoringiem reżimów.
    /// </summary>
    public static class ModeGlobalGates
    {
        /// <summary>
        /// Zwraca (ok, reason). ok=false oznacza twardy no-trade na nowe wejścia
        /// w bieżącym cyklu (można nadal zarządzać istniejącą pozycją).
        /// </summary>
        public static (bool ok, string reason) Evaluate(SigmaData d, DateTime nowUtc, SigmaStrategyOptions o)
        {
            // 1) Spread gate (twardy)
            if (!double.IsFinite(d.SpreadBps) || d.SpreadBps > (double)o.MaxSpreadBps)
                return (false, $"Global gate: Spread {d.SpreadBps:F2} bps > {o.MaxSpreadBps}");

            // 2) Macro freeze (twardy)
            if (IsMacroFreeze(nowUtc, o))
                return (false, "Global gate: Macro freeze window");

            // 3) Funding window freeze (twardy) – wokół najbliższego cyklu funding
            if (IsFundingFreeze(d, nowUtc, o))
                return (false, "Global gate: Funding window");

            // 4) Correlation gate (twardy): wysoka |ρ| z BTC + przeciwny bias BTC ⇒ blokada
            if (IsCorrOppositeBlocked(d, o))
                return (false, $"Global gate: Corr {d.CorrToBtc15m:F3} with opposite BTC bias");

            return (true, "OK");
        }

        internal static bool IsMacroFreeze(DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (o?.MacroEventsUtc == null || o.MacroEventsUtc.Count == 0)
                return false;

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

        internal static bool IsFundingFreeze(SigmaData d, DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (!d.NextFundingUtc.HasValue)
                return false;

            var dt = DateTime.SpecifyKind(d.NextFundingUtc.Value, DateTimeKind.Utc);

            var from = dt.AddMinutes(-o.FundingFreezeMinutesBefore);
            var to = dt.AddMinutes(o.FundingFreezeMinutesAfter);

            return nowUtc >= from && nowUtc <= to;
        }

        internal static bool IsCorrOppositeBlocked(SigmaData d, SigmaStrategyOptions o)
        {
            if (!double.IsFinite(d.CorrToBtc15m))
                return false;

            return Math.Abs(d.CorrToBtc15m) >= (double)o.CorrOppositeBlock
                && d.BtcBiasOpposite;
        }
    }
}

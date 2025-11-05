namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public static class RegimeEngine
    {
        public static (bool Tradable, string Reason, (bool Changed, RegimeState State) Decision)
            Evaluate(FeatureSnapshot f, RegimeState prev, DateTime nowUtc, SigmaStrategyOptions o)
        {
            // Global gates: wyłącznie SPREAD (twardy). ATR% obsługujemy maskami per-tryb w scorerze.
            if (f.SpreadBps > (double)o.MaxSpreadBps)
                if (f.SpreadBps > (double)o.MaxSpreadBps)
                return (
                    false, 
                    $"Spread gate: {f.SpreadBps:F2} bps > {o.MaxSpreadBps}",
                    (false, new RegimeState(Regime.None, prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc, RegimeScores.Zero)));

            // (opcjonalnie) brak krytycznych danych derywatowych → degraduj, ale nie twardy no-trade
            var scores = RegimeClassifier.Score(f, o);
            var decision = RegimeClassifier.Decide(scores, prev, nowUtc, o);

            return (true, "OK", decision);
        }
    }
}

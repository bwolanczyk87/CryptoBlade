namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public static class RegimeEngine
    {
        public static (bool Tradable, string Reason, (bool Changed, RegimeState State) Decision)
            Evaluate(FeatureSnapshot f, RegimeState prev, DateTime nowUtc, SigmaStrategyOptions o)
        {
            // Global gates: spread oraz zakres ATR% (sanity)
            if (f.SpreadBps > (double)o.MaxSpreadBps)
                return (false, $"Spread gate: {f.SpreadBps:F2} bps > {o.MaxSpreadBps}", (false, new RegimeState(Regime.None, prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc, RegimeScores.Zero)));

            if (f.AtrPct1h < (double)o.MinAtr1hPct || f.AtrPct1h > (double)o.MaxAtr1hPct)
                return (false, $"ATR gate: {f.AtrPct1h:F2}% not in [{o.MinAtr1hPct},{o.MaxAtr1hPct}]", (false, new RegimeState(Regime.None, prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc, RegimeScores.Zero)));

            // (opcjonalnie) brak krytycznych danych derywatowych → degraduj, ale nie twardy no-trade
            var scores = RegimeClassifier.Score(f, o);
            var decision = RegimeClassifier.Decide(scores, prev, nowUtc, o);

            return (true, "OK", decision);
        }
    }
}

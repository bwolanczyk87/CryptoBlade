namespace CryptoBlade.Strategies.Sigma.Regimes
{
    internal enum Regime { None, Momentum, MeanReversion, Breakout }

    internal static class RegimeClassifier
    {
        public static RegimeScores Score(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            // Szkic: licz proste punkty; finalnie wstawisz pełne formuły
            double mm = 0, mr = 0, bo = 0;

            // Momentum: ADX wysoki, nachylenie wartości >, OI↑, dodatnia autokorelacja
            if (f.Adx1h >= (double)o.AdxEnableMomentum) mm += 20;
            if (Math.Abs(f.ZSlopeDvwap) >= 0.8) mm += 15;
            if (f.OiDelta1hPct > 0) mm += 15;
            if (f.AutoCorr5m > 0) mm += 10;
            if (f.Bbw15mPct >= 60) mm += 10;

            // Mean Reversion: trend słaby, |zVWAP| duże, OI neutral/↓
            if (f.Adx1h <= (double)o.AdxDisableMomentum) mr += 20;
            if (Math.Abs(f.ZDvwap) >= (double)o.ZVwapEnableMR) mr += 15;
            if (f.Bbw15mPct is >= 35 and <= 60) mr += 15;
            if (f.OiDelta1hPct <= 0) mr += 10;

            // Breakout: niska BBW, inside/NR7 flagi, OI↑ w kompresji
            if (f.Bbw15mPct <= (double)o.BbWidthBreakoutPct) bo += 25;
            if (f.HasInsideOrNr7) bo += 15;
            if (f.OiDelta1hPct > 0 && f.Bbw15mPct <= 40) bo += 10;

            return new RegimeScores(mm, mr, bo);
        }

        public static (bool Changed, RegimeState NewState) Decide(RegimeScores s, RegimeState prev, DateTime nowUtc, SigmaStrategyOptions o)
        {
            // argmax + margines + minimum score + dwell lock
            var list = new List<(Regime Mode, double Score)>
            {
                (Regime.Momentum, s.Momentum),
                (Regime.MeanReversion, s.MeanReversion),
                (Regime.Breakout, s.Breakout),
            }.OrderByDescending(x => x.Score).ToArray();

            var best = list[0];
            var second = list[1];

            bool pass = best.Score >= (double)o.MinScore && best.Score - second.Score >= (double)o.MinMargin;
            bool dwellOk = nowUtc - prev.SinceUtc >= TimeSpan.FromMinutes(o.HysteresisLockMinutes);

            var target = pass ? best.Mode : Regime.None;

            if (target == prev.Mode) // brak zmiany trybu
                return (false, new RegimeState(prev.Mode, prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc, s));

            // jeśli chcemy przełączyć, ale lock nie minął, zostajemy
            if (!dwellOk && prev.Mode != Regime.None)
                return (false, new RegimeState(prev.Mode, prev.SinceUtc, s));

            // przełącz
            return (true, new RegimeState(target, nowUtc, s));
        }
    }
}

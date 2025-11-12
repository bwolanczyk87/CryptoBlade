using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public enum Regime { None, Momentum, MeanReversion, Breakout }

    public static class RegimeClassifier
    {
        public static RegimeScores Score(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            double mm = 0, mr = 0, bo = 0;

            // ATR gates per-mode (NaN => false)
            bool mmAtrOk = Finite(f.AtrPct1h) && f.AtrPct1h >= (double)o.MmAtrMinPct && f.AtrPct1h <= (double)o.MmAtrMaxPct;
            bool mrAtrOk = Finite(f.AtrPct1h) && f.AtrPct1h >= (double)o.MrAtrMinPct && f.AtrPct1h <= (double)o.MrAtrMaxPct;
            bool boAtrOk = !Finite(f.AtrPct1h) || f.AtrPct1h <= (double)o.BoAtrMaxPct; // brak dolnego progu dla BO; NaN traktuj neutralnie

            // ===== Momentum: silny trend, "value" rośnie, przepływy +, autokorelacja +, wysoka zmienność =====
            if (Finite(f.Adx1h) && f.Adx1h >= (double)o.AdxEnableMomentum) mm += 20;
            if (Finite(f.ZSlopeDvwap) && Math.Abs(f.ZSlopeDvwap) >= 0.8) mm += 15;
            if (Finite(f.OiDelta1hPct) && f.OiDelta1hPct > 0) mm += 15;
            if (Finite(f.AutoCorr5m) && f.AutoCorr5m > 0) mm += 10;
            if (Finite(f.Bbw15mPct) && f.Bbw15mPct >= 60) mm += 10;
            if (!mmAtrOk) mm = 0;

            // ===== Mean Reversion: trend słaby, odchylenie od wartości duże, BBW średnie, OI neutral/↓ =====
            if (Finite(f.Adx1h) && f.Adx1h <= (double)o.AdxDisableMomentum) mr += 20;
            if (Finite(f.ZDvwap) && Math.Abs(f.ZDvwap) >= (double)o.ZVwapEnableMR) mr += 15;
            if (Finite(f.Bbw15mPct) && f.Bbw15mPct is >= 35 and <= 60) mr += 15;
            if (Finite(f.OiDelta1hPct) && f.OiDelta1hPct <= 0) mr += 10;
            if (!mrAtrOk) mr = 0;

            // ===== Breakout: kompresja + wybicie + preferencja ekspansji i flow =====
            if (Finite(f.Bbw15mPct) && f.Bbw15mPct <= (double)o.BbWidthBreakoutPct) bo += 25; // squeeze
            if (f.HasInsideOrNr7) bo += 15;        // mikro-kompresja świec
            if (f.DonchianBreak && Finite(f.Bbw15mPct) && f.Bbw15mPct <= 40) bo += 10;        // wybicie kanału z niskiej BBW
            if (f.Bbw15mExpanding) bo += 5;         // ekspansja po squeeze
            if (Finite(f.OiDelta1hPct) && f.OiDelta1hPct > 0 && Finite(f.Bbw15mPct) && f.Bbw15mPct <= 40) bo += 5; // ΔOI$ w kompresji
            if (!boAtrOk) bo = 0;

            return new RegimeScores(mm, mr, bo);
        }

        public static (bool Changed, RegimeState NewState) Decide(
            RegimeScores s,
            RegimeState prev,
            DateTime nowUtc,
            SigmaStrategyOptions o)
        {
            // Brak kandydatów
            if (s.Momentum == 0 && s.MeanReversion == 0 && s.Breakout == 0)
            {
                if (prev.Mode == Regime.None)
                    return (false, new RegimeState(
                        Regime.None,
                        prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc,
                        s));

                return (true, new RegimeState(Regime.None, nowUtc, s));
            }

            // argmax + margines + minimum score + dwell lock
            var list = new List<(Regime Mode, double Score)>
            {
                (Regime.Momentum, s.Momentum),
                (Regime.MeanReversion, s.MeanReversion),
                (Regime.Breakout, s.Breakout),
            }.OrderByDescending(x => x.Score).ToArray();

            var best = list[0];
            var second = list[1];

            bool pass = best.Score >= (double)o.MinScore &&
                        best.Score - second.Score >= (double)o.MinMargin;

            bool dwellOk = nowUtc - prev.SinceUtc >= TimeSpan.FromMinutes(o.HysteresisLockMinutes);
            var target = pass ? best.Mode : Regime.None;

            if (target == prev.Mode)
                return (false, new RegimeState(prev.Mode,
                    prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc, s));

            if (!dwellOk && prev.Mode != Regime.None)
                return (false, new RegimeState(prev.Mode, prev.SinceUtc, s));

            return (true, new RegimeState(target, nowUtc, s));
        }

        // ===== utils =====
        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}

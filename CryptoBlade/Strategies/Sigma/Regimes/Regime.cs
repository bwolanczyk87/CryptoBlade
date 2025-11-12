using CryptoBlade.Strategies.Sigma.Modes;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public enum Regime { None, Momentum, MeanReversion, Breakout }
    public readonly struct RegimeState(Regime mode, DateTime sinceUtc, RegimeScores scores)
    {
        public readonly Regime Mode = mode;
        public readonly DateTime SinceUtc = sinceUtc;
        public readonly RegimeScores Scores = scores;
    }

    public readonly struct RegimeScores(double mm, double mr, double bo)
    {
        public readonly double Momentum = mm;
        public readonly double MeanReversion = mr;
        public readonly double Breakout = bo;

        public static RegimeScores Zero => new(0, 0, 0);
    }

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

    public static class RegimeEngine
    {
        /// <summary>
        /// Tylko selekcja reżimu (bez wejścia w trade). Opcjonalnie zapisuje audyt.
        /// </summary>
        public static (bool Tradable, string Reason, (bool Changed, RegimeState State) Decision)
            Evaluate(FeatureSnapshot f, RegimeState prev, DateTime nowUtc, SigmaStrategyOptions o, IRegimeAuditSink audit)
        {
            var noneState = new RegimeState(Regime.None,
                prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc,
                RegimeScores.Zero);

            bool tradable = true;
            string reason = "OK";

            // [1] Globalny: twardy gate na spread
            if (!double.IsFinite(f.SpreadBps) || f.SpreadBps > (double)o.MaxSpreadBps)
            {
                tradable = false;
                var spreadTxt = double.IsFinite(f.SpreadBps) ? f.SpreadBps.ToString("F2") : "NaN";
                reason = $"Spread gate: {spreadTxt} bps > {o.MaxSpreadBps}";
                var recGate = MakeRecord(f, prev, nowUtc, o, tradable, reason, noneState);
                audit.Add(recGate);
                return (false, reason, (false, noneState));
            }

            // [2] Supervisor: korelacja BTC
            if (double.IsFinite(f.CorrToBtc15m) &&
                Math.Abs(f.CorrToBtc15m) >= 0.85 &&
                f.BtcBiasOpposite)
            {
                tradable = false;
                reason = "Supervisor: BTC corr≥0.85 & opposite bias";
                var recSup = MakeRecord(f, prev, nowUtc, o, tradable, reason, noneState);
                audit.Add(recSup);
                return (false, reason, (false, noneState));
            }

            // [3] Scoring + decyzja
            var scores = RegimeClassifier.Score(f, o);
            var decision = RegimeClassifier.Decide(scores, prev, nowUtc, o);

            // Audyt z decyzją
            var rec = MakeRecord(f, prev, nowUtc, o, tradable, reason, decision.NewState);
            audit.Add(rec);

            return (tradable, reason, decision);
        }

        /// <summary>
        /// Selekcja reżimu + decyzja kontrolera. Audyt tworzy Evaluate(...).
        /// </summary>
        public static (bool Tradable, string Reason, (bool Changed, RegimeState State) Decision, ModeDecision TradeDecision)
            EvaluateTrade(
                FeatureSnapshot f,
                RegimeState prev,
                DateTime nowUtc,
                SigmaStrategyOptions o,
                IModeController momentumCtrl,
                IModeController meanReversionCtrl,
                IModeController breakoutCtrl,
                IRegimeAuditSink? audit,
                CancellationToken cancel = default)
        {
            var (tradable, reason, decision) = Evaluate(f, prev, nowUtc, o, audit);
            if (!tradable)
                return (false, reason, decision, ModeDecision.None);

            var activeState = decision.State;

            // Supervisory gates (miękkie)
            if (IsFundingFreeze(nowUtc, TimeSpan.FromMinutes(3)))
                return (false, "Supervisor: funding freeze ±3m", decision, ModeDecision.None);

            if (IsMacroFreeze(nowUtc, o))
                return (false, "Supervisor: macro freeze window", decision, ModeDecision.None);

            IModeController? ctrl = activeState.Mode switch
            {
                Regime.Momentum => momentumCtrl,
                Regime.MeanReversion => meanReversionCtrl,
                Regime.Breakout => breakoutCtrl,
                _ => null
            };

            if (ctrl is null) return (true, "OK", decision, ModeDecision.None);
            var tradeDecision = ctrl.Evaluate(f, activeState, nowUtc, cancel);
            return (true, "OK", decision, tradeDecision);
        }

        // ===== Helpers =====

        private static RegimeAuditRecord MakeRecord(
            FeatureSnapshot f,
            RegimeState prev,
            DateTime nowUtc,
            SigmaStrategyOptions o,
            bool tradable,
            string reason,
            RegimeState decided)
        {
            return new RegimeAuditRecord
            {
                TimeUtc = nowUtc,
                Symbol = f.Symbol,
                Selected = (RegimeLabel)decided.Mode,
                Prev = (RegimeLabel)prev.Mode,
                Oracle = null,

                Tradable = tradable,
                Reason = reason,
                MinScore = (double)o.MinScore,
                MinMargin = (double)o.MinMargin,
                HysteresisLockMinutes = o.HysteresisLockMinutes,

                ScoreMM = decided.Scores.Momentum,
                ScoreMR = decided.Scores.MeanReversion,
                ScoreBO = decided.Scores.Breakout,

                Adx1h = f.Adx1h,
                AtrPct1h = f.AtrPct1h,
                Atr1hAbs = f.Atr1hAbs,
                ZDvwap = f.ZDvwap,
                VwapKindUsed = (int)f.VwapKindUsed,
                ZSlopeDvwap = f.ZSlopeDvwap,
                AutoCorr5m = f.AutoCorr5m,
                Bbw15mPct = f.Bbw15mPct,
                Bbw15mRaw = f.Bbw15mRaw,

                SpreadBps = f.SpreadBps,

                OiDelta1hPct = f.OiDelta1hPct,
                Funding8h = f.Funding8h,
                BasisPct = f.BasisPct,
                DeltaCvd5m = f.DeltaCvd5m,
                DistToLiqPct = f.DistToLiqPct,

                HasInsideOrNr7 = f.HasInsideOrNr7,
                DonchianBreakUp = f.DonchianBreakUp,
                DonchianBreakDown = f.DonchianBreakDown,
                Bbw15mExpanding = f.Bbw15mExpanding,
                OpeningRangeHigh = f.OpeningRangeHigh,
                OpeningRangeLow = f.OpeningRangeLow,

                CorrToBtc15m = f.CorrToBtc15m,
                BtcBiasOpposite = f.BtcBiasOpposite,

                SinceUtc = decided.SinceUtc,
            };
        }

        private static bool IsFundingFreeze(DateTime nowUtc, TimeSpan around, int intervalHours = 8, int anchorHourUtc = 0)
        {
            var dayAnchor = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, anchorHourUtc, 0, 0, DateTimeKind.Utc);
            var sinceAnchor = nowUtc - dayAnchor;
            var hours = sinceAnchor.TotalHours;
            var mod = hours % intervalHours;
            var minutesFromFunding = Math.Min(mod, intervalHours - mod) * 60.0;
            return Math.Abs(minutesFromFunding) <= Math.Abs(around.TotalMinutes);
        }

        private static bool IsMacroFreeze(DateTime nowUtc, SigmaStrategyOptions o)
        {
            var arr = o.MacroEventsUtc ?? [.. Array.Empty<DateTime>()
                .Select(d => DateTime.SpecifyKind(d, DateTimeKind.Utc))
                .OrderBy(d => d)];

            if (arr.Count == 0) return false;

            var before = TimeSpan.FromMinutes(o.MacroFreezeMinutesBefore);
            var after = TimeSpan.FromMinutes(o.MacroFreezeMinutesAfter);

            // arr jest posortowane – przerywamy, kiedy minęliśmy okno
            foreach (var t in arr)
            {
                var from = t - before;
                if (nowUtc < from) return false;           // przed najbliższym oknem ⇒ brak freeze
                var to = t + after;
                if (nowUtc <= to) return true;             // w oknie [t-before, t+after]
                                                           // else: jesteśmy po tym oknie, sprawdzamy kolejne
            }
            return false;
        }
    }
}

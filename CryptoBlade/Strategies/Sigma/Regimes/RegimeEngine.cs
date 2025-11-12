using Accord.IO;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Modes;
using System;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
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

            if (IsMacroFreeze(nowUtc))
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

        private static bool IsMacroFreeze(DateTime nowUtc) => false;
    }
}

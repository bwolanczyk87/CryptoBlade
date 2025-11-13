using Accord.MachineLearning;
using Accord.Statistics.Kernels;
using CryptoBlade.Strategies.Sigma.Modes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public enum Regime { None, MM, MR, BO }

    public readonly record struct RegimeScores(double Momentum, double MeanReversion, double Breakout)
    {
        public static readonly RegimeScores Zero = new(0,0,0);
        public double this[Regime r] => r switch
        {
            Regime.MM => Momentum,
            Regime.MR => MeanReversion,
            Regime.BO => Breakout,
            _ => 0
        };
    }

    public readonly record struct RegimeState(Regime Mode, DateTime SinceUtc, RegimeScores Scores);

    public readonly record struct RegimeDecision(
        bool Changed,
        RegimeState State,
        Regime ProposedMode,
        double ProposedScore,
        double SecondBestScore,
        double Margin);

    public static class RegimeEngine
    {
        // ====== scoring: pair-aware ATR + clamping/tanh dla outlierów ======

        const int ATR_CAP = 500; // ~500 godzin historii (kilka tygodni)
        static readonly ConcurrentDictionary<string, Queue<double>> _atrHist = new();
        static readonly ConcurrentDictionary<string, object> _atrLocks = new();

        public static RegimeScores Score(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            double mm = 0, mr = 0, bo = 0;

            // Guardy/normalizacje
            double adx  = Helpers.Stats.Clamp(f.Adx1h,        0, 100);
            double zdev = Helpers.Stats.Clamp(f.ZDvwap,     -10,  10);
            double zslo = Helpers.Stats.Clamp(f.ZSlopeDvwap, -10,  10);
            double bbwP = Helpers.Stats.Clamp(f.Bbw15mPct,     0, 100);
            double ac   = Helpers.Stats.Clamp(f.AutoCorr5m,   -1,   1);
            double oiT  = Helpers.Stats.TanhScaled(f.OiDelta1hPct, 0.8);

            double zdevT = Helpers.Stats.TanhScaled(Math.Abs(zdev), 2.0); // [0,1]
            double zsloT = Helpers.Stats.TanhScaled(zslo,           2.0); // [-1,1]

            // Rolling ATR%_1h histogram per symbol (pair-aware progi)
            var q = _atrHist.GetOrAdd(f.Symbol, _ => new Queue<double>(ATR_CAP));
            var locker = _atrLocks.GetOrAdd(f.Symbol, _ => new object());
            if (double.IsFinite(f.AtrPct1h) && f.AtrPct1h > 0)
            {
                lock (locker)
                {
                    if (q.Count >= ATR_CAP) q.Dequeue();
                    q.Enqueue(f.AtrPct1h);
                }
            }

            double mmMin = (double)o.MmAtrMinPct, mmMax = (double)o.MmAtrMaxPct;
            double mrMin = (double)o.MrAtrMinPct, mrMax = (double)o.MrAtrMaxPct;
            double boMax = (double)o.BoAtrMaxPct;

            int histCount; double[] xs;
            lock (locker) { histCount = q.Count; xs = q.ToArray(); }

            if (histCount >= 120) // dopiero po sensownej historii
            {
                double p20 = Helpers.Stats.Percentiles.Pctl(xs, 20);
                double p50 = Helpers.Stats.Percentiles.Pctl(xs, 50);
                double p60 = Helpers.Stats.Percentiles.Pctl(xs, 60);
                double p80 = Helpers.Stats.Percentiles.Pctl(xs, 80);

                // Momentum: wyższe percentyle ATR
                mmMin = p50; mmMax = p80;

                // MR: umiarkowane percentyle
                mrMin = p20; mrMax = p60;

                // BO: górny bezpiecznik
                boMax = Math.Max(boMax, p80);
            }

            bool haveAtr = double.IsFinite(f.AtrPct1h);
            bool mmAtrOk = haveAtr && f.AtrPct1h >= mmMin && f.AtrPct1h <= mmMax;
            bool mrAtrOk = haveAtr && f.AtrPct1h >= mrMin && f.AtrPct1h <= mrMax;
            bool boAtrOk = haveAtr && f.AtrPct1h <= boMax;

            // ===== Momentum =====
            if (adx >= (double)o.AdxEnableMomentum)          mm += 20;
            if (Math.Abs(zsloT) >= 0.30)                     mm += 10 * (1.0 + Math.Abs(zsloT)); // 10..20
            if (oiT > 0)                                     mm += 12 * oiT;                      // 0..12
            if (ac  > 0)                                     mm +=  8 * ac;                       // 0..8
            if (bbwP >= (double)o.BbWidthBreakoutPct)        mm +=  8;
            if (!mmAtrOk)                                    mm  =  0;

            // ===== Mean Reversion =====
            if (adx <= (double)o.AdxDisableMomentum)         mr += 20;
            mr += (zdevT >= 0.9 ? 18 : 12);
            if (bbwP >= 25 && bbwP <= 65)                    mr +=  8;
            if (Math.Abs(oiT) <= 0.30)                       mr +=  6;
            if (!mrAtrOk)                                    mr  =  0;

            // ===== Breakout =====
            if (bbwP <= (double)o.BbWidthBreakoutPct)        bo += 20; // kompresja
            bo += 10 * Math.Abs(zsloT);                              // 0..10
            if (ac > 0)                                      bo +=  6 * ac;
            if (!boAtrOk)                                    bo  =  0;

            mm = Helpers.Stats.Clamp(mm, 0, 100);
            mr = Helpers.Stats.Clamp(mr, 0, 100);
            bo = Helpers.Stats.Clamp(bo, 0, 100);

            return new RegimeScores(mm, mr, bo);
        }

        // ====== klasyfikacja: argmax + histereza/dwell/minScore/minMargin ======

        public static RegimeDecision Classify(
            FeatureSnapshot f,
            RegimeState prev,
            DateTime nowUtc,
            SigmaStrategyOptions o,
            IRegimeAuditSink? audit = null,
            double stickyBoost = 6.0,      // boost dla aktywnego reżimu w czasie dwell
            double minSwitchGain = 8.0)    // dodatkowy warunek gdy dwell minął
        {
            var scores = Score(f, o);
            var dict = new Dictionary<Regime, double>
            {
                { Regime.MM, scores.Momentum },
                { Regime.MR, scores.MeanReversion },
                { Regime.BO, scores.Breakout }
            };

            // Sticky boost w okresie histerezy
            var dwell = TimeSpan.FromMinutes(o.HysteresisLockMinutes);
            if (prev.Mode != Regime.None && (nowUtc - prev.SinceUtc) < dwell)
                dict[prev.Mode] += stickyBoost;

            // argmax
            var ordered = dict.OrderByDescending(kv => kv.Value).ToArray();
            var proposed = ordered[0].Key;
            var proposedScore = ordered[0].Value;
            var secondScore = ordered.Length > 1 ? ordered[1].Value : 0;
            var margin = proposedScore - secondScore;

            // Warunki aktywacji (bez globalnych gate'ów!)
            bool passMinScore  = proposedScore >= (double)o.MinScore;
            bool passMinMargin = margin        >= (double)o.MinMargin;

            Regime next = prev.Mode;
            DateTime since = prev.SinceUtc;

            // pozwól na przełączenie:
            bool dwellOver = (nowUtc - prev.SinceUtc) >= dwell;
            bool enoughGain = proposedScore - dict.GetValueOrDefault(prev.Mode, 0) >= minSwitchGain;

            if ((passMinScore && passMinMargin) || (dwellOver && enoughGain))
            {
                if (proposed != prev.Mode)
                {
                    next  = proposed;
                    since = nowUtc;
                }
            }

            var state = new RegimeState(next, since == DateTime.MinValue ? nowUtc : since, scores);
            var changed = next != prev.Mode;

            return new RegimeDecision(
                Changed: changed,
                State: state,
                ProposedMode: proposed,
                ProposedScore: proposedScore,
                SecondBestScore: secondScore,
                Margin: margin);
        }
    }
}

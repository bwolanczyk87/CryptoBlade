using System;
using System.Collections.Generic;
using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public interface IMode
    {
        Mode Kind { get; }
        ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel);
    }

    public enum Mode { None, MM, MR, BO }

    public readonly record struct ModeState(Mode Mode, DateTime SinceUtc, ModeScores Scores);

    public readonly record struct ModeScores(double Momentum, double MeanReversion, double Breakout)
    {
        public static readonly ModeScores Zero = new(0, 0, 0);

        public double this[Mode r] => r switch
        {
            Mode.MM => Momentum,
            Mode.MR => MeanReversion,
            Mode.BO => Breakout,
            _ => 0
        };
    }

    public readonly struct ModeSignal(bool buy, bool sell, bool buyExtra, bool sellExtra)
    {
        public readonly bool HasBuy = buy;
        public readonly bool HasSell = sell;
        public readonly bool HasBuyExtra = buyExtra;
        public readonly bool HasSellExtra = sellExtra;

        public static ModeSignal None => new(false, false, false, false);
    }

    public readonly record struct ModeDecision(
        bool Changed,
        ModeState State,
        Mode ProposedMode,
        double ProposedScore,
        double SecondBestScore,
        double Margin
    );

    /// <summary>
    /// Orkiestrator trybów Sigmy.
    /// - odpowiada za:
    ///   * globalne bramki (Spread / Macro / Funding / Corr),
    ///   * scoring trybów (Momentum / MeanReversion / Breakout),
    ///   * histerezę / dwell / MinScore / MinMargin,
    ///   * wybór jednego aktywnego Mode.
    /// - NIE zawiera logiki wejść/wyjść z pozycji – to robią konkretne Mode'y.
    /// </summary>
    public sealed class ModeEngine
    {
        private readonly SigmaStrategyOptions _options;
        private readonly IRegimeAuditSink? _audit;

        // Tryby zarejestrowane przez strategię.
        private readonly IReadOnlyDictionary<Mode, IMode> _modes;

        // Bieżący stan (Mode + Since + Scores)
        private ModeState _state;

        public ModeEngine(
            SigmaStrategyOptions options,
            IMode momentumMode,
            IMode meanReversionMode,
            IMode breakoutMode,
            IRegimeAuditSink? audit = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _audit = audit;

            _modes = new Dictionary<Mode, IMode>
            {
                [Mode.MM] = momentumMode ?? throw new ArgumentNullException(nameof(momentumMode)),
                [Mode.MR] = meanReversionMode ?? throw new ArgumentNullException(nameof(meanReversionMode)),
                [Mode.BO] = breakoutMode ?? throw new ArgumentNullException(nameof(breakoutMode)),
            };

            _state = new ModeState(Mode.None, DateTime.MinValue, ModeScores.Zero);
        }

        /// <summary>
        /// Główna metoda orkiestratora:
        /// - stosuje globalne bramki (Spread/Macro/Funding/Corr),
        /// - jeśli OK, woła Classify(SigmaData, ...),
        /// - aktualizuje stan i zwraca:
        ///   * wybrany IMode (albo null gdy Mode.None),
        ///   * ModeDecision (dla logowania / diagnostyki),
        ///   * reason z globalnych bramek.
        /// </summary>
        public (IMode? Mode, ModeDecision ModeDecision, string GlobalGateReason) Evaluate(
            SigmaData data,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            // 1) Globalne bramki (twarde) – decydują, czy w ogóle wolno otwierać nowe pozycje.
            var (ok, reason) = CheckGlobalGates(data, nowUtc, _options);

            if (!ok)
            {
                // W trybie gate-u logicznie przechodzimy do None (ignorujemy dwell).
                var scores = ModeScores.Zero;
                var state = new ModeState(
                    Mode.None,
                    _state.SinceUtc == DateTime.MinValue ? nowUtc : _state.SinceUtc,
                    scores);

                var noneDecision = new ModeDecision(
                    Changed: state.Mode != _state.Mode,
                    State: state,
                    ProposedMode: Mode.None,
                    ProposedScore: 0.0,
                    SecondBestScore: 0.0,
                    Margin: 0.0);

                _state = state;

                return (null, noneDecision, reason);
            }

            // 2) Klasyfikacja trybu (scoring + histereza + dwell + progi MinScore/MinMargin)
            var decision = Classify(data, _state, nowUtc, _options);
            _state = decision.State;

            // 3) Mapowanie trybu na konkretne Mode (fabryka)
            IMode? mode = decision.State.Mode switch
            {
                Mode.MM => _modes[Mode.MM],
                Mode.MR => _modes[Mode.MR],
                Mode.BO => _modes[Mode.BO],
                _ => null
            };

            return (mode, decision, reason ?? "OK");
        }

        // =====================================================================
        //  SCORING (MM / MR / BO)
        // =====================================================================

        /// <summary>
        /// Liczy surowe score'y dla trzech trybów (0..100) na podstawie SigmaData.
        /// ATR gating per-tryb jest wbudowany (gdy ATR poza zakresem, score=0).
        /// </summary>
        private static ModeScores Score(SigmaData d, SigmaStrategyOptions o)
        {
            double mm = 0, mr = 0, bo = 0;

            double atr = d.AtrPct1h;
            double adx = Safe(d.Adx1h);
            double zdev = Safe(d.ZDvwap, -10, 10);
            double zslope = Safe(d.ZSlopeDvwap, -25, 25);
            double bbwP = Safe(d.Bbw15mPct, 0, 100);
            double ac = Safe(d.AutoCorr5m, -1, 1);
            double oi = Safe(d.OiDelta1hPct, -500, 500);
            double basis = Safe(d.BasisPct, -100, 100);

            bool haveAtr = double.IsFinite(atr) && atr > 0;

            // ATR-maski per-tryb
            bool mmAtrOk = haveAtr &&
                           atr >= (double)o.MmAtrMinPct &&
                           atr <= (double)o.MmAtrMaxPct;

            bool mrAtrOk = haveAtr &&
                           atr >= (double)o.MrAtrMinPct &&
                           atr <= (double)o.MrAtrMaxPct;

            bool boAtrOk = haveAtr &&
                           atr <= (double)o.BoAtrMaxPct;

            // ===== Momentum (MM) =====
            if (mmAtrOk)
            {
                // 1) Trend wg ADX – rośnie między AdxDisableMomentum a AdxEnableMomentum
                double adxNorm = Normalize01(adx,
                    (double)o.AdxDisableMomentum,
                    (double)o.AdxEnableMomentum);
                mm += 40.0 * Clamp01(adxNorm); // 0..40

                // 2) Slope DVWAP – dodatni/ujemny trend (tylko dodatnia część jako "siła")
                double slopeScore = Math.Tanh(zslope / 2.0); // ~[-1..1]
                mm += 20.0 * Math.Max(0.0, slopeScore);      // 0..20

                // 3) Oddalenie od DVWAP – większe |z| = po korekcie / daleko od value
                double zAbs = Math.Abs(zdev);
                double zScore = Normalize01(zAbs, 0.5, 3.0);
                mm += 20.0 * Clamp01(zScore);                // 0..20

                // 4) ΔOI zgodny z kierunkiem nachylenia DVWAP
                int trendSign = Sign(zslope, 0.1);
                double oiAligned = 0.0;
                if (trendSign != 0)
                {
                    int oiSign = Sign(oi, 0.2);
                    if (oiSign == trendSign)
                    {
                        // saturacja przy ok. 10% zmiany OI
                        double oiMag = Math.Min(Math.Abs(oi) / 10.0, 1.0);
                        oiAligned = oiMag;
                    }
                }
                mm += 20.0 * oiAligned;                      // 0..20

                // 5) Lekka premia za dodatnią autokorelację (kontynuacja)
                if (ac > 0)
                    mm += 10.0 * ac;                         // max +10
            }

            // ===== Mean Reversion (MR) =====
            if (mrAtrOk)
            {
                // 1) Niski ADX – im niższy tym lepiej dla MR
                double adxLow = 1.0 - Normalize01(adx,
                    (double)o.AdxDisableMomentum,
                    (double)o.AdxEnableMomentum);
                adxLow = Clamp01(adxLow);
                mr += 30.0 * adxLow;                         // 0..30

                // 2) Duże oddalenie od DVWAP – sygnał "przesterowania"
                double zAbs = Math.Abs(zdev);
                double zScore = Normalize01(zAbs,
                    (double)o.ZVwapEnableMR,
                    3.0);
                mr += 40.0 * Clamp01(zScore);                // 0..40

                // 3) Ujemna autokorelacja sprzyja MR; blisko zera też OK
                if (ac < 0)
                    mr += 20.0 * (-ac);                      // max +20 przy ac=-1
                else
                    mr += 10.0 * (1.0 - ac);                 // flattish rynek dostaje lekką premię

                // 4) MR lubi relatywnie wąskie BB – kompresja
                double bbwNorm = Normalize01(bbwP, 0.0, (double)o.BbWidthExitBreakoutPct);
                mr += 10.0 * (1.0 - Clamp01(bbwNorm));       // 0..10, im mniejsze bbw tym więcej
            }

            // ===== Breakout (BO) =====
            if (boAtrOk)
            {
                // 1) Szerokie pasma BB – breakout z kompresji w kierunku ekspansji
                double bbwNorm = Normalize01(bbwP,
                    (double)o.BbWidthBreakoutPct,
                    (double)o.BbWidthExitBreakoutPct);
                bbwNorm = Clamp01(bbwNorm);
                bo += 40.0 * bbwNorm;                        // 0..40

                // 2) Ekspansja BB – gwałtowna zmiana zmienności
                if (d.Bbw15mExpanding)
                    bo += 10.0;

                // 3) Strukturalne wybicia (Donchian, inside/NR7)
                if (d.DonchianBreakUp || d.DonchianBreakDown)
                    bo += 20.0;

                if (d.HasInsideOrNr7)
                    bo += 10.0;

                // 4) ΔOI>0 – napływ kapitału na wybiciu
                if (oi > 0)
                {
                    double oiMag = Math.Min(oi / 10.0, 1.0);
                    bo += 20.0 * oiMag;
                }

                // 5) Dodatnia autokorelacja – kontynuacja ruchu po wybiciu
                if (ac > 0)
                    bo += 10.0 * ac;
            }

            // Clamp do [0..100]
            mm = Math.Min(Math.Max(mm, 0.0), 100.0);
            mr = Math.Min(Math.Max(mr, 0.0), 100.0);
            bo = Math.Min(Math.Max(bo, 0.0), 100.0);

            return new ModeScores(mm, mr, bo);
        }

        // =====================================================================
        //  KLASYFIKACJA (argmax + histereza/dwell/MinScore/MinMargin)
        // =====================================================================

        private static ModeDecision Classify(
            SigmaData d,
            ModeState prev,
            DateTime nowUtc,
            SigmaStrategyOptions o)
        {
            var scores = Score(d, o);

            var dict = new Dictionary<Mode, double>
            {
                { Mode.MM, scores.Momentum },
                { Mode.MR, scores.MeanReversion },
                { Mode.BO, scores.Breakout }
            };

            // argmax + drugi najlepszy
            Mode proposed = Mode.None;
            double proposedScore = 0.0;
            double secondScore = 0.0;

            foreach (var kv in dict)
            {
                var m = kv.Key;
                var s = kv.Value;
                if (s > proposedScore)
                {
                    secondScore = proposedScore;
                    proposedScore = s;
                    proposed = m;
                }
                else if (s > secondScore)
                {
                    secondScore = s;
                }
            }

            double margin = proposedScore - secondScore;

            // Jeśli wszystkie score'y ≈ 0 → natychmiast do None (ignorujemy dwell)
            if (proposed == Mode.None || proposedScore <= 0.0)
            {
                var stateNone = new ModeState(
                    Mode.None,
                    nowUtc,
                    scores);

                return new ModeDecision(
                    Changed: prev.Mode != Mode.None,
                    State: stateNone,
                    ProposedMode: Mode.None,
                    ProposedScore: 0.0,
                    SecondBestScore: 0.0,
                    Margin: 0.0);
            }

            double minScore = (double)o.MinScore;
            double minMargin = (double)o.MinMargin;

            bool passMinScore = proposedScore >= minScore;
            bool passMinMargin = margin >= minMargin;

            Mode next = prev.Mode;
            DateTime since = prev.SinceUtc;
            var dwell = TimeSpan.FromMinutes(o.HysteresisLockMinutes);

            // Jeśli kandydat nie spełnia progów – natychmiast None (bez dwell).
            if (!passMinScore || !passMinMargin)
            {
                next = Mode.None;
                since = nowUtc;
            }
            else
            {
                if (prev.Mode == Mode.None)
                {
                    // Brak aktywnego trybu → wybierz kandydata
                    next = proposed;
                    since = nowUtc;
                }
                else if (proposed == prev.Mode)
                {
                    // Ten sam tryb – zostajemy, reset since tylko gdy było puste
                    next = prev.Mode;
                    since = prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc;
                }
                else
                {
                    // Inny tryb niż poprzedni – sprawdzamy dwell
                    bool dwellOver = (nowUtc - prev.SinceUtc) >= dwell;

                    if (dwellOver)
                    {
                        next = proposed;
                        since = nowUtc;
                    }
                    else
                    {
                        // Lepkość: dwell jeszcze trwa → trzymamy stary tryb
                        next = prev.Mode;
                        since = prev.SinceUtc;
                    }
                }
            }

            var state = new ModeState(
                Mode: next,
                SinceUtc: since == DateTime.MinValue ? nowUtc : since,
                Scores: scores);

            bool changed = next != prev.Mode;

            return new ModeDecision(
                Changed: changed,
                State: state,
                ProposedMode: proposed,
                ProposedScore: proposedScore,
                SecondBestScore: secondScore,
                Margin: margin);
        }

        // =====================================================================
        //  GLOBAL GATES
        // =====================================================================

        private static (bool ok, string reason) CheckGlobalGates(SigmaData d, DateTime nowUtc, SigmaStrategyOptions o)
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

        private static bool IsMacroFreeze(DateTime nowUtc, SigmaStrategyOptions o)
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

        private static bool IsFundingFreeze(SigmaData d, DateTime nowUtc, SigmaStrategyOptions o)
        {
            if (!d.NextFundingUtc.HasValue)
                return false;

            var dt = DateTime.SpecifyKind(d.NextFundingUtc.Value, DateTimeKind.Utc);

            var from = dt.AddMinutes(-o.FundingFreezeMinutesBefore);
            var to = dt.AddMinutes(o.FundingFreezeMinutesAfter);

            return nowUtc >= from && nowUtc <= to;
        }

        private static bool IsCorrOppositeBlocked(SigmaData d, SigmaStrategyOptions o)
        {
            if (!double.IsFinite(d.CorrToBtc15m))
                return false;

            return Math.Abs(d.CorrToBtc15m) >= (double)o.CorrOppositeBlock
                && d.BtcBiasOpposite;
        }

        // =====================================================================
        //  POMOCNICZE
        // =====================================================================

        private static double Safe(double v, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
        {
            if (!double.IsFinite(v))
                return 0.0;

            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static double Normalize01(double x, double min, double max)
        {
            if (!double.IsFinite(x) || max <= min)
                return 0.0;

            var t = (x - min) / (max - min);
            return t;
        }

        private static double Clamp01(double x)
        {
            if (x < 0.0) return 0.0;
            if (x > 1.0) return 1.0;
            return x;
        }

        /// <summary>
        /// Znak z martwą strefą eps:
        /// 1 gdy x &gt; eps, -1 gdy x &lt; -eps, 0 gdy |x| ≤ eps.
        /// </summary>
        private static int Sign(double x, double eps)
        {
            if (eps < 0) eps = -eps;
            if (x > eps) return 1;
            if (x < -eps) return -1;
            return 0;
        }
    }
}

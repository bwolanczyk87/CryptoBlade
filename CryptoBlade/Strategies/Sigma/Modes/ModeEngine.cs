using CryptoBlade.Strategies.Sigma.Helpers;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public interface IMode
    {
        Mode Kind { get; }
        ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel);
    }

    public enum Mode { 
        None = 0, 
        MM = 1, 
        MR = 2, 
        BO = 3
    }

    public enum ModeTier {
        None = 0,
        Soft = 1,
        Medium = 2,
        Hard = 3
    }

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

    public readonly struct ModeSignal(bool buy, bool sell, ModeTier tier)
    {
        public readonly bool HasBuy = buy;
        public readonly bool HasSell = sell;
        public readonly ModeTier Tier = tier;

        public static ModeSignal None => new(false, false, ModeTier.None);
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

        // Tryby zarejestrowane przez strategię.
        private readonly Dictionary<Mode, IMode> _modes;

        // Bieżący stan (Mode + Since + Scores)
        private ModeState _state;

        // Stan dynamicznego gate'u na spread (per-symbol, per-strategia)
        private double _spreadEmaBps;
        private bool _spreadEmaInitialized;

        // Wspólne cechy wejściowe dla scoringu trybów
        private readonly record struct RegimeFeatures(
            double AtrPct1h,
            double Adx1h,
            double ZDvwap,
            double ZSlopeDvwap,
            double Bbw15mPct,
            double AutoCorr5m,
            double OiDelta1hPct,
            double BasisPct);

        public ModeEngine(
            SigmaStrategyOptions options,
            IMode momentumMode,
            IMode meanReversionMode,
            IMode breakoutMode,
            ISigmaAuditSink? audit = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));

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
            var (ok, reason) = CheckGlobalGates(data, nowUtc);

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
            var f = ExtractFeatures(d);

            double atr = f.AtrPct1h;
            bool haveAtr = double.IsFinite(atr) && atr > 0.0;

            // ATR-maski per-tryb
            bool mmAtrOk = haveAtr &&
                           atr >= (double)o.MmAtrMinPct &&
                           atr <= (double)o.MmAtrMaxPct;

            bool mrAtrOk = haveAtr &&
                           atr >= (double)o.MrAtrMinPct &&
                           atr <= (double)o.MrAtrMaxPct;

            bool boAtrOk = haveAtr &&
                           atr <= (double)o.BoAtrMaxPct;

            double mm = mmAtrOk
                ? ScoreMomentum(f, o)
                : 0.0;

            double mr = mrAtrOk
                ? ScoreMeanReversion(f, o)
                : 0.0;

            double bo = boAtrOk
                ? ScoreBreakout(f, o, d)
                : 0.0;

            // Clamp do [0..100] – zachowujemy dotychczasową semantykę
            mm = Math.Min(Math.Max(mm, 0.0), 100.0);
            mr = Math.Min(Math.Max(mr, 0.0), 100.0);
            bo = Math.Min(Math.Max(bo, 0.0), 100.0);

            return new ModeScores(mm, mr, bo);
        }

        private static RegimeFeatures ExtractFeatures(SigmaData d)
        {
            return new RegimeFeatures(
                AtrPct1h: d.AtrPct1h,
                Adx1h: StatisticsHelpers.Safe(d.Adx1h),
                ZDvwap: StatisticsHelpers.Safe(d.ZDvwap, -10, 10),
                ZSlopeDvwap: StatisticsHelpers.Safe(d.ZSlopeDvwap, -25, 25),
                Bbw15mPct: StatisticsHelpers.Safe(d.Bbw15mPct, 0, 100),
                AutoCorr5m: StatisticsHelpers.Safe(d.AutoCorr5m, -1, 1),
                OiDelta1hPct: StatisticsHelpers.Safe(d.OiDelta1hPct, -500, 500),
                BasisPct: StatisticsHelpers.Safe(d.BasisPct, -100, 100));
        }

        // --- per-mode scoring ---

        private static double ScoreMomentum(RegimeFeatures f, SigmaStrategyOptions o)
        {
            double mm = 0.0;

            double adx = f.Adx1h;
            double zSlope = f.ZSlopeDvwap;
            double zDev = f.ZDvwap;
            double ac = f.AutoCorr5m;
            double oi = f.OiDelta1hPct;

            // 1) ADX – im bliżej AdxEnableMomentum, tym wyższy score (0..40)
            double adxNorm = StatisticsHelpers.Normalize01(
                adx,
                (double)o.AdxDisableMomentum,
                (double)o.AdxEnableMomentum);

            adxNorm = StatisticsHelpers.Clamp01(adxNorm);
            mm += 40.0 * adxNorm;                  // 0..40

            // 2) Absolutne nachylenie DVWAP – "siła trendu" w obie strony (0..25)
            if (double.IsFinite(zSlope))
            {
                // 4 sigma nachylenia → pełna premia
                double slopeMag = Math.Min(Math.Abs(zSlope) / 4.0, 1.0);
                mm += 25.0 * slopeMag;             // 0..25
            }

            // 3) Umiarkowane odchylenie od DVWAP (nie za blisko, nie ekstremalnie daleko) (0..15)
            if (double.IsFinite(zDev))
            {
                double absDev = Math.Abs(zDev);

                // pełne 1.0 w okolicach 1.0–2.0 sigma, 0 przy 0 i >=4
                double devScore = 0.0;
                if (absDev > 0.2 && absDev < 4.0)
                {
                    if (absDev <= 2.0)
                        devScore = (absDev - 0.2) / (2.0 - 0.2);   // rośnie 0→1
                    else
                        devScore = (4.0 - absDev) / (4.0 - 2.0);   // spada 1→0
                }

                devScore = StatisticsHelpers.Clamp01(devScore);
                mm += 15.0 * devScore;              // 0..15
            }

            // 4) ΔOI wyrównany z kierunkiem trendu (0..10)
            double oiAligned = 0.0;
            if (double.IsFinite(oi) && double.IsFinite(zSlope) && Math.Abs(zSlope) > 0.1)
            {
                int trendSign = Math.Sign(zSlope);
                int oiSign = Math.Sign(oi);

                if (oiSign == trendSign)
                {
                    // saturacja przy ~10% zmiany OI
                    double oiMag = Math.Min(Math.Abs(oi) / 10.0, 1.0);
                    oiAligned = oiMag;
                }
            }
            mm += 10.0 * oiAligned;                 // 0..10

            // 5) Dodatnia autokorelacja – kontynuacja (0..10)
            if (double.IsFinite(ac) && ac > 0.0)
            {
                double acClamped = Math.Min(ac, 1.0);
                mm += 10.0 * acClamped;             // 0..10
            }

            return mm;  // później i tak jest clampowane do [0..100] w Score(...)
        }

        private static double ScoreMeanReversion(RegimeFeatures f, SigmaStrategyOptions o)
        {
            double mr = 0.0;

            double adx = f.Adx1h;
            double zDev = f.ZDvwap;
            double zSlope = f.ZSlopeDvwap;
            double ac = f.AutoCorr5m;
            double bbwP = f.Bbw15mPct;

            // 1) Niski ADX – im niższy, tym lepiej dla MR (0..30)
            double adxLow = 1.0 - StatisticsHelpers.Normalize01(
                adx,
                (double)o.AdxDisableMomentum,
                (double)o.AdxEnableMomentum);

            adxLow = StatisticsHelpers.Clamp01(adxLow);
            mr += 30.0 * adxLow;                    // 0..30

            // 2) Bliskość DVWAP – preferujemy |zDev| blisko 0 (0..35)
            if (double.IsFinite(zDev))
            {
                double absDev = Math.Abs(zDev);
                double devScore = 0.0;

                // 1.0 przy zDev=0, 0 przy |zDev|>=3
                if (absDev <= 3.0)
                    devScore = 1.0 - (absDev / 3.0);

                devScore = StatisticsHelpers.Clamp01(devScore);
                mr += 35.0 * devScore;              // 0..35
            }

            // 3) Kara za duże |slope| – MR nie lubi runaway-trendów (do -25)
            if (double.IsFinite(zSlope))
            {
                // 5 sigma nachylenia → pełna kara
                double slopeMag = Math.Min(Math.Abs(zSlope) / 5.0, 1.0);
                mr -= 25.0 * slopeMag;              // 0..-25
            }

            // 4) Autokorelacja: ujemna lub blisko zera sprzyja MR (0..20)
            if (double.IsFinite(ac))
            {
                if (ac < 0.0)
                {
                    double acMag = Math.Min(-ac, 1.0);
                    mr += 20.0 * acMag;             // 0..20 przy ac=-1
                }
                else
                {
                    // im bliżej 0, tym lepiej; ac->1 obniża score
                    double flatScore = 1.0 - Math.Min(ac, 1.0);
                    mr += 10.0 * flatScore;         // 0..10
                }
            }

            // 5) Wąskie BB – kompresja (0..15)
            if (double.IsFinite(bbwP))
            {
                double bbwNorm = StatisticsHelpers.Normalize01(
                    bbwP,
                    0.0,
                    (double)o.BbWidthExitBreakoutPct);

                double bbwScore = 1.0 - StatisticsHelpers.Clamp01(bbwNorm);
                mr += 15.0 * bbwScore;              // 0..15
            }

            // Ograniczenie do [0..100] – dodatkowy safety poza globalnym clampem
            if (mr < 0.0) mr = 0.0;
            if (mr > 100.0) mr = 100.0;

            return mr;
        }

        private static double ScoreBreakout(RegimeFeatures f, SigmaStrategyOptions o, SigmaData d)
        {
            double bo = 0.0;

            double atr = f.AtrPct1h;
            double bbwP = f.Bbw15mPct;
            double zSlope = f.ZSlopeDvwap;
            double ac = f.AutoCorr5m;
            double oi = f.OiDelta1hPct;

            // 1) Szerokie pasma BB – breakout z kompresji → ekspansja (0..40)
            double bbwNorm = StatisticsHelpers.Normalize01(
                bbwP,
                (double)o.BbWidthBreakoutPct,
                (double)o.BbWidthExitBreakoutPct);

            bbwNorm = StatisticsHelpers.Clamp01(bbwNorm);
            bo += 40.0 * bbwNorm;                   // 0..40

            // 2) ATR – breakout lubi wyższe ATR, ale z limitem (0..15)
            if (double.IsFinite(atr))
            {
                // brak dolnego progu – rosnący score do BoAtrMaxPct
                double atrNorm = StatisticsHelpers.Normalize01(
                    atr,
                    0.0,
                    (double)o.BoAtrMaxPct);

                atrNorm = StatisticsHelpers.Clamp01(atrNorm);
                bo += 15.0 * atrNorm;               // 0..15
            }

            // 3) Absolutny slope DVWAP – siła jednokierunkowego ruchu (0..25)
            if (double.IsFinite(zSlope))
            {
                // 4 sigma nachylenia → pełna premia
                double slopeMag = Math.Min(Math.Abs(zSlope) / 4.0, 1.0);
                bo += 25.0 * slopeMag;              // 0..25
            }

            // 4) ΔOI>0 – napływ kapitału na wybiciu (0..10)
            if (double.IsFinite(oi) && oi > 0.0)
            {
                // saturacja przy ~10% zmiany OI
                double oiMag = Math.Min(oi / 10.0, 1.0);
                bo += 10.0 * oiMag;                 // 0..10
            }

            // 5) Dodatnia autokorelacja – kontynuacja po wybiciu (0..10)
            if (double.IsFinite(ac) && ac > 0.0)
            {
                double acClamped = Math.Min(ac, 1.0);
                bo += 10.0 * acClamped;             // 0..10
            }

            return bo;
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

            double minScoreStay = o.MinScoreStay > 0
                ? (double)o.MinScoreStay
                : minScore * 0.5;   // np. połowa progu wejścia

            bool passMinScore = proposedScore >= minScore;
            bool passMinMargin = margin >= minMargin;

            Mode next = prev.Mode;
            DateTime since = prev.SinceUtc;
            var dwell = TimeSpan.FromMinutes(o.HysteresisLockMinutes);

            // 1) Jeśli w ogóle nie ma sensownego kandydata:
            if (proposed == Mode.None || proposedScore <= 0.0)
            {
                // tu możesz nadal natychmiast zrzucić do None:
                next = Mode.None;
                since = nowUtc;
            }
            else if (prev.Mode == Mode.None)
            {
                // 2) Byliśmy w None → wejście tylko, gdy kandydat spełnia progi
                if (passMinScore && passMinMargin)
                {
                    next = proposed;
                    since = nowUtc;
                }
                else
                {
                    next = Mode.None;
                    since = nowUtc;
                }
            }
            else
            {
                // 3) Już jesteśmy w jakimś trybie (MM/MR/BO)

                double prevScore = dict[prev.Mode];

                // 3a) Kill-condition: aktualny tryb całkiem umiera
                bool kill = prevScore < minScoreStay;

                if (kill)
                {
                    next = Mode.None;
                    since = nowUtc;
                }
                else if (!passMinScore || !passMinMargin)
                {
                    // 3b) Kandydat nie jest wystarczająco dobry → zostajemy w starym trybie
                    next = prev.Mode;
                    since = prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc;
                }
                else if (proposed == prev.Mode)
                {
                    // 3c) Ten sam tryb wygrywa → zostajemy
                    next = prev.Mode;
                    since = prev.SinceUtc == DateTime.MinValue ? nowUtc : prev.SinceUtc;
                }
                else
                {
                    // 3d) Inny tryb jest wyraźnym kandydatem → działa dwell
                    bool dwellOver = (nowUtc - prev.SinceUtc) >= dwell;

                    if (dwellOver)
                    {
                        next = proposed;
                        since = nowUtc;
                    }
                    else
                    {
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

        private (bool ok, string reason) CheckGlobalGates(SigmaData d, DateTime nowUtc)
        {
            var o = _options;

            // 1) Dynamiczny spread gate (twardy, pair-aware)
            if (!double.IsFinite(d.SpreadBps) || d.SpreadBps <= 0)
                return (false, $"Global gate: invalid spread {d.SpreadBps:F2} bps");

            double current = d.SpreadBps;

            double minBps = (double)o.SpreadGateMinBps;
            double absMax = (double)o.MaxSpreadBps;          // twardy cap
            double alpha = o.SpreadGateEmaAlpha;
            double multiple = o.SpreadGateMultiplier;

            if (!_spreadEmaInitialized)
            {
                // seed: zaciśnięty do [minBps, absMax], żeby egzotyki od razu nie przebiły sufitu
                double seed = Math.Max(minBps, Math.Min(current, absMax));
                _spreadEmaBps = seed;
                _spreadEmaInitialized = true;
            }
            else
            {
                double x = Math.Max(minBps, Math.Min(current, absMax));
                double oneMinusAlpha = 1.0 - alpha;
                _spreadEmaBps = alpha * x + oneMinusAlpha * _spreadEmaBps;
            }

            // typowy spread dla tej pary
            double baseline = Math.Max(minBps, _spreadEmaBps);

            // gate pair-aware: max(EMA * k, minBps), ale nie powyżej absMax
            double dynamicGate = baseline * multiple;
            double threshold = Math.Min(dynamicGate, absMax);

            // zapis do audytu
            d.SpreadGateThresholdBps = threshold;

            if (current > threshold + 1e-6)
                return (false,
                    $"Global gate: Spread {current:F2} bps > dynamic gate {threshold:F2} bps (ema={_spreadEmaBps:F2})");

            // 2) Macro freeze (twardy)
            if (IsMacroFreeze(nowUtc, o))
                return (false, "Global gate: Macro freeze window");

            // 3) Funding window freeze (twardy)
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
    }
}

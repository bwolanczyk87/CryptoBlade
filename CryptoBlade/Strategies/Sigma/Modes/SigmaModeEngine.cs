using CryptoBlade.Strategies.Sigma.Modes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
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

    public readonly struct ModeDecision(bool buy, bool sell, bool buyExtra, bool sellExtra)
    {
        public readonly bool HasBuy = buy;
        public readonly bool HasSell = sell;
        public readonly bool HasBuyExtra = buyExtra;
        public readonly bool HasSellExtra = sellExtra;

        public static ModeDecision None => new(false, false, false, false);
    }

    /// <summary>
    /// Orkiestrator trybów Sigmy.
    /// - odpowiada za:
    ///   * globalne bramki (Spread / Macro / Funding / Corr),
    ///   * scoring reżimów (Momentum / MeanReversion / Breakout),
    ///   * histerezę / dwell / minScore / minMargin,
    ///   * wybór jednego aktywnego Mode.
    /// - NIE zawiera logiki wejść/wyjść z pozycji – to robią konkretne Mode'y.
    /// </summary>
    public sealed class SigmaModeEngine
    {
        private readonly SigmaStrategyOptions _options;
        private readonly IRegimeAuditSink? _audit;

        // Tryby zarejestrowane przez strategię.
        private readonly IReadOnlyDictionary<Mode, IMode> _modes;

        // Bieżący stan reżimu (Mode + Since + Scores)
        private ModeState _state;

        public SigmaModeEngine(
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
        /// - jeśli OK, woła RegimeEngine.Classify(SigmaData, ...),
        /// - aktualizuje stan i zwraca:
        ///   * wybrany IMode (albo null gdy Regime.None),
        ///   * RegimeDecision (dla logowania),
        ///   * reason z globalnych bramek.
        /// </summary>
        public (IMode? Mode, ModeDecision RegimeDecision, string GlobalGateReason) Evaluate(
            SigmaData data,
            DateTime nowUtc,
            CancellationToken cancel)
        {
            // 1) Globalne bramki (twarde) – decydują, czy w ogóle wolno otwierać nowe pozycje.
            var (ok, reason) = Modes.ModeGlobalGates.Evaluate(data, nowUtc, _options);

            if (!ok)
            {
                // W trybie gate-u reżim logicznie przechodzi do None
                var scores = ModeScores.Zero;
                var state = new ModeState(Mode.None,
                    _state.SinceUtc == DateTime.MinValue ? nowUtc : _state.SinceUtc,
                    scores);

                var decision = new RegimeDecision(
                    Changed: state.Mode != _state.Mode,
                    State: state,
                    ProposedMode: Mode.None,
                    ProposedScore: 0.0,
                    SecondBestScore: 0.0,
                    Margin: 0.0);

                _state = state;

                _audit?.OnDecision(data, decision, reason);

                return (Mode: null, RegimeDecision: decision, GlobalGateReason: reason);
            }

            // 2) Klasyfikacja reżimu (scoring + histereza + dwell)
            var regimeDecision = RegimeEngine.Classify(
                data,
                _state,
                nowUtc,
                _options,
                _audit);

            _state = regimeDecision.State;

            // 3) Mapowanie reżimu na konkretne Mode (fabryka)
            IMode? mode = regimeDecision.State.Mode switch
            {
                Mode.MM => _modes[Mode.MM],
                Mode.MR => _modes[Mode.MR],
                Mode.BO => _modes[Mode.BO],
                _ => null
            };

            return (Mode: mode,
                    RegimeDecision: regimeDecision,
                    GlobalGateReason: reason ?? "OK");
        }
    }
}

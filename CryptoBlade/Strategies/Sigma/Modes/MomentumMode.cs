namespace CryptoBlade.Strategies.Sigma.Modes
{
    public sealed class MomentumMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public MomentumMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Mode Kind => Mode.MM;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            // TODO: tutaj w kolejnym kroku dorobimy triggery:
            // sweep -> reclaim, delta/CVD flip, oscylator itp.
            // Na razie tylko klasyfikujemy reżimy, bez generowania wejść.
            return ModeSignal.None;
        }
    }
}

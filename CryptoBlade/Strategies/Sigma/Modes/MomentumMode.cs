namespace CryptoBlade.Strategies.Sigma.Modes
{
    public sealed class MomentumMode(SigmaStrategyOptions options) : IMode
    {
        private readonly SigmaStrategyOptions _options = options;
        public Mode Kind => Mode.MM;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            return new ModeSignal();
        }
    }
}
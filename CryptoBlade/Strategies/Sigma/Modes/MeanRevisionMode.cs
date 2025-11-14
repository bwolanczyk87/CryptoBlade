using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public sealed class MeanReversionMode(SigmaStrategyOptions options) : IMode
    {
        private readonly SigmaStrategyOptions _options = options;
        public Mode Kind => Mode.MR;

        public ModeDecision Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            return new ModeDecision();
        }
    }
}
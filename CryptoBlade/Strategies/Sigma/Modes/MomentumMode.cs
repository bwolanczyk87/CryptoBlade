using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Sigma.Regimes;


namespace CryptoBlade.Strategies.Sigma.Modes
{
    public sealed class MomentumMode(SigmaStrategyOptions options) : IMode
    {
        private readonly SigmaStrategyOptions _options = options;
        public Mode Kind => Mode.MM;

        Mode IMode.Kind => throw new NotImplementedException();

        public ModeDecision Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            return new ModeDecision();
        }
    }
}
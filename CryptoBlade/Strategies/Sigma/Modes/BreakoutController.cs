using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public class BreakoutController: IModeController
    {
        public ModeDecision Evaluate(SigmaData f, DateTime nowUtc, CancellationToken cancel)
        {
            return ModeDecision.None;
        }
    }
}

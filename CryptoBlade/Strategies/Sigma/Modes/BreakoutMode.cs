using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public class BreakoutMode: IModeController
    {
        public ModeSignal Evaluate(SigmaData f, DateTime nowUtc, CancellationToken cancel)
        {
            return ModeSignal.None;
        }
    }
}

using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public class MeanReversionController : IModeController
    {
        public MeanReversionController()
        {
        }

        public static ModeDecision Evaluate(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            // TODO: close-back-in do D-VWAP/VA + delta flip; time-stop
            return ModeDecision.None;
        }

        public ModeDecision Evaluate(FeatureSnapshot f, RegimeState state, DateTime nowUtc, CancellationToken cancel)
        {
            return ModeDecision.None;
        }
    }
}

using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public class BreakoutController: IModeController
    {
        public static ModeDecision Evaluate(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            // TODO: wybicie OR/inside/NR7 + retest + OI↑
            return ModeDecision.None;
        }

        public ModeDecision Evaluate(FeatureSnapshot f, RegimeState state, DateTime nowUtc, CancellationToken cancel)
        {
            throw new NotImplementedException();
        }
    }
}

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public static class MomentumController
    {
        public static ModeDecision Evaluate(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            // TODO: wykrycie sweep->reclaim + delta flip + oscylator (5m)
            return ModeDecision.None;
        }
    }
}

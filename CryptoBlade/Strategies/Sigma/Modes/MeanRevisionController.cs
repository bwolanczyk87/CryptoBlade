namespace CryptoBlade.Strategies.Sigma.Modes
{
    public static class MeanReversionController
    {
        public static ModeDecision Evaluate(FeatureSnapshot f, SigmaStrategyOptions o)
        {
            // TODO: close-back-in do D-VWAP/VA + delta flip; time-stop
            return ModeDecision.None;
        }
    }
}

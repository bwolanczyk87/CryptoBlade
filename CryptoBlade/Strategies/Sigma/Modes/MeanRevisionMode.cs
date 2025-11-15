namespace CryptoBlade.Strategies.Sigma.Modes
{
    public sealed class MeanReversionMode : IMode
    {
        private readonly SigmaStrategyOptions _options;

        public MeanReversionMode(SigmaStrategyOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public Mode Kind => Mode.MR;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            // TODO: kolejny krok – implementacja triggerów MR:
            // close-back-in do D-VWAP/VA + delta flip, time-stop itd.
            return ModeSignal.None;
        }
    }
}

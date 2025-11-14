namespace CryptoBlade.Strategies.Sigma.Modes
{
    public class BreakoutMode: IMode
    {
        public Mode Kind => Mode.BO;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            throw new NotImplementedException();
        }
    }
}

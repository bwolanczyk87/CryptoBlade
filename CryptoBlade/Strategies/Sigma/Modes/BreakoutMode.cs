namespace CryptoBlade.Strategies.Sigma.Modes
{
    public sealed class BreakoutMode : IMode
    {
        public Mode Kind => Mode.BO;

        public ModeSignal Execute(SigmaData data, DateTime nowUtc, CancellationToken cancel)
        {
            // TODO: w kolejnym etapie:
            // wybicie OR/inside/NR7 + retest, konfirmacja ΔOI$↑ itd.
            return ModeSignal.None;
        }
    }
}

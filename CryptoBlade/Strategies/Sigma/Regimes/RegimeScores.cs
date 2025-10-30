namespace CryptoBlade.Strategies.Sigma.Regimes
{
    internal readonly struct RegimeScores(double mm, double mr, double bo)
    {
        public readonly double Momentum = mm;
        public readonly double MeanReversion = mr;
        public readonly double Breakout = bo;

        public static RegimeScores Zero => new(0, 0, 0);
    }
}

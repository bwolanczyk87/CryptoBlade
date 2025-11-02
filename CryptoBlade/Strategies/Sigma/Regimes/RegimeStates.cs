namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public readonly struct RegimeState(Regime mode, DateTime sinceUtc, RegimeScores scores)
    {
        public readonly Regime Mode = mode;
        public readonly DateTime SinceUtc = sinceUtc;
        public readonly RegimeScores Scores = scores;
    }
}

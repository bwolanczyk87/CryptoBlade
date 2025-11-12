namespace CryptoBlade.Strategies.Sigma
{
    public interface IBybitSigmaDataProvider
    {
        Task<double> GetBasisPctAsync(string symbol, CancellationToken cancel);
        Task<double> GetDeltaCvd5mAsync(string symbol, CancellationToken cancel);
        Task<double> GetDistToNearestLiquidationPctAsync(string symbol, decimal lastPrice, CancellationToken cancel);
        Task<double> GetFundingRateAsync(string symbol, CancellationToken cancel);
        Task<double> GetOpenInterestDelta1hPctAsync(string symbol, CancellationToken cancel);
        Task<double> GetSpreadBpsAsync(string symbol, CancellationToken cancel);
        Task<(double Corr, double LastBtcRet)> GetCorrToBtc15mAsync(string symbol, int window, CancellationToken cancel);
    }
}
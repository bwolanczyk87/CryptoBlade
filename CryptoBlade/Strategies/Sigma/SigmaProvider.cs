using CryptoBlade.Exchanges;

namespace CryptoBlade.Strategies.Sigma
{
    public interface ISigmaDataProvider
    {
        Task<double> GetOpenInterestDelta1hPctAsync(string symbol, CancellationToken cancel);
        Task<double> GetFundingRateAsync(string symbol, CancellationToken cancel);
        Task<double> GetBasisPctAsync(string symbol, CancellationToken cancel);
        Task<double> GetDeltaCvd5mAsync(string symbol, CancellationToken cancel);
        Task<double> GetDistToNearestLiquidationPctAsync(string symbol, decimal lastPrice, CancellationToken cancel);
        Task<double> GetSpreadBpsAsync(string symbol, CancellationToken cancel);
    }

    public sealed class BybitSigmaDataProvider : ISigmaDataProvider
    {
        private readonly ICbFuturesRestClient _rest;

        public BybitSigmaDataProvider(ICbFuturesRestClient rest) => _rest = rest;

        public Task<double> GetOpenInterestDelta1hPctAsync(string symbol, CancellationToken cancel)
        {
            // TODO: dodać w ICbFuturesRestClient metodę np. GetOpenInterestAsync(symbol, timeframe=1h, lookback=2)
            // i policzyć procentową zmianę OI$ pomiędzy dwiema ostatnimi kropkami.
            // Bybit ma endpointy OI w v5 (market/open-interest). W Twoim kliencie obecnie widzę obsługę trade/SL/TP/itd. (zlecenia). 
            return Task.FromResult(0.0);
        }

        public Task<double> GetFundingRateAsync(string symbol, CancellationToken cancel)
        {
            // TODO: wrapper do funding-rate-history (v5) + ostatnia wartość
            return Task.FromResult(0.0);
        }

        public Task<double> GetBasisPctAsync(string symbol, CancellationToken cancel)
        {
            // TODO: basis = (Mark - Index) / Index; pobierz mark/index (v5 kline mark/index) i policz aktualny %
            return Task.FromResult(0.0);
        }

        public Task<double> GetDeltaCvd5mAsync(string symbol, CancellationToken cancel)
        {
            // TODO: policz CVD z recent trades (WS/REST) i zwróć różnicę z 5m
            return Task.FromResult(0.0);
        }

        public Task<double> GetDistToNearestLiquidationPctAsync(string symbol, decimal lastPrice, CancellationToken cancel)
        {
            // TODO: jeżeli subskrybujesz All Liquidation (WS), utrzymuj własny bufor heatmapy
            // i licz dystans (%) do najbliższego klastra względem lastPrice.
            return Task.FromResult(1.0); // placeholder
        }

        public Task<double> GetSpreadBpsAsync(string symbol, CancellationToken cancel)
        {
            throw new NotImplementedException();
        }
    }
}

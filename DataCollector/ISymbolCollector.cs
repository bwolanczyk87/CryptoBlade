using Bybit.Net.Enums;
using Bybit.Net.Objects.Models.V5;

namespace DataCollector
{
    public interface ISymbolCollector
    {
        string Name { get; }

        BybitLinearInverseSymbol Symbol { get; }

        bool ConsistentData { get; }

        BybitLinearInverseTicker? Ticker { get; }

        DateTime LastTickerUpdate { get; }

        DateTime LastCandleUpdate { get; }

        public KlineInterval[] KlineIntervals { get; }

        Task SetupSymbolAsync(BybitLinearInverseSymbol symbol, CancellationToken cancel);

        Task InitializeAsync(BybitKlineUpdate[] candles, BybitLinearInverseTicker ticker, CancellationToken cancel);

        Task AddCandleDataAsync(BybitKlineUpdate candle, CancellationToken cancel);

        Task UpdatePriceDataAsync(BybitLinearInverseTicker ticker, CancellationToken cancel);

        Task OrderUpdatedAsync(BybitOrderUpdate orderUpdate, CancellationToken cancel);
    }
}
using Bybit.Net.Objects.Models.V5;

namespace DataCollector
{
    public interface ICollectorManager
    {
        DateTime LastExecution { get; }
        Task<List<BybitLinearInverseSymbol>> GetSymbolsAsync(IFuturesRestClient restClient, List<string> symbols, CancellationToken cancel);
        Task<ITradingStrategy[]> GetCollectorsAsync(CancellationToken cancel);
        Task StartCollectorsAsync(CancellationToken cancel);
        Task StopCollectorsAsync(CancellationToken cancel);
    }
}
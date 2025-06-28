// Services/OrderBookService.cs
using System.Threading;
using System.Threading.Tasks;
using CryptoBlade.Models;

namespace CryptoBlade.Services;

public interface IOrderBookService
{
    Task<OrderBookStats> GetStatsAsync(string symbol, CancellationToken ct);
}

public sealed class DummyOrderBookService : IOrderBookService
{
    public Task<OrderBookStats> GetStatsAsync(string symbol, CancellationToken ct)
        => Task.FromResult(new OrderBookStats(
            AvgSpread: 0.00018m,
            ImbalancePct: 18m,
            WallBid: (0.60470m, 280_000m),
            WallAsk: null,
            TopTurnover60s: 27));
}

public interface IVolumeFlowService
{
    Task<VolumeFlow> GetFlowAsync(string symbol, CancellationToken ct);
}

public sealed class DummyVolumeFlowService : IVolumeFlowService
{
    public Task<VolumeFlow> GetFlowAsync(string symbol, CancellationToken ct)
        => Task.FromResult(new VolumeFlow(
            Cvd5m: 125_000m,
            BuyVol1m: 6_400m,
            SellVol1m: 5_800m));
}

public interface IRiskMetricsService
{
    Task<RiskMetrics> GetAsync(string symbol, CancellationToken ct);
}

public sealed class DummyRiskMetricsService : IRiskMetricsService
{
    public Task<RiskMetrics> GetAsync(string symbol, CancellationToken ct)
        => Task.FromResult(new RiskMetrics(
            Atr5m: 0.00105m,
            Funding8h: -0.00018m,
            OiDelta5m: 0.011m));
}

public interface IBtcBiasService
{
    Task<BtcBias> GetAsync(CancellationToken ct);
}

public sealed class DummyBtcBiasService : IBtcBiasService
{
    public Task<BtcBias> GetAsync(CancellationToken ct)
        => Task.FromResult(new BtcBias(
            PriceDelta5m: 0.0023m,
            RollingCorr30d: 0.58m));
}

public enum NewsImpact { NONE, MEDIUM, HIGH }

public interface INewsSentimentService
{
    Task<NewsImpact> GetImpactAsync(string symbol, CancellationToken ct);
}

public sealed class DummyNewsSentimentService : INewsSentimentService
{
    public Task<NewsImpact> GetImpactAsync(string symbol, CancellationToken ct)
        => Task.FromResult(NewsImpact.NONE);
}

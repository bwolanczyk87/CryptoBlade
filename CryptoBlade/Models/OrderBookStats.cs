namespace CryptoBlade.Models
{
    public readonly record struct OrderBookStats(
    decimal AvgSpread,
    decimal ImbalancePct,            // –100 … +100
    (decimal Price, decimal Size)? WallBid,
    (decimal Price, decimal Size)? WallAsk,
    int TopTurnover60s);             // zmiany bid/ask w 60 s

    public readonly record struct VolumeFlow(
        decimal Cvd5m,
        decimal BuyVol1m,
        decimal SellVol1m);

    public readonly record struct RiskMetrics(
        decimal Atr5m,
        decimal Funding8h,
        decimal OiDelta5m);

    public readonly record struct BtcBias(
        decimal PriceDelta5m,
        decimal RollingCorr30d);
}

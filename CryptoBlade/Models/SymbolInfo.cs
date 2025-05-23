﻿namespace CryptoBlade.Models
{
    public record struct SymbolInfo(string Name, decimal PriceScale, string QuoteAsset, string BaseAsset, decimal? MinOrderQty, decimal? QtyStep, decimal? MaxLeverage, DateTime LaunchTime, decimal? Volume, decimal? Volatility);
}

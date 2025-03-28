﻿using System.Text.Json;
using CryptoBlade.Strategies;
using CryptoBlade.Strategies.Symbols;

namespace CryptoBlade.Configuration
{
    public class TradingBotOptions
    {
        public BotMode BotMode { get; set; } = BotMode.Backtest;
        public ExchangeAccount[] Accounts { get; set; } = Array.Empty<ExchangeAccount>();
        public string AccountName { get; set; } = string.Empty;
        public string QuoteAsset { get; set; } = "USDT";
        public int MaxRunningStrategies { get; set; } = 15;
        public int DcaOrdersCount { get; set; } = 1000;
        public DynamicBotCount DynamicBotCount { get; set; } = new DynamicBotCount();
        public decimal WalletExposureLong { get; set; } = 1.0m;
        public decimal WalletExposureShort { get; set; } = 1.0m;
        public string[] Whitelist { get; set; } = Array.Empty<string>();
        public string[] Blacklist { get; set; } = Array.Empty<string>();
        public SymbolTradingMode[] SymbolTradingModes { get; set; } = Array.Empty<SymbolTradingMode>();
        public decimal MinimumVolume { get; set; }
        public decimal MinimumPriceDistance { get; set; }
        public string StrategyName { get; set; } = "AutoHedge";
        public TradingMode TradingMode { get; set; } = TradingMode.Normal;
        public bool ForceMinQty { get; set; } = true;
        public decimal QtyFactorLong { get; set; } = 1.0m;
        public decimal QtyFactorShort { get; set; } = 1.0m;
        public bool EnableRecursiveQtyFactorLong { get; set; }
        public bool EnableRecursiveQtyFactorShort { get; set; }
        public int PlaceOrderAttempts { get; set; } = 3;
        public decimal MaxAbsFundingRate { get; set; } = 0.0004m;
        public decimal MakerFeeRate { get; set; } = 0.0002m;
        public decimal TakerFeeRate { get; set; } = 0.00055m;
        public decimal MinProfitRate { get; set; } = 0.0006m;
        public decimal SpotRebalancingRatio { get; set; } = 0.5m;
        public StrategySelectPreference StrategySelectPreference { get; set; } = StrategySelectPreference.Volume;
        public int NormalizedAverageTrueRangePeriod { get; set; } = 14;
        public decimal MinNormalizedAverageTrueRangePeriod { get; set; } = 1.0m;
        public BackTest BackTest { get; set; } = new BackTest();
        public Unstucking Unstucking { get; set; } = new Unstucking();
        public StrategyOptions Strategies { get; set; } = new StrategyOptions();
        public CriticalMode CriticalMode { get; set; } = new CriticalMode();
        public OptimizerOptions Optimizer { get; set; } = new OptimizerOptions();
        public SymbolClassificationLevel[] SymbolMaturityPreference { get; set; } = Array.Empty<SymbolClassificationLevel>();
        public SymbolClassificationLevel[] SymbolVolumePreference { get; set; } = Array.Empty<SymbolClassificationLevel>();
        public SymbolClassificationLevel[] SymbolVolatilityPreference { get; set; } = Array.Empty<SymbolClassificationLevel>();
    }
}
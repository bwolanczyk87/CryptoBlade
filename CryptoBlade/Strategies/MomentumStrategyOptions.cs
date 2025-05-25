using CryptoBlade.Models;

namespace CryptoBlade.Strategies
{
    public class MomentumStrategyOptions : TradingStrategyBaseOptions
    {
        public decimal MinimumVolume { get; set; } = 15000m;
        public decimal MinimumPriceDistance { get; set; } = 0.01m;

        public decimal MinReentryPositionDistanceLong { get; set; } = 0.02m;
        public decimal MinReentryPositionDistanceShort { get; set; } = 0.02m;

        public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(5);
        public decimal FixedStopLossPercentage { get; set; } = 0.004m;
        public decimal RiskRewardRatio { get; set; } = 1.0m;
        public int BollingerBandsPeriod { get; set; } = 20;
        public double BollingerBandsStdDev { get; set; } = 2.0;
        public int SqueezeLookback { get; set; } = 60;
        public decimal SqueezeStdRatioThreshold { get; set; } = 0.70m;
        public int VolumeLookbackPeriod { get; set; } = 20;
        public decimal VolumeSpikeMultiplier { get; set; } = 2.0m;
        public int RsiPeriod { get; set; } = 14;
        public decimal RsiLongThreshold { get; set; } = 65m;
        public decimal RsiShortThreshold { get; set; } = 35m;
        public int AdxPeriod { get; set; } = 10;
        public decimal AdxTrendThreshold { get; set; } = 22m;
        public int BreakoutConfirmationCandles { get; set; } = 1;
        public decimal RsiContextLongThreshold { get; set; } = 50m;
        public decimal RsiContextShortThreshold { get; set; } = 50m;
        public int EmaTrendPeriod { get; set; } = 21;
        public decimal MinCandleBodySizePercent { get; set; } = 0.0005m;
        public decimal MinVolumeUsd { get; set; } = 100000m;
    }
}

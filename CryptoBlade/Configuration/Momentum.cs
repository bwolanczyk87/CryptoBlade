using CryptoBlade.Models;

namespace CryptoBlade.Configuration
{
    public class Momentum
    {
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
        public decimal MinCandleBodySizePercent { get; set; } = 0.0005m;
        public decimal MinVolumeUsd { get; set; } = 100000m;
        public int EmaTrendPeriod { get; set; } = 21;
        public decimal RsiContextLongThreshold { get; set; } = 50m;
        public decimal RsiContextShortThreshold { get; set; } = 50m;

        public int VolatilityPeriod { get; set; } = 14;
        public decimal RsiVolatilityFactor { get; set; } = 0.5m;
        public decimal TrendThreshold { get; set; } = 0.005m;
        public int VolumeConfirmationPeriod { get; set; } = 5;
        public decimal VolumeConfirmationFactor { get; set; } = 1.2m;
        public decimal RsiContextLongThresholdBase { get; set; } = 50m;
        public decimal RsiContextShortThresholdBase { get; set; } = 50m;
    }
}

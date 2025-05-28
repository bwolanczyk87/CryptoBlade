using System;

namespace CryptoBlade.Configuration
{
    public class Momentum
    {
        public TimeSpan? CooldownPeriod { get; set; }
        public decimal RiskRewardRatio { get; set; }
        public decimal MaxSlippagePercent { get; set; }
        public int BollingerBandsPeriod { get; set; }
        public double BollingerBandsStdDev { get; set; }
        public int SqueezeLookback { get; set; }
        public decimal SqueezeStdRatioThreshold { get; set; }
        public int VolumeLookbackPeriod { get; set; }
        public decimal VolumeSpikeMultiplier { get; set; }
        public int RsiPeriod { get; set; }
        public decimal RsiLongThreshold { get; set; }
        public decimal RsiShortThreshold { get; set; }
        public int AdxPeriod { get; set; }
        public decimal AdxTrendThreshold { get; set; }
        public int TrendEmaPeriod { get; set; }
        public decimal RsiContextLongThresholdBase { get; set; }
        public decimal RsiContextShortThresholdBase { get; set; }
        public decimal RsiVolatilityFactor { get; set; }
        public int VolatilityPeriod { get; set; }
        public decimal AtrMultiplierSl { get; set; }
        public decimal AtrMultiplierTp { get; set; }
        public int BreakoutConfirmationCandles { get; set; }
    }
}
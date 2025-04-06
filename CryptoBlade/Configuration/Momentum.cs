using CryptoBlade.Models;

namespace CryptoBlade.Configuration
{
    public class Momentum
    {
        public int MacdFastPeriod { get; set; } = 9;
        public int MacdSlowPeriod { get; set; } = 21;
        public int MacdSignalPeriod { get; set; } = 9;
        public int RsiPeriod { get; set; } = 14;
        public decimal RsiUpperThreshold { get; set; } = 70;
        public decimal RsiLowerThreshold { get; set; } = 30;
        public decimal MinReentryPositionDistanceLong { get; set; } = 0.02m;
        public decimal MinReentryPositionDistanceShort { get; set; } = 0.02m;
        public int ConfirmationCandles { get; set; } = 1;
        public bool UseSecondaryTimeFrameFilter { get; set; } = false;
        public TimeFrame PrimaryTimeFrame { get; set; } = TimeFrame.OneHour;
        public TimeFrame SecondaryTimeFrame { get; set; } = TimeFrame.FourHours;
        public int PrimaryTimeFrameWindowSize { get; set; } = 150;
        public int SecondaryTimeFrameWindowSize { get; set; } = 150;
        public bool UseAdxFilter { get; set; } = false;
        public int AdxPeriod { get; set; } = 14;
        public decimal MinAdxThreshold { get; set; } = 20m;
    }
}

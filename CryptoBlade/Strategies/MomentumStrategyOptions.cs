using CryptoBlade.Strategies;

public class MomentumStrategyOptions : TradingStrategyBaseOptions
    {
        // Core Parameters
        public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(1);  // Zmniejszony czas cooldown
        public decimal RiskRewardRatio { get; set; } = 1.5m;
        public decimal MaxSlippagePercent { get; set; } = 0.1m;
        public int MinimumVolume { get; set; } = 1000;
        // Bollinger Bands
        public int BollingerBandsPeriod { get; set; } = 10;  // Krótszy okres dla szybszej reakcji
        public double BollingerBandsStdDev { get; set; } = 1.4;  // Węższe pasma
        public int SqueezeLookback { get; set; } = 10;  // Krótszy lookback
        public decimal SqueezeStdRatioThreshold { get; set; } = 0.8m;  // Łatwiejsza detekcja squeeze

        // Volume Analysis
        public int VolumeLookbackPeriod { get; set; } = 12;
        public decimal VolumeSpikeMultiplier { get; set; } = 2.0m;  // Niższy próg spików

        // Momentum Indicators
        public int RsiPeriod { get; set; } = 5;  // Bardziej responsywny RSI
        public decimal RsiLongThreshold { get; set; } = 60m;  // Obniżony próg
        public decimal RsiShortThreshold { get; set; } = 40m;  // Podniesiony próg
        public int AdxPeriod { get; set; } = 12;
        public decimal AdxTrendThreshold { get; set; } = 25m;  // Niższy próg trendu

        // Trend Context
        public int TrendEmaPeriod { get; set; } = 15;
        public decimal RsiContextLongThresholdBase { get; set; } = 55m;  // Łagodniejsze warunki trendu
        public decimal RsiContextShortThresholdBase { get; set; } = 45m;
        public decimal RsiVolatilityFactor { get; set; } = 0.3m;  // Mniejszy wpływ zmienności

        // Risk Management
        public int VolatilityPeriod { get; set; } = 8;
        public decimal AtrMultiplierSl { get; set; } = 1.0m;  // Mniejszy SL
        public decimal AtrMultiplierTp { get; set; } = 1.5m;  // Mniejszy TP

        // Execution
        public int BreakoutConfirmationCandles { get; set; } = 1;  // Mniej świec potwierdzenia
    }
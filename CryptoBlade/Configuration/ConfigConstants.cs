namespace CryptoBlade.Configuration
{
    public class ConfigConstants
    {
        public const string DefaultHistoricalDataDirectory = "Data/HistoricalData";
        public static string GetBackTestResultDirectory(string strategyName) => $"Data/Strategies/{strategyName}/Backtest/Results";
        public static string GetOptimizerResultDirectory(string strategyName) => $"Data/Strategies/{strategyName}/Optimizer/Results";
    }
}

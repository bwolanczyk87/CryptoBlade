namespace CryptoBlade.Strategies.Sigma
{
    public class SigmaStrategyOptions : TradingStrategyBaseOptions
    {
        // Okna buforów
        public int RecalcMinutes { get; init; } = 5;
        public int HysteresisLockMinutes { get; init; } = 2;
        public int OneMinuteWindow { get; init; } = 500;
        public int FiveMinuteWindow { get; init; } = 200;
        public int FifteenMinuteWindow { get; init; } = 200;
        public int OneHourWindow { get; init; } = 200;

        // Progi reżimów
        public decimal AdxEnableMomentum { get; init; } = 22m;
        public decimal AdxDisableMomentum { get; init; } = 18m;
        public decimal BbWidthBreakoutPct { get; init; } = 30m;
        public decimal BbWidthExitBreakoutPct { get; init; } = 45m;
        public decimal ZVwapEnableMR { get; init; } = 1.8m;
        public decimal ZVwapExitMR { get; init; } = 1.0m;
        public decimal MinScore { get; init; } = 30m;
        public decimal MinScoreStay { get; init; } = 15m;
        public decimal MinMargin { get; init; } = 3m;

        // Globalne gate’y
        public decimal MaxSpreadBps { get; init; } = 2m;

        // ATR gates per-mode
        public decimal MmAtrMinPct { get; init; } = 1.2m;
        public decimal MmAtrMaxPct { get; init; } = 4.0m;
        public decimal MrAtrMinPct { get; init; } = 1.0m;
        public decimal MrAtrMaxPct { get; init; } = 3.5m;
        public decimal BoAtrMaxPct { get; init; } = 7.0m;

        // Egzekucja
        public decimal DefaultQuoteSize { get; init; } = 500m;
        public decimal MinAtr5mFloor { get; init; } = 0.5m;

        // Makro
        public int MacroFreezeMinutesBefore { get; init; } = 10;
        public int MacroFreezeMinutesAfter { get; init; } = 30;
        public IReadOnlyList<DateTime> MacroEventsUtc { get; init; } =
        [
            new DateTime(2025, 12, 05, 13, 30, 00, DateTimeKind.Utc), // NFP
            new DateTime(2025, 12, 10, 13, 30, 00, DateTimeKind.Utc), // CPI
            new DateTime(2025, 12, 10, 19, 00, 00, DateTimeKind.Utc), // FOMC statement
            new DateTime(2025, 12, 18, 13, 15, 00, DateTimeKind.Utc), // ECB
        ];

        // Founding Rate
        public int FundingFreezeMinutesBefore { get; init; } = 3;
        public int FundingFreezeMinutesAfter { get; init; } = 3;
        public decimal CorrOppositeBlock { get; init; } = 0.85m;


    }
}

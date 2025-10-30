namespace CryptoBlade.Strategies.Sigma
{
    public sealed class SigmaStrategyOptions : TradingStrategyBaseOptions
    {
        // Okna na cechy/regime
        public int RecalcMinutes { get; init; } = 5;              // decyzja reżimu co 5m
        public int HysteresisLockMinutes { get; init; } = 30;     // minimalny dwell reżimu
        public int OneMinuteWindow { get; init; } = 500;          // bufor 1m
        public int FiveMinuteWindow { get; init; } = 200;         // bufor 5m
        public int FifteenMinuteWindow { get; init; } = 200;      // bufor 15m
        public int OneHourWindow { get; init; } = 200;            // bufor 1h

        // Progi reżimów (startowe; kalibrowalne)
        public decimal AdxEnableMomentum { get; init; } = 22m;
        public decimal AdxDisableMomentum { get; init; } = 18m;
        public decimal BbWidthBreakoutPct { get; init; } = 30m;   // percentyl
        public decimal BbWidthExitBreakoutPct { get; init; } = 45m;
        public decimal ZVwapEnableMR { get; init; } = 1.8m;       // |z| od D-VWAP
        public decimal ZVwapExitMR { get; init; } = 1.0m;
        public decimal MinScore { get; init; } = 55m;
        public decimal MinMargin { get; init; } = 10m;            // przewaga nad 2. trybem

        // Ryzyka / filtry globalne (hook pod Macro/Funding gate można dodać w Supervisorze)
        public decimal MaxSpreadBps { get; init; } = 2m;
        public decimal MinAtr1hPct { get; init; } = 1.2m;
        public decimal MaxAtr1hPct { get; init; } = 4.0m;

        // Drobne: użycie marketu na wejścia – strategia bazowa ma flagę UseMarketOrdersForEntries
    }
}

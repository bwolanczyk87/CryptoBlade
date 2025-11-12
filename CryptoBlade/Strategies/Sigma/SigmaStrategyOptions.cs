namespace CryptoBlade.Strategies.Sigma
{
    public class SigmaStrategyOptions : TradingStrategyBaseOptions
    {
        // Okna buforów (informacyjne; zarządza tym warstwa danych)
        public int RecalcMinutes { get; init; } = 5;          // decyzja co 5m
        public int HysteresisLockMinutes { get; init; } = 30; // minimalny dwell reżimu
        public int OneMinuteWindow { get; init; } = 500;
        public int FiveMinuteWindow { get; init; } = 200;
        public int FifteenMinuteWindow { get; init; } = 200;
        public int OneHourWindow { get; init; } = 200;

        // Progi reżimów (kalibrowalne, pair-aware docelowo)
        public decimal AdxEnableMomentum { get; init; } = 22m;
        public decimal AdxDisableMomentum { get; init; } = 18m;
        public decimal BbWidthBreakoutPct { get; init; } = 30m;   // percentyl
        public decimal BbWidthExitBreakoutPct { get; init; } = 45m;
        public decimal ZVwapEnableMR { get; init; } = 1.8m;       // |z| od D-VWAP
        public decimal ZVwapExitMR { get; init; } = 1.0m;
        public decimal MinScore { get; init; } = 55m;
        public decimal MinMargin { get; init; } = 10m;            // przewaga nad 2. trybem

        // Globalne gate’y koszt/zmienność (twarde)
        public decimal MaxSpreadBps { get; init; } = 2m;

        // ATR gates per-mode (domyślne; kalibrowalne)
        public decimal MmAtrMinPct { get; init; } = 1.2m;
        public decimal MmAtrMaxPct { get; init; } = 4.0m;
        public decimal MrAtrMinPct { get; init; } = 1.0m;
        public decimal MrAtrMaxPct { get; init; } = 3.5m;

        // BO: brak dolnego progu; tylko górny bezpiecznik
        public decimal BoAtrMaxPct { get; init; } = 7.0m;
        public decimal DefaultQuoteSize { get; init; } = 500m; // kwota per trade (USDT)
        public decimal MinAtr5mFloor { get; init; } = 0.5m;    // minimalny „floor” ATR5m w USD, by SL nie był zbyt blisko
    }
}

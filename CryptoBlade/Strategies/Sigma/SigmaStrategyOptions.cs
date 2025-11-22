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
        public decimal ZVwapEnableMR { get; init; } = 1.6m;
        public decimal ZVwapExitMR { get; init; } = 0.8m;
        public decimal MinScore { get; init; } = 30m;
        public decimal MinScoreStay { get; init; } = 15m;
        public decimal MinMargin { get; init; } = 3m;

        // Dynamiczny spread gate (pair-aware)
        public decimal MaxSpreadBps { get; init; } = 6m;
        public decimal SpreadGateMinBps { get; init; } = 0.4m;   // floor dla EMA (stabilność)
        public double SpreadGateEmaAlpha { get; init; } = 0.05;  // ~20 pomiarów half-life
        public double SpreadGateMultiplier { get; init; } = 2.5; // gate ≈ EMA * k, ścięty MaxSpreadBps

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

        // --- Risk management (per-trade / day) ---

        /// <summary>
        /// Docelowe ryzyko na trade w % ekwity strategii (np. 0.003 = 0.3%).
        /// </summary>
        public decimal RiskPerTradePct { get; init; } = 0.003m;

        /// <summary>
        /// Minimalne ryzyko na trade w USDT – zabezpiecza przed zbyt małymi pozycjami.
        /// </summary>
        public decimal MinRiskPerTradeUsd { get; init; } = 5m;

        /// <summary>
        /// Maksymalne ryzyko na trade w USDT – dodatkowy bezpiecznik niezależny od ekwity.
        /// </summary>
        public decimal MaxRiskPerTradeUsd { get; init; } = 50m;

        /// <summary>
        /// Dzienny limit strat w jednostkach R (np. 2R oznacza -2 * RiskPerTrade).
        /// Implementacja kill-switch'a żyje wyżej (portfolio manager).
        /// </summary>
        public decimal MaxDailyLossR { get; init; } = 2m;

        /// <summary>
        /// Maksymalna liczba przegranych trade'ów z rzędu zanim zadziała kill-switch.
        /// </summary>
        public int MaxLosingTradesStreak { get; init; } = 3;

        /// <summary>
        /// Próg "wysokiej" korelacji (|ρ|) powyżej którego możemy zmniejszyć size zamiast
        /// całkowicie blokować trade (twardy blok zostaje w CorrOppositeBlock).
        /// </summary>
        public decimal CorrHighReduceSizeThreshold { get; init; } = 0.7m;

        /// <summary>
        /// Mnożnik size przy wysokiej korelacji (np. 0.5 = połowa standardowego ryzyka).
        /// </summary>
        public decimal CorrHighSizeMultiplier { get; init; } = 0.5m;

        /// <summary>
        /// Próg atr% BTC powyżej którego traktujemy sytuację jako BTC shock regime.
        /// (pełne wykorzystanie wymaga dopięcia Btc ATR w SigmaData – kolejny krok).
        /// </summary>
        public decimal BtcAtrShockThresholdPct { get; init; } = 6.0m;

        /// <summary>
        /// Mnożnik size w BTC shock regime (np. 0.5 = połowa standardowego size).
        /// </summary>
        public decimal BtcShockSizeMultiplier { get; init; } = 0.5m;

        /// <summary>
        /// Maksymalna liczba równoległych pozycji na altach podczas BTC shock regime.
        /// (kontrola na poziomie portfolio managera).
        /// </summary>
        public int BtcShockMaxConcurrentAltPositions { get; init; } = 2;

        // --- Mode tier risk multipliers ---

        /// <summary>
        /// Mnożnik ryzyka dla najlepszych setupów (Hard).
        /// Np. 1.4 = 40% więcej ryzyka niż bazowe R z equity.
        /// </summary>
        public decimal TierHardRiskMultiplier { get; init; } = 1.4m;

        /// <summary>
        /// Mnożnik ryzyka dla normalnych setupów (Medium).
        /// </summary>
        public decimal TierMediumRiskMultiplier { get; init; } = 1.0m;

        /// <summary>
        /// Mnożnik ryzyka dla miękkich setupów (Soft).
        /// </summary>
        public decimal TierSoftRiskMultiplier { get; init; } = 0.6m;

        /// <summary>
        /// Mnożnik ryzyka dla tieru None / fallback.
        /// </summary>
        public decimal TierNoneRiskMultiplier { get; init; } = 0.4m;


    }
}

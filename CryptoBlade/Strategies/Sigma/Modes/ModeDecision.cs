using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma.Modes
{
    public readonly struct ModeDecision(bool buy, bool sell, bool buyExtra, bool sellExtra)
    {
        public readonly bool HasBuy = buy, HasSell = sell, HasBuyExtra = buyExtra, HasSellExtra = sellExtra;
        public static ModeDecision None => new(false, false, false, false);
    }

    namespace CryptoBlade.Strategies.Sigma
    {
        public interface IModeController
        {
            /// <summary>
            /// Zwraca decyzję wejścia/zarządzania pozycją dla danego reżimu.
            /// </summary>
            ModeDecision Evaluate(FeatureSnapshot f, RegimeState state, DateTime nowUtc, CancellationToken cancel);
        }

        public sealed class EntryPlan
        {
            public decimal? EntryPrice { get; init; }           // limit na 50% świecy triggera / retest
            public decimal? StopLoss { get; init; }             // za ekstremum + 0.5–0.6*ATR5m (min floor)
            public decimal? TakeProfit1 { get; init; }          // 1R
            public decimal? TakeProfit2 { get; init; }          // 1.6–2.2R (MM/BO); dla MR opcjonalny
            public decimal? SizeQuote { get; init; }            // kwota w USDT
            public string? Reason { get; init; }
        }
    }
}

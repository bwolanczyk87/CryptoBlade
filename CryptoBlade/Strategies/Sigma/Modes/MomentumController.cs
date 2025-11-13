using CryptoBlade.Strategies.Sigma.Modes;
using CryptoBlade.Strategies.Sigma.Regimes;

namespace CryptoBlade.Strategies.Sigma
{
    /// <summary>
    /// MM (Momentum): kontynuacja po korekcie – proxy na bazie dostępnych cech:
    /// - Gate: SpreadBps <= MaxSpreadBps (twardy)
    /// - ATR%_1H w "zdrowym" paśmie [1.2%; 4.0%]
    /// - Reclaim proxy: |ZDvwap| <= 0.6
    /// - Kierunek: znak ZSlopeDvwap (trend) + zgodny znak DeltaCvd5m (taker imbalance)
    /// - Extra: przy mocniejszej konfirmacji (|OiDelta1hPct| >= 0.5 lub ADX >= 22)
    /// Zwracamy wyłącznie flagi (ModeDecision): HasBuy/HasSell/(opcjonalnie) HasBuyExtra/HasSellExtra.
    /// </summary>
    public sealed class MomentumController(SigmaStrategyOptions options) : IModeController
    {
        private readonly SigmaStrategyOptions _o = options;

        public ModeDecision Evaluate(FeatureSnapshot f, DateTime nowUtc, CancellationToken cancel)
        {
            // 2) ATR "zdrowy trend"
            if (f.AtrPct1h < 1.2 || f.AtrPct1h > 4.0)
                return ModeDecision.None;

            // 3) Reclaim proxy: blisko wartości (VWAP) — małe |z|
            var absZ = f.ZDvwap >= 0 ? f.ZDvwap : -f.ZDvwap;
            if (absZ > 0.6)
                return ModeDecision.None;

            // 4) Kierunek: trend + orderflow muszą być zgodne ze znakiem
            //    Długie: ZSlopeDvwap > 0 oraz DeltaCvd5m > 0
            //    Krótkie: ZSlopeDvwap < 0 oraz DeltaCvd5m < 0
            bool longTrigger = (f.ZSlopeDvwap > 0.0) && (f.DeltaCvd5m > 0.0);
            bool shortTrigger = (f.ZSlopeDvwap < 0.0) && (f.DeltaCvd5m < 0.0);

            if (!longTrigger && !shortTrigger)
                return ModeDecision.None;

            // 5) Extra konfirmacja: OI↑ lub silniejszy trend (ADX)
            bool extraLong = (f.OiDelta1hPct >= 0.5) || (f.Adx1h >= 22.0);
            bool extraShort = (f.OiDelta1hPct <= -0.5) || (f.Adx1h >= 22.0);

            if (longTrigger)
            {
                return new ModeDecision(
                    buy: true,
                    sell: false,
                    buyExtra: extraLong,
                    sellExtra: false
                );
            }

            // shortTrigger
            return new ModeDecision(
                buy: false,
                sell: true,
                buyExtra: false,
                sellExtra: extraShort
            );
        }
    }
}

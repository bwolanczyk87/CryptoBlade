using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public enum RegimeLabel { None = 0, Momentum = 1, MeanReversion = 2, Breakout = 3 }

    /// <summary>
    /// Pełny audyt pojedynczej ewaluacji reżimu i cech rynkowych.
    /// Braki danych = NaN. Booleany jako 0/1 w CSV.
    /// </summary>
    public sealed class RegimeAuditRecord
    {
        // Meta
        public DateTime TimeUtc { get; init; }
        public string Symbol { get; init; } = "";
        public RegimeLabel Selected { get; init; }
        public RegimeLabel Prev { get; init; }
        public RegimeLabel? Oracle { get; set; }

        // Wynik decyzji i progów
        public bool Tradable { get; init; }
        public string Reason { get; init; } = "";
        public double MinScore { get; init; }
        public double MinMargin { get; init; }
        public double HysteresisLockMinutes { get; init; }

        // Scores
        public double ScoreMM { get; init; }
        public double ScoreMR { get; init; }
        public double ScoreBO { get; init; }

        // Cechy – trend/value/vol
        public double Adx1h { get; init; }
        public double AtrPct1h { get; init; }
        public double Atr1hAbs { get; init; }
        public double ZDvwap { get; init; }
        public double ZSlopeDvwap { get; init; }
        public double AutoCorr5m { get; init; }
        public double Bbw15mPct { get; init; }
        public double Bbw15mRaw { get; init; }

        // Mikrostruktura
        public double SpreadBps { get; init; }

        // Derywaty/flow
        public double OiDelta1hPct { get; init; }
        public double Funding8h { get; init; }
        public double BasisPct { get; init; }
        public double DeltaCvd5m { get; init; }
        public double DistToLiqPct { get; init; }

        // Patterny/struktura
        public bool HasInsideOrNr7 { get; init; }
        public bool DonchianBreakUp { get; init; }
        public bool DonchianBreakDown { get; init; }
        public bool Bbw15mExpanding { get; init; }
        public decimal? OpeningRangeHigh { get; init; }
        public decimal? OpeningRangeLow { get; init; }

        // Supervisor – korelacja do BTC
        public double CorrToBtc15m { get; init; }
        public bool BtcBiasOpposite { get; init; }

        // Dodatkowo: stan histerezy
        public DateTime SinceUtc { get; init; }
    }
}

using CryptoBlade.Strategies.Sigma.Modes;
using static CryptoBlade.Strategies.Sigma.Audit.SigmaAuditCsv;

namespace CryptoBlade.Strategies.Sigma.Audit
{
    public sealed class SigmaAuditRecord
    {
        // ========= META / TRYBY =========

        [AuditColumn(Order = 0, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime TimeUtc { get; init; }

        [AuditColumn(Order = 1)]
        public string Symbol { get; init; } = "";

        [AuditColumn(Order = 2, EnumFormat = AuditEnumFormat.Int)]
        public ModeKind Mode { get; init; }

        [AuditColumn(Order = 3)]
        public string GateReason { get; init; } = "";

        [AuditColumn(Order = 4, Decimals = 2)]
        public double MinScore { get; init; }

        [AuditColumn(Order = 5, Decimals = 2)]
        public double MinMargin { get; init; }

        [AuditColumn(Order = 6, Decimals = 0)]
        public double HysteresisLockMinutes { get; init; }

        // ========= SCORES =========

        [AuditColumn(Order = 7, Decimals = 2)]
        public double ScoreMM { get; init; }

        [AuditColumn(Order = 8, Decimals = 2)]
        public double ScoreMR { get; init; }

        [AuditColumn(Order = 9, Decimals = 2)]
        public double ScoreBO { get; init; }


        [AuditColumn(Order = 10)]
        public int MmEntryTier { get; init; }

        [AuditColumn(Order = 11)]
        public int MrEntryTier { get; init; }

        [AuditColumn(Order = 12)]
        public int BoEntryTier { get; init; }

        [AuditColumn(Order = 13)]

        public bool MmLongCandidate { get; init; }

        [AuditColumn(Order = 14)]
        public bool MmShortCandidate { get; init; }

        [AuditColumn(Order = 15)]
        public bool MrLongCandidate { get; init; }

        [AuditColumn(Order = 16)]
        public bool MrShortCandidate { get; init; }

        [AuditColumn(Order = 17)]
        public bool BoLongCandidate { get; init; }

        [AuditColumn(Order = 18)]
        public bool BoShortCandidate { get; init; }

        // ========= CECHY – TREND / VALUE / VOL =========

        [AuditColumn(Order = 19, Decimals = 2)]
        public double Adx1h { get; init; }

        [AuditColumn(Order = 20, Decimals = 3)]
        public double AtrPct1h { get; init; }

        [AuditColumn(Order = 21, Decimals = 2)]
        public double Atr1hAbs { get; init; }

        [AuditColumn(Order = 22, Decimals = 3)]
        public double ZDvwap { get; init; }

        [AuditColumn(Order = 23, Decimals = 3)]
        public double ZDvwapPrev { get; init; }

        [AuditColumn(Order = 24, Decimals = 3)]
        public double ZSlopeDvwap { get; init; }

        [AuditColumn(Order = 25, Decimals = 4)]
        public double AutoCorr5m { get; init; }

        [AuditColumn(Order = 26, Decimals = 2)]
        public double Bbw15mPct { get; init; }

        [AuditColumn(Order = 27, Decimals = 6)]
        public double Bbw15mRaw { get; init; }

        [AuditColumn(Order = 28, BoolAsInt = true)]
        public bool Bbw15mExpanding { get; init; }

        // ========= MIKROSTRUKTURA / DERYWATY / FLOW =========

        [AuditColumn(Order = 29, Decimals = 4)]
        public double SpreadBps { get; init; }

        [AuditColumn(Order = 30, Decimals = 4)]
        public double SpreadGateThresholdBps { get; init; }

        [AuditColumn(Order = 31, Decimals = 3)]
        public double OiDelta1hPct { get; init; }

        [AuditColumn(Order = 32, Decimals = 6)]
        public double DeltaCvd5m { get; init; }

        [AuditColumn(Order = 33, Decimals = 6)]
        public double DeltaCvdPrev5m { get; init; }

        [AuditColumn(Order = 34, BoolAsInt = true)]
        public bool CvdFlipUp5m { get; init; }

        [AuditColumn(Order = 35, BoolAsInt = true)]
        public bool CvdFlipDown5m { get; init; }

        [AuditColumn(Order = 36, Decimals = 5)]
        public double FundingPredictedPct { get; init; }

        [AuditColumn(Order = 38, Decimals = 5)]
        public double FundingLastSettledPct { get; init; }

        [AuditColumn(Order = 39, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime? NextFundingUtc { get; init; }

        [AuditColumn(Order = 40, Decimals = 4)]
        public double BasisPct { get; init; }

        [AuditColumn(Order = 41, Decimals = 2)]
        public double DistToLiqPct { get; init; }

        // ========= PATTERNY / STRUKTURA / OR / DONCHIAN =========

        [AuditColumn(Order = 42, BoolAsInt = true)]
        public bool HasInsideOrNr7 { get; init; }

        [AuditColumn(Order = 43, BoolAsInt = true)]
        public bool DonchianBreakUp { get; init; }

        [AuditColumn(Order = 44, BoolAsInt = true)]
        public bool DonchianBreakDown { get; init; }

        [AuditColumn(Order = 45, Decimals = 4)]
        public decimal? DonchianUpper15m { get; init; }

        [AuditColumn(Order = 46, Decimals = 4)]
        public decimal? DonchianLower15m { get; init; }

        [AuditColumn(Order = 47, Decimals = 2)]
        public decimal? OpeningRangeHigh { get; init; }

        [AuditColumn(Order = 48, Decimals = 2)]
        public decimal? OpeningRangeLow { get; init; }

        [AuditColumn(Order = 49, BoolAsInt = true)]
        public bool OrBreakoutRetestUp5m { get; init; }

        [AuditColumn(Order = 50, BoolAsInt = true)]
        public bool OrBreakoutRetestDown5m { get; init; }

        [AuditColumn(Order = 51, Decimals = 2)]
        public double OrRetestDepthBpsUp5m { get; init; }

        [AuditColumn(Order = 52, Decimals = 2)]
        public double OrRetestDepthBpsDown5m { get; init; }

        [AuditColumn(Order = 53, BoolAsInt = true)]
        public bool SweepReclaimUp5m { get; init; }

        [AuditColumn(Order = 54, BoolAsInt = true)]
        public bool SweepReclaimDown5m { get; init; }

        [AuditColumn(Order = 55, Decimals = 2)]
        public double SweepUpOvershootBps5m { get; init; }

        [AuditColumn(Order = 56, Decimals = 2)]
        public double SweepDownOvershootBps5m { get; init; }

        // ========= SUPERVISOR – BTC =========

        [AuditColumn(Order = 57, Decimals = 4)]
        public double CorrToBtc15m { get; init; }

        [AuditColumn(Order = 57, BoolAsInt = true)]
        public bool BtcBiasOpposite { get; init; }

        // ========= SUROWY SNAPSHOT TICKERA =========

        [AuditColumn(Order = 58, Decimals = 2)]
        public decimal? LastPrice { get; init; }

        [AuditColumn(Order = 60, Decimals = 2)]
        public decimal? BestBidPrice { get; init; }

        [AuditColumn(Order = 61, Decimals = 2)]
        public decimal? BestAskPrice { get; init; }

        [AuditColumn(Order = 62, Decimals = 2)]
        public decimal? MarkPrice { get; init; }

        [AuditColumn(Order = 63, Decimals = 2)]
        public decimal? IndexPrice { get; init; }

        // ========= OSTATNI BAR 1M =========

        [AuditColumn(Order = 64, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime? Last1mTimeUtc { get; init; }

        [AuditColumn(Order = 65, Decimals = 2)]
        public decimal? Last1mOpen { get; init; }

        [AuditColumn(Order = 66, Decimals = 2)]
        public decimal? Last1mHigh { get; init; }

        [AuditColumn(Order = 67, Decimals = 2)]
        public decimal? Last1mLow { get; init; }

        [AuditColumn(Order = 68, Decimals = 2)]
        public decimal? Last1mClose { get; init; }

        [AuditColumn(Order = 69, Decimals = 4)]
        public decimal? Last1mVolume { get; init; }
    }

    // ======== BUDOWANIE REKORDU (z ModeEngine.Classify) ========

    public static class SigmaAudit
    {
        public static SigmaAuditRecord MakeRecord(
            DateTime nowUtc,
            string symbol,
            SigmaData data,
            SigmaStrategyOptions options,
            string gateReason,
            IMode? mode,
            ModeScores scores)
        {
            return new SigmaAuditRecord
            {
                TimeUtc = nowUtc,
                Symbol = symbol,
                Mode = mode?.Kind ?? ModeKind.None,

                GateReason = gateReason,
                MinScore = options.MinScore,
                MinMargin = options.MinMargin,
                HysteresisLockMinutes = options.HysteresisLockMinutes,

                ScoreMM = scores.Momentum,
                ScoreMR = scores.MeanReversion,
                ScoreBO = scores.Breakout,

                MmEntryTier = data.MomentumEntryTier,
                MrEntryTier = data.MeanReversionEntryTier,
                BoEntryTier = data.BreakoutEntryTier,

                MmLongCandidate = data.MomentumLongCandidate,
                MmShortCandidate = data.MomentumShortCandidate,
                MrLongCandidate = data.MeanReversionLongCandidate,
                MrShortCandidate = data.MeanReversionShortCandidate,
                BoLongCandidate = data.BreakoutLongCandidate,
                BoShortCandidate = data.BreakoutShortCandidate,

                Adx1h = data.Adx1h,
                AtrPct1h = data.AtrPct1h,
                Atr1hAbs = data.Atr1hAbs,
                ZDvwap = data.ZDvwap,
                ZDvwapPrev = data.ZDvwapPrev,
                ZSlopeDvwap = data.ZSlopeDvwap,
                AutoCorr5m = data.AutoCorr5m,
                Bbw15mPct = data.Bbw15mPct,
                Bbw15mRaw = data.Bbw15mRaw,
                Bbw15mExpanding = data.Bbw15mExpanding,

                SpreadBps = data.SpreadBps,
                SpreadGateThresholdBps = data.SpreadGateThresholdBps,

                OiDelta1hPct = data.OiDelta1hPct,
                DeltaCvd5m = data.DeltaCvd5m,
                DeltaCvdPrev5m = data.DeltaCvdPrev5m,
                CvdFlipUp5m = data.CvdFlipUp5m,
                CvdFlipDown5m = data.CvdFlipDown5m,

                FundingPredictedPct = data.FundingPredictedPct,
                FundingLastSettledPct = data.FundingLastSettledPct,
                NextFundingUtc = data.NextFundingUtc,

                BasisPct = data.BasisPct,
                DistToLiqPct = data.DistToLiqPct,

                HasInsideOrNr7 = data.HasInsideOrNr7,
                DonchianBreakUp = data.DonchianBreakUp,
                DonchianBreakDown = data.DonchianBreakDown,
                DonchianUpper15m = data.DonchianResult?.UpperBand,
                DonchianLower15m = data.DonchianResult?.LowerBand,

                SweepReclaimUp5m = data.SweepReclaimUp5m,
                SweepReclaimDown5m = data.SweepReclaimDown5m,
                SweepUpOvershootBps5m = data.SweepUpOvershootBps5m,
                SweepDownOvershootBps5m = data.SweepDownOvershootBps5m,

                CorrToBtc15m = data.CorrToBtc15m,
                BtcBiasOpposite = data.BtcBiasOpposite,

                LastPrice = data.LastPrice,
                BestBidPrice = data.BestBidPrice,
                BestAskPrice = data.BestAskPrice,
                MarkPrice = data.MarkPrice,
                IndexPrice = data.IndexPrice,

                Last1mTimeUtc = data.Last1mTimeUtc,
                Last1mOpen = data.Last1mOpen,
                Last1mHigh = data.Last1mHigh,
                Last1mLow = data.Last1mLow,
                Last1mClose = data.Last1mClose,
                Last1mVolume = data.Last1mVolume
            };
        }
    }
}

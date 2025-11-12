// RegimeAuditCsv.cs
using System.Globalization;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public static class RegimeAuditCsv
    {
        private static readonly string[] _cols = new[]
        {
            "TimeUtc","Symbol","Selected","Prev","Oracle",
            "Tradable","Reason","MinScore","MinMargin","HysteresisLockMinutes",
            "ScoreMM","ScoreMR","ScoreBO",
            "Adx1h","AtrPct1h","Atr1hAbs","ZDvwap","ZSlopeDvwap","AutoCorr5m","Bbw15mPct","Bbw15mRaw",
            "SpreadBps","OiDelta1hPct","Funding8h","BasisPct","DeltaCvd5m","DistToLiqPct",
            "HasInsideOrNr7","DonchianBreakUp","DonchianBreakDown","Bbw15mExpanding","OpeningRangeHigh","OpeningRangeLow",
            "CorrToBtc15m","BtcBiasOpposite","SinceUtc"
        };

        public static string Header() => string.Join(",", _cols);

        public static string Row(RegimeAuditRecord r)
        {
            var inv = CultureInfo.InvariantCulture;
            string B(bool b) => b ? "1" : "0";
            string D(double v) => double.IsFinite(v) ? v.ToString(inv) : "";
            string N(decimal? v) => v.HasValue ? v.Value.ToString(inv) : "";

            return string.Join(",", new[]
            {
                r.TimeUtc.ToString("o"),
                r.Symbol,
                ((int)r.Selected).ToString(inv),
                ((int)r.Prev).ToString(inv),
                r.Oracle.HasValue ? ((int)r.Oracle.Value).ToString(inv) : "",

                B(r.Tradable),
                (r.Reason ?? string.Empty).Replace(',', ';'),
                D(r.MinScore),
                D(r.MinMargin),
                D(r.HysteresisLockMinutes),

                D(r.ScoreMM), D(r.ScoreMR), D(r.ScoreBO),

                D(r.Adx1h), D(r.AtrPct1h), D(r.Atr1hAbs), D(r.ZDvwap), D(r.ZSlopeDvwap),
                D(r.AutoCorr5m), D(r.Bbw15mPct), D(r.Bbw15mRaw),

                D(r.SpreadBps), D(r.OiDelta1hPct), D(r.Funding8h), D(r.BasisPct), D(r.DeltaCvd5m), D(r.DistToLiqPct),

                B(r.HasInsideOrNr7), B(r.DonchianBreakUp), B(r.DonchianBreakDown), B(r.Bbw15mExpanding),
                N(r.OpeningRangeHigh), N(r.OpeningRangeLow),

                D(r.CorrToBtc15m), B(r.BtcBiasOpposite),
                r.SinceUtc.ToString("o"),
            });
        }

        // nadal możesz używać Export(...) do jednorazowego zrzutu całości
        public static string Export(IEnumerable<RegimeAuditRecord> rows)
        {
            var lines = new List<string> { Header() };
            foreach (var r in rows) lines.Add(Row(r));
            return string.Join(Environment.NewLine, lines);
        }
    }
}

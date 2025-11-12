using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    public enum RegimeLabel { None = 0, Momentum = 1, MeanReversion = 2, Breakout = 3 }
    public interface IRegimeAuditSink
    {
        void Add(RegimeAuditRecord r);
        IReadOnlyList<RegimeAuditRecord> Snapshot();
        void Clear();
    }
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
        public int VwapKindUsed { get; init; }   
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

    public sealed class RegimeAuditSink : IRegimeAuditSink
    {
        private readonly string _path;
        private readonly ConcurrentQueue<RegimeAuditRecord> _q = new();
        private static readonly object _fileLock = new();

        public RegimeAuditSink(string path)
        {
            _path = path;
            EnsureHeader();
        }

        public void Add(RegimeAuditRecord r)
        {
            _q.Enqueue(r);
            AppendRow(r);
        }

        public IReadOnlyList<RegimeAuditRecord> Snapshot()
        {
            var list = new List<RegimeAuditRecord>(_q.Count);
            foreach (var r in _q) list.Add(r);
            return list;
        }

        public void Clear()
        {
            while (_q.TryDequeue(out _)) { }
            // Pliku nie czyścimy (to log historyczny). Jeśli chcesz rotację — daj znać.
        }

        private void EnsureHeader()
        {
            lock (_fileLock)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!File.Exists(_path) || new FileInfo(_path).Length == 0)
                {
                    File.AppendAllText(_path, RegimeAuditCsv.Header() + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
        }

        private void AppendRow(RegimeAuditRecord r)
        {
            lock (_fileLock)
            {
                File.AppendAllText(_path, RegimeAuditCsv.Row(r) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }
}

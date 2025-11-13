using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace CryptoBlade.Strategies.Sigma.Regimes
{
    // ======== ATRYBUTY I FORMATOWANIE ========

    public enum AuditEnumFormat { Int = 0, String = 1 }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AuditIgnoreAttribute : Attribute { }

    /// <summary>
    /// Konfiguracja kolumny CSV. Wszystkie parametry opcjonalne.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AuditColumnAttribute : Attribute
    {
        /// <summary> Kolejność kolumny (mniejsza = wcześniej). Bez podania: int.MaxValue (na końcu). </summary>
        public int Order { get; init; } = int.MaxValue;
        /// <summary> Nazwa kolumny (domyślnie: nazwa właściwości). </summary>
        public string? Name { get; init; }
        /// <summary> Liczba miejsc po przecinku dla double/decimal. (-1 = użyj domyślnej). </summary>
        public int Decimals { get; init; } = -1;
        /// <summary> Format datetime (domyślnie "yyyy-MM-ddTHH:mm:ss.fffZ"). </summary>
        public string? DateFormat { get; init; }
        /// <summary> Czy bool ma być 1/0 (domyślnie tak). Gdy false → "true"/"false". </summary>
        public bool BoolAsInt { get; init; } = true;
        /// <summary> Jak formatować enum (domyślnie Int). </summary>
        public AuditEnumFormat EnumFormat { get; init; } = AuditEnumFormat.Int;
        /// <summary> Dla liczb: ukryj NaN (pusty string). </summary>
        public bool SkipIfNaN { get; init; } = true;
    }

    // ======== MODEL I INTERFEJS ========

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
        [AuditColumn(Order = 0, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime TimeUtc { get; init; }

        [AuditColumn(Order = 1)]
        public string Symbol { get; init; } = "";

        // Reżimy: aktywny (Selected) vs poprzedni vs proponowany
        [AuditColumn(Order = 2, EnumFormat = AuditEnumFormat.Int)]
        public RegimeLabel Selected { get; init; }      // Active (po histerezie/dwell)

        [AuditColumn(Order = 3, EnumFormat = AuditEnumFormat.Int)]
        public RegimeLabel Prev { get; init; }          // Poprzedni Active

        [AuditColumn(Order = 4, EnumFormat = AuditEnumFormat.Int)]
        public RegimeLabel? Oracle { get; set; }        // Opcjonalny label referencyjny

        [AuditColumn(Order = 5, EnumFormat = AuditEnumFormat.Int)]
        public RegimeLabel Proposed { get; init; }      // Argmax z bieżących score'ów

        // Wynik decyzji i progów
        [AuditColumn(Order = 6, BoolAsInt = true)]
        public bool Tradable { get; init; }             // Global gates OK/NOK

        [AuditColumn(Order = 7)]
        public string Reason { get; init; } = "";

        [AuditColumn(Order = 8, Decimals = 2)]
        public double MinScore { get; init; }

        [AuditColumn(Order = 9, Decimals = 2)]
        public double MinMargin { get; init; }

        [AuditColumn(Order = 10, Decimals = 0)]
        public double HysteresisLockMinutes { get; init; }

        // Scores (pełne) + metryki wyboru
        [AuditColumn(Order = 11, Decimals = 2)]
        public double ScoreMM { get; init; }

        [AuditColumn(Order = 12, Decimals = 2)]
        public double ScoreMR { get; init; }

        [AuditColumn(Order = 13, Decimals = 2)]
        public double ScoreBO { get; init; }

        [AuditColumn(Order = 14, Decimals = 2)]
        public double ProposedScore { get; init; }      // max(scoreMM, scoreMR, scoreBO)

        [AuditColumn(Order = 15, Decimals = 2)]
        public double SecondBestScore { get; init; }    // drugi najlepszy

        [AuditColumn(Order = 16, Decimals = 2)]
        public double Margin { get; init; }             // ProposedScore - SecondBestScore

        [AuditColumn(Order = 17, Decimals = 2)]
        public double ActiveScore { get; init; }        // score wybranego Active

        // Cechy – trend/value/vol
        [AuditColumn(Order = 18, Decimals = 2)]
        public double Adx1h { get; init; }

        [AuditColumn(Order = 19, Decimals = 3)]
        public double AtrPct1h { get; init; }

        [AuditColumn(Order = 20, Decimals = 2)]
        public double Atr1hAbs { get; init; }

        [AuditColumn(Order = 21, Decimals = 3)]
        public double ZDvwap { get; init; }

        [AuditColumn(Order = 22, Decimals = 3)]
        public double ZSlopeDvwap { get; init; }

        [AuditColumn(Order = 23, Decimals = 4)]
        public double AutoCorr5m { get; init; }

        [AuditColumn(Order = 24, Decimals = 2)]
        public double Bbw15mPct { get; init; }

        [AuditColumn(Order = 25, Decimals = 6)]
        public double Bbw15mRaw { get; init; }

        // Mikrostruktura
        [AuditColumn(Order = 26, Decimals = 4)]
        public double SpreadBps { get; init; }

        // Derywaty/flow
        [AuditColumn(Order = 27, Decimals = 3)]
        public double OiDelta1hPct { get; init; }

        [AuditColumn(Order = 28, Decimals = 5)]
        public double FundingPredictedPct { get; init; }

        [AuditColumn(Order = 29, Decimals = 5)]
        public double FundingLastSettledPct { get; init; }

        [AuditColumn(Order = 30, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime? NextFundingUtc { get; init; }

        [AuditColumn(Order = 31, Decimals = 4)]
        public double BasisPct { get; init; }

        [AuditColumn(Order = 32, Decimals = 6)]
        public double DeltaCvd5m { get; init; }

        [AuditColumn(Order = 33, Decimals = 2)]
        public double DistToLiqPct { get; init; }

        // Patterny/struktura
        [AuditColumn(Order = 34, BoolAsInt = true)]
        public bool HasInsideOrNr7 { get; init; }

        [AuditColumn(Order = 35, BoolAsInt = true)]
        public bool DonchianBreakUp { get; init; }

        [AuditColumn(Order = 36, BoolAsInt = true)]
        public bool DonchianBreakDown { get; init; }

        [AuditColumn(Order = 37, BoolAsInt = true)]
        public bool Bbw15mExpanding { get; init; }

        [AuditColumn(Order = 38, Decimals = 2)]
        public decimal? OpeningRangeHigh { get; init; }

        [AuditColumn(Order = 39, Decimals = 2)]
        public decimal? OpeningRangeLow { get; init; }

        // Supervisor – korelacja do BTC
        [AuditColumn(Order = 40, Decimals = 4)]
        public double CorrToBtc15m { get; init; }

        [AuditColumn(Order = 41, BoolAsInt = true)]
        public bool BtcBiasOpposite { get; init; }

        // Histereza (stan reżimu)
        [AuditColumn(Order = 42, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime SinceUtc { get; init; }         // od kiedy aktywny reżim

        [AuditColumn(Order = 43, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime LastDecisionUtc { get; init; }  // ostatni heartbeat decyzji reżimu
    }

    // ======== BUDOWANIE REKORDU (z Classify) ========

    public static class RegimeAudit
    {
        // Preferowany overload – korzysta z pełnego RegimeDecision
        public static RegimeAuditRecord MakeRecord(
            FeatureSnapshot f,
            RegimeState prev,
            DateTime nowUtc,
            SigmaStrategyOptions o,
            bool tradable,
            string reason,
            RegimeDecision decision,
            DateTime lastDecisionUtc
        )
        {
            var active = decision.State.Mode;
            var proposed = decision.ProposedMode;

            var mm = decision.State.Scores.Momentum;
            var mr = decision.State.Scores.MeanReversion;
            var bo = decision.State.Scores.Breakout;
            var activeScore = decision.State.Scores[active];

            return new RegimeAuditRecord
            {
                TimeUtc = nowUtc,
                Symbol = f.Symbol,

                Selected = (RegimeLabel)active,
                Prev = (RegimeLabel)prev.Mode,
                Oracle = null,
                Proposed = (RegimeLabel)proposed,

                Tradable = tradable,
                Reason = reason ?? string.Empty,
                MinScore = (double)o.MinScore,
                MinMargin = (double)o.MinMargin,
                HysteresisLockMinutes = o.HysteresisLockMinutes,

                ScoreMM = mm,
                ScoreMR = mr,
                ScoreBO = bo,
                ProposedScore = decision.ProposedScore,
                SecondBestScore = decision.SecondBestScore,
                Margin = decision.Margin,
                ActiveScore = activeScore,

                Adx1h = f.Adx1h,
                AtrPct1h = f.AtrPct1h,
                Atr1hAbs = f.Atr1hAbs,
                ZDvwap = f.ZDvwap,
                ZSlopeDvwap = f.ZSlopeDvwap,
                AutoCorr5m = f.AutoCorr5m,
                Bbw15mPct = f.Bbw15mPct,
                Bbw15mRaw = f.Bbw15mRaw,

                SpreadBps = f.SpreadBps,

                OiDelta1hPct = f.OiDelta1hPct,
                FundingLastSettledPct = f.FundingLastSettledPct,
                FundingPredictedPct = f.FundingPredictedPct,
                NextFundingUtc = f.NextFundingUtc,

                BasisPct = f.BasisPct,
                DeltaCvd5m = f.DeltaCvd5m,
                DistToLiqPct = f.DistToLiqPct,

                HasInsideOrNr7 = f.HasInsideOrNr7,
                DonchianBreakUp = f.DonchianBreakUp,
                DonchianBreakDown = f.DonchianBreakDown,
                Bbw15mExpanding = f.Bbw15mExpanding,
                OpeningRangeHigh = f.OpeningRangeHigh,
                OpeningRangeLow = f.OpeningRangeLow,

                CorrToBtc15m = f.CorrToBtc15m,
                BtcBiasOpposite = f.BtcBiasOpposite,

                SinceUtc = decision.State.SinceUtc,
                LastDecisionUtc = lastDecisionUtc,
            };
        }
    }

    // ======== CSV (GENERYCZNE, REFLEKSYJNE, Z CACHINGIEM) ========

    public static class RegimeAuditCsv
    {
        private sealed record ColMeta(
            PropertyInfo Prop,
            string Name,
            int Order,
            int Decimals,
            string DateFormat,
            bool BoolAsInt,
            AuditEnumFormat EnumFormat,
            bool SkipIfNaN);

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const int DEFAULT_DOUBLE_DECIMALS = 6;        // fallback dla double/decimal
        private const string DEFAULT_DATE_FMT = "yyyy-MM-ddTHH:mm:ss.fffZ";

        private static readonly ColMeta[] _cols = BuildColumns();

        private static ColMeta[] BuildColumns()
        {
            var t = typeof(RegimeAuditRecord);
            var props = t.GetProperties(BindingFlags.Instance | BindingFlags.Public);

            var list = new List<ColMeta>(props.Length);

            foreach (var p in props)
            {
                if (p.GetCustomAttribute<AuditIgnoreAttribute>() != null)
                    continue;

                var a = p.GetCustomAttribute<AuditColumnAttribute>();
                var name = a?.Name ?? p.Name;
                var order = a?.Order ?? int.MaxValue;
                var decimals = a?.Decimals ?? -1;
                var fmt = a?.DateFormat ?? DEFAULT_DATE_FMT;
                var boolAsInt = a?.BoolAsInt ?? true;
                var enumFmt = a?.EnumFormat ?? AuditEnumFormat.Int;
                var skipIfNaN = a?.SkipIfNaN ?? true;

                list.Add(new ColMeta(p, name, order, decimals, fmt, boolAsInt, enumFmt, skipIfNaN));
            }

            // sortowanie: najpierw po Order, potem po nazwie dla stabilności
            return list
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToArray();
        }

        public static string Header() => string.Join(",", _cols.Select(c => c.Name));

        public static string Row(RegimeAuditRecord r)
        {
            var values = new string[_cols.Length];
            for (int i = 0; i < _cols.Length; i++)
            {
                var c = _cols[i];
                var v = c.Prop.GetValue(r);
                values[i] = FormatValue(v, c);
            }
            // twardy sanity-check
            if (values.Length != _cols.Length)
                throw new InvalidOperationException($"Audit CSV column mismatch: {values.Length} vs {_cols.Length}");
            return string.Join(",", values);
        }

        public static string Export(IEnumerable<RegimeAuditRecord> rows)
        {
            var sb = new StringBuilder(1 << 16);
            sb.AppendLine(Header());
            foreach (var r in rows)
                sb.AppendLine(Row(r));
            return sb.ToString();
        }

        // ---------- FORMATERY ----------

        private static string FormatValue(object? value, ColMeta c)
        {
            if (value is null) return "";

            var t = c.Prop.PropertyType;
            var ut = Nullable.GetUnderlyingType(t) ?? t;

            if (ut == typeof(bool))
            {
                var b = (bool)value;
                return c.BoolAsInt ? (b ? "1" : "0") : (b ? "true" : "false");
            }

            if (ut.IsEnum)
            {
                return c.EnumFormat == AuditEnumFormat.Int
                    ? Convert.ToInt32(value, Inv).ToString(Inv)
                    : value.ToString() ?? "";
            }

            if (ut == typeof(DateTime))
            {
                var dt = (DateTime)value;
                // wymuś UTC → 'Z' jeśli to UTC
                var dtu = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return dtu.ToString(c.DateFormat ?? DEFAULT_DATE_FMT, Inv);
            }

            if (ut == typeof(double))
            {
                var d = (double)value;
                if (double.IsNaN(d) || double.IsInfinity(d)) return c.SkipIfNaN ? "" : d.ToString(Inv);
                var dec = c.Decimals >= 0 ? c.Decimals : DEFAULT_DOUBLE_DECIMALS;
                return Math.Round(d, dec).ToString(Inv);
            }

            if (ut == typeof(decimal))
            {
                var dec = (decimal)value;
                var places = c.Decimals >= 0 ? c.Decimals : DEFAULT_DOUBLE_DECIMALS;
                return Math.Round(dec, places).ToString(Inv);
            }

            if (ut == typeof(int) || ut == typeof(long) || ut == typeof(short) ||
                ut == typeof(uint) || ut == typeof(ulong) || ut == typeof(byte) || ut == typeof(sbyte))
            {
                return Convert.ToString(value, Inv) ?? "";
            }

            // string i reszta → CSV-escape
            return CsvEscape(value.ToString() ?? "");
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            // jeżeli zawiera przecinek, cudzysłów lub znak nowej linii → cytuj i zdubluj cudzysłowy
            bool needQuotes = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needQuotes) return s;
            var inner = s.Replace("\"", "\"\"");
            return $"\"{inner}\"";
        }
    }

    // ======== SINK (zapis do pliku) ========

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
                    File.AppendAllText(_path, RegimeAuditCsv.Header() + Environment.NewLine,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
        }

        private void AppendRow(RegimeAuditRecord r)
        {
            lock (_fileLock)
            {
                File.AppendAllText(_path, RegimeAuditCsv.Row(r) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }
}

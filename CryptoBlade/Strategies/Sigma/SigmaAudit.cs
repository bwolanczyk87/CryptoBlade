using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using CryptoBlade.Strategies.Sigma.Modes;

namespace CryptoBlade.Strategies.Sigma
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

    /// <summary>
    /// Label trybu w audycie (stabilne kody liczbowe 0..3 dla CSV).
    /// </summary>
    public enum ModeLabel
    {
        None = 0,
        Momentum = 1,
        MeanReversion = 2,
        Breakout = 3
    }

    public interface ISigmaAuditSink
    {
        void Add(SigmaAuditRecord r);
        IReadOnlyList<SigmaAuditRecord> Snapshot();
        void Clear();
    }

    /// <summary>
    /// Pojedynczy rekord audytu trybu Sigmy – spłaszczony snapshot:
    /// - meta (czas, symbol),
    /// - wybór trybu (Selected/Prev/Proposed),
    /// - parametry progów (MinScore, MinMargin, hysteresis),
    /// - pełne score'y (MM/MR/BO),
    /// - cechy z SigmaData + surowy snapshot rynku.
    /// </summary>
    public sealed class SigmaAuditRecord
    {
        // ========= META / TRYBY =========

        [AuditColumn(Order = 0, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime TimeUtc { get; init; }

        [AuditColumn(Order = 1)]
        public string Symbol { get; init; } = "";

        // Tryby: aktywny (Selected) vs poprzedni vs proponowany
        [AuditColumn(Order = 2, EnumFormat = AuditEnumFormat.Int)]
        public ModeLabel Selected { get; init; }      // Active (po histerezie/dwell)

        [AuditColumn(Order = 3, EnumFormat = AuditEnumFormat.Int)]
        public ModeLabel Prev { get; init; }          // Poprzedni Active

        [AuditColumn(Order = 4, EnumFormat = AuditEnumFormat.Int)]
        public ModeLabel? Oracle { get; set; }        // Opcjonalny label referencyjny (np. z datasetu)

        [AuditColumn(Order = 5, EnumFormat = AuditEnumFormat.Int)]
        public ModeLabel Proposed { get; init; }      // Argmax z bieżących score'ów

        // Wynik decyzji i progów
        [AuditColumn(Order = 6, BoolAsInt = true)]
        public bool Tradable { get; init; }           // Global gates OK/NOK

        [AuditColumn(Order = 7)]
        public string Reason { get; init; } = "";

        [AuditColumn(Order = 8, Decimals = 2)]
        public double MinScore { get; init; }

        [AuditColumn(Order = 9, Decimals = 2)]
        public double MinMargin { get; init; }

        [AuditColumn(Order = 10, Decimals = 0)]
        public double HysteresisLockMinutes { get; init; }

        // ========= SCORES =========

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

        // ========= CECHY – TREND / VALUE / VOL =========

        [AuditColumn(Order = 18, Decimals = 2)]
        public double Adx1h { get; init; }

        [AuditColumn(Order = 19, Decimals = 3)]
        public double AtrPct1h { get; init; }

        [AuditColumn(Order = 20, Decimals = 2)]
        public double Atr1hAbs { get; init; }

        [AuditColumn(Order = 21, Decimals = 3)]
        public double ZDvwap { get; init; }

        [AuditColumn(Order = 22, Decimals = 3)]
        public double ZDvwapPrev { get; init; }

        [AuditColumn(Order = 23, Decimals = 3)]
        public double ZSlopeDvwap { get; init; }

        [AuditColumn(Order = 24, Decimals = 4)]
        public double AutoCorr5m { get; init; }

        [AuditColumn(Order = 25, Decimals = 2)]
        public double Bbw15mPct { get; init; }

        [AuditColumn(Order = 26, Decimals = 6)]
        public double Bbw15mRaw { get; init; }

        [AuditColumn(Order = 27, BoolAsInt = true)]
        public bool Bbw15mExpanding { get; init; }

        // ========= MIKROSTRUKTURA / DERYWATY / FLOW =========

        [AuditColumn(Order = 28, Decimals = 4)]
        public double SpreadBps { get; init; }

        [AuditColumn(Order = 29, Decimals = 3)]
        public double OiDelta1hPct { get; init; }

        [AuditColumn(Order = 30, Decimals = 6)]
        public double DeltaCvd5m { get; init; }

        [AuditColumn(Order = 31, Decimals = 6)]
        public double DeltaCvdPrev5m { get; init; }

        [AuditColumn(Order = 32, BoolAsInt = true)]
        public bool CvdFlipUp5m { get; init; }

        [AuditColumn(Order = 33, BoolAsInt = true)]
        public bool CvdFlipDown5m { get; init; }

        [AuditColumn(Order = 34, Decimals = 5)]
        public double FundingPredictedPct { get; init; }

        [AuditColumn(Order = 35, Decimals = 5)]
        public double FundingLastSettledPct { get; init; }

        [AuditColumn(Order = 36, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime? NextFundingUtc { get; init; }

        [AuditColumn(Order = 37, Decimals = 4)]
        public double BasisPct { get; init; }

        [AuditColumn(Order = 38, Decimals = 2)]
        public double DistToLiqPct { get; init; }

        // ========= PATTERNY / STRUKTURA / OR / DONCHIAN =========

        [AuditColumn(Order = 39, BoolAsInt = true)]
        public bool HasInsideOrNr7 { get; init; }

        [AuditColumn(Order = 40, BoolAsInt = true)]
        public bool DonchianBreakUp { get; init; }

        [AuditColumn(Order = 41, BoolAsInt = true)]
        public bool DonchianBreakDown { get; init; }

        [AuditColumn(Order = 42, Decimals = 4)]
        public decimal? DonchianUpper15m { get; init; }

        [AuditColumn(Order = 43, Decimals = 4)]
        public decimal? DonchianLower15m { get; init; }

        [AuditColumn(Order = 44, Decimals = 2)]
        public decimal? OpeningRangeHigh { get; init; }

        [AuditColumn(Order = 45, Decimals = 2)]
        public decimal? OpeningRangeLow { get; init; }

        [AuditColumn(Order = 46, BoolAsInt = true)]
        public bool OrBreakoutRetestUp5m { get; init; }

        [AuditColumn(Order = 47, BoolAsInt = true)]
        public bool OrBreakoutRetestDown5m { get; init; }

        [AuditColumn(Order = 48, Decimals = 2)]
        public double OrRetestDepthBpsUp5m { get; init; }

        [AuditColumn(Order = 49, Decimals = 2)]
        public double OrRetestDepthBpsDown5m { get; init; }

        [AuditColumn(Order = 50, BoolAsInt = true)]
        public bool SweepReclaimUp5m { get; init; }

        [AuditColumn(Order = 51, BoolAsInt = true)]
        public bool SweepReclaimDown5m { get; init; }

        [AuditColumn(Order = 52, Decimals = 2)]
        public double SweepUpOvershootBps5m { get; init; }

        [AuditColumn(Order = 53, Decimals = 2)]
        public double SweepDownOvershootBps5m { get; init; }

        // ========= SUPERVISOR – BTC =========

        [AuditColumn(Order = 54, Decimals = 4)]
        public double CorrToBtc15m { get; init; }

        [AuditColumn(Order = 55, BoolAsInt = true)]
        public bool BtcBiasOpposite { get; init; }

        // ========= HISTEReZA (STAN TRYBU) =========

        [AuditColumn(Order = 56, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime SinceUtc { get; init; }         // od kiedy aktywny tryb

        [AuditColumn(Order = 57, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime LastDecisionUtc { get; init; }  // ostatni heartbeat decyzji trybu

        // ========= SUROWY SNAPSHOT TICKERA =========

        [AuditColumn(Order = 58, Decimals = 2)]
        public decimal? LastPrice { get; init; }

        [AuditColumn(Order = 59, Decimals = 2)]
        public decimal? BestBidPrice { get; init; }

        [AuditColumn(Order = 60, Decimals = 2)]
        public decimal? BestAskPrice { get; init; }

        [AuditColumn(Order = 61, Decimals = 2)]
        public decimal? MarkPrice { get; init; }

        [AuditColumn(Order = 62, Decimals = 2)]
        public decimal? IndexPrice { get; init; }

        // ========= OSTATNI BAR 1M =========

        [AuditColumn(Order = 63, DateFormat = "yyyy-MM-ddTHH:mm:ss.fffZ")]
        public DateTime? Last1mTimeUtc { get; init; }

        [AuditColumn(Order = 64, Decimals = 2)]
        public decimal? Last1mOpen { get; init; }

        [AuditColumn(Order = 65, Decimals = 2)]
        public decimal? Last1mHigh { get; init; }

        [AuditColumn(Order = 66, Decimals = 2)]
        public decimal? Last1mLow { get; init; }

        [AuditColumn(Order = 67, Decimals = 2)]
        public decimal? Last1mClose { get; init; }

        [AuditColumn(Order = 68, Decimals = 4)]
        public decimal? Last1mVolume { get; init; }

        [AuditColumn(Order = 69)]
        public int MmEntryTier { get; init; }

        [AuditColumn(Order = 70)]
        public int MrEntryTier { get; init; }

        [AuditColumn(Order = 71)]
        public int BoEntryTier { get; init; }

        [AuditColumn(Order = 72)]
        public bool MmLongCandidate { get; init; }

        [AuditColumn(Order = 73)]
        public bool MmShortCandidate { get; init; }

        [AuditColumn(Order = 74)]
        public bool MrLongCandidate { get; init; }

        [AuditColumn(Order = 75)]
        public bool MrShortCandidate { get; init; }

        [AuditColumn(Order = 76)]
        public bool BoLongCandidate { get; init; }

        [AuditColumn(Order = 77)]
        public bool BoShortCandidate { get; init; }

    }

    // ======== BUDOWANIE REKORDU (z ModeEngine.Classify) ========

    public static class SigmaAudit
    {
        private static ModeLabel ToLabel(Mode mode) => mode switch
        {
            Mode.MM => ModeLabel.Momentum,
            Mode.MR => ModeLabel.MeanReversion,
            Mode.BO => ModeLabel.Breakout,
            _ => ModeLabel.None
        };

        /// <summary>
        /// Buduje rekord audytu na podstawie:
        /// - SigmaData,
        /// - poprzedniego stanu ModeState,
        /// - bieżącej decyzji ModeDecision,
        /// - flagi tradable (global gates),
        /// - konfiguracji progów (SigmaStrategyOptions).
        /// </summary>
        public static SigmaAuditRecord MakeRecord(
            SigmaData data,
            ModeState prev,
            DateTime nowUtc,
            SigmaStrategyOptions options,
            bool tradable,
            string reason,
            ModeDecision decision,
            DateTime lastDecisionUtc)
        {
            var active = decision.State.Mode;
            var proposed = decision.ProposedMode;

            var mm = decision.State.Scores.Momentum;
            var mr = decision.State.Scores.MeanReversion;
            var bo = decision.State.Scores.Breakout;
            var activeScore = decision.State.Scores[active];

            return new SigmaAuditRecord
            {
                TimeUtc = nowUtc,
                Symbol = data.Symbol,

                Selected = ToLabel(active),
                Prev = ToLabel(prev.Mode),
                Oracle = null,
                Proposed = ToLabel(proposed),

                Tradable = tradable,
                Reason = reason ?? string.Empty,
                MinScore = (double)options.MinScore,
                MinMargin = (double)options.MinMargin,
                HysteresisLockMinutes = options.HysteresisLockMinutes,

                ScoreMM = mm,
                ScoreMR = mr,
                ScoreBO = bo,
                ProposedScore = decision.ProposedScore,
                SecondBestScore = decision.SecondBestScore,
                Margin = decision.Margin,
                ActiveScore = activeScore,

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

                OpeningRangeHigh = data.OpeningRangeHigh,
                OpeningRangeLow = data.OpeningRangeLow,

                OrBreakoutRetestUp5m = data.OrBreakoutRetestUp5m,
                OrBreakoutRetestDown5m = data.OrBreakoutRetestDown5m,
                OrRetestDepthBpsUp5m = data.OrRetestDepthBpsUp5m,
                OrRetestDepthBpsDown5m = data.OrRetestDepthBpsDown5m,

                SweepReclaimUp5m = data.SweepReclaimUp5m,
                SweepReclaimDown5m = data.SweepReclaimDown5m,
                SweepUpOvershootBps5m = data.SweepUpOvershootBps5m,
                SweepDownOvershootBps5m = data.SweepDownOvershootBps5m,

                CorrToBtc15m = data.CorrToBtc15m,
                BtcBiasOpposite = data.BtcBiasOpposite,

                SinceUtc = decision.State.SinceUtc,
                LastDecisionUtc = lastDecisionUtc,

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
                Last1mVolume = data.Last1mVolume,

                // --- Per-mode entry debug ---
                MmEntryTier = data.MomentumEntryTier,
                MrEntryTier = data.MeanReversionEntryTier,
                BoEntryTier = data.BreakoutEntryTier,

                MmLongCandidate = data.MomentumLongCandidate,
                MmShortCandidate = data.MomentumShortCandidate,
                MrLongCandidate = data.MeanReversionLongCandidate,
                MrShortCandidate = data.MeanReversionShortCandidate,
                BoLongCandidate = data.BreakoutLongCandidate,
                BoShortCandidate = data.BreakoutShortCandidate,
            };
        }
    }


    // ======== CSV (GENERYCZNE, REFLEKSYJNE, Z CACHINGIEM) ========

    public static class SigmaAuditCsv
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
            var t = typeof(SigmaAuditRecord);
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

        public static string Row(SigmaAuditRecord r)
        {
            var values = new string[_cols.Length];
            for (int i = 0; i < _cols.Length; i++)
            {
                var c = _cols[i];
                var v = c.Prop.GetValue(r);
                values[i] = FormatValue(v, c);
            }

            if (values.Length != _cols.Length)
                throw new InvalidOperationException($"Audit CSV column mismatch: {values.Length} vs {_cols.Length}");

            return string.Join(",", values);
        }

        public static string Export(IEnumerable<SigmaAuditRecord> rows)
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
                var dtu = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return dtu.ToString(c.DateFormat ?? DEFAULT_DATE_FMT, Inv);
            }

            if (ut == typeof(double))
            {
                var d = (double)value;
                if (double.IsNaN(d) || double.IsInfinity(d))
                    return c.SkipIfNaN ? "" : d.ToString(Inv);

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
            bool needQuotes = s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (!needQuotes) return s;
            var inner = s.Replace("\"", "\"\"");
            return $"\"{inner}\"";
        }
    }

    // ======== SINK (zapis do pliku) ========

    public sealed class SigmaAuditSink : ISigmaAuditSink
    {
        private readonly string _path;
        private readonly ConcurrentQueue<SigmaAuditRecord> _q = new();
        private static readonly object _fileLock = new();

        public SigmaAuditSink(string path)
        {
            _path = path;
            EnsureHeader();
        }

        public void Add(SigmaAuditRecord r)
        {
            _q.Enqueue(r);
            AppendRow(r);
        }

        public IReadOnlyList<SigmaAuditRecord> Snapshot()
        {
            var list = new List<SigmaAuditRecord>(_q.Count);
            foreach (var r in _q) list.Add(r);
            return list;
        }

        public void Clear()
        {
            while (_q.TryDequeue(out _)) { }
            // Pliku nie czyścimy (to log historyczny). Jeśli chcesz rotację — dorobimy osobno.
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
                    File.AppendAllText(
                        _path,
                        SigmaAuditCsv.Header() + Environment.NewLine,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
            }
        }

        private void AppendRow(SigmaAuditRecord r)
        {
            lock (_fileLock)
            {
                File.AppendAllText(
                    _path,
                    SigmaAuditCsv.Row(r) + Environment.NewLine,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
        }
    }
}

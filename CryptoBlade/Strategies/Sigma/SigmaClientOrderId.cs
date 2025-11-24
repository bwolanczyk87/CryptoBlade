using System;
using System.Globalization;
using CryptoBlade.Strategies.Sigma.Modes;

namespace CryptoBlade.Strategies.Sigma
{
    /// <summary>
    /// Typy zleceń Sigmy zakodowane w clientOrderId.
    /// </summary>
    public enum SigmaOrderKind
    {
        Unknown = 0,
        Entry = 1,
        StopLoss = 2,
        StopLossBreakEven = 3,
        TrailingStopLoss = 4,
        MRTimeStop = 5,
        TakeProfit1 = 6,
        TakeProfit2 = 7
    }

    /// <summary>
    /// Rozparsowany clientOrderId Sigmy.
    /// </summary>
    public sealed record ParsedSigmaClientOrderId(
        string Raw,
        string Symbol,
        ModeKind Mode,
        string DirectionTag,
        SigmaOrderKind Kind,
        DateTime? TimestampUtc);

    /// <summary>
    /// Jedno źródło prawdy dla formatu clientOrderId Sigmy:
    ///
    /// Format (bazowy):
    ///   SIGMA|{symbol}|{mode}|{directionTag}|{kind}|{timestamp}
    ///
    /// - prefix: "SIGMA"
    /// - separator: '|'
    /// - mode: enum Mode (MM/MR/BO/None)
    /// - directionTag: np. "LONG"/"SHORT"
    /// - kind: "ENTRY", "SL", "SLBE", "SLTRAIL", "MRTSTOP", "TP1", "TP2", ...
    /// - timestamp: domyślnie "yyyyMMddHHmmssfff" (17 cyfr),
    ///   ale parser obsługuje też formaty legacy:
    ///   "yyyyMMddHHmmss", "yyMMddHHmmss", "HHmmss".
    ///
    /// MaxLength domyślnie 45 (jak w przykładowych logach).
    /// </summary>
    public static class SigmaClientOrderId
    {
        public const string ClientIdPrefix = "SIG";
        public const char ClientIdSeparator = '|';
        public const int MaxLength = 45;

        // Obsługiwane formaty timesta mpu (dla kompatybilności wstecznej)
        private static readonly string[] TimestampFormats =
        {
            "yyyyMMddHHmmssfff",
            "yyyyMMddHHmmss",
            "yyMMddHHmmss",
            "HHmmss"
        };

        /// <summary>
        /// Buduje clientOrderId w spójnym formacie Sigmy.
        /// </summary>
        public static string Build(
            string symbol,
            ModeKind mode,
            string directionTag,
            string kind,
            DateTime nowUtc)
        {
            if (symbol is null) throw new ArgumentNullException(nameof(symbol));
            if (directionTag is null) throw new ArgumentNullException(nameof(directionTag));
            if (kind is null) throw new ArgumentNullException(nameof(kind));

            // Wymuszamy UTC
            if (nowUtc.Kind == DateTimeKind.Unspecified)
                nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            else
                nowUtc = nowUtc.ToUniversalTime();

            var dirTagNorm = directionTag.ToUpperInvariant();
            var modeCode = mode.ToString(); // "MM", "MR", "BO", "None"

            // Bazowy timestamp z milisekundami (zgodny z logami typu 20251123171909460)
            var ts = nowUtc.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);

            string Compose(string sym, string m, string dir, string k, string tsPart)
                => string.Join(ClientIdSeparator, ClientIdPrefix, sym, m, dir, k, tsPart);

            var id = Compose(symbol, modeCode, dirTagNorm, kind, ts);

            if (id.Length <= MaxLength)
                return id;

            // 1) Jeśli za długie – obcinamy milisekundy w timestamp
            ts = nowUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            id = Compose(symbol, modeCode, dirTagNorm, kind, ts);
            if (id.Length <= MaxLength)
                return id;

            // 2) Jeśli nadal za długie – skracamy symbol od prawej
            var symTrim = symbol;
            var extra = id.Length - MaxLength;
            if (symTrim.Length > extra)
            {
                symTrim = symTrim[..(symTrim.Length - extra)];
                id = Compose(symTrim, modeCode, dirTagNorm, kind, ts);
            }

            // 3) Ostateczny bezpiecznik – przycinamy do MaxLength
            if (id.Length > MaxLength)
                id = id[..MaxLength];

            return id;
        }

        /// <summary>
        /// Szybki check, czy dany clientOrderId wygląda na Sigmowy.
        /// </summary>
        public static bool IsSigmaOrderId(string? clientOrderId)
        {
            if (string.IsNullOrWhiteSpace(clientOrderId))
                return false;

            return clientOrderId.StartsWith(
                ClientIdPrefix + ClientIdSeparator,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Pełny parsing clientOrderId Sigmy.
        /// </summary>
        public static bool TryParse(
            string clientOrderId,
            out ParsedSigmaClientOrderId parsed)
        {
            parsed = default!;

            if (string.IsNullOrWhiteSpace(clientOrderId))
                return false;

            if (!IsSigmaOrderId(clientOrderId))
                return false;

            var parts = clientOrderId.Split(ClientIdSeparator);
            // prefix | symbol | mode | directionTag | kind | [timestamp]
            if (parts.Length < 5)
                return false;

            if (!string.Equals(parts[0], ClientIdPrefix, StringComparison.Ordinal))
                return false;

            var symbol = parts[1];

            if (!Enum.TryParse(parts[2], ignoreCase: true, out ModeKind mode))
                mode = ModeKind.None;

            var directionTag = parts[3];
            var kindToken = parts[4];

            var kind = kindToken switch
            {
                "ENTRY" => SigmaOrderKind.Entry,
                "SL" => SigmaOrderKind.StopLoss,
                "SLBE" => SigmaOrderKind.StopLoss,
                "TSL" => SigmaOrderKind.TrailingStopLoss,
                "MRTS" => SigmaOrderKind.MRTimeStop,
                "TP1" => SigmaOrderKind.TakeProfit1,
                "TP2" => SigmaOrderKind.TakeProfit2,
                _ => SigmaOrderKind.Unknown
            };

            DateTime? ts = null;
            if (parts.Length >= 6)
            {
                var tsToken = parts[5];

                foreach (var fmt in TimestampFormats)
                {
                    if (DateTime.TryParseExact(
                            tsToken,
                            fmt,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                            out var dt))
                    {
                        ts = dt;
                        break;
                    }
                }
            }

            parsed = new ParsedSigmaClientOrderId(
                clientOrderId,
                symbol,
                mode,
                directionTag,
                kind,
                ts);

            return true;
        }

        public static SigmaOrderKind TryParseKind(string clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed)
                ? parsed.Kind
                : SigmaOrderKind.Unknown;
        }

        public static ModeKind TryParseMode(string clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed)
                ? parsed.Mode
                : ModeKind.None;
        }

        public static string? TryGetSymbol(string clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed) ? parsed.Symbol : null;
        }

        public static string? TryGetDirectionTag(string clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed) ? parsed.DirectionTag : null;
        }
    }
}

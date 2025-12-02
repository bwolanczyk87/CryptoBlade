using System.Globalization;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Modes;

namespace CryptoBlade.Strategies.Sigma
{
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

    public sealed record ParsedSigmaClientOrderId(
        string Raw,
        string Symbol,
        ModeKind Mode,
        OrderSide Side,
        SigmaOrderKind Kind,
        DateTime? TimestampUtc);

    public static class SigmaClientOrderId
    {
        public const string ClientIdPrefix = "SIG";
        public const char ClientIdSeparator = '|';
        public const int MaxLength = 45;

        private const string TimestampFormat = "yyyyMMddHHmmss";

        public static string Build(string symbol, ModeKind mode, OrderSide side, SigmaOrderKind kind, DateTime nowUtc)
        {
            // Wymuszamy UTC
            if (nowUtc.Kind == DateTimeKind.Unspecified)
                nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
            else
                nowUtc = nowUtc.ToUniversalTime();

            var dirToken = DirectionToToken(side);
            var modeCode = mode.ToString();
            var kindToken = KindToToken(kind);
            var ts = nowUtc.ToString(TimestampFormat, CultureInfo.InvariantCulture);

            static string Compose(string sym, string m, string dir, string k, string tsPart)
                => string.Join(ClientIdSeparator, ClientIdPrefix, sym, m, dir, k, tsPart);

            var id = Compose(symbol, modeCode, dirToken, kindToken, ts);

            if (id.Length <= MaxLength)
                return id;

            // Jeśli za długie – skracamy symbol od prawej
            var symTrim = symbol;
            var extra = id.Length - MaxLength;
            if (symTrim.Length > extra)
            {
                symTrim = symTrim[..(symTrim.Length - extra)];
                id = Compose(symTrim, modeCode, dirToken, kindToken, ts);
            }

            // Ostateczny bezpiecznik – przycinamy do MaxLength
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

        private static bool TryParse(
            string? clientOrderId,
            out ParsedSigmaClientOrderId parsed)
        {
            parsed = default!;

            if (string.IsNullOrWhiteSpace(clientOrderId))
                return false;

            if (!IsSigmaOrderId(clientOrderId))
                return false;

            // prefix | symbol | mode | directionTag | kind | [timestamp]
            var parts = clientOrderId.Split(ClientIdSeparator);

            if (parts.Length < 5)
                return false;

            if (!string.Equals(parts[0], ClientIdPrefix, StringComparison.Ordinal))
                return false;

            var symbol = parts[1];

            if (!Enum.TryParse(parts[2], ignoreCase: true, out ModeKind mode))
                mode = ModeKind.None;

            var directionTag = parts[3];
            var kindToken = parts[4];

            var side = ParseSide(directionTag);
            var kind = ParseKind(kindToken);

            DateTime? ts = null;
            if (parts.Length >= 6)
            {
                var tsToken = parts[5];

                if (DateTime.TryParseExact(
                        tsToken,
                        TimestampFormat,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    ts = dt;
                }
            }

            parsed = new ParsedSigmaClientOrderId(
                clientOrderId,
                symbol,
                mode,
                side,
                kind,
                ts);

            return true;
        }

        public static SigmaOrderKind TryParseKind(string? clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed)
                ? parsed.Kind
                : SigmaOrderKind.Unknown;
        }

        public static ModeKind TryParseMode(string? clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed)
                ? parsed.Mode
                : ModeKind.None;
        }

        public static string? TryGetSymbol(string clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed) ? parsed.Symbol : null;
        }

        public static string? TryGetDirectionTag(string? clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed)
                ? DirectionToToken(parsed.Side)
                : null;
        }

        public static OrderSide? TryGetSide(string? clientOrderId)
        {
            return TryParse(clientOrderId, out var parsed)
                ? parsed.Side
                : null;
        }

        private static string DirectionToToken(OrderSide side)
            => side switch
            {
                OrderSide.Buy => "LONG",
                OrderSide.Sell => "SHORT",
                _ => "UNK"
            };

        private static OrderSide ParseSide(string token)
        {
            if (token.Equals("LONG", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("BUY", StringComparison.OrdinalIgnoreCase))
                return OrderSide.Buy;

            if (token.Equals("SHORT", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("SELL", StringComparison.OrdinalIgnoreCase))
                return OrderSide.Sell;

            // nieznany – zostawiamy default(0)
            return default;
        }

        private static string KindToToken(SigmaOrderKind kind)
            => kind switch
            {
                SigmaOrderKind.Entry => "ENTRY",
                SigmaOrderKind.StopLoss => "SL",
                SigmaOrderKind.StopLossBreakEven => "SLBE",
                SigmaOrderKind.TrailingStopLoss => "TSL",
                SigmaOrderKind.MRTimeStop => "MRTS",
                SigmaOrderKind.TakeProfit1 => "TP1",
                SigmaOrderKind.TakeProfit2 => "TP2",
                _ => "UNK"
            };

        private static SigmaOrderKind ParseKind(string token)
            => token switch
            {
                "ENTRY" => SigmaOrderKind.Entry,
                "SL" => SigmaOrderKind.StopLoss,
                "SLBE" => SigmaOrderKind.StopLossBreakEven,
                "TSL" => SigmaOrderKind.TrailingStopLoss,
                "MRTS" => SigmaOrderKind.MRTimeStop,
                "TP1" => SigmaOrderKind.TakeProfit1,
                "TP2" => SigmaOrderKind.TakeProfit2,
                _ => SigmaOrderKind.Unknown
            };
    }
}

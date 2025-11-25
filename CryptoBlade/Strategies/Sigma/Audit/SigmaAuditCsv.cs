using System.Globalization;
using System.Reflection;
using System.Text;

namespace CryptoBlade.Strategies.Sigma.Audit
{
    public static class SigmaAuditCsv
    {
        public enum AuditEnumFormat { Int = 0, String = 1 }

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class AuditIgnoreAttribute : Attribute { }

        [AttributeUsage(AttributeTargets.Property)]
        public sealed class AuditColumnAttribute : Attribute
        {
            public int Order { get; init; } = int.MaxValue;
            public string? Name { get; init; }
            public int Decimals { get; init; } = -1;
            public string? DateFormat { get; init; }
            public bool BoolAsInt { get; init; } = true;
            public AuditEnumFormat EnumFormat { get; init; } = AuditEnumFormat.Int;
            public bool SkipIfNaN { get; init; } = true;
        }

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
        private const int DEFAULT_DOUBLE_DECIMALS = 6;
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

            return [.. list
                .OrderBy(c => c.Order)
                .ThenBy(c => c.Name, StringComparer.Ordinal)];
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


        private static string FormatValue(object? value, ColMeta c)
        {
            if (value is null) return "";

            var t = c.Prop.PropertyType;
            var ut = Nullable.GetUnderlyingType(t) ?? t;

            if (ut == typeof(bool))
            {
                var b = (bool)value;
                return c.BoolAsInt ? b ? "1" : "0" : b ? "true" : "false";
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
}

using Skender.Stock.Indicators;
using System.Globalization;
using System.Reflection;
using System.Collections;

namespace CryptoBlade.Strategies.AI
{
    /// <summary>
    /// Reflection‑based bridge between AI requests and Skender.Stock.Indicators 2.6.1.
    /// </summary>
    public static class IndicatorEngine
    {
        /* ------------------------------------------------------------------ */
        /*  COMPUTE                                                           */
        /* ------------------------------------------------------------------ */

        public static object Compute(IndicatorRequest req, IEnumerable<Quote> quotes)
        {
            // locate matching GetXxx method (generic → close over <Quote>)
            MethodInfo? m = typeof(Indicator).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(mi => mi.Name == "Get" + req.Name && FirstArgIsQuoteEnumerable(mi));

            if (m is null)
                throw new InvalidOperationException($"Indicator {req.Name} not found in Skender.Stock.Indicators");

            if (m.IsGenericMethodDefinition)
                m = m.MakeGenericMethod(typeof(Quote));

            var raw = m.Invoke(null, new object[] { quotes }.Concat(req.Params.Cast<object>()).ToArray());

            // if the method returns IEnumerable ― grab last element (current bar)
            if (raw is IEnumerable seq && raw is not string)
            {
                object? last = null;
                foreach (var x in seq) last = x;
                return last ?? raw;
            }
            return raw;          // single value
        }

        private static bool FirstArgIsQuoteEnumerable(MethodInfo mi)
        {
            var p = mi.GetParameters();
            if (p.Length == 0) return false;
            var t = p[0].ParameterType;
            return t.IsGenericType &&
                   t.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                   typeof(IQuote).IsAssignableFrom(t.GetGenericArguments()[0]);
        }

        /* ------------------------------------------------------------------ */
        /*  FORMAT                                                            */
        /* ------------------------------------------------------------------ */

        /// <summary>
        /// Serialises indicator output to a compact string using dot as decimal separator.
        /// Numbers are rounded to <paramref name="scale"/> decimal places (price scale).
        /// </summary>
        public static string Format(object result, int scale = 2)
        {
            if (result is null) return "null";

            string ToStr(object v) => Convert.ToDecimal(v)
                .ToString($"F{scale}", CultureInfo.InvariantCulture);

            // simple numeric value
            if (result is IConvertible && result is not string)
                return ToStr(result);

            // composite result → concatenate all numeric public props
            var vals = result.GetType()
                              .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                              .Select(p => p.GetValue(result))
                              .Where(v => v is IConvertible
                                  && v is not string
                                  && v is not DateTime
                                  && v is not null)
                              .Select(v => ToStr(v!));

            return string.Join(',', vals);
        }
    }
}

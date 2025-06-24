using Skender.Stock.Indicators;
using System.Globalization;
using System.Reflection;
using System.Collections;

namespace CryptoBlade.Strategies.AI
{
    public static class IndicatorEngine
    {
        private static object? GetDefault(Type t) => t.IsValueType ? Activator.CreateInstance(t) : null;

        public static object Compute(IndicatorRequest req, IEnumerable<Quote> quotes)
        {
            try
            {
                var mi = typeof(Indicator).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "Get" + req.Name && FirstArgIsQuoteEnumerable(m))
                    ?? throw new InvalidOperationException($"Indicator '{req.Name}' not found");

                if (mi.IsGenericMethodDefinition)
                    mi = mi.MakeGenericMethod(typeof(Quote));

                var paramInfos = mi.GetParameters();
                var args = new List<object?> { quotes };

                int supplied = 0;
                for (int i = 1; i < paramInfos.Length; i++)
                {
                    if (supplied < req.Params.Length)
                    {
                        args.Add(req.Params[supplied++]);
                    }
                    else
                    {
                        args.Add(paramInfos[i].HasDefaultValue
                                 ? paramInfos[i].DefaultValue
                                 : GetDefault(paramInfos[i].ParameterType));
                    }
                }
                var raw = mi.Invoke(null, [.. args]);

                if (raw is IEnumerable seq && raw is not string)
                {
                    object? last = null;
                    foreach (var x in seq) last = x;
                    return last ?? raw;
                }
                return raw;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw new InvalidOperationException(
                    $"Runtime error while computing '{req.Name}': {ex.InnerException!.Message}", ex.InnerException);
            }
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
            if (result == null) return "N/A";

            string ToStr(object v)
                => Convert.ToDecimal(v, CultureInfo.InvariantCulture)
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

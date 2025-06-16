using Skender.Stock.Indicators;
using System.Globalization;
using System.Reflection;

namespace CryptoBlade.Strategies.AI
{
    public static class IndicatorEngine
    {
        public static object Compute(IndicatorRequest req, IEnumerable<Quote> quotes)
        {
            var method = typeof(Indicator).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Get" + req.Name &&
                                     m.GetParameters()[0].ParameterType == typeof(IEnumerable<Quote>));

            if (method is null)
                throw new InvalidOperationException($"Indicator {req.Name} not found");

            // budujemy listę argumentów: quotes + paramy (boxujemy do object)
            var args = new object[] { quotes }.Concat(req.Params.Cast<object>()).ToArray();
            return method.Invoke(null, args);
        }

        public static string Format(object result)
        {
            if (result is null) return "null";

            // liczba prosta
            if (result is IConvertible && result is not string)
                return Convert.ToDecimal(result)
                             .ToString("G", CultureInfo.InvariantCulture);

            // klasa z publicznymi właściwościami liczbowymi
            var numericVals = result.GetType()
                                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                    .Where(p => Type.GetTypeCode(p.PropertyType) switch
                                    {
                                        TypeCode.Decimal or TypeCode.Double or TypeCode.Single
                            or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64
                            or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 => true,
                                        _ => false
                                    })
                                    .Select(p => p.GetValue(result))
                                    .Where(v => v != null)
                                    .Select(v => Convert.ToDecimal(v)
                                           .ToString("G", CultureInfo.InvariantCulture));

            return string.Join(",", numericVals);
        }
    }
}

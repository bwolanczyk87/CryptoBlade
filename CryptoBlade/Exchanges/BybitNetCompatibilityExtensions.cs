using Bybit.Net.Enums;
namespace CryptoBlade.Exchanges
{
    internal static class BybitNetCompatibilityExtensions
    {
        // Compatibility overload for older call sites:
        // GetClosedProfitLossAsync(category, symbol, start, end, settleAsset, cursor, ct)
        public static dynamic GetClosedProfitLossAsync(
            this object api,
            Category category,
            string? symbol,
            DateTime? startTime,
            DateTime? endTime,
            string? settleAsset,
            string? cursor,
            CancellationToken ct = default)
        {
            return ((dynamic)api).GetClosedProfitLossAsync(
                category,
                symbol,
                startTime,
                endTime,
                settleAsset,
                null,
                cursor,
                ct);
        }

        // Compatibility overload for older call sites:
        // GetClosedProfitLossAsync(category, symbol, start, end, cursor, ct)
        public static dynamic GetClosedProfitLossAsync(
            this object api,
            Category category,
            string? symbol,
            DateTime? startTime,
            DateTime? endTime,
            string? cursor,
            CancellationToken ct = default)
        {
            return ((dynamic)api).GetClosedProfitLossAsync(
                category,
                symbol,
                startTime,
                endTime,
                null,
                null,
                cursor,
                ct);
        }
    }
}

using Bybit.Net.Enums;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Wallet;

namespace CryptoBlade.Exchanges
{
    public interface IBybitCBRestClient
    {
        Task<BybitCBRestClient.OrderActionResult> AmendOrderAsync(string symbol, string? orderId = null, string? clientOrderId = null, decimal? price = null, decimal? qty = null, decimal? takeProfit = null, decimal? stopLoss = null, TriggerType? tpTriggerBy = null, TriggerType? slTriggerBy = null, StopLossTakeProfitMode? tpslMode = null, decimal? tpLimitPrice = null, decimal? slLimitPrice = null, CancellationToken cancel = default);
        Task<BybitCBRestClient.CancelAllResult> CancelAllOrdersAndCountAsync(string? symbol = null, CancellationToken cancel = default);
        Task<BybitCBRestClient.OrderActionResult> CancelOrderAsync(string symbol, string orderId, CancellationToken cancel = default);
        Task<Balance> GetBalancesAsync(CancellationToken cancel = default);
        Task<object?> GetClosedPnlRawAsync(string? symbol = null, DateTime? start = null, DateTime? end = null, string? cursor = null, CancellationToken cancel = default);
        Task<object?> GetFeeRatesRawAsync(string? symbol = null, CancellationToken cancel = default);
        Task<FundingRate[]> GetFundingRatesAsync(string symbol, DateTime start, DateTime end, CancellationToken cancel = default);
        Task<Candle[]> GetKlinesAsync(string symbol, TimeFrame interval, DateTime start, DateTime end, CancellationToken cancel = default);
        Task<Candle[]> GetKlinesAsync(string symbol, TimeFrame interval, int limit, CancellationToken cancel = default);
        Task<Candle[]> GetKlinesClosedAsync(string symbol, TimeFrame interval, int limit, CancellationToken cancel = default);
        Task<object?> GetOrderHistoryRawAsync(string? symbol = null, string? cursor = null, CancellationToken cancel = default);
        Task<Order[]> GetOrdersAsync(CancellationToken cancel = default);
        Task<Position[]> GetPositionsAsync(CancellationToken cancel = default);
        Task<SymbolInfo[]> GetSymbolInfoAsync(CancellationToken cancel = default);
        Task<decimal?> GetSymbolVolatility(string symbol, CancellationToken cancel = default);
        Task<decimal?> GetSymbolVolumeAsync(string symbol, CancellationToken cancel = default);
        Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancel = default);
        Task<object?> GetTransactionLogRawAsync(AccountType accountType = AccountType.Unified, DateTime? start = null, DateTime? end = null, string? cursor = null, CancellationToken cancel = default);
        Task<BybitCBRestClient.OrderActionResult> PlaceLongTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force, CancellationToken cancel = default);
        Task<BybitCBRestClient.OrderActionResult> PlaceOrderAsync(string symbol, Bybit.Net.Enums.OrderSide side, NewOrderType orderType, decimal qty, decimal? price = null, PositionIdx? positionIdx = null, TimeInForce? timeInForce = null, bool reduceOnly = false, bool closeOnTrigger = false, decimal? triggerPrice = null, TriggerType? triggerBy = null, TriggerDirection? triggerDirection = null, decimal? takeProfit = null, decimal? stopLoss = null, TriggerType? tpTriggerBy = TriggerType.MarkPrice, TriggerType? slTriggerBy = TriggerType.MarkPrice, StopLossTakeProfitMode? tpslMode = null, OrderType? tpOrderType = null, OrderType? slOrderType = null, decimal? tpLimitPrice = null, decimal? slLimitPrice = null, string? clientOrderId = null, CancellationToken cancel = default);
        Task<BybitCBRestClient.OrderActionResult> PlaceShortTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force, CancellationToken cancel = default);
        Task<bool> SetLeverageAsync(SymbolInfo symbol, CancellationToken cancel = default);
        Task<BybitCBRestClient.OperationResult> SetTradingStopAsync(string symbol, decimal priceScale, decimal stopLoss, decimal? takeProfit, decimal? trailingStop, PositionIdx positionIdx, decimal? activePrice = null, decimal? takeProfitQuantity = null, decimal? stopLossQuantity = null, StopLossTakeProfitMode? stopLossTakeProfitMode = StopLossTakeProfitMode.Full, OrderType? tpOrderType = null, OrderType? slOrderType = null, decimal? tpLimitPrice = null, decimal? slLimitPrice = null, CancellationToken cancel = default);
        Task<bool> SwitchPositionModeAsync(Models.PositionMode mode, string symbol, CancellationToken cancel = default);
    }
}
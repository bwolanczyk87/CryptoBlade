using CryptoBlade.Models;
using CryptoBlade.Strategies.Wallet;

namespace CryptoBlade.Exchanges.Interfaces
{
    public interface IFuturesSocketClient
    {
        Task<IUpdateSubscription> SubscribeToWalletUpdatesAsync(Action<Balance> handler,
            CancellationToken cancel = default);

        Task<IUpdateSubscription> SubscribeToOrderUpdatesAsync(Action<OrderUpdate> handler,
            CancellationToken cancel = default);

        Task<IUpdateSubscription> SubscribeToKlineUpdatesAsync(string[] symbols,
            TimeFrame timeFrame,
            Action<string, Candle> handler,
            CancellationToken cancel = default);

        Task<IUpdateSubscription> SubscribeToTickerUpdatesAsync(string[] symbols,
            Action<string, Ticker> handler,
            CancellationToken cancel = default);

        Task<IUpdateSubscription> SubscribeToAllLiquidationUpdatesAsync(string[] symbols, 
            Action<string, LiquidationEvent> handler, 
            CancellationToken cancel = default);

        Task<IUpdateSubscription> SubscribeToPublicTradeUpdatesAsync(string[] symbols, 
            Action<string, PublicTrade> handler, 
            CancellationToken cancel = default);

        Task<IUpdateSubscription> SubscribeToOrderBookUpdatesAsync(string[] symbols, 
            Action<string, OrderBook> handler, 
            CancellationToken cancel = default);
    }
}
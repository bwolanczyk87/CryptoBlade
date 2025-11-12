using Bybit.Net.Enums;
using Bybit.Net.Interfaces.Clients;
using Bybit.Net.Objects.Models.V5;
using CryptoBlade.Configuration;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Policies;
using Microsoft.Extensions.Options;
using System;

namespace CryptoBlade.Exchanges
{
    public class BybitCbFuturesSocketClient : ICbFuturesSocketClient
    {
        private readonly IOptions<TradingBotOptions> m_options;
        private readonly IBybitSocketClient m_bybitSocketLinearClient;
        private readonly IBybitSocketClient m_bybitSocketClient;

        public BybitCbFuturesSocketClient(IBybitSocketClient bybitSocketClient, IBybitSocketClient? bybitSocketLinearClient, IOptions<TradingBotOptions> options)
        {
            m_bybitSocketClient = bybitSocketClient;
            m_bybitSocketLinearClient = bybitSocketLinearClient ?? bybitSocketClient;
            m_options = options;
        }

        public async Task<IUpdateSubscription> SubscribeToWalletUpdatesAsync(Action<Strategies.Wallet.Balance> handler, CancellationToken cancel = default)
        {
            var subscription = await ExchangePolicies.RetryForever
                .ExecuteAsync(async () =>
                {
                    var subscriptionResult = await m_bybitSocketClient.V5PrivateApi
                        .SubscribeToWalletUpdatesAsync(walletUpdateEvent =>
                    {
                        foreach (BybitBalance bybitBalance in walletUpdateEvent.Data)
                        {
                            if (bybitBalance.AccountType == AccountType.Unified)
                            {
                                var asset = bybitBalance.Assets.FirstOrDefault(x => string.Equals(x.Asset, m_options.Value.QuoteAsset, StringComparison.OrdinalIgnoreCase));
                                if (asset != null)
                                {
                                    var contractBalance = asset.ToBalance();
                                    handler(contractBalance);
                                }
                            }
                        }
                    }, cancel);
                    if (subscriptionResult.GetResultOrError(out var data, out var error))
                        return data;
                    throw new InvalidOperationException(error.Message);
                });
            return new BybitUpdateSubscription(subscription);
        }

        public async Task<IUpdateSubscription> SubscribeToOrderUpdatesAsync(Action<OrderUpdate> handler, CancellationToken cancel = default)
        {
            var orderUpdateSubscription = await ExchangePolicies.RetryForever
                .ExecuteAsync(async () =>
                {
                    var subscriptionResult = await m_bybitSocketClient.V5PrivateApi.SubscribeToOrderUpdatesAsync(
                        orderUpdateEvent =>
                        {
                            foreach (BybitOrderUpdate bybitOrderUpdate in orderUpdateEvent.Data)
                            {
                                if (bybitOrderUpdate.Category != Category.Linear)
                                    continue;
                                var orderUpdate = bybitOrderUpdate.ToOrderUpdate();
                                handler(orderUpdate);
                            }
                        }, cancel);
                    if (subscriptionResult.GetResultOrError(out var data, out var error))
                        return data;
                    throw new InvalidOperationException(error.Message);
                });

            return new BybitUpdateSubscription(orderUpdateSubscription);
        }

        public async Task<IUpdateSubscription> SubscribeToKlineUpdatesAsync(string[] symbols, TimeFrame timeFrame, Action<string, Candle> handler,
            CancellationToken cancel = default)
        {
            var klineUpdatesSubscription = await ExchangePolicies.RetryForever
                .ExecuteAsync(async () =>
                {
                    var subscriptionResult = await m_bybitSocketLinearClient.V5LinearApi.SubscribeToKlineUpdatesAsync(
                        symbols,
                        timeFrame.ToKlineInterval(),
                        klineUpdateEvent =>
                        {
                            string? symbol = klineUpdateEvent.Symbol;
                            foreach (BybitKlineUpdate bybitKlineUpdate in klineUpdateEvent.Data)
                            {
                                if (!bybitKlineUpdate.Confirm)
                                    continue;
                                var candle = bybitKlineUpdate.ToCandle();
                                handler(symbol ?? string.Empty, candle);
                            }
                        },
                        cancel);
                    if (subscriptionResult.GetResultOrError(out var data, out var error))
                        return data;
                    throw new InvalidOperationException(error.Message);
                });

            return new BybitUpdateSubscription(klineUpdatesSubscription);
        }

        public async Task<IUpdateSubscription> SubscribeToTickerUpdatesAsync(string[] symbols, Action<string, Models.Ticker> handler, CancellationToken cancel = default)
        {
            var tickerSubscription = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var tickerSubscriptionResult = await m_bybitSocketLinearClient.V5LinearApi
                    .SubscribeToTickerUpdatesAsync(symbols,
                        tickerUpdateEvent =>
                        {
                            var ticker = tickerUpdateEvent.Data.ToTicker();
                            if (ticker == null)
                                return;
                            handler(tickerUpdateEvent.Data.Symbol, ticker);
                        }, 
                        cancel);
                if (tickerSubscriptionResult.GetResultOrError(out var data, out var error))
                    return data;
                throw new InvalidOperationException(error.Message);
            });

            return new BybitUpdateSubscription(tickerSubscription);
        }

        public async Task<IUpdateSubscription> SubscribeToAllLiquidationUpdatesAsync(
            string[] symbols,
            Action<string, LiquidationEvent> handler,
            CancellationToken cancel = default)
        {
            var sub = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var res = await m_bybitSocketLinearClient.V5LinearApi
                    .SubscribeToAllLiquidationUpdatesAsync(
                        symbols,
                        evt =>
                        {
                            // evt.Symbol, evt.Data (lista)
                            var symbol = evt.Symbol ?? string.Empty;
                            foreach (var x in evt.Data)
                            {
                                var liq = new LiquidationEvent
                                {
                                    Timestamp = x.UpdateTime,
                                    Price = x.Price,
                                    Quantity = x.Quantity,
                                    Side = x.Side.ToOrderSide()
                                };
                                handler(symbol, liq);
                            }
                        },
                        cancel);
                if (res.GetResultOrError(out var data, out var error)) return data;
                throw new InvalidOperationException(error.Message);
            });

            return new BybitUpdateSubscription(sub);
        }

        public async Task<IUpdateSubscription> SubscribeToPublicTradeUpdatesAsync(
            string[] symbols,
            Action<string, PublicTrade> handler,
            CancellationToken cancel = default)
        {
            var sub = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var res = await m_bybitSocketLinearClient.V5LinearApi
                    .SubscribeToTradeUpdatesAsync(
                        symbols,
                        evt =>
                        {
                            var symbol = evt.Symbol ?? string.Empty;
                            foreach (var t in evt.Data)
                            {
                                var trade = new PublicTrade
                                {
                                    Timestamp = t.Timestamp,
                                    Price = t.Price,
                                    Quantity = t.Quantity,
                                    Side = t.Side.ToOrderSide()
                                };
                                handler(symbol, trade);
                            }
                        },
                        cancel);
                if (res.GetResultOrError(out var data, out var error)) return data;
                throw new InvalidOperationException(error.Message);
            });

            return new BybitUpdateSubscription(sub);
        }

        // Top of book (bid/ask) pod spread bps:
        public async Task<IUpdateSubscription> SubscribeToOrderBookTopUpdatesAsync(
            string[] symbols,
            Action<string, decimal, decimal> handler,
            CancellationToken cancel = default)
        {
            var sub = await ExchangePolicies.RetryForever.ExecuteAsync(async () =>
            {
                var res = await m_bybitSocketLinearClient.V5LinearApi
                    .SubscribeToOrderbookUpdatesAsync(
                        symbols,
                        depth: 1, // tylko top
                        evt =>
                        {
                            var symbol = evt.Symbol ?? string.Empty;
                            var ob = evt.Data; // <— pojedynczy obiekt

                            if (ob?.Bids?.Count() > 0 && ob.Asks?.Count() > 0)
                            {
                                var bestBid = ob.Bids.First().Price;
                                var bestAsk = ob.Asks.First().Price;
                                handler(symbol, bestBid, bestAsk);
                            }
                        },
                        cancel);

                if (res.GetResultOrError(out var data, out var error)) return data;
                throw new InvalidOperationException(error.Message);
            });

            return new BybitUpdateSubscription(sub);
        }
    }
}
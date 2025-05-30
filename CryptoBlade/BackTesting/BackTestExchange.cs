﻿using System.Text.Json;
using CryptoBlade.Exchanges;
using CryptoBlade.Helpers;
using CryptoBlade.Mapping;
using CryptoBlade.Models;
using CryptoBlade.Strategies.Symbols;
using CryptoBlade.Strategies.Wallet;
using CryptoExchange.Net.Interfaces;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;

namespace CryptoBlade.BackTesting
{
    public class BackTestExchange : ICbFuturesRestClient, ICbFuturesSocketClient, IBackTestRunner
    {
        private readonly IOptions<BackTestExchangeOptions> m_options;
        private readonly IBackTestDataDownloader m_backTestDataDownloader;
        private readonly IHistoricalDataStorage m_historicalDataStorage;
        private readonly ITradingSymbolsManager m_tradingSymbolsManager;
        private DateTime m_currentTime;
        private DateTime? m_nextTime;
        private readonly Dictionary<string, BackTestDataProcessor> m_candleProcessors;
        private readonly AsyncLock m_lock;
        private readonly HashSet<CandleUpdateSubscription> m_candleSubscriptions;
        private readonly HashSet<TickerUpdateSubscription> m_tickerSubscriptions;
        private readonly HashSet<BalanceUpdateSubscription> m_balanceSubscriptions;
        private readonly HashSet<OrderUpdateSubscription> m_orderSubscriptions;
        private readonly HashSet<NextStepSubscription> m_nextStepSubscriptions;
        private readonly HashSet<FundRateFeeSubscription> m_fundingRateFeeSubscriptions;
        private readonly ICbFuturesRestClient m_cbFuturesRestClient;
        private Balance m_currentBalance;
        private readonly Dictionary<string, HashSet<Order>> m_openOrders;
        private readonly Dictionary<string, OpenPositionWithOrders> m_longPositions;
        private readonly Dictionary<string, OpenPositionWithOrders> m_shortPositions;
        private SymbolInfo[] m_tradingSymbols = Array.Empty<SymbolInfo>();

        public BackTestExchange(IOptions<BackTestExchangeOptions> options, 
            IBackTestDataDownloader backTestDataDownloader, 
            IHistoricalDataStorage historicalDataStorage, 
            ICbFuturesRestClient cbFuturesRestClient,
            ITradingSymbolsManager symbolsManager)
        {
            m_lock = new AsyncLock();
            m_candleSubscriptions = new HashSet<CandleUpdateSubscription>();
            m_tickerSubscriptions = new HashSet<TickerUpdateSubscription>();
            m_balanceSubscriptions = new HashSet<BalanceUpdateSubscription>();
            m_orderSubscriptions = new HashSet<OrderUpdateSubscription>();
            m_nextStepSubscriptions = new HashSet<NextStepSubscription>();
            m_fundingRateFeeSubscriptions = new HashSet<FundRateFeeSubscription>();
            m_candleProcessors = new Dictionary<string, BackTestDataProcessor>();
            m_backTestDataDownloader = backTestDataDownloader;
            m_historicalDataStorage = historicalDataStorage;
            m_cbFuturesRestClient = cbFuturesRestClient;
            m_tradingSymbolsManager = symbolsManager;
            m_options = options;
            m_currentTime = m_options.Value.Start;
            m_currentBalance = new Balance(m_options.Value.InitialBalance, m_options.Value.InitialBalance, 0m, 0m);
            m_openOrders = new Dictionary<string, HashSet<Order>>();
            m_longPositions = new Dictionary<string, OpenPositionWithOrders>();
            m_shortPositions = new Dictionary<string, OpenPositionWithOrders>();
        }
        
        public DateTime CurrentTime => m_currentTime;

        public decimal SpotBalance { get; private set; }

        public Task<OpenPositionWithOrders[]> GetOpenPositionsWithOrdersAsync(CancellationToken cancel = default)
        {
            return Task.FromResult(m_longPositions.Values.Concat(m_shortPositions.Values).ToArray().Select(x => x.Clone()).ToArray());
        }

        public Task<bool> SetLeverageAsync(SymbolInfo symbol, CancellationToken cancel = default)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SwitchPositionModeAsync(PositionMode mode, string symbol, CancellationToken cancel = default)
        {
            return Task.FromResult(true);
        }

        public async Task<bool> CancelOrderAsync(string symbol, string orderId, CancellationToken cancel = default)
        {
            Order? order = null;
            using (await m_lock.LockAsync())
            {
                if (m_openOrders.TryGetValue(symbol, out var openOrders))
                {
                    var openOrder = openOrders.FirstOrDefault(o => o.OrderId == orderId);
                    if (openOrder != null)
                    {
                        openOrders.Remove(openOrder);
                        openOrder.Status = OrderStatus.Cancelled;
                        order = openOrder;
                    }
                }
            }

            if (order != null)
                await NotifyOrderAsync(order);

            return true;
        }

        public async Task<bool> PlaceLimitBuyOrderAsync(string symbol, decimal quantity, decimal price, CancellationToken cancel = default)
        {
            bool res = await PlaceBuyOrderAsync(symbol, quantity, price, m_options.Value.MakerFeeRate);
            return res;
        }

        public async Task<bool> PlaceLimitSellOrderAsync(string symbol, decimal quantity, decimal price, CancellationToken cancel = default)
        {
            bool res = await PlaceSellOrderAsync(symbol, quantity, price, m_options.Value.MakerFeeRate);
            return res;
        }

        public async Task<bool> PlaceMarketBuyOrderAsync(string symbol, decimal quantity, decimal price, CancellationToken cancel = default)
        {
            bool res = await PlaceBuyOrderAsync(symbol, quantity, price, m_options.Value.TakerFeeRate);
            return res;
        }

        public async Task<bool> PlaceMarketSellOrderAsync(string symbol, decimal quantity, decimal price, CancellationToken cancel = default)
        {
            bool res = await PlaceSellOrderAsync(symbol, quantity, price, m_options.Value.TakerFeeRate);
            return res;
        }

        private async Task<bool> PlaceBuyOrderAsync(string symbol, decimal quantity, decimal price, decimal feeRate)
        {
            Order order;
            using (await m_lock.LockAsync())
            {
                var currentBalance = m_currentBalance;
                if (currentBalance.Equity < quantity * price)
                    return false;
                order = new Order
                {
                    Symbol = symbol,
                    Status = OrderStatus.Filled,
                    AveragePrice = price,
                    Side = OrderSide.Buy,
                    Price = price,
                    Quantity = quantity,
                    OrderId = Guid.NewGuid().ToString(),
                    PositionMode = OrderPositionMode.BothSideBuy,
                    QuantityFilled = quantity,
                    ValueFilled = quantity * price,
                    QuantityRemaining = 0,
                    ReduceOnly = false,
                    ValueRemaining = 0,
                    CreateTime = m_currentTime,
                };
                decimal fee = quantity * price * feeRate;
                await AddFeeToBalanceAsync(-fee);
                if (!m_longPositions.TryGetValue(symbol, out var openPosition))
                {
                    openPosition = new OpenPositionWithOrders(order);
                    m_longPositions.Add(symbol, openPosition);
                }
                else
                {
                    openPosition.AddOrder(order);
                }
            }

            await NotifyOrderAsync(order);

            return true;
        }

        private async Task<bool> PlaceSellOrderAsync(string symbol, decimal quantity, decimal price, decimal feeRate)
        {
            Order order;
            using (await m_lock.LockAsync())
            {
                var currentBalance = m_currentBalance;
                if (currentBalance.Equity < quantity * price)
                    return false;
                order = new Order
                {
                    Symbol = symbol,
                    Status = OrderStatus.Filled,
                    AveragePrice = price,
                    Side = OrderSide.Sell,
                    Price = price,
                    Quantity = quantity,
                    OrderId = Guid.NewGuid().ToString(),
                    PositionMode = OrderPositionMode.BothSideSell,
                    QuantityFilled = quantity,
                    ValueFilled = quantity * price,
                    QuantityRemaining = 0,
                    ReduceOnly = false,
                    ValueRemaining = 0,
                    CreateTime = m_currentTime,
                };
                decimal fee = quantity * price * feeRate;
                await AddFeeToBalanceAsync(-fee);
                if (!m_shortPositions.TryGetValue(symbol, out var openPosition))
                {
                    openPosition = new OpenPositionWithOrders(order);
                    m_shortPositions.Add(symbol, openPosition);
                }
                else
                {
                    openPosition.AddOrder(order);
                }
            }

            await NotifyOrderAsync(order);

            return true;
        }

        public async Task<bool> PlaceLongTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force, CancellationToken cancel = default)
        {
            using (await m_lock.LockAsync())
            {
                if (!m_longPositions.TryGetValue(symbol, out var openPosition))
                    return false;
                var position = openPosition.Position;
                if (position.Quantity < qty)
                    return false;
                if (force)
                {
                    var profitOrLoss = (price - position.AveragePrice) * qty;
                    await UpdateBalanceAsync(profitOrLoss);
                    decimal fee = price * qty * m_options.Value.TakerFeeRate;
                    await AddFeeToBalanceAsync(-fee);
                    
                    if (position.Quantity != qty)
                    {
                        Order order = new Order
                        {
                            Symbol = symbol,
                            AveragePrice = price,
                            Status = OrderStatus.Filled,
                            Side = OrderSide.Sell,
                            Price = price,
                            Quantity = qty,
                            OrderId = Guid.NewGuid().ToString(),
                            PositionMode = OrderPositionMode.BothSideBuy,
                            QuantityFilled = qty,
                            ValueFilled = qty * price,
                            QuantityRemaining = 0,
                            ReduceOnly = true,
                            ValueRemaining = 0,
                            CreateTime = m_currentTime,
                        };
                        openPosition.AddOrder(order);
                    }
                    else
                    {
                        m_longPositions.Remove(symbol);
                    }
                }
                else
                {
                    Order order = new Order
                    {
                        Symbol = symbol,
                        AveragePrice = price,
                        Status = OrderStatus.New,
                        Side = OrderSide.Sell,
                        Price = price,
                        Quantity = qty,
                        OrderId = Guid.NewGuid().ToString(),
                        PositionMode = OrderPositionMode.BothSideBuy,
                        QuantityFilled = 0,
                        ValueFilled = 0,
                        QuantityRemaining = qty,
                        ReduceOnly = true,
                        ValueRemaining = qty * price,
                        CreateTime = m_currentTime,
                    };

                    if (!m_openOrders.TryGetValue(symbol, out var openOrders))
                    {
                        openOrders = new HashSet<Order>();
                        m_openOrders.Add(symbol, openOrders);
                    }
                    openOrders.Add(order);
                }
            }

            return true;
        }

        public async Task<bool> PlaceShortTakeProfitOrderAsync(string symbol, decimal qty, decimal price, bool force, CancellationToken cancel = default)
        {
            using (await m_lock.LockAsync())
            {
                if (!m_shortPositions.TryGetValue(symbol, out var openPosition))
                    return false;
                var position = openPosition.Position;
                if (position.Quantity < qty)
                    return false;
                if (force)
                {
                    var profitOrLoss = (position.AveragePrice - price) * qty;
                    await UpdateBalanceAsync(profitOrLoss);
                    decimal fee = price * qty * m_options.Value.TakerFeeRate;
                    await AddFeeToBalanceAsync(-fee);

                    if (position.Quantity != qty)
                    {
                        Order order = new Order
                        {
                            Symbol = symbol,
                            AveragePrice = price,
                            Status = OrderStatus.Filled,
                            Side = OrderSide.Buy,
                            Price = price,
                            Quantity = qty,
                            OrderId = Guid.NewGuid().ToString(),
                            PositionMode = OrderPositionMode.BothSideSell,
                            QuantityFilled = qty,
                            ValueFilled = qty * price,
                            QuantityRemaining = 0,
                            ReduceOnly = true,
                            ValueRemaining = 0,
                            CreateTime = m_currentTime,
                        };
                        openPosition.AddOrder(order);
                    }
                    else
                    {
                        m_shortPositions.Remove(symbol);
                    }
                }
                else
                {
                    Order order = new Order
                    {
                        Symbol = symbol,
                        AveragePrice = price,
                        Status = OrderStatus.New,
                        Side = OrderSide.Buy,
                        Price = price,
                        Quantity = qty,
                        OrderId = Guid.NewGuid().ToString(),
                        PositionMode = OrderPositionMode.BothSideSell,
                        QuantityFilled = 0,
                        ValueFilled = 0,
                        QuantityRemaining = qty,
                        ReduceOnly = true,
                        ValueRemaining = qty * price,
                        CreateTime = m_currentTime,
                    };
                    if (!m_openOrders.TryGetValue(symbol, out var openOrders))
                    {
                        openOrders = new HashSet<Order>();
                        m_openOrders.Add(symbol, openOrders);
                    }
                    openOrders.Add(order);
                }
            }

            return true;
        }

        public Task<Balance> GetBalancesAsync(CancellationToken cancel = default)
        {
            return Task.FromResult(m_currentBalance);
        }

        public async Task<SymbolInfo[]> GetSymbolInfoAsync(CancellationToken cancel = default)
        {
            if (m_tradingSymbols.Length == 0)
            {
                m_tradingSymbols = (await m_tradingSymbolsManager.GetTradingSymbolsAsync(
                    m_cbFuturesRestClient,
                    m_options.Value.Whitelist.ToList(),
                    m_options.Value.Blacklist.ToList(),
                    new SymbolPreferences
                    {
                        Maturity = m_options.Value.SymbolMaturityPreference,
                        Volume = m_options.Value.SymbolVolumePreference,
                        Volatility = m_options.Value.SymbolVolatilityPreference
                    },
                    m_options.Value.HistoricalDataDirectory,
                    cancel)).ToArray();
            } 
            return m_tradingSymbols;
        }

        public async Task<Candle[]> GetKlinesAsync(string symbol, TimeFrame interval, int limit, CancellationToken cancel = default)
        {
            var currentTime = m_currentTime;
            if (currentTime != m_options.Value.Start)
            {
                throw new InvalidOperationException("There is something wrong with the logic or data. We should call this only at the beginning.");
            }
            var currentDay = currentTime.Date;
            var previousDay = currentDay.AddDays(-1);
            var previousDayData = await m_historicalDataStorage.ReadAsync(symbol, previousDay, cancel);
            var currentDayData = await m_historicalDataStorage.ReadAsync(symbol, currentDay, cancel);
            var candles = previousDayData.Candles.Concat(currentDayData.Candles).ToArray();
            var currentCandles = candles
                .Where(x => x.TimeFrame == interval)
                .Where(x => x.StartTime.Add(x.TimeFrame.ToTimeSpan()) <= currentTime)
                .OrderByDescending(x => x.StartTime).Take(limit)
                .Reverse()
                .ToArray();
            return currentCandles.ToArray();
        }

        public Task<Candle[]> GetKlinesAsync(string symbol, TimeFrame interval, DateTime start, DateTime end,
            CancellationToken cancel = default)
        {
            throw new NotSupportedException("We don't need this for backtesting at the moment.");
        }

        public async Task<Ticker> GetTickerAsync(string symbol, CancellationToken cancel = default)
        {
            var currentTime = m_currentTime;
            var currentTimeOnMinute = new DateTime(
                currentTime.Year, 
                currentTime.Month, 
                currentTime.Day, 
                currentTime.Hour, 
                currentTime.Minute, 
                0, DateTimeKind.Utc);
            var currentDay = currentTime.Date;
            var currentDayData = await m_historicalDataStorage.ReadAsync(symbol, currentDay, cancel);
            var currentCandle = currentDayData.Candles
                .FirstOrDefault(x => x.TimeFrame == TimeFrame.OneMinute && x.StartTime == currentTimeOnMinute);
            if (currentCandle == null)
            {
                return new Ticker();
            }
            return new Ticker
            {
                FundingRate = null,
                BestAskPrice = currentCandle.Close,
                BestBidPrice = currentCandle.Close,
                LastPrice = currentCandle.Close,
                Timestamp = currentCandle.StartTime + TimeSpan.FromMinutes(1),
            };
        }

        public async Task<Order[]> GetOrdersAsync(CancellationToken cancel = default)
        {
            Order[] orders;
            using (await m_lock.LockAsync())
                orders = m_openOrders.Values.Select(x => x.ToArray()).SelectMany(x => x).ToArray();

            return orders;
        }

        public async Task<Position[]> GetPositionsAsync(CancellationToken cancel = default)
        {
            Position[] positions;
            using (await m_lock.LockAsync())
            {
                positions = m_longPositions.Values
                    .Select(x => x.Position)
                    .Concat(m_shortPositions.Values.Select(x => x.Position))
                    .ToArray();
            }

            return positions;
        }

        public Task<FundingRate[]> GetFundingRatesAsync(string symbol, DateTime start, DateTime end, CancellationToken cancel = default)
        {
            throw new NotSupportedException("Not needed for the client. Funding rates are sent in ticker data");
        }

        public Task<IUpdateSubscription> SubscribeToWalletUpdatesAsync(Action<Balance> handler, CancellationToken cancel = default)
        {
            var subscription = new BalanceUpdateSubscription(this, handler);
            m_balanceSubscriptions.Add(subscription);
            return Task.FromResult<IUpdateSubscription>(subscription);
        }

        public Task<IUpdateSubscription> SubscribeToOrderUpdatesAsync(Action<OrderUpdate> handler, CancellationToken cancel = default)
        {
            var subscription = new OrderUpdateSubscription(this, handler);
            m_orderSubscriptions.Add(subscription);
            return Task.FromResult<IUpdateSubscription>(subscription);
        }

        public Task<IUpdateSubscription> SubscribeToKlineUpdatesAsync(string[] symbols, TimeFrame timeFrame, Action<string, Candle> handler,
            CancellationToken cancel = default)
        {
            CandleUpdateSubscription subscription = new CandleUpdateSubscription(this, handler, symbols, timeFrame);
            m_candleSubscriptions.Add(subscription);
            return Task.FromResult<IUpdateSubscription>(subscription);
        }

        public Task<IUpdateSubscription> SubscribeToTickerUpdatesAsync(string[] symbols, Action<string, Ticker> handler, CancellationToken cancel = default)
        {
            TickerUpdateSubscription subscription = new TickerUpdateSubscription(this, handler, symbols);
            m_tickerSubscriptions.Add(subscription);
            return Task.FromResult<IUpdateSubscription>(subscription);
        }

        public Task<IUpdateSubscription> SubscribeToNextStepAsync(Action<DateTime> handler, CancellationToken cancel = default)
        {
            NextStepSubscription subscription = new NextStepSubscription(this, handler);
            m_nextStepSubscriptions.Add(subscription);
            return Task.FromResult<IUpdateSubscription>(subscription);
        }

        public Task<IUpdateSubscription> SubscribeToFundingRateFeeUpdatesAsync(Action<string, decimal> handler,
            CancellationToken cancel = default)
        {
            FundRateFeeSubscription subscription = new FundRateFeeSubscription(this, handler);
            m_fundingRateFeeSubscriptions.Add(subscription);
            return Task.FromResult<IUpdateSubscription>(subscription);
        }

        public async Task PrepareDataAsync(CancellationToken cancel = default)
        {
            var start = m_options.Value.Start;
            var end = m_options.Value.End;
            start -= m_options.Value.StartupCandleData;
            start = start.Date;
            end = end.Date;
            var symbols = (await GetSymbolInfoAsync(cancel)).Select(x => x.Name).ToArray();
            await m_backTestDataDownloader.DownloadDataForBackTestAsync(symbols, start, end, cancel);
            await LoadDataForDayAsync(m_currentTime.Date, cancel);
        }

        public async Task<bool> AdvanceTimeAsync(CancellationToken cancel = default)
        {
            if (m_nextTime.HasValue)
                m_currentTime = m_nextTime.Value;
            var currentTime = m_currentTime;
            await NotifyNextStepAsync(currentTime);
            foreach (var backtestProcessor in m_candleProcessors)
            {
                var timeData = backtestProcessor.Value.AdvanceTime(currentTime);
                foreach (var candle in timeData.Candles)
                {
                    if (candle.TimeFrame == TimeFrame.OneMinute)
                    {
                        await ProcessPositionsAndOrdersAsync(backtestProcessor.Key, candle, timeData.CurrentFundingRate);
                        foreach (TickerUpdateSubscription tickerUpdateSubscription in m_tickerSubscriptions)
                        {
                            tickerUpdateSubscription.Notify(backtestProcessor.Key, new Ticker
                            {
                                FundingRate = timeData.LastFundingRate?.Rate,
                                BestAskPrice = candle.Close,
                                BestBidPrice = candle.Close,
                                LastPrice = candle.Close,
                                Timestamp = candle.StartTime + TimeSpan.FromMinutes(1),
                            });
                        }
                    }

                    foreach (var subscription in m_candleSubscriptions)
                        subscription.Notify(backtestProcessor.Key, candle);
                }
            }

            DateTime nextTime = currentTime.AddMinutes(1);

            if (nextTime.Date != currentTime.Date)
                await LoadDataForDayAsync(nextTime.Date, cancel);

            m_nextTime = nextTime;
            bool hasMoreData = nextTime < m_options.Value.End;
            return hasMoreData;
        }

        public Task ClearPositionsAndOrders(CancellationToken cancel = default)
        {
            m_openOrders.Clear();
            m_longPositions.Clear();
            m_shortPositions.Clear();
            return Task.CompletedTask;
        }

        public async Task MoveFromSpotToFuturesAsync(decimal amount, CancellationToken cancel)
        {
            var balance = m_currentBalance;
            var walletBalance = m_currentBalance.WalletBalance + amount;
            var equity = walletBalance + balance.UnrealizedPnl;
            var newBalance = balance with { Equity = equity, WalletBalance = walletBalance };
            m_currentBalance = newBalance;
            SpotBalance -= amount;
            await NotifyBalanceAsync(newBalance);
        }

        public async Task MoveFromFuturesToSpotAsync(decimal amount, CancellationToken cancel)
        {
            var balance = m_currentBalance;
            var walletBalance = m_currentBalance.WalletBalance - amount;
            var equity = walletBalance + balance.UnrealizedPnl;
            var newBalance = balance with { Equity = equity, WalletBalance = walletBalance };
            m_currentBalance = newBalance;
            SpotBalance += amount;
            await NotifyBalanceAsync(newBalance);
        }

        private async Task ProcessPositionsAndOrdersAsync(string symbol, Candle candle, FundingRate? fundingRate)
        {
            List<Order> filledOrders = new List<Order>();
            using (await m_lock.LockAsync())
            {
                if (m_openOrders.TryGetValue(symbol, out var openOrders))
                {
                    foreach (var order in openOrders)
                    {
                        if (order.Side == OrderSide.Sell && order.CreateTime < candle.StartTime && candle.Close > order.Price)
                        {
                            order.Status = OrderStatus.Filled;
                            filledOrders.Add(order);
                        }

                        if (order.Side == OrderSide.Buy && order.CreateTime < candle.StartTime && candle.Close < order.Price)
                        {
                            order.Status = OrderStatus.Filled;
                            filledOrders.Add(order);
                        }
                    }

                    foreach (Order filledOrder in filledOrders)
                    {
                        openOrders.Remove(filledOrder);
                        if (!filledOrder.ReduceOnly!.Value)
                            throw new NotSupportedException("Currently we can handle only reduce only. Other orders are filled immediately.");
                        if (filledOrder.PositionMode == OrderPositionMode.BothSideBuy)
                        {
                            if (!m_longPositions.TryGetValue(symbol, out var longPosition))
                                throw new InvalidOperationException("Long position should exist");
                            var position = longPosition.Position;
                            var profitOrLoss = (filledOrder.Price!.Value - position.AveragePrice) * filledOrder.Quantity;
                            if (position.Quantity != filledOrder.Quantity)
                            {
                                longPosition.AddOrder(new Order
                                {
                                    Symbol = filledOrder.Symbol,
                                    Side = filledOrder.Side,
                                    Price = filledOrder.Price,
                                    Quantity = filledOrder.Quantity,
                                    CreateTime = filledOrder.CreateTime,
                                    Status = OrderStatus.Filled,
                                    AveragePrice = filledOrder.AveragePrice,
                                    PositionMode = filledOrder.PositionMode,
                                    ReduceOnly = filledOrder.ReduceOnly,
                                    OrderId = filledOrder.OrderId,
                                    ValueRemaining = 0,
                                    QuantityFilled = filledOrder.Quantity,
                                    QuantityRemaining = 0,
                                    ValueFilled = profitOrLoss,
                                });
                            }
                            else
                            {
                                m_longPositions.Remove(symbol);
                            }
                            await UpdateBalanceAsync(profitOrLoss);
                            
                        }

                        if (filledOrder.PositionMode == OrderPositionMode.BothSideSell)
                        {
                            if (!m_shortPositions.TryGetValue(symbol, out var shortPosition))
                                throw new InvalidOperationException("Short position should exist");
                            var position = shortPosition.Position;
                            var profitOrLoss = (position.AveragePrice - filledOrder.Price!.Value) * filledOrder.Quantity;
                            if (position.Quantity != filledOrder.Quantity)
                            {
                                shortPosition.AddOrder(new Order
                                {
                                    Symbol = filledOrder.Symbol,
                                    Side = filledOrder.Side,
                                    Price = filledOrder.Price,
                                    Quantity = filledOrder.Quantity,
                                    CreateTime = filledOrder.CreateTime,
                                    Status = OrderStatus.Filled,
                                    AveragePrice = filledOrder.AveragePrice,
                                    PositionMode = filledOrder.PositionMode,
                                    ReduceOnly = filledOrder.ReduceOnly,
                                    OrderId = filledOrder.OrderId,
                                    ValueRemaining = 0,
                                    QuantityFilled = filledOrder.Quantity,
                                    QuantityRemaining = 0,
                                    ValueFilled = profitOrLoss,
                                });
                            }
                            else
                            {
                                m_shortPositions.Remove(symbol);
                            }
                            
                            await UpdateBalanceAsync(profitOrLoss);
                        }

                        decimal fee = filledOrder.Price!.Value * filledOrder.Quantity * m_options.Value.MakerFeeRate;
                        await AddFeeToBalanceAsync(-fee);
                    }
                }

                if (m_longPositions.TryGetValue(symbol, out var lp))
                {
                    lp.UpdateUnrealizedProfitOrLoss(candle);
                    await UpdateUnrealizedBalanceAsync();
                    if (fundingRate != null)
                        await ApplyFundingRate(symbol, lp, fundingRate);
                }

                if (m_shortPositions.TryGetValue(symbol, out var sp))
                {
                    sp.UpdateUnrealizedProfitOrLoss(candle);
                    await UpdateUnrealizedBalanceAsync();
                    if (fundingRate != null)
                        await ApplyFundingRate(symbol, sp, fundingRate);
                }
            }
        }

        private async Task UpdateBalanceAsync(decimal profitOrLoss)
        {
            var balance = m_currentBalance;
            var walletBalance = balance.WalletBalance ?? 0;
            if (profitOrLoss < 0)
            {
                var absProfitOrLoss = Math.Abs((double)profitOrLoss);
                profitOrLoss = -(decimal)Math.Min(absProfitOrLoss, (double)walletBalance);
            }
            walletBalance += profitOrLoss;
            var realizedProfitAndLoss = balance.RealizedPnl + profitOrLoss;
            var unrealizedProfitAndLoss = CalculateUnrealizedProfitAndLoss();
            if (unrealizedProfitAndLoss < 0)
            {
                var absUnrealizedProfitAndLoss = Math.Abs((double)unrealizedProfitAndLoss);
                unrealizedProfitAndLoss = -(decimal)Math.Min(absUnrealizedProfitAndLoss, (double)walletBalance);
            }
            var equity = walletBalance + unrealizedProfitAndLoss;
            if (equity <= 1.0m) // there should probably be some ratio from exchange
            {
                // liquidation
                equity = 0;
                walletBalance = 0;
                unrealizedProfitAndLoss = 0;
            }

            var newBalance = new Balance(equity, walletBalance, unrealizedProfitAndLoss, realizedProfitAndLoss);
            m_currentBalance = newBalance;
            await NotifyBalanceAsync(newBalance);
        }

        private decimal CalculateUnrealizedProfitAndLoss()
        {
            decimal profitAndLoss = 0;
            foreach (var position in m_longPositions.Values)
                profitAndLoss += position.UnrealizedProfitOrLoss;

            foreach (var position in m_shortPositions.Values)
                profitAndLoss += position.UnrealizedProfitOrLoss;
            return profitAndLoss;
        }

        private async Task AddFeeToBalanceAsync(decimal fee)
        {
            await UpdateBalanceAsync(fee);
        }

        private async Task AddFundingRateFeeToBalanceAsync(string symbol, decimal fee)
        {
            await UpdateBalanceAsync(fee);
            await NotifyFundingRateFeeAsync(symbol, fee);
        }

        private async Task UpdateUnrealizedBalanceAsync()
        {
            await UpdateBalanceAsync(0m);
        }

        private async Task ApplyFundingRate(string symbol, OpenPositionWithOrders position, FundingRate fundingRate)
        {
            var positionValue = position.Position.Quantity * position.Position.AveragePrice;
            var fundingRateValue = positionValue * Math.Abs(fundingRate.Rate);
            bool shortsPay = fundingRate.Rate < 0;
            bool longsPay = fundingRate.Rate > 0;
            if (position.Position.Side == PositionSide.Buy)
            {
                if (longsPay)
                    await AddFundingRateFeeToBalanceAsync(symbol, -fundingRateValue);

                if (shortsPay)
                    await AddFundingRateFeeToBalanceAsync(symbol, fundingRateValue);
            }
            else if(position.Position.Side == PositionSide.Sell)
            {
                if (longsPay)
                    await AddFundingRateFeeToBalanceAsync(symbol, fundingRateValue);

                if (shortsPay)
                    await AddFundingRateFeeToBalanceAsync(symbol, -fundingRateValue);
            }
        }

        private async Task LoadDataForDayAsync(DateTime day, CancellationToken cancel = default)
        {
            var symbols = await GetSymbolInfoAsync(cancel);
            m_candleProcessors.Clear();
            foreach (var symbol in symbols)
            {
                var dayData = await m_historicalDataStorage.ReadAsync(symbol.Name, day, cancel);
                var processor = new BackTestDataProcessor(dayData);
                m_candleProcessors[symbol.Name] = processor;
            }
        }

        private Task NotifyBalanceAsync(Balance balance)
        {
            m_currentBalance = balance;
            foreach (var subscription in m_balanceSubscriptions)
                subscription.Notify(balance);
            return Task.CompletedTask;
        }

        private Task NotifyOrderAsync(Order order)
        {
            OrderUpdate orderUpdate = new OrderUpdate
            {
                OrderId = order.OrderId,
                Status = order.Status,
                Symbol = order.Symbol,
            };
            foreach (var subscription in m_orderSubscriptions)
                subscription.Notify(orderUpdate);
            return Task.CompletedTask;
        }

        private Task NotifyNextStepAsync(DateTime time)
        {
            foreach (var subscription in m_nextStepSubscriptions)
                subscription.Notify(time);
            return Task.CompletedTask;
        }

        private Task NotifyFundingRateFeeAsync(string symbol, decimal fee)
        {
            foreach (var subscription in m_fundingRateFeeSubscriptions)
                subscription.Notify(symbol, fee);
            return Task.CompletedTask;
        }

        #region Subscriptions
        private class CandleUpdateSubscription : IUpdateSubscription
        {
            private readonly BackTestExchange m_exchange;
            private readonly Action<string, Candle> m_handler;
            private readonly HashSet<string> m_symbols;
            private readonly TimeFrame m_timeFrame;

            public CandleUpdateSubscription(BackTestExchange exchange, Action<string, Candle> handler, string[] symbols, TimeFrame timeFrame)
            {
                m_exchange = exchange;
                m_handler = handler;
                m_timeFrame = timeFrame;
                m_symbols = new HashSet<string>(symbols);
            }

            public void Notify(string symbol, Candle candle)
            {
                if(m_symbols.Contains(symbol) && candle.TimeFrame == m_timeFrame)
                    m_handler(symbol, candle);
            }

            public void AutoReconnect(ILogger logger)
            {
                // no need to reconnect
            }

            public async Task CloseAsync()
            {
                using var l = await m_exchange.m_lock.LockAsync();
                m_exchange.m_candleSubscriptions.Remove(this);
            }
        }

        private class BalanceUpdateSubscription : IUpdateSubscription
        {
            private readonly BackTestExchange m_exchange;
            private readonly Action<Balance> m_handler;

            public BalanceUpdateSubscription(BackTestExchange exchange, Action<Balance> handler)
            {
                m_exchange = exchange;
                m_handler = handler;
            }

            public void Notify(Balance balance)
            {
                m_handler(balance);
            }

            public void AutoReconnect(ILogger logger)
            {
                // no need to reconnect
            }

            public async Task CloseAsync()
            {
                using var l = await m_exchange.m_lock.LockAsync();
                m_exchange.m_balanceSubscriptions.Remove(this);
            }
        }

        private class TickerUpdateSubscription : IUpdateSubscription
        {
            private readonly BackTestExchange m_exchange;
            private readonly Action<string, Ticker> m_handler;
            private readonly HashSet<string> m_symbols;

            public TickerUpdateSubscription(BackTestExchange exchange, Action<string, Ticker> handler, string[] symbols)
            {
                m_exchange = exchange;
                m_handler = handler;
                m_symbols = new HashSet<string>(symbols);
            }

            public void Notify(string symbol, Ticker ticker)
            {
                if(m_symbols.Contains(symbol))
                    m_handler(symbol, ticker);
            }

            public void AutoReconnect(ILogger logger)
            {
                // no need to reconnect
            }

            public async Task CloseAsync()
            {
                using var l = await m_exchange.m_lock.LockAsync();
                m_exchange.m_tickerSubscriptions.Remove(this);
            }
        }

        private class OrderUpdateSubscription : IUpdateSubscription
        {
            private readonly BackTestExchange m_exchange;
            private readonly Action<OrderUpdate> m_handler;

            public OrderUpdateSubscription(BackTestExchange exchange, Action<OrderUpdate> handler)
            {
                m_exchange = exchange;
                m_handler = handler;
            }

            public void Notify(OrderUpdate order)
            {
                m_handler(order);
            }

            public void AutoReconnect(ILogger logger)
            {
                // no need to reconnect
            }

            public async Task CloseAsync()
            {
                using var l = await m_exchange.m_lock.LockAsync();
                m_exchange.m_orderSubscriptions.Remove(this);
            }
        }

        private class NextStepSubscription : IUpdateSubscription
        {
            private readonly BackTestExchange m_exchange;
            private readonly Action<DateTime> m_handler;

            public NextStepSubscription(BackTestExchange exchange, Action<DateTime> handler)
            {
                m_exchange = exchange;
                m_handler = handler;
            }

            public void Notify(DateTime time)
            {
                m_handler(time);
            }

            public void AutoReconnect(ILogger logger)
            {
                // no need to reconnect
            }

            public async Task CloseAsync()
            {
                using var l = await m_exchange.m_lock.LockAsync();
                m_exchange.m_nextStepSubscriptions.Remove(this);
            }
        }

        private class FundRateFeeSubscription : IUpdateSubscription
        {
            private readonly BackTestExchange m_exchange;
            private readonly Action<string, decimal> m_handler;

            public FundRateFeeSubscription(BackTestExchange exchange, Action<string, decimal> handler)
            {
                m_exchange = exchange;
                m_handler = handler;
            }

            public void Notify(string symbol, decimal fee)
            {
                m_handler(symbol, fee);
            }

            public void AutoReconnect(ILogger logger)
            {
                // no need to reconnect
            }

            public async Task CloseAsync()
            {
                using var l = await m_exchange.m_lock.LockAsync();
                m_exchange.m_fundingRateFeeSubscriptions.Remove(this);
            }
        }
        #endregion // Subscriptions
    }
}

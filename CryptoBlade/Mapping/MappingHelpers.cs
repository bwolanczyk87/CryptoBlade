﻿using CryptoBlade.Models;
using CryptoBlade.Strategies.Wallet;
using Skender.Stock.Indicators;

namespace CryptoBlade.Mapping
{
    public static class MappingHelpers
    {
        public static Bybit.Net.Enums.KlineInterval ToKlineInterval(this TimeFrame timeFrame)
        {
            switch (timeFrame)
            {
                case TimeFrame.OneMinute:
                    return Bybit.Net.Enums.KlineInterval.OneMinute;
                case TimeFrame.FiveMinutes:
                    return Bybit.Net.Enums.KlineInterval.FiveMinutes;
                case TimeFrame.FifteenMinutes:
                    return Bybit.Net.Enums.KlineInterval.FifteenMinutes;
                case TimeFrame.ThirtyMinutes:
                    return Bybit.Net.Enums.KlineInterval.ThirtyMinutes;
                case TimeFrame.OneHour:
                    return Bybit.Net.Enums.KlineInterval.OneHour;
                case TimeFrame.FourHours:
                    return Bybit.Net.Enums.KlineInterval.FourHours;
                case TimeFrame.OneDay:
                    return Bybit.Net.Enums.KlineInterval.OneDay;
                case TimeFrame.OneWeek:
                    return Bybit.Net.Enums.KlineInterval.OneWeek;
                case TimeFrame.OneMonth:
                    return Bybit.Net.Enums.KlineInterval.OneMonth;
                default: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, null);
            }
        }

        public static TimeFrame ToTimeFrame(this Bybit.Net.Enums.KlineInterval value)
        {
            switch (value)
            {
                case Bybit.Net.Enums.KlineInterval.OneMinute:
                    return TimeFrame.OneMinute;
                case Bybit.Net.Enums.KlineInterval.FiveMinutes:
                    return TimeFrame.FiveMinutes;
                case Bybit.Net.Enums.KlineInterval.FifteenMinutes:
                    return TimeFrame.FifteenMinutes;
                case Bybit.Net.Enums.KlineInterval.ThirtyMinutes:
                    return TimeFrame.ThirtyMinutes;
                case Bybit.Net.Enums.KlineInterval.OneHour:
                    return TimeFrame.OneHour;
                case Bybit.Net.Enums.KlineInterval.FourHours:
                    return TimeFrame.FourHours;
                case Bybit.Net.Enums.KlineInterval.OneDay:
                    return TimeFrame.OneDay;
                case Bybit.Net.Enums.KlineInterval.OneWeek:
                    return TimeFrame.OneWeek;
                case Bybit.Net.Enums.KlineInterval.OneMonth:
                    return TimeFrame.OneMonth;
                default: throw new ArgumentOutOfRangeException(nameof(value), value, null);
            }
        }

        public static Candle ToCandle(this Bybit.Net.Objects.Models.V5.BybitKline kline, TimeFrame timeFrame)
        {
            return new Candle
            {
                Open = kline.OpenPrice,
                Close = kline.ClosePrice,
                High = kline.HighPrice,
                Low = kline.LowPrice,
                Volume = kline.Volume,
                TimeFrame = timeFrame,
                StartTime = kline.StartTime,
            };
        }

        public static FundingRate ToFundingRate(this Bybit.Net.Objects.Models.V5.BybitFundingHistory fundingHistory)
        {
            return new FundingRate
            {
                Rate = fundingHistory.FundingRate,
                Time = fundingHistory.Timestamp,
            };
        }

        public static Candle ToCandle(this Bybit.Net.Objects.Models.V5.BybitKlineUpdate klineUpdate)
        {
            return new Candle
            {
                Open = klineUpdate.OpenPrice,
                Close = klineUpdate.ClosePrice,
                High = klineUpdate.HighPrice,
                Low = klineUpdate.LowPrice,
                Volume = klineUpdate.Volume,
                TimeFrame = klineUpdate.Interval.ToTimeFrame(),
                StartTime = klineUpdate.StartTime,
            };
        }

        public static Quote ToQuote(this Candle candle)
        {
            return new Quote
            {
                Close = candle.Close,
                Date = candle.StartTime,
                High = candle.High,
                Low = candle.Low,
                Open = candle.Open,
                Volume = candle.Volume
            };
        }

        public static Ticker ToTicker(this Bybit.Net.Objects.Models.V5.BybitLinearInverseTicker ticker)
        {
            return new Ticker
            {
                BestAskPrice = ticker.BestAskPrice ?? 0,
                LastPrice = ticker.LastPrice,
                BestBidPrice = ticker.BestBidPrice ?? 0,
                FundingRate = ticker.FundingRate,
                Timestamp = DateTime.UtcNow,
                Volume24H = ticker.Volume24h
            };
        }

        public static TimeSpan ToTimeSpan(this TimeFrame timeFrame)
        {
            switch (timeFrame)
            {
                case TimeFrame.OneMinute:
                    return TimeSpan.FromMinutes(1);
                case TimeFrame.FiveMinutes:
                    return TimeSpan.FromMinutes(5);
                case TimeFrame.FifteenMinutes:
                    return TimeSpan.FromMinutes(15);
                case TimeFrame.ThirtyMinutes:
                    return TimeSpan.FromMinutes(30);
                case TimeFrame.OneHour:
                    return TimeSpan.FromHours(1);
                case TimeFrame.FourHours:
                    return TimeSpan.FromHours(4);
                case TimeFrame.OneDay:
                    return TimeSpan.FromDays(1);
                case TimeFrame.OneWeek:
                    return TimeSpan.FromDays(7);
                case TimeFrame.OneMonth:
                    return TimeSpan.FromDays(30);
                default: throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, null);
            }
        }

        public static Balance ToBalance(this Bybit.Net.Objects.Models.V5.BybitAssetBalance balance)
        {
            return new Balance(
                balance.Equity,
                balance.WalletBalance,
                balance.UnrealizedPnl,
                balance.RealizedPnl);
        }

        public static Ticker? ToTicker(this Bybit.Net.Objects.Models.V5.BybitLinearTickerUpdate ticker)
        {
            if(ticker.BestAskPrice == null || ticker.BestBidPrice == null || ticker.LastPrice == null)
                return null;
            return new Ticker
            {
                BestAskPrice = ticker.BestAskPrice.Value,
                LastPrice = ticker.LastPrice.Value,
                BestBidPrice = ticker.BestBidPrice.Value,
                FundingRate = ticker.FundingRate,
                Timestamp = DateTime.UtcNow,
            };
        }

        public static SymbolInfo ToSymbolInfo(this Bybit.Net.Objects.Models.V5.BybitLinearInverseSymbol symbol)
        {
            return new SymbolInfo
            {
                Name = symbol.Name,
                PriceScale = symbol.PriceScale,
                BaseAsset = symbol.BaseAsset,
                QuoteAsset = symbol.QuoteAsset,
                MinOrderQty = symbol.LotSizeFilter?.MinOrderQuantity,
                QtyStep = symbol.LotSizeFilter?.QuantityStep,
                MaxLeverage = symbol.LeverageFilter?.MaxLeverage,
                LaunchTime = symbol.LaunchTime
            };
        }

        public static Order ToOrder(this Bybit.Net.Objects.Models.V5.BybitOrder value)
        {
            return new Order
            {
                Symbol = value.Symbol,
                Price = value.Price,
                AveragePrice = value.AveragePrice,
                OrderId = value.OrderId,
                PositionMode = value.PositionIdx.ToPositionMode(),
                Quantity = value.Quantity,
                Side = value.Side.ToOrderSide(),
                QuantityFilled = value.QuantityFilled,
                QuantityRemaining = value.QuantityRemaining,
                Status = value.Status.ToOrderStatus(),
                ValueFilled = value.ValueFilled,
                ValueRemaining = value.ValueRemaining,
                ReduceOnly = value.ReduceOnly,
                CreateTime = value.CreateTime,
            };
        }

        public static OrderSide ToOrderSide(this Bybit.Net.Enums.OrderSide value)
        {
            return value switch
            {
                Bybit.Net.Enums.OrderSide.Buy => OrderSide.Buy,
                Bybit.Net.Enums.OrderSide.Sell => OrderSide.Sell,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }

        public static OrderPositionMode? ToPositionMode(this Bybit.Net.Enums.PositionIdx? value)
        {
            return value switch
            {
                Bybit.Net.Enums.PositionIdx.OneWayMode => (OrderPositionMode?)OrderPositionMode.OneWay,
                Bybit.Net.Enums.PositionIdx.BuyHedgeMode => (OrderPositionMode?)OrderPositionMode.BothSideBuy,
                Bybit.Net.Enums.PositionIdx.SellHedgeMode => (OrderPositionMode?)OrderPositionMode.BothSideSell,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }

        public static OrderStatus ToOrderStatus(this Bybit.Net.Enums.OrderStatus value)
        {
            return value switch
            {
                Bybit.Net.Enums.OrderStatus.Created => OrderStatus.Created,
                Bybit.Net.Enums.OrderStatus.New => OrderStatus.New,
                Bybit.Net.Enums.OrderStatus.Rejected => OrderStatus.Rejected,
                Bybit.Net.Enums.OrderStatus.PartiallyFilled => OrderStatus.PartiallyFilled,
                Bybit.Net.Enums.OrderStatus.PartiallyFilledCanceled => OrderStatus.PartiallyFilledCanceled,
                Bybit.Net.Enums.OrderStatus.Filled => OrderStatus.Filled,
                Bybit.Net.Enums.OrderStatus.Cancelled => OrderStatus.Cancelled,
                Bybit.Net.Enums.OrderStatus.Untriggered => OrderStatus.Untriggered,
                Bybit.Net.Enums.OrderStatus.Triggered => OrderStatus.Triggered,
                Bybit.Net.Enums.OrderStatus.Deactivated => OrderStatus.Deactivated,
                Bybit.Net.Enums.OrderStatus.Active => OrderStatus.Active,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }

        public static Position? ToPosition(this Bybit.Net.Objects.Models.V5.BybitPosition value)
        {
            if (!value.AveragePrice.HasValue)
                return null;
            var position = new Position
            {
                AveragePrice = value.AveragePrice.Value,
                Quantity = value.Quantity,
                Side = value.Side.ToPositionSide(),
                Symbol = value.Symbol,
                TradeMode = value.TradeMode.ToTradeMode(),
                CreateTime = value.CreateTime ?? default,
                UpdateTime = value.UpdateTime ?? default
            };

            if (position.UpdateTime < position.CreateTime)
                position.UpdateTime = position.CreateTime;

            return position;
        }

        public static PositionSide ToPositionSide(this Bybit.Net.Enums.PositionSide? value)
        {
            return value switch
            {
                Bybit.Net.Enums.PositionSide.Buy => PositionSide.Buy,
                Bybit.Net.Enums.PositionSide.Sell => PositionSide.Sell,
                Bybit.Net.Enums.PositionSide.None => PositionSide.None,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }

        public static TradeMode ToTradeMode(this Bybit.Net.Enums.TradeMode value)
        {
            return value switch
            {
                Bybit.Net.Enums.TradeMode.CrossMargin => TradeMode.CrossMargin,
                Bybit.Net.Enums.TradeMode.Isolated => TradeMode.Isolated,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
        }

        public static Bybit.Net.Enums.PositionMode ToBybitPositionMode(this PositionMode positionMode)
        {
            return positionMode switch
            {
                PositionMode.Hedge => Bybit.Net.Enums.PositionMode.BothSides,
                PositionMode.OneWay => Bybit.Net.Enums.PositionMode.MergedSingle,
                _ => throw new ArgumentOutOfRangeException(nameof(positionMode), positionMode, null),
            };
        }

        public static Bybit.Net.Enums.TradeMode ToBybitTradeMode(this TradeMode tradeMode)
        {
            return tradeMode switch
            {
                TradeMode.CrossMargin => Bybit.Net.Enums.TradeMode.CrossMargin,
                TradeMode.Isolated => Bybit.Net.Enums.TradeMode.Isolated,
                _ => throw new ArgumentOutOfRangeException(nameof(tradeMode), tradeMode, null),
            };
        }

        public static OrderUpdate ToOrderUpdate(this Bybit.Net.Objects.Models.V5.BybitOrderUpdate value)
        {
            return new OrderUpdate
            {
                Symbol = value.Symbol,
                OrderId = value.OrderId,
                Status = value.Status.ToOrderStatus(),
            };
        }
    }
}
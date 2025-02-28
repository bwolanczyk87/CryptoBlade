// *** METADATA ***
// Version: 1.0.0
// Generated: 2025-02-28 23:16:54 UTC
// Module: CryptoBlade.Models
// ****************

// *** INDEX OF INCLUDED FILES ***
1. Candle.cs
2. FundingRate.cs
3. Order.cs
4. OrderPositionMode.cs
5. OrderSide.cs
6. OrderStatus.cs
7. OrderUpdate.cs
8. Position.cs
9. PositionMode.cs
10. PositionSide.cs
11. QuoteQueue.cs
12. SymbolInfo.cs
13. Ticker.cs
14. TimeFrame.cs
15. TradeMode.cs
// *******************************

using Bybit.Net.Enums;
using CryptoBlade.Mapping;
using ProtoBuf;

// ==== FILE #1: Candle.cs ====
namespace CryptoBlade.Models
{
    [ProtoContract]
    public class Candle
    {
        [ProtoMember(1)]
        public TimeFrame TimeFrame { get; set; }
        [ProtoMember(2)]
        public DateTime StartTime { get; set; }
        [ProtoMember(3)]
        public decimal Open { get; set; }
        [ProtoMember(4)]
        public decimal High { get; set; }
        [ProtoMember(5)]
        public decimal Low { get; set; }
        [ProtoMember(6)]
        public decimal Close { get; set; }
        [ProtoMember(7)]
        public decimal Volume { get; set; }

        public override string ToString()
        {
            return $"{TimeFrame} {StartTime} O:{Open} H:{High} L:{Low} C:{Close} V:{Volume}";
        }
    }
}

// -----------------------------

// ==== FILE #2: FundingRate.cs ====
namespace CryptoBlade.Models
{
    [ProtoContract]
    public class FundingRate
    {
        [ProtoMember(1)]
        public DateTime Time { get; set; }
        
        [ProtoMember(2)]
        public decimal Rate { get; set; }
    }
}

// -----------------------------

// ==== FILE #3: Order.cs ====
namespace CryptoBlade.Models {
using CryptoExchange.Net.Attributes;
using System;

namespace CryptoBlade.Models
{
    public class Order
    {
        public string OrderId { get; set; } = string.Empty;

        public string Symbol { get; set; } = string.Empty;

        public decimal? Price { get; set; }

        public decimal Quantity { get; set; }

        public OrderSide Side { get; set; }

        public OrderPositionMode? PositionMode { get; set; }

        public OrderStatus Status { get; set; }

        public decimal? AveragePrice { get; set; }

        public decimal? QuantityRemaining { get; set; }

        public decimal? ValueRemaining { get; set; }
        
        public decimal? QuantityFilled { get; set; }

        public decimal? ValueFilled { get; set; }

        public bool? ReduceOnly { get; set; }

        public DateTime CreateTime { get; set; }

        public Order Clone()
        {
            return new Order
            {
                OrderId = OrderId,
                Symbol = Symbol,
                Price = Price,
                Quantity = Quantity,
                Side = Side,
                PositionMode = PositionMode,
                Status = Status,
                AveragePrice = AveragePrice,
                QuantityRemaining = QuantityRemaining,
                ValueRemaining = ValueRemaining,
                QuantityFilled = QuantityFilled,
                ValueFilled = ValueFilled,
                ReduceOnly = ReduceOnly,
                CreateTime = CreateTime
            };
        }
    }
}
}

// -----------------------------

// ==== FILE #4: OrderPositionMode.cs ====
namespace CryptoBlade.Models
{
    public enum OrderPositionMode
    {
        OneWay,
        BothSideBuy,
        BothSideSell
    }
}

// -----------------------------

// ==== FILE #5: OrderSide.cs ====
namespace CryptoBlade.Models
{
    public enum OrderSide
    {
        Buy,
        Sell
    }
}

// -----------------------------

// ==== FILE #6: OrderStatus.cs ====
namespace CryptoBlade.Models
{
    public enum OrderStatus
    {
        Created,
        New,
        Rejected,
        PartiallyFilled,
        PartiallyFilledCanceled,
        Filled,
        Cancelled,
        Untriggered,
        Triggered,
        Deactivated,
        Active
    }
}

// -----------------------------

// ==== FILE #7: OrderUpdate.cs ====
namespace CryptoBlade.Models
{
    public class OrderUpdate
    {
        public string Symbol { get; set; } = string.Empty;

        public OrderStatus Status { get; set; }

        public string OrderId { get; set; } = string.Empty;
    }
}

// -----------------------------

// ==== FILE #8: Position.cs ====
namespace CryptoBlade.Models
{
    public class Position
    {
        public string Symbol { get; set; } = string.Empty;

        public PositionSide Side { get; set; }

        public decimal Quantity { get; set; }

        public decimal AveragePrice { get; set; }

        public TradeMode TradeMode { get; set; }

        public DateTime CreateTime { get; set; }

        public DateTime UpdateTime { get; set; }

        public Position Clone()
        {
            return new Position
            {
                Symbol = Symbol,
                Side = Side,
                Quantity = Quantity,
                AveragePrice = AveragePrice,
                TradeMode = TradeMode,
                CreateTime = CreateTime,
                UpdateTime = UpdateTime
            };
        }
    }
}

// -----------------------------

// ==== FILE #9: PositionMode.cs ====
namespace CryptoBlade.Models
{
    public enum PositionMode
    {
        Hedge,
        OneWay,
    }
}

// -----------------------------

// ==== FILE #10: PositionSide.cs ====
namespace CryptoBlade.Models
{
    public enum PositionSide
    {
        Buy,
        Sell,
        None
    }
}

// -----------------------------

// ==== FILE #11: QuoteQueue.cs ====
namespace CryptoBlade.Models {
using Skender.Stock.Indicators;

namespace CryptoBlade.Models
{
    public class QuoteQueue
    {
        private readonly Queue<Quote> m_queue;
        private readonly int m_maxSize;
        private readonly object m_lock = new();
        private readonly TimeFrame m_timeFrame;
        private Quote? m_lastQuote;

        public QuoteQueue(int maxSize, TimeFrame timeFrame)
        {
            m_maxSize = maxSize;
            m_timeFrame = timeFrame;
            m_queue = new Queue<Quote>();
        }

        public bool Enqueue(Quote candle)
        {
            lock (m_lock)
            {
                bool consistent = true;
                if (m_lastQuote != null)
                {
                    if (m_lastQuote.Date.Equals(candle.Date))
                        return true; // do not add duplicate
                    if (m_lastQuote.Date > candle.Date)
                        return true; // do not add out of order
                    var timeSpan = candle.Date - m_lastQuote.Date;
                    var tfTimespan = m_timeFrame.ToTimeSpan();
                    if (!timeSpan.Equals(tfTimespan))
                        consistent = false;
                }
                m_queue.Enqueue(candle);
                m_lastQuote = candle;

                if (m_queue.Count > m_maxSize)
                {
                    m_queue.Dequeue();
                }

                return consistent;
            }
        }

        public Quote[] GetQuotes()
        {
            lock (m_lock)
            {
                return m_queue.ToArray();
            }
        }

        public void Clear()
        {
            lock (m_lock)
            {
                m_lastQuote = null;
                m_queue.Clear();
            }
        }
    }
}
}

// -----------------------------

// ==== FILE #12: SymbolInfo.cs ====
namespace CryptoBlade.Models
{
    public readonly record struct SymbolInfo(string Name, decimal PriceScale, string QuoteAsset, string BaseAsset, decimal? MinOrderQty, decimal? QtyStep, decimal? MaxLeverage, DateTime LaunchTime);
}

// -----------------------------

// ==== FILE #13: Ticker.cs ====
namespace CryptoBlade.Models
{
    public class Ticker
    {
        public decimal BestAskPrice { get; set; }

        public decimal BestBidPrice { get; set; }

        public decimal LastPrice { get; set; }

        public decimal? FundingRate { get; set; }

        public DateTime Timestamp { get; set; }
    }
}

// -----------------------------

// ==== FILE #14: TimeFrame.cs ====
namespace CryptoBlade.Models
{
    public enum TimeFrame
    {
        OneMinute,
        FiveMinutes,
        FifteenMinutes,
        ThirtyMinutes,
        OneHour,
        FourHours,
        OneDay,
        OneWeek,
        OneMonth
    }
}

// -----------------------------

// ==== FILE #15: TradeMode.cs ====
namespace CryptoBlade.Models
{
    public enum TradeMode
    {
        CrossMargin,
        Isolated
    }
}

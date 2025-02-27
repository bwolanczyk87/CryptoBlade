using ProtoBuf;
using Bybit.Net.Enums;
using CryptoBlade.Mapping;

namespace CryptoBlade.Models {
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

[ProtoContract]
    public class FundingRate
    {
        [ProtoMember(1)]
        public DateTime Time { get; set; }
        
        [ProtoMember(2)]
        public decimal Rate { get; set; }
    }

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

public enum OrderPositionMode
    {
        OneWay,
        BothSideBuy,
        BothSideSell
    }

public enum OrderSide
    {
        Buy,
        Sell
    }

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

public class OrderUpdate
    {
        public string Symbol { get; set; } = string.Empty;

        public OrderStatus Status { get; set; }

        public string OrderId { get; set; } = string.Empty;
    }

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

public enum PositionMode
    {
        Hedge,
        OneWay,
    }

public enum PositionSide
    {
        Buy,
        Sell,
        None
    }

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

public readonly record struct SymbolInfo(string Name, decimal PriceScale, string QuoteAsset, string BaseAsset, decimal? MinOrderQty, decimal? QtyStep, decimal? MaxLeverage, DateTime LaunchTime);

public class Ticker
    {
        public decimal BestAskPrice { get; set; }

        public decimal BestBidPrice { get; set; }

        public decimal LastPrice { get; set; }

        public decimal? FundingRate { get; set; }

        public DateTime Timestamp { get; set; }
    }

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

public enum TradeMode
    {
        CrossMargin,
        Isolated
    }
}

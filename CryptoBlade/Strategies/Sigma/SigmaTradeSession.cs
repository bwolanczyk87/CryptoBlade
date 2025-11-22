using CryptoBlade.Models;
using CryptoBlade.Strategies.Sigma.Modes;

namespace CryptoBlade.Strategies.Sigma
{
    public enum SigmaTradeState
    {
        Flat = 0,
        WaitingForEntryFill = 1,
        Active = 2
    }

    public sealed class SigmaTradeSession
    {
        public SigmaTradeState State { get; set; } = SigmaTradeState.Flat;
        public OrderSide? Side { get; set; }
        public string? DirectionTag { get; set; }     // "LONG"/"SHORT"
        public decimal? Quantity { get; set; }
        public string? EntryClientOrderId { get; set; }
        public string? EntryOrderId { get; set; }
        public DateTime? EntryCreatedUtc { get; set; }
        public DateTime? EntryFilledUtc { get; set; }
        public decimal? EntryPrice { get; set; }
        public Mode? EntryMode { get; set; }

        public string? SlClientOrderId { get; set; }
        public string? SlOrderId { get; set; }
        public decimal? SlPrice { get; set; }

        public string? Tp1ClientOrderId { get; set; }
        public string? Tp1OrderId { get; set; }
        public decimal? Tp1Price { get; set; }
        public bool Tp1Hit { get; set; }

        public string? Tp2ClientOrderId { get; set; }
        public string? Tp2OrderId { get; set; }
        public decimal? Tp2Price { get; set; }

        public double? Tp1R { get; set; }
        public double? Tp2R { get; set; }
        public TargetKind Tp1Kind { get; set; }
        public TargetKind Tp2Kind { get; set; }


        public void Reset()
        {
            State = SigmaTradeState.Flat;
            Side = null;
            DirectionTag = null;
            Quantity = null;

            EntryClientOrderId = null;
            EntryOrderId = null;
            EntryCreatedUtc = null;
            EntryFilledUtc = null;
            EntryPrice = null;
            EntryMode = null;

            SlClientOrderId = null;
            SlOrderId = null;
            SlPrice = null;

            Tp1ClientOrderId = null;
            Tp1OrderId = null;
            Tp1Price = null;
            Tp1Hit = false;

            Tp2ClientOrderId = null;
            Tp2OrderId = null;
            Tp2Price = null;
        }

        public bool HasPendingEntry => State == SigmaTradeState.WaitingForEntryFill && EntryClientOrderId is not null;

        public bool IsActive => State == SigmaTradeState.Active;

        /// <summary>Ustawia sesję w stan oczekiwania na fill nowego ENTRY.</summary>
        public void InitPendingEntry(
            OrderSide side,
            string directionTag,
            decimal quantity,
            string entryClientOrderId,
            string entryOrderId,
            DateTime entryCreatedUtc,
            decimal entryPrice,
            Mode entryMode,
            decimal slPrice,
            decimal tp1Price,
            decimal tp2Price)
        {
            Reset();
            State = SigmaTradeState.WaitingForEntryFill;
            Side = side;
            DirectionTag = directionTag;
            Quantity = quantity;

            EntryClientOrderId = entryClientOrderId;
            EntryOrderId = entryOrderId;
            EntryCreatedUtc = entryCreatedUtc;
            EntryPrice = entryPrice;
            EntryMode = entryMode;

            SlPrice = slPrice;
            Tp1Price = tp1Price;
            Tp2Price = tp2Price;
        }

        /// <summary>Odtwarza stan oczekującego ENTRY z istniejącego zlecenia.</summary>
        public void InitPendingEntryFromRecovery(Order entry)
        {
            Reset();
            State = SigmaTradeState.WaitingForEntryFill;
            Side = entry.Side;
            DirectionTag = entry.Side == OrderSide.Buy ? "LONG" : "SHORT";
            EntryMode = null;
            Quantity = entry.Quantity;
            EntryClientOrderId = entry.ClientOrderId;
            EntryOrderId = entry.OrderId;
            EntryCreatedUtc = entry.CreateTime;
            EntryPrice = entry.Price > 0m ? entry.Price : null;
        }

        /// <summary>Odtwarza stan aktywnego trade'u z istniejącego SL i opcjonalnie TP1/TP2.</summary>
        public void InitActiveFromRecovery(Order sl, Order? tp1, Order? tp2)
        {
            Reset();

            var side = sl.Side == OrderSide.Sell ? OrderSide.Buy : OrderSide.Sell;
            State = SigmaTradeState.Active;
            Side = side;
            DirectionTag = side == OrderSide.Buy ? "LONG" : "SHORT";
            EntryMode = null;
            Quantity = sl.Quantity;

            SlClientOrderId = sl.ClientOrderId;
            SlOrderId = sl.OrderId;
            SlPrice = sl.Price;

            if (tp1 is not null)
            {
                Tp1ClientOrderId = tp1.ClientOrderId;
                Tp1OrderId = tp1.OrderId;
                Tp1Price = tp1.Price;
            }

            if (tp2 is not null)
            {
                Tp2ClientOrderId = tp2.ClientOrderId;
                Tp2OrderId = tp2.OrderId;
                Tp2Price = tp2.Price;
            }

            decimal? guessEntry = null;
            if (Tp1Price.HasValue && SlPrice.HasValue)
                guessEntry = (Tp1Price.Value + SlPrice.Value) / 2m;
            else if (Tp2Price.HasValue && SlPrice.HasValue)
                guessEntry = (Tp2Price.Value + SlPrice.Value) / 2m;

            EntryPrice = guessEntry;
            EntryFilledUtc = sl.CreateTime;
        }
    }
}

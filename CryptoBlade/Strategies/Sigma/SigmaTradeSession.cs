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
        public SigmaOrderKind? Kind { get; set; }
        public decimal? Quantity { get; set; }
        public string? EntryClientOrderId { get; set; }
        public string? EntryOrderId { get; set; }
        public DateTime? EntryCreatedUtc { get; set; }
        public DateTime? EntryFilledUtc { get; set; }
        public decimal? EntryPrice { get; set; }
        public ModeKind? EntryMode { get; set; }

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
            Kind = null;
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

        public void InitPendingEntry(
            OrderSide side,
            SigmaOrderKind kind,
            decimal quantity,
            string entryClientOrderId,
            string entryOrderId,
            DateTime entryCreatedUtc,
            decimal entryPrice,
            ModeKind entryMode,
            decimal slPrice,
            decimal tp1Price,
            decimal tp2Price)
        {
            Reset();
            State = SigmaTradeState.WaitingForEntryFill;
            Side = side;
            Kind = kind;
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
    }
}

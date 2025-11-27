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

        /// <summary>
        /// Docelowa ilość kontraktów dla tej sesji (pełny size ENTRY).
        /// </summary>
        public decimal? Quantity { get; set; }

        /// <summary>
        /// Aktualnie otwarta ilość pozycji zarządzanej przez Sigmę.
        /// </summary>
        public decimal? ActiveQuantity { get; set; }

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

        /// <summary>
        /// Ilość przypisana do TP1 / TP2 / runnera.
        /// </summary>
        public decimal? Tp1Quantity { get; set; }
        public decimal? Tp2Quantity { get; set; }
        public decimal? RunnerQuantity { get; set; }

        public double? Tp1R { get; set; }
        public double? Tp2R { get; set; }

        public void Reset()
        {
            State = SigmaTradeState.Flat;
            Side = null;
            Kind = null;
            Quantity = null;
            ActiveQuantity = null;

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

            Tp1Quantity = null;
            Tp2Quantity = null;
            RunnerQuantity = null;

            Tp1R = null;
            Tp2R = null;
        }

        public bool HasPendingEntry =>
            State == SigmaTradeState.WaitingForEntryFill &&
            EntryClientOrderId is not null;

        public bool IsActive => State == SigmaTradeState.Active;

        public bool HasActivePosition =>
            IsActive &&
            ActiveQuantity.HasValue &&
            ActiveQuantity.Value > 0m;

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
            ActiveQuantity = null; // jeszcze nic nie jest zafillowane

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

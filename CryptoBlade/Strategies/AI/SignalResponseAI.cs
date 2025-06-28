// =========================================================
//  SignalResponseAI.cs   (NEW SCHEMA 2025-06)               
// =========================================================
namespace CryptoBlade.Strategies.AI
{
    /// <summary>
    /// Plain record that maps 1-to-1 to the JSON the LLM returns after the
    /// June-2025 prompt update. Quantity and Need* fields were removed – the
    /// strategy now computes position size locally.
    /// </summary>
    public sealed record SignalResponseAI
    {
        /// <summary>"LONG", "SHORT" or "NONE".</summary>
        public string Signal { get; init; } = "NONE";

        /// <summary>0-100 – already rounded by the LLM.</summary>
        public int Confidence { get; init; }

        /// <summary>Current market price that the model considers as entry.</summary>
        public decimal EntryPrice { get; init; }

        /// <summary>Absolute stop-loss price in USDT.</summary>
        public decimal StopLoss { get; init; }

        /// <summary>Absolute take-profit price in USDT.</summary>
        public decimal TakeProfit { get; init; }

        /// <summary>Short explanation &lt;300 chars.</summary>
        public string Reason { get; init; } = string.Empty;

        /// <summary>Minutes until the next evaluation cycle (1-5).</summary>
        public int Delay { get; init; }
    }
}
# Strategy Parameters

## Global

### CB_TradingBot__WalletExposureLong
- **Description:**  
  Sets the maximum exposure that any single trading strategy can allocate to opening a long position. It effectively defines an upper bound (in monetary units, e.g., USDT) for the size of an individual trade.

- **Unit:**  
  Currency (e.g., USDT)

- **Usage Example:**  
  If the portfolio holds 1000 USDT:
  - When set to **2**, the strategy caps trade size at 2 USDT, implying a very conservative approach.
  - When set to **100**, the strategy can open trades up to 100 USDT, allowing for a more aggressive usage of funds.

---

### CB_TradingBot__DynamicBotCount__TargetLongExposure
- **Description:**  
  Specifies the overall target for total long exposure across all active strategies. The dynamic strategy manager checks this threshold to decide whether new long positions can be opened. No new strategies start if the existing total long exposure already meets or exceeds the target.

- **Unit:**  
  Currency (e.g., USDT)

- **Usage Example:**  
  If you have 1000 USDT in your portfolio:
  - When set to **2**, the system only initiates new long positions if total long exposure is below 2 USDT, representing an extremely cautious limit.
  - When set to **100**, new long positions will be opened until the total long exposure hits 100 USDT, facilitating a more aggressive deployment of capital.

---

**Note:**  
Global parameters like the ones above apply to multiple or all strategies. If there are strategy-specific parameters, they appear in dedicated subsections related to each strategy.

## AutoHedge

### CB_TradingBot__Strategies__AutoHedge__MinReentryPositionDistanceLong
- **Description:**  
  Determines the minimum price drop (expressed as a fraction or percentage) below the current long position’s average price, at which AutoHedge will consider adding a new (re-entry) long portion.
- **Usage Example:**  
  If set to **0.015**, it means a 1.5% decrease (relative to the average entry) is required to place an additional long order.

---

### CB_TradingBot__Strategies__AutoHedge__MinReentryPositionDistanceShort
- **Description:**  
  The minimum price increase (expressed as a fraction or percentage) above the current short position’s average price, at which AutoHedge will consider adding to the short position (a re-entry).
- **Usage Example:**  
  If set to **0.02**, it requires a 2% increase over the average short entry price to initiate another short add-on.

---

### CB_TradingBot__Strategies__AutoHedge__QtyFactorShort
- **Description:**  
  A multiplier used to adjust the size of short positions. When smaller than 1 (e.g., 0.3), shorts open more conservatively; when larger (e.g., 1.0 or 2.0), each short entry can be bigger.
- **Usage Example:**  
  - **0.3** – each short order is only 30% of the baseline calculation for position size.
  - **1.0** – each short order follows the baseline 1:1 size.

---

### CB_TradingBot__Strategies__AutoHedge__InitialQtyPctShort
- **Description:**  
  Defines the initial percentage of total capital allocated to the first short entry. If it’s too low, subsequent short entries (re-entry) might be very small. Increasing this helps ensure the second or third short entry is more significant (e.g., ~0.5 USDT).
- **Usage Example:**  
  - **0.003** (0.3%) – at 100 USDT capital, the first short might be around 0.3 USDT (before applying any further multipliers).
  - **0.005** (0.5%) – ensures a slightly larger short position out of the gate.

---

### CB_TradingBot__Strategies__AutoHedge__DDownFactorShort
- **Description:**  
  A multiplier that scales up or down each subsequent short position (dogrywka). For example, if set to 2.0, each new short order can be up to twice the size of the previous one (depending on other conditions like `MinReentryPositionDistanceShort` or the wallet exposure limits).
- **Usage Example:**  
  - **1.0** – each re-entry has roughly the same size.
  - **2.0** – each consecutive short re-entry is double the previous one, accelerating the average position size.

---

**Summary:**  
AutoHedge uses a combination of global parameters (WalletExposure, DynamicBotCount) and its own re-entry logic. If you find the second short or long dogrywka is too small, increase `InitialQtyPctShort` (or `Long`), adjust `QtyFactorShort` (or `Long`), and consider raising `DDownFactorShort` (or `Long`) to get larger re-entry trades.

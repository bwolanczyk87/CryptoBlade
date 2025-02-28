# Strategy Parameters

## Global

### CB_TradingBot__WalletExposureLong

- **Description:**  
  Defines the maximum exposure that a single trading strategy can allocate for opening a long position. In other words, it sets a threshold (in monetary units, e.g., USDT) used to calculate the size of an individual trade.

- **Unit:**  
  Currency (e.g., USDT)

- **Usage Example:**  
  Suppose the portfolio has 1000 USDT:
  - When set to **2**, the strategy will limit the trade size to a maximum of 2 USDT, indicating a very conservative risk approach.
  - When set to **100**, the strategy can open trades up to 100 USDT, allowing for a more aggressive use of capital.

---

### CB_TradingBot__DynamicBotCount__TargetLongExposure

- **Description:**  
  Specifies the global target for total long exposure across all active strategies. This parameter is used by the dynamic strategy manager to determine whether new long positions can be initiated. New strategies will be started only if the cumulative long exposure is below this threshold.

- **Unit:**  
  Currency (e.g., USDT)

- **Usage Example:**  
  For a portfolio of 1000 USDT:
  - When set to **2**, the system will initiate new long positions only if the total long exposure is less than 2 USDT, resulting in a highly conservative overall risk.
  - When set to **100**, new long positions will continue to be opened until the total long exposure reaches 100 USDT, permitting a more aggressive capital allocation.

---

**Note:**  
If a dedicated parameter is introduced for a specific strategy, a separate sub-section with the strategy’s name and its corresponding parameter description will be added. Global parameters apply universally to all strategies, whereas specific parameters may fine-tune individual strategy behavior.

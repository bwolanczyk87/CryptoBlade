
# ===== DOCUMENT: BotLanguage.md =====
# Bot Language Documentation

## Overview
Bot is a command-based language designed to streamline interactions with the CryptoBlade system and its automation. Commands follow a structured format:

bot {command} {options}

This language enables the user to execute specific actions, retrieve system statuses, and manage the CryptoBlade project efficiently.

## Syntax Rules
- Commands must start with `bot`.
- Options are prefixed with `--` and can modify command behavior.
- Arguments can be provided after the command to specify additional parameters.

---

## Command List

### 1. **Project Management**
#### Retrieve and manage project status
- **bot show status**  
  Displays the current status of the CryptoBlade project from bot_notes.md file.
- **bot show status --last**  
  Shows the most recent saved project state.
- **bot save status**  
  Saves the current project state to `bot_notes.md`.

### 2. **Strategy Management**
#### Control trading strategies
- **bot strategy list**  
  Lists all available trading strategies.
- **bot strategy start {name}**  
  Starts the specified trading strategy.
- **bot strategy stop {name}**  
  Stops the specified trading strategy.
- **bot strategy optimize {name}**  
  Optimizes the parameters for the specified strategy.
- **bot strategy backtest {name}**  
  Runs a backtest for the given strategy.

### 3. **System Health Checks**
#### Monitor system health
- **bot health check**  
  Performs a general system health check.
- **bot health check --trading**  
  Checks the status of the live trading system.
- **bot health check --backtest**  
  Checks the status of the backtesting system.

### 4. **Exchange Interaction**
#### Manage API connections and orders
- **bot exchange status**  
  Displays connection status for Bybit and Binance.
- **bot exchange reconnect**  
  Re-establishes API connections.
- **bot order list**  
  Retrieves a list of active orders.
- **bot order cancel {order_id}**  
  Cancels a specific order by ID.

### 5. **Configuration Management**
#### Adjust system settings dynamically
- **bot config show**  
  Displays the current configuration.
- **bot config set {key} {value}**  
  Updates a configuration setting dynamically.
- **bot config reset**  
  Restores default configurations.

### 6. **Optimization and Performance**
#### Run optimization processes
- **bot optimizer start**  
  Initiates an optimization session using genetic algorithms.
- **bot optimizer status**  
  Displays the current optimizer progress.
- **bot optimizer stop**  
  Halts an ongoing optimization session.

### 7. **Logging and Debugging**
#### Access and manage system logs
- **bot logs show**  
  Displays the latest logs.
- **bot logs clear**  
  Clears the log files.
- **bot debug mode {on|off}**  
  Enables or disables debug mode.

### 8. **Output Format Conversion**
#### Change output format
- **bot convert --md**  
  Outputs the content in Markdown format.
- **bot convert --json**  
  Outputs the content in JSON format.
- **bot convert --txt**  
  Outputs the content in plain text format.

### 9. **Documentation**
#### Update and display project documentation
- **bot save docs**  
  Updates all relevant documentation files (BotLanguage.md, GettingStarted.md, Parameters.md, Strategies.md) with any new knowledge gained in the session. This includes modifications to strategies, parameter settings, or any new commands. Once updated, the new or updated docs can be displayed or saved as needed.

---

## Integration with Asystent (ChatGPT)
Asystent must read and learn the Bot language upon startup and apply the defined commands accordingly. If new commands are introduced during a session, they must be added to this documentation at the end of the chat.

---

## Future Enhancements
- Support for batch execution of commands.
- Additional debugging and diagnostic tools.
- Custom user-defined commands for specialized workflows.


# ===== DOCUMENT: GettingStarted.md =====
# CryptoBlade â€“ Overview and Action Plan

## Table of Contents

- [1. Project Goal](#1-project-goal)
- [2. Project Structure â€“ Main Modules and Their Connections](#2-project-structure--main-modules-and-their-connections)
  - [A. Backtesting Module](#a-backtesting-module)
  - [B. Configuration Module](#b-configuration-module)
  - [C. Optimization Module](#c-optimization-module)
  - [D. Real-Time Trading Module](#d-real-time-trading-module)
  - [E. Health and Monitoring Module](#e-health-and-monitoring-module)
  - [F. Auxiliary Modules and Mappings](#f-auxiliary-modules-and-mappings)
- [3. Map of Interconnections and References](#3-map-of-interconnections-and-references)
- [4. Key Processes and Operating Model](#4-key-processes-and-operating-model)
  - [A. Backtesting Process](#a-backtesting-process)
  - [B. Parameter Optimization Process](#b-parameter-optimization-process)
  - [C. Real-Time Trading Process](#c-real-time-trading-process)
  - [D. Integration and Deployment Process](#d-integration-and-deployment-process)
- [5. Concrete List of Action Items](#5-concrete-list-of-action-items)
- [Summary](#summary)

---

## 1. Project Goal

**Primary objective:**
- Create an automated trading system that generates profits on the Bybit exchange.
- The system is based on trading strategies written in .NET, with the ability to optimize these strategies using genetic algorithms as well as to backtest them on historical data.
- The projectâ€™s functionality includes sending requests to the exchangeâ€™s API to open and close positions continuously, while managing the portfolio on the fly.

---

## 2. Project Structure â€“ Main Modules and Their Connections

### A. Backtesting Module
- **Functions:**
  - Fetch historical data (candles, funding rate, ticks) from the exchanges (Binance, Bybit).
  - Process the data to simulate trading strategies.
  - Store backtest results (BacktestPerformanceResult) and track strategy performance metrics.
- **Key Classes:**
  - `BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`
  - `BackTestDataDownloader`, `BackTestDataProcessor`
  - `BackTestExchange`, `BackTestPerformanceTracker`

### B. Configuration Module
- **Functions:**
  - Store settings for accounts, strategies, backtesting, and optimization.
  - Define value ranges for optimization (for example, `OptimizerFloatRange`, `OptimizerIntRange`).
- **Key Elements:**
  - `TradingBotOptions`, `BackTest`, `CriticalMode`, `DynamicBotCount`
  - Strategy definitions: `StrategyNames`, `StrategySelectPreference`

### C. Optimization Module
- **Functions:**
  - Optimize trading-strategy parameters using a genetic algorithm.
  - Represent strategies as chromosomes/genes.
  - Manage populations and compute a fitness function to rank solutions.
- **Key Classes:**
  - `GeneticAlgorithmOptimizer`, `OptimizerBacktestExecutor`
  - Chromosome representations: `TradingBotChromosome`, `ComplexChromosome`, and specialized derivatives (e.g., `TartagliaChromosome`, `QiqiChromosome`).

### D. Real-Time Trading Module
- **Functions:**
  - Execute trading strategies in real time (and in backtest mode).
  - Receive live market data (candles, tickers) via sockets.
  - Generate entry/exit signals and send requests to the exchange API (methods like `ExecuteAsync` and `ExecuteUnstuckAsync`).
  - Manage the portfolio (class `WalletManager`).
- **Key Classes:**
  - `TradingStrategyBase` / `TradingStrategyCommonBase` â€“ base classes for strategies.
  - Concrete strategies: `AutoHedgeStrategy`, `LinearRegressionStrategy`, `TartagliaStrategy`, `MonaStrategy`, `MfiRsiEriTrendTradingStrategy`, `QiqiStrategy`.
  - Strategy managers: `DefaultTradingStrategyManager`, `DynamicTradingStrategyManager`, `TradingHostedService`.

### E. Health and Monitoring Module
- **Functions:**
  - Monitor system status (health checks for backtesting and strategy execution).
- **Key Classes:**
  - `BacktestExecutionHealthCheck`, `TradeExecutionHealthCheck`

### F. Auxiliary Modules and Mappings
- **Functions:**
  - Map API data (Binance, Bybit) into internal models (e.g. `Candle`, `FundingRate`, `Order`, `Position`, `Ticker`).
  - Provide helper utilities: calculations for technical indicators, market data processing.
  - Define data models used throughout the system.

---

## 3. Map of Interconnections and References

- **Backtesting** uses:
  - Data-fetching classes (`BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`).
  - Interfaces for data storage (`IHistoricalDataStorage`).
  - Results are passed to the optimization module (e.g., through `StrategyFitness`).

- **Optimization**:
  - Relies on the trading strategies and optimizes their parameters via genetic algorithms.
  - Uses backtest data to evaluate a strategyâ€™s fitness.

- **Real-Time Trading**:
  - Integrates with exchange APIs via REST and sockets (classes implementing `ICbFuturesRestClient` and `ICbFuturesSocketClient`).
  - The trading strategy processes market data (through `QuoteQueue` and subscription) and generates signals to open/close positions.
  - Portfolio management is handled by `WalletManager`.

- **Mapping and Helper Tools**:
  - Ensure consistent communication among modules, providing standardized data models and computational routines.

---

## 4. Key Processes and Operating Model

### A. Backtesting Process
- **Goal**: Simulate trading strategies on historical data.
- **Process**:
  1. Fetch historical data (candles, funding rates) from the exchange.
  2. Store the data in the system (e.g. `ProtoHistoricalDataStorage`).
  3. Process the data and simulate the strategy (class `BackTestExchange`).
  4. Generate performance reports (class `BackTestPerformanceTracker`).
- **Potential Improvements**: Optimize data-processing algorithms, improve data consistency, implement better indexing.

### B. Parameter Optimization Process
- **Goal**: Determine optimal parameters for trading strategies.
- **Process**:
  1. Represent parameters as genes (chromosomes).
  2. Run backtests for different configurations (via `OptimizerBacktestExecutor`).
  3. Evaluate fitness (class `StrategyFitness`).
  4. Evolve the population through crossover, mutation, and selection.
- **Potential Improvements**: Enhance the fitness function to better reflect risk and performance; fine-tune mutation/crossover parameters.

### C. Real-Time Trading Process
- **Goal**: Execute trading strategies in real time.
- **Process**:
  1. Receive market data live (candles, tickers) via sockets.
  2. Process data and generate signals (`EvaluateSignalsInnerAsync`).
  3. Send orders to the exchangeâ€™s API (`ExecuteAsync`, `ExecuteUnstuckAsync`).
  4. Manage the portfolio (class `WalletManager`).
- **Potential Improvements**: Optimize signaling logic, reduce latency, improve error handling and reconnection logic.

### D. Integration and Deployment Process
- **Goal**: Run the trading system 24/7 on a local or hosted environment.
- **Process**:
  1. Build the project using a Dockerfile.
  2. Configure settings (`appsettings.json`) for the Live environment.
  3. Implement health checks and monitoring endpoints.
  4. Automate container startup and restarts.
- **Potential Improvements**: Improve log management, enable auto-scaling, integrate with external monitoring tools.

---

## 5. Concrete List of Action Items

1. **Code Audit and Refactoring**:
   - Review modules that fetch and process data (BackTestDataDownloader, HistoricalDataStorage).
   - Verify signaling logic in strategies (LinearRegression, Tartaglia, Mona, MfiRsiEriTrend, Qiqi).

2. **Configuration Fixes**:
   - Ensure `appsettings.json` contains the correct account data (ApiKey/ApiSecret) and the right mode (Live, Backtest, Optimizer).
   - Validate and standardize your strategy/optimizer settings.

3. **Quality Control and Monitoring Integration**:
   - Check the functionality of health-check endpoints (e.g., `/healthz`).
   - Confirm that your logging system (`ApplicationLogging`) is appropriately configured.

4. **Unit and Integration Testing**:
   - Run all tests (e.g., BackTestDataDownloaderTest, BybitHistoricalTradesDownloaderTest, GridHelpersTest, and exchange tests).
   - Possibly add new integration tests.

5. **Optimization Module Enhancements**:
   - Validate the genetic algorithm approach (via StrategyFitness, GeneticAlgorithmOptimizer).
   - Improve the fitness function to better reflect risk and efficiency.

6. **Building and Deploying the Docker Image**:
   - Use the provided Dockerfile:
     - Build with: `docker build -t cryptoblade:latest .`
     - Run with: `docker run -d -p 80:80 --restart always --name cryptoblade cryptoblade:latest`
   - Check whether the container responds at `/healthz`.

7. **Automation and Monitoring**:
   - Configure automatic container restarts (e.g., `--restart always`).
   - Set up log monitoring and health checks to detect potential errors.

8. **Production/Demo Environment Deployment**:
   - Push code to a dedicated branch (e.g., `release/live`).
   - Configure CI/CD (GitHub Actions, Azure DevOps) for automatic Docker image builds and deployments.

---

## Summary

CryptoBlade is a comprehensive trading system integrating:
- Historical data backtesting,
- Real-time trading with dynamic portfolio management,
- Strategy parameter optimization via genetic algorithms,
- Monitoring and health endpoints.

To achieve the main goal (generating profits on Bybit), you should:
1. Audit and refactor the key modules.
2. Standardize configuration and run thorough testing.
3. Build a Docker image and deploy the system for continuous operation (24/7) with automated restarts and monitoring.

Completing these steps will allow you to deploy a stable, working trading system that continuously analyzes the market and executes orders automatically, aiming to maximize profit.


# ===== DOCUMENT: Parameters.md =====
# Strategy Parameters

## Global

### CB_TradingBot__WalletExposureLong
- **Description:**  
  Sets the maximum exposure that any single trading strategy can allocate to opening a long position. It effectively defines an upper bound (in monetary units, e.g., USDT) for the size of an individual trade.

- **Unit:**  
  Currency (e.g., USDT)

- **Usage Example:**  
  If the portfolio holds 1000â€ŻUSDT:
  - When set to **2**, the strategy caps trade size at 2â€ŻUSDT, implying a very conservative approach.
  - When set to **100**, the strategy can open trades up to 100â€ŻUSDT, allowing for a more aggressive usage of funds.

---

### CB_TradingBot__DynamicBotCount__TargetLongExposure
- **Description:**  
  Specifies the overall target for total long exposure across all active strategies. The dynamic strategy manager checks this threshold to decide whether new long positions can be opened. No new strategies start if the existing total long exposure already meets or exceeds the target.

- **Unit:**  
  Currency (e.g., USDT)

- **Usage Example:**  
  If you have 1000â€ŻUSDT in your portfolio:
  - When set to **2**, the system only initiates new long positions if total long exposure is below 2â€ŻUSDT, representing an extremely cautious limit.
  - When set to **100**, new long positions will be opened until the total long exposure hits 100â€ŻUSDT, facilitating a more aggressive deployment of capital.

---

**Note:**  
Global parameters like the ones above apply to multiple or all strategies. If there are strategy-specific parameters, they appear in dedicated subsections related to each strategy.

## AutoHedge

### CB_TradingBot__Strategies__AutoHedge__MinReentryPositionDistanceLong
- **Description:**  
  Determines the minimum price drop (expressed as a fraction or percentage) below the current long positionâ€™s average price, at which AutoHedge will consider adding a new (re-entry) long portion.
- **Usage Example:**  
  If set to **0.015**, it means a 1.5% decrease (relative to the average entry) is required to place an additional long order.

---

### CB_TradingBot__Strategies__AutoHedge__MinReentryPositionDistanceShort
- **Description:**  
  The minimum price increase (expressed as a fraction or percentage) above the current short positionâ€™s average price, at which AutoHedge will consider adding to the short position (a re-entry).
- **Usage Example:**  
  If set to **0.02**, it requires a 2% increase over the average short entry price to initiate another short add-on.

---

### CB_TradingBot__Strategies__AutoHedge__QtyFactorShort
- **Description:**  
  A multiplier used to adjust the size of short positions. When smaller than 1 (e.g., 0.3), shorts open more conservatively; when larger (e.g., 1.0 or 2.0), each short entry can be bigger.
- **Usage Example:**  
  - **0.3** â€“ each short order is only 30% of the baseline calculation for position size.
  - **1.0** â€“ each short order follows the baseline 1:1 size.

---

### CB_TradingBot__Strategies__AutoHedge__InitialQtyPctShort
- **Description:**  
  Defines the initial percentage of total capital allocated to the first short entry. If itâ€™s too low, subsequent short entries (re-entry) might be very small. Increasing this helps ensure the second or third short entry is more significant (e.g., ~0.5â€ŻUSDT).
- **Usage Example:**  
  - **0.003** (0.3%) â€“ at 100â€ŻUSDT capital, the first short might be around 0.3â€ŻUSDT (before applying any further multipliers).
  - **0.005** (0.5%) â€“ ensures a slightly larger short position out of the gate.

---

### CB_TradingBot__Strategies__AutoHedge__DDownFactorShort
- **Description:**  
  A multiplier that scales up or down each subsequent short position (dogrywka). For example, if set to 2.0, each new short order can be up to twice the size of the previous one (depending on other conditions like `MinReentryPositionDistanceShort` or the wallet exposure limits).
- **Usage Example:**  
  - **1.0** â€“ each re-entry has roughly the same size.
  - **2.0** â€“ each consecutive short re-entry is double the previous one, accelerating the average position size.

---

**Summary:**  
AutoHedge uses a combination of global parameters (WalletExposure, DynamicBotCount) and its own re-entry logic. If you find the second short or long dogrywka is too small, increase `InitialQtyPctShort` (or `Long`), adjust `QtyFactorShort` (or `Long`), and consider raising `DDownFactorShort` (or `Long`) to get larger re-entry trades.


# ===== DOCUMENT: Strategies.md =====
# CryptoBlade Strategies Documentation

## Table of Contents
- [1. Introduction](#1-introduction)
- [2. Detailed Description of Each Strategy](#2-detailed-description-of-each-strategy)
  - [2.1. AutoHedge Strategy](#21-autohedge-strategy)
  - [2.2. Linear Regression Strategy](#22-linear-regression-strategy)
  - [2.3. Tartaglia Strategy](#23-tartaglia-strategy)
  - [2.4. Mona Strategy](#24-mona-strategy)
  - [2.5. MfiRsiCandlePrecise Trading Strategy](#25-mfirsicandleprecise-trading-strategy)
  - [2.6. MfiRsiEriTrend Trading Strategy](#26-mfirsieritrend-trading-strategy)
  - [2.7. Qiqi Strategy](#27-qiqi-strategy)
  - [2.8. Recursive Strategy (Generalized DCA/Re-entry)](#28-recursive-strategy-generalized-dcare-entry)
- [3. Suggestions for Strategy Improvements](#3-suggestions-for-strategy-improvements)

---

## 1. Introduction

This document provides a comprehensive look at each trading strategy available in CryptoBlade, discussing the underlying logic, notable parameters, and the ways to improve or expand these approaches. Each strategy leverages the `.NET` environment, real-time data feeds, and historical backtesting to refine parameters. While many strategies share certain configuration fields (e.g. wallet exposure, re-entry distances, or minimum volume thresholds), each approach has unique signals, logic, or re-entry patterns.

---

## 2. Detailed Description of Each Strategy

### 2.1. AutoHedge Strategy
- **Class:** `AutoHedgeStrategy`
- **Purpose:**  
  Automatically hedges positions by dynamically opening or adding to hedged trades based on volume, spread, and short/long signals. The core idea is to balance or offset risk—especially in volatile markets—by scaling positions in the opposite direction of an existing bias, or adding to winning trades under certain conditions.

- **Key Logic Details:**  
  1. **Data and Indicators**:  
     - Primarily uses one-minute candles to compute short-term moving averages, volume indicators, and spread.  
     - May incorporate an optional oscillator or a threshold (like `MinReentryPositionDistanceLong/Short`) to decide whether the price has moved “enough” to justify adding another portion of the position.
  2. **Long and Short Re-entry**:  
     - Supports partial additions (dogrywki) to both long and short positions.  
     - `QtyFactorLong`/`QtyFactorShort` and `InitialQtyPctLong`/`InitialQtyPctShort` help define the magnitude of each new partial entry, preventing overly small or large top-ups.
  3. **Risk and Wallet Exposure**:  
     - Respects global wallet-exposure parameters (e.g. `WalletExposureLong`, `WalletExposureShort`) to cap risk on each side.  
     - The re-entry logic uses `DDownFactorLong/Short` if it’s coded similarly to a recursive or grid approach, meaning the next partial entry can be bigger or smaller, depending on the factor.

- **Key Configuration Options:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`  
  - `QtyFactorLong`, `QtyFactorShort`  
  - `InitialQtyPctLong`, `InitialQtyPctShort`  
  - `DDownFactorLong`, `DDownFactorShort`

- **Potential Improvements:**  
  1. **Adaptive Re-entry**: Dynamically adjust the re-entry distance or multiplier based on market volatility or average true range (ATR), so in high-volatility periods, the strategy gives more distance.  
  2. **Expanded Hedging Logic**: Incorporate an advanced check if the strategy is nearing a major support/resistance (from e.g. Mona or LinearRegression signals) to reduce or intensify hedging.  
  3. **Profit-taking Overhaul**: Instead of a single `MinProfitRate`, implement multiple partial TPs.  
  4. **Code Suggestions**: Where small second positions appear, ensure that `InitialQtyPctShort` (or Long) is not too low, and that the code checks relevant environment variables or config fields consistently. Possibly expose these fields in `appsettings.json` if they are not already.

---

### 2.2. Linear Regression Strategy
- **Class:** `LinearRegressionStrategy`
- **Purpose:**  
  Uses a regression line over recent candle data to estimate price channels. Signals appear when the price deviates sufficiently from the regression line, providing buy-low and sell-high opportunities.

- **Key Logic Details:**  
  1. **Regression Calculation**:  
     - Typically uses a fixed window size (`ChannelLength`) of one-minute candles, and applies something like Accord.NET’s `OrdinaryLeastSquares` to fit a linear model.  
     - `StandardDeviation` determines how wide the channel extends around the regression line.
  2. **Entry/Exit**:  
     - Buys if price crosses below the lower channel boundary (perceived undervaluation).  
     - Sells if price crosses above the upper channel boundary (perceived overvaluation).
  3. **Volume Filter**:  
     - Typically requires `MinimumVolume` or other checks to ensure the symbol is sufficiently liquid.

- **Configuration:**  
  - `ChannelLength`, `StandardDeviation`  
  - `MinimumVolume`, `MinimumPriceDistance`

- **Potential Improvements:**  
  1. **Adaptive Window**: Dynamically adjust `ChannelLength` based on volatility or average daily range.  
  2. **Augmented Regression**: Incorporate additional data (like time-of-day effects or a polynomial fit) to catch more nuanced patterns.  
  3. **Multiple Time Frame Confirmation**: Combine signals from a 1-min regression with a 5-min or 15-min slope check.

---

### 2.3. Tartaglia Strategy
- **Class:** `TartagliaStrategy`
- **Purpose:**  
  Implements multiple channel-based logic for long and short entries, partially inspired by advanced geometric or historical references (Tartaglia was a mathematician historically known for ballistic and polynomial solutions).

- **Key Logic Details:**  
  1. **Separate Channels**:  
     - Uses a different regression (or channel) length for longs (`ChannelLengthLong`) vs. shorts (`ChannelLengthShort`).  
     - Possibly allows independent standard deviation thresholds (`StandardDeviationLong`, `StandardDeviationShort`).
  2. **Entry/Exit**:  
     - Signals appear when the ticker price breaks above or below each respective channel.  
     - May also rely on re-entry logic (`MinReentryPositionDistanceLong`/`Short`) to add partial positions if the price continues to move in the same direction.
  3. **Multi-lane Regression**:  
     - Potentially uses multiple polynomials or linear fits to produce a combined “channel.”

- **Configuration:**  
  - `ChannelLengthLong`, `ChannelLengthShort`  
  - `StandardDeviationLong`, `StandardDeviationShort`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`

- **Potential Improvements:**  
  1. **Adaptive Standard Deviations**: Let the strategy widen or shrink each channel automatically in fast- vs. slow-moving markets.  
  2. **Volume or Volatility Overlay**: Incorporate volume thresholds to reduce false positives.  
  3. **Shared Gains**: Possibly unify partial logic if the short and long channels are triggered in opposite directions simultaneously.

---

### 2.4. Mona Strategy
- **Class:** `MonaStrategy`
- **Purpose:**  
  Clusters recent price data (Mean Shift with a Gaussian kernel) to detect key support/resistance “modes,” entering trades when price crosses below or above these modes.

- **Key Logic Details:**  
  1. **Mean Shift Clustering**:  
     - Collects a specified number of 1-min candles (`ClusteringLength`).  
     - Applies a Mean Shift algorithm (Accord.Statistics, for instance) with a `BandwidthCoefficient`.  
     - Identifies cluster centers (modes) that can act as pivot levels.
  2. **Crossing Logic**:  
     - If the price crosses below a cluster level, that often signals an undervalued region → potential buy.  
     - Crossing above might signal overvaluation → potential short.
  3. **Oscillator Filter**:  
     - Often uses MFI/RSI (`MfiRsiLookback`) or a basic spread/volume check to avoid entering in illiquid or narrow conditions.

- **Configuration:**  
  - `ClusteringLength`, `BandwidthCoefficient`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`  
  - `MfiRsiLookback`, `MinimumVolume`, `MinimumPriceDistance`

- **Potential Improvements:**  
  1. **Cluster Counting**: Adjust the bandwidth or add noise handling if too many trivial clusters appear.  
  2. **Adaptive Bandwidth**: Possibly recalculate the kernel bandwidth based on real-time volatility.  
  3. **Re-entry Criteria**: Evaluate whether a second or third re-entry at the same cluster level is beneficial.

---

### 2.5. MfiRsiCandlePrecise Trading Strategy
- **Class:** `MfiRsiCandlePreciseTradingStrategy`
- **Purpose:**  
  Leverages precise oscillator thresholds (MFI + RSI) on one-minute candles to trigger buys/sells.

- **Key Logic Details:**  
  1. **Indicators**:  
     - Calculates Money Flow Index (MFI) and Relative Strength Index (RSI).  
     - Typically checks if they’re below some threshold (e.g., MFI < 20 and RSI < 30) for a buy signal, or above thresholds (MFI > 80 and RSI > 70) for a sell signal.
  2. **Volume & Spread**:  
     - Uses `MinimumVolume` and `MinimumPriceDistance` to skip trades in poor liquidity or insufficient spread conditions.
  3. **Partial Adds**:  
     - May add extra positions if the MFI/RSI remain deeply oversold or overbought for multiple consecutive candles.

- **Configuration:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - Potential hidden fields for MFI or RSI thresholds (like `MfiThresholdBuy = 20`, `RsiThresholdBuy = 30`, etc.)

- **Potential Improvements:**  
  1. **Dynamic Thresholding**: If volatility spikes, shift thresholds so that fewer false signals occur.  
  2. **Time-based Exit**: Combine typical oscillator-based exit with a “max time in trade” limit.  
  3. **Integration**: Confirm buy signals with a short SMA cross or a momentum factor.

---

### 2.6. MfiRsiEriTrend Trading Strategy
- **Class:** `MfiRsiEriTrendTradingStrategy`
- **Purpose:**  
  Merges MFI/RSI oscillators with a separate trend detection system—often an ERI (Ehlers or a custom “ERI indicator”)—to confirm bullish/bearish signals.

- **Key Logic Details:**  
  1. **Trend Confirmation**:  
     - Compares an “ERI trend” or modified average with the MFI/RSI oscillator.  
     - If both are bullish, it triggers a buy signal; if both are bearish, it triggers a short.
  2. **Re-entry**:  
     - `MinReentryPositionDistanceLong/Short` sets how far price must move from the average entry to allow adding more.  
     - `MfiRsiLookbackPeriod` can drastically change how quickly the strategy reacts.
  3. **Optional ERI-Only**:  
     - If `UseEriOnly` is set true, the strategy relies more heavily on the ERI’s indicated momentum.

- **Configuration:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`  
  - `MfiRsiLookbackPeriod`, `UseEriOnly`

- **Potential Improvements:**  
  1. **Adaptive Lookback**: Let the strategy adjust `MfiRsiLookbackPeriod` in real-time if volatility changes.  
  2. **Combining Multi Time Frames**: Evaluate a short-term ERI plus a mid-term RSI to mitigate choppy signals.  
  3. **Advanced Exits**: Possibly define trailing stops based on the same indicators.

---

### 2.7. Qiqi Strategy
- **Class:** `QiqiStrategy`
- **Purpose:**  
  Merges RSI-based take-profit triggers and QFL fractal analysis to identify strong reversal points for entry.

- **Key Logic Details:**  
  1. **Fractal (QFL) Levels**:  
     - Identifies local fractal “bases” below which the price rarely dips; if it does, the strategy sees a potential undervaluation.  
     - For short signals, does the inverse with fractal levels above current price.
  2. **RSI Validation**:  
     - If RSI is under a threshold (e.g. 30) when the price is near or below a fractal base, Qiqi triggers a buy.  
     - `RsiTakeProfitLong` can define if the position closes once RSI crosses some upper boundary.
  3. **Timed Exits**:  
     - Includes `MaxTimeStuck` to exit losing or stuck positions after a certain period.  
     - `TakeProfitPercentLong/Short` sets partial or total profit-taking triggers.

- **Configuration:**  
  - `RsiTakeProfitLong`, `RsiTakeProfitShort`  
  - `QflBellowPercentEnterLong`, `QflAbovePercentEnterShort`  
  - `MaxTimeStuck`, `TakeProfitPercentLong`, `TakeProfitPercentShort`

- **Potential Improvements:**  
  1. **Adaptive RSI**: Let the threshold vary if the strategy is missing big rebounds.  
  2. **Multi-lot Exits**: Add partial sells as RSI climbs, instead of a single threshold.  
  3. **Fractal Smoothing**: Filter fractal levels with a moving average to reduce noise.

---

### 2.8. Recursive Strategy (Generalized DCA/Re-entry)
While not listed as a separate class in the user docs, many of these strategies can incorporate or do incorporate a “recursive” or “grid” approach to partial entries. For example:

- **DDownFactorLong/Short**: If set to 2.0, each subsequent position after the first might be double the size of the previous one.  
- **ReentryPositionPriceDistanceLong/Short**: The price must diverge from the average position by this fraction/percentage to add a new partial.  
- **InitialQtyPctLong/Short**: The first chunk of capital used for a new position, which can be crucial in preventing extremely small or large second entries.

**Why it matters**:  
- Adjusting these parameters is critical for scaling in or out of trades. A modest initial entry followed by heavier re-entries might drastically improve or worsen performance depending on volatility.  
- Example fix: If the second short entry is too small (~0.06 USDT), raise `InitialQtyPctShort` or `QtyFactorShort` to approach ~0.5 USDT for subsequent partials.

---

## 3. Suggestions for Strategy Improvements

1. **Adaptive Param Tuning**  
   - Many strategies rely on fixed thresholds (`MinReentryPositionDistance`, `StandardDeviation`, etc.). Making them adapt based on real-time volatility, e.g. ATR-based gating, could reduce both overtrading in sideways markets and undertrading in fast markets.

2. **Multi-indicator Confirmation**  
   - Combining signals from multiple strategies might reduce false positives. For instance, AutoHedge can detect volume peaks, while Mona might supply pivot cluster levels.

3. **Partial Take-profits**  
   - Instead of a single `MinProfitRate` or one RSI threshold, allow for partial profit captures in stages. That approach can lock in gains while letting the rest of the position run.

4. **Stop-loss / Unstucking Enhancements**  
   - Evaluate the “unstucking” code across all strategies (e.g., forcibly closing losing positions if they exceed a certain drawdown or time limit). Add more nuanced logic—like trailing stops, or forced kills if correlated market movements turn strongly against a strategy.

5. **Enhanced Logging and Live Analysis**  
   - Provide real-time logs of which signals triggered and why, to speed up debugging.  
   - Possibly implement a real-time chart overlay (outside .NET or with a library) to see the channels, fractal bases, regression lines, and cluster modes.

6. **Expanded Dockerization**  
   - For multi-strategy or multi-instance setups, confirm that each strategy can run in an isolated container or environment with an appropriate container_name or project name.  
   - This ensures no naming conflicts and can allow multiple strategies or multiple versions of the same strategy to run side by side for live A/B testing.

In all cases, thorough backtesting on relevant pairs (e.g. BTC/USDT, ETH/USDT) plus forward testing (demo environment or small real capital) is recommended before scaling up.


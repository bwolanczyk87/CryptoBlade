# CryptoBlade Strategies Documentation

This document provides a high‑level overview of the CryptoBlade project. It covers the project’s goal, the structure and interconnections of its main modules, detailed descriptions of each trading strategy (with code references), and a concrete roadmap of next‑steps to improve and deploy the system.

## Table of Contents

- [Project Goal](#project-goal)
- [Project Structure – Main Modules and Their Relationships](#project-structure)
  - [Strategies Module](#strategies-module)
  - [Exchanges Module](#exchanges-module)
  - [Backtesting Module](#backtesting-module)
  - [Optimizer Module](#optimizer-module)
  - [Wallet Module](#wallet-module)
  - [Configuration Module](#configuration-module)
- [Map of Connections and References](#map-of-connections-and-references)
- [Detailed Description of Each Strategy](#detailed-description-of-each-strategy)
  - [AutoHedge Strategy](#autohedge-strategy)
  - [Linear Regression Strategy](#linear-regression-strategy)
  - [Tartaglia Strategy](#tartaglia-strategy)
  - [Mona Strategy](#mona-strategy)
  - [MfiRsiCandlePrecise Trading Strategy](#mfirsi-candleprecise-trading-strategy)
  - [MfiRsiEriTrend Trading Strategy](#mfirsi-eri-trend-trading-strategy)
  - [Qiqi Strategy](#qiqi-strategy)
- [Key Processes and Model of Operation](#key-processes-and-model-of-operation)
- [Concrete Steps to Improve and Deploy the Project](#concrete-steps-to-improve-and-deploy-the-project)
- [Conclusion](#conclusion)

---

## Project Goal

The primary goal of CryptoBlade is to maximize profits on a Bybit (and optionally Binance) account by running automated trading bots. These bots, built in .NET and enhanced by artificial intelligence techniques, analyze live market data, generate trade signals based on technical indicators and statistical models, and execute trading requests to the exchange.

---

## Project Structure

### Strategies Module
- **Purpose:**  
  Contains multiple trading strategy implementations that determine when and how to enter/exit trades.
- **Key Components:**  
  - *AutoHedgeStrategy*  
  - *LinearRegressionStrategy*  
  - *TartagliaStrategy*  
  - *MonaStrategy*  
  - *MfiRsiCandlePreciseTradingStrategy*  
  - *MfiRsiEriTrendTradingStrategy*  
  - *QiqiStrategy*
- **Interconnections:**  
  Each strategy inherits from a common base (e.g. `TradingStrategyBase` or `TradingStrategyCommonBase`) and leverages shared helper methods and indicator calculations.

### Exchanges Module
- **Purpose:**  
  Provides clients to interact with the exchanges (Bybit and Binance). These classes handle both REST and socket communications.
- **Key Components:**  
  - *BybitCbFuturesRestClient*  
  - *BybitCbFuturesSocketClient*  
  - *BinanceCbFuturesRestClient*
- **Interconnections:**  
  Strategies use these clients to obtain market data (candles, tickers, funding rates) and to send order requests.

### Backtesting Module
- **Purpose:**  
  Simulates historical trading to evaluate strategy performance.
- **Key Components:**  
  - *BackTestExchange*  
  - *ProtoHistoricalDataStorage*  
  - *BackTestDataDownloader*
- **Interconnections:**  
  Strategies and optimizer modules use historical data to simulate trading performance and fine‑tune parameters.

### Optimizer Module
- **Purpose:**  
  Uses genetic algorithms (via GeneticSharp) to optimize strategy parameters based on backtest performance.
- **Key Components:**  
  - *GeneticAlgorithmOptimizer*  
  - *Chromosome classes* (e.g. `TartagliaChromosome`, `AutoHedgeChromosome`, `MfiRsiTrendChromosome`, `QiqiChromosome`)
- **Interconnections:**  
  The optimizer adjusts strategy parameters by repeatedly executing backtests and evaluating a fitness function.

### Wallet Module
- **Purpose:**  
  Manages account balance, position exposures, and risk management.
- **Key Components:**  
  - *WalletManager*  
  - *IWalletManager* implementations
- **Interconnections:**  
  Strategies refer to the wallet manager to obtain current balances and calculate dynamic position sizes.

### Configuration Module
- **Purpose:**  
  Centralizes configuration settings (typically from *appsettings.json* and environment variables).
- **Key Components:**  
  - *TradingBotOptions*  
  - Strategy‑specific options classes (e.g. `LinearRegressionStrategyOptions`, `MfiRsiEriTrendTradingStrategyOptions`)
- **Interconnections:**  
  All other modules obtain configuration via dependency injection (DI) using these classes.

---

## Map of Connections and References

- **Strategies ↔ Exchanges:**  
  Strategies invoke methods from REST and Socket client classes (e.g. `GetKlinesAsync`, `GetTickerAsync`, `PlaceLimitBuyOrderAsync`) to obtain market data and execute orders.
  
- **Strategies ↔ Wallet:**  
  Strategies use the wallet manager to access current account balances and to compute appropriate trade sizes.
  
- **Backtesting ↔ Historical Data Storage:**  
  The backtesting module leverages *ProtoHistoricalDataStorage* (and optionally a caching layer) to load and store historical market data.
  
- **Optimizer ↔ Strategies:**  
  The optimizer instantiates chromosome objects (which encapsulate strategy parameters) and uses a backtest executor to compute a fitness score for each configuration.
  
- **Configuration:**  
  The `TradingBotOptions` and various strategy‑specific options drive the configuration of each module and are injected via DI.

---

## Detailed Description of Each Strategy

### AutoHedge Strategy
- **Class:** `AutoHedgeStrategy`
- **Purpose:**  
  Manages hedged positions by dynamically adjusting orders based on volume, spread, and moving average conditions.
- **Key Logic:**  
  - Uses one‑minute candles to compute indicators (e.g. SMA, spread, volume).  
  - Determines buy and sell signals by comparing the ticker’s best bid/ask prices against calculated moving averages and thresholds.  
  - Includes conditions to add to an existing position if the price moves favorably.
- **Configuration:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`
- **Potential Improvements:**  
  - Refine thresholds and risk parameters via further backtesting.  
  - Enhance dynamic order sizing and risk management logic.

### Linear Regression Strategy
- **Class:** `LinearRegressionStrategy`
- **Purpose:**  
  Uses linear regression to compute expected price levels and generate entry/exit signals.
- **Key Logic:**  
  - Implements regression analysis using Accord.NET’s `OrdinaryLeastSquares` on one‑minute candles.  
  - Defines upper and lower channel boundaries using a standard deviation multiplier.  
  - Generates buy signals when the price is below the lower channel and sell signals when above the upper channel.
- **Configuration:**  
  - `ChannelLength`, `StandardDeviation`, `MinimumVolume`, `MinimumPriceDistance`
- **Potential Improvements:**  
  - Adjust the regression window dynamically based on market volatility.  
  - Experiment with alternative models for trend detection.

### Tartaglia Strategy
- **Class:** `TartagliaStrategy`
- **Purpose:**  
  Applies a channel‑based approach, inspired by Tartaglia’s methods, to detect price extremes.
- **Key Logic:**  
  - Computes separate regression channels for long and short trades using different channel lengths and standard deviation multipliers.  
  - Signals are generated when the ticker price breaches these channels.  
  - Integrates re‑entry distance adjustments based on the regression output.
- **Configuration:**  
  - `ChannelLengthLong`, `ChannelLengthShort`, `StandardDeviationLong`, `StandardDeviationShort`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`
- **Potential Improvements:**  
  - Fine‑tune channel parameters based on live market data.  
  - Integrate volume filters and dynamic thresholds.

### Mona Strategy
- **Class:** `MonaStrategy`
- **Purpose:**  
  Uses clustering techniques (Mean Shift with Gaussian kernel) to identify key support/resistance levels.
- **Key Logic:**  
  - Applies a Mean Shift algorithm to one‑minute candle data to find clustering “modes” (key trading levels).  
  - Signals are generated when the current price crosses below (for longs) or above (for shorts) these clustering levels.
- **Configuration:**  
  - `ClusteringLength`, `BandwidthCoefficient`, `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`, `MfiRsiLookback`
- **Potential Improvements:**  
  - Experiment with different clustering parameters and noise reduction techniques.  
  - Refine entry thresholds based on historical performance.

### MfiRsiCandlePrecise Trading Strategy
- **Class:** `MfiRsiCandlePreciseTradingStrategy`
- **Purpose:**  
  Uses precise oscillator thresholds (MFI and RSI) calculated on one‑minute candles to determine entry signals.
- **Key Logic:**  
  - Computes Money Flow Index (MFI) and Relative Strength Index (RSI) from one‑minute data.  
  - Generates buy signals when both indicators are below predefined thresholds and sell signals when above.  
  - Considers volume and spread to filter signals.
- **Configuration:**  
  - `MinimumVolume`, `MinimumPriceDistance`
- **Potential Improvements:**  
  - Optimize oscillator thresholds and lookback periods.  
  - Combine with additional indicators for confirmation.

### MfiRsiEriTrend Trading Strategy
- **Class:** `MfiRsiEriTrendTradingStrategy`
- **Purpose:**  
  Combines MFI/RSI oscillators with trend analysis using a modified ERI indicator and moving averages.
- **Key Logic:**  
  - Calculates an “MFI trend” over a configurable lookback period.  
  - Retrieves a modified ERI trend and moving average trend to assess the overall market direction.  
  - Generates buy signals when both the MFI trend and overall trend are bullish (and vice‑versa for sell signals).  
  - Provides extra signal conditions for scaling into positions.
- **Configuration:**  
  - `MinimumVolume`, `MinimumPriceDistance`  
  - `MinReentryPositionDistanceLong`, `MinReentryPositionDistanceShort`  
  - `MfiRsiLookbackPeriod`, `UseEriOnly`
- **Potential Improvements:**  
  - Adjust the lookback period dynamically based on market conditions.  
  - Refine re‑entry criteria using volatility measures.

### Qiqi Strategy
- **Class:** `QiqiStrategy`
- **Purpose:**  
  Integrates RSI and fractal (QFL) analysis to identify reversal points and optimize entry/exit.
- **Key Logic:**  
  - Uses multiple time frames (one‑minute, one‑hour, and one‑day) to calculate RSI and fractal bases.  
  - Generates signals when the current price is below computed QFL levels for longs (or above for shorts) and when RSI is in a favorable range.  
  - Dynamically adjusts take profit orders based on RSI changes and time thresholds.
- **Configuration:**  
  - `RsiTakeProfitLong`, `RsiTakeProfitShort`, `QflBellowPercentEnterLong`, `QflAbovePercentEnterShort`  
  - `MaxTimeStuck`, `TakeProfitPercentLong`, `TakeProfitPercentShort`
- **Potential Improvements:**  
  - Further tune RSI and QFL parameters using extensive backtests.  
  - Enhance dynamic risk management based on evolving market conditions.

---

## Key Processes and Model of Operation

CryptoBlade operates through several interconnected processes:

1. **Initialization & Configuration Loading:**  
   - The application loads configuration settings from *appsettings.json* and environment variables.  
   - Dependency injection (DI) wires up services such as strategies, exchange clients, wallet management, backtesting, and optimization modules.  
   - The `TradingStrategyFactory` creates the appropriate strategy instance based on the configured `StrategyName`.

2. **Market Data Acquisition:**  
   - Exchange clients (both REST and socket-based) retrieve market data (candles, tickers, funding rates).  
   - Strategies subscribe to these data streams, processing incoming data through shared helper methods to compute indicators.

3. **Signal Evaluation:**  
   - Each strategy evaluates signals via its overridden `EvaluateSignalsInnerAsync` method.  
   - Common calculations (e.g., volume, spread, moving averages, RSI, MFI, regression, clustering) are performed using helper classes (`TradeSignalHelpers`, `TradingHelpers`, etc.).  
   - The result is a set of boolean signals (buy, sell, extra buy/sell) and associated indicators for further decision‑making.

4. **Trade Execution & Management:**  
   - Based on signal evaluation, strategies execute orders through exchange client methods.  
   - Position sizing is dynamically calculated using wallet balance and configurable exposure settings.  
   - The wallet manager tracks balances and exposures to ensure proper risk management.

5. **Backtesting & Optimization:**  
   - Historical data is acquired and stored via *BackTestDataDownloader* and *ProtoHistoricalDataStorage*.  
   - Backtesting modules simulate strategy performance using historical data.  
   - The optimizer module uses genetic algorithms (via GeneticSharp) to iterate on strategy parameters for enhanced profitability.

---

## Concrete Steps to Improve and Deploy the Project

1. **Code Review and Refactoring:**  
   - Review and refactor signal evaluation logic across strategies.  
   - Extract common indicator and risk management routines into shared helper classes.  
   - Improve error handling and API retry logic.

2. **Parameter Tuning:**  
   - Run comprehensive backtests for each strategy to determine optimal parameter values.  
   - Use the optimizer module to automatically refine strategy parameters.

3. **Version Control and Branching:**  
   - Create a new branch (e.g. `feature/improvements`) to integrate updates.  
   - Commit and push changes for peer review and testing.

4. **Docker Build and Deployment:**  
   - Update and verify the Dockerfile.  
   - Build the Docker image:
     ```bash
     docker build -t cryptoblade:latest .
     ```
   - Run the container in detached mode:
     ```bash
     docker run -d -p 80:80 --restart always --name cryptoblade cryptoblade:latest
     ```

5. **Monitoring and Logging:**  
   - Verify the health endpoint (`/healthz`) and monitor logs:
     ```bash
     docker ps
     docker logs cryptoblade
     ```
   - Set up alerts for API errors and performance issues.

6. **Continuous Deployment:**  
   - Deploy the updated container to your laptop or a dedicated server for 24/7 operation.  
   - Consider using a service manager (e.g. systemd on Linux or Windows Service) to ensure continuous operation and automatic restarts.

7. **Documentation and Dashboard:**  
   - Update project documentation with the detailed design and roadmap.  
   - Consider building a dashboard for real‑time monitoring of bot performance and strategy health.

8. **Backtesting and Live Testing:**  
   - Continuously run backtests and live simulations to validate strategy performance.  
   - Iteratively refine strategies based on performance metrics and market conditions.

---

## Conclusion

CryptoBlade is designed to run automated trading strategies on Bybit (and optionally Binance) to maximize account profits through advanced technical analysis and parameter optimization. By following the steps outlined—from code refactoring, parameter tuning, Docker deployment, and continuous monitoring—you will build a robust, 24/7 trading system that adapts to market conditions and aims to generate consistent profits.

---

*This document serves as a high‑level guide for developers and stakeholders to understand the architecture, functionality, and future roadmap of the CryptoBlade project.*

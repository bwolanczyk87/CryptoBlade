# CryptoBlade – Overview and Action Plan

## Table of Contents

- [1. Project Goal](#1-project-goal)
- [2. Project Structure – Main Modules and Their Connections](#2-project-structure--main-modules-and-their-connections)
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
- The project’s functionality includes sending requests to the exchange’s API to open and close positions continuously, while managing the portfolio on the fly.

---

## 2. Project Structure – Main Modules and Their Connections

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
  - `TradingStrategyBase` / `TradingStrategyCommonBase` – base classes for strategies.
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
  - Uses backtest data to evaluate a strategy’s fitness.

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
  3. Send orders to the exchange’s API (`ExecuteAsync`, `ExecuteUnstuckAsync`).
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
   - Ensure `appsettings.Accounts.json` contains the correct account data (ApiKey/ApiSecret) and the right mode (Live, Backtest, Optimizer).
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

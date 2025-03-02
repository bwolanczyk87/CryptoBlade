
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

---

## Integration with Asystent (ChatGPT)
Asystent must read and learn the Bot language upon startup and apply the defined commands accordingly. If new commands are introduced during a session, they must be added to this documentation at the end of the chat.

---

## Future Enhancements
- Support for batch execution of commands.
- Additional debugging and diagnostic tools.
- Custom user-defined commands for specialized workflows.


# ===== DOCUMENT: GettingStarted.md =====
# CryptoBlade – Overview and Action Plan

## Spis Treści

- [1. Cel Projektu](#1-cel-projektu)
- [2. Struktura Projektu – Główne Moduły i Ich Powiązania](#2-struktura-projektu--główne-moduły-i-ich-powiązania)
  - [A. Moduł Backtestingu](#a-moduł-backtestingu)
  - [B. Moduł Konfiguracji](#b-moduł-konfiguracji)
  - [C. Moduł Optymalizacji](#c-moduł-optymalizacji)
  - [D. Moduł Real-Time Tradingu](#d-moduł-real-time-tradingu)
  - [E. Moduł Zdrowia i Monitorowania](#e-moduł-zdrowia-i-monitorowania)
  - [F. Moduły Pomocnicze i Mapowania](#f-moduły-pomocnicze-i-mapowania)
- [3. Mapa Połączeń i Referencji](#3-mapa-połączeń-i-referencji)
- [4. Kluczowe Procesy i Model Działania](#4-kluczowe-procesy-i-model-działania)
  - [A. Proces Backtestingu](#a-proces-backtestingu)
  - [B. Proces Optymalizacji Parametrów](#b-proces-optymalizacji-parametrów)
  - [C. Proces Real-Time Tradingu](#c-proces-real-time-tradingu)
  - [D. Proces Integracji i Wdrożenia](#d-proces-integracji-i-wdrożenia)
- [5. Konkretna Lista Kroków do Podjęcia](#5-konkretna-lista-kroków-do-podjęcia)
- [Podsumowanie](#podsumowanie)

---

## 1. Cel Projektu

**Główny cel:**
- Stworzenie zautomatyzowanego systemu tradingowego, który generuje zyski na giełdzie Bybit.
- System oparty jest o strategie tradingowe napisane w .NET, z możliwością ich optymalizacji przy użyciu algorytmów genetycznych oraz backtestingu.
- Projekt ma za zadanie wysyłać requesty do API giełdy, aby na bieżąco otwierać i zamykać pozycje oraz zarządzać portfelem.

---

## 2. Struktura Projektu – Główne Moduły i Ich Powiązania

### A. Moduł Backtestingu
- **Funkcje:**
  - Pobieranie historycznych danych (candles, funding rate, ticki) z giełd (Binance, Bybit).
  - Przetwarzanie danych w celu symulacji strategii.
  - Przechowywanie wyników backtestów (BacktestPerformanceResult) oraz monitorowanie efektywności strategii.
- **Kluczowe klasy:**
  - `BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`
  - `BackTestDataDownloader`, `BackTestDataProcessor`
  - `BackTestExchange`, `BackTestPerformanceTracker`

### B. Moduł Konfiguracji
- **Funkcje:**
  - Przechowywanie ustawień dla konta, strategii, backtestingu i optymalizacji.
  - Definicje zakresów wartości dla optymalizacji (np. `OptimizerFloatRange`, `OptimizerIntRange`).
- **Kluczowe elementy:**
  - `TradingBotOptions`, `BackTest`, `CriticalMode`, `DynamicBotCount`
  - Definicje strategii: `StrategyNames`, `StrategySelectPreference`

### C. Moduł Optymalizacji
- **Funkcje:**
  - Optymalizacja parametrów strategii tradingowych przy użyciu algorytmu genetycznego.
  - Reprezentacja genetyczna strategii (chromosomy, geny).
  - Zarządzanie populacją i wyliczanie funkcji fitness.
- **Kluczowe klasy:**
  - `GeneticAlgorithmOptimizer`, `OptimizerBacktestExecutor`
  - Reprezentacja chromosomów: `TradingBotChromosome`, `ComplexChromosome` oraz ich specyficzne implementacje (np. `TartagliaChromosome`, `QiqiChromosome`).

### D. Moduł Real-Time Tradingu
- **Funkcje:**
  - Realizacja strategii tradingowych na żywo (oraz w trybie backtest).
  - Odbieranie danych rynkowych w czasie rzeczywistym (candles, tickery) za pomocą socketów.
  - Generowanie sygnałów wejścia/wyjścia i wysyłanie requestów do API (metody `ExecuteAsync`, `ExecuteUnstuckAsync`).
  - Zarządzanie portfelem (klasa `WalletManager`).
- **Kluczowe klasy:**
  - `TradingStrategyBase` / `TradingStrategyCommonBase` – bazowe klasy strategii.
  - Konkretne strategie: `AutoHedgeStrategy`, `LinearRegressionStrategy`, `TartagliaStrategy`, `MonaStrategy`, `MfiRsiEriTrendTradingStrategy`, `QiqiStrategy`.
  - Menedżery strategii: `DefaultTradingStrategyManager`, `DynamicTradingStrategyManager`, `TradingHostedService`.

### E. Moduł Zdrowia i Monitorowania
- **Funkcje:**
  - Monitorowanie stanu systemu (health checki dla backtestingu i egzekucji strategii).
- **Kluczowe klasy:**
  - `BacktestExecutionHealthCheck`, `TradeExecutionHealthCheck`

### F. Moduły Pomocnicze i Mapowania
- **Funkcje:**
  - Mapowanie danych z API (Binance, Bybit) do wewnętrznych modeli (np. `Candle`, `FundingRate`, `Order`, `Position`, `Ticker`).
  - Zestaw narzędzi pomocniczych: obliczenia wskaźników technicznych, operacje na danych rynkowych.
  - Definicje modeli danych używanych w całym systemie.
  
---

## 3. Mapa Połączeń i Referencji

- **Backtesting** wykorzystuje:
  - Klasy pobierające dane (`BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`).
  - Interfejsy do przechowywania danych (`IHistoricalDataStorage`).
  - Wyniki symulacji przekazywane są do modułu optymalizacji (np. przez `StrategyFitness`).

- **Optymalizacja:**
  - Bazuje na strategiach tradingowych – optymalizuje ich parametry przy użyciu algorytmów genetycznych.
  - Korzysta z danych backtestowych do oceny funkcji fitness.

- **Real-Time Trading:**
  - Integracja z API giełd poprzez REST i Socket (klasy implementujące `ICbFuturesRestClient` i `ICbFuturesSocketClient`).
  - Strategia tradingowa przetwarza dane z rynków (przez `QuoteQueue` i subskrypcje) oraz generuje sygnały do otwierania/zamykania pozycji.
  - Zarządzanie portfelem przez `WalletManager`.

- **Mapowanie i Narzędzia Pomocnicze:**
  - Umożliwiają spójną komunikację między modułami, zapewniając standaryzację modeli danych oraz operacji obliczeniowych.

---

## 4. Kluczowe Procesy i Model Działania

### A. Proces Backtestingu
- **Cel:** Symulacja strategii tradingowych na danych historycznych.
- **Proces:**
  1. Pobieranie danych historycznych (candles, funding rates) z giełd.
  2. Przechowywanie danych w systemie (np. `ProtoHistoricalDataStorage`).
  3. Przetwarzanie danych i symulacja strategii (klasa `BackTestExchange`).
  4. Generowanie raportów wynikowych (klasa `BackTestPerformanceTracker`).
- **Obszary ulepszeń:** Optymalizacja algorytmów przetwarzania danych, poprawa spójności danych, lepsze indeksowanie.

### B. Proces Optymalizacji Parametrów
- **Cel:** Wyznaczenie optymalnych parametrów strategii tradingowych.
- **Proces:**
  1. Reprezentacja parametrów jako geny (chromosomy).
  2. Uruchamianie backtestu dla różnych konfiguracji (poprzez `OptimizerBacktestExecutor`).
  3. Ocena fitness (klasa `StrategyFitness`).
  4. Ewolucja populacji poprzez krzyżowanie, mutację i selekcję.
- **Obszary ulepszeń:** Udoskonalenie funkcji fitness, optymalizacja parametrów mutacji/krzyżowania.

### C. Proces Real-Time Tradingu
- **Cel:** Realizacja strategii tradingowych w czasie rzeczywistym.
- **Proces:**
  1. Odbiór danych rynkowych na żywo (candles, tickery) za pomocą socketów.
  2. Przetwarzanie danych i generowanie sygnałów (metody `EvaluateSignalsInnerAsync`).
  3. Wysyłanie zleceń do API giełdy (metody `ExecuteAsync`, `ExecuteUnstuckAsync`).
  4. Zarządzanie portfelem (klasa `WalletManager`).
- **Obszary ulepszeń:** Optymalizacja logiki sygnalizacji, redukcja opóźnień, usprawnienie obsługi błędów i reconnect.

### D. Proces Integracji i Wdrożenia
- **Cel:** Uruchomienie systemu tradingowego 24h na lokalnym środowisku.
- **Proces:**
  1. Budowanie projektu przy użyciu Dockerfile.
  2. Konfiguracja ustawień (appsettings.json) dla środowiska Live.
  3. Wdrożenie health checks i monitoringu.
  4. Automatyczne uruchomienie i restartowanie kontenera.
- **Obszary ulepszeń:** Lepsza obsługa logów, automatyczne skalowanie, integracja z narzędziami monitorującymi.

---

## 5. Konkretna Lista Kroków do Podjęcia

1. **Audyt i Refaktoryzacja Kodów:**
   - Przejrzyj moduły pobierania i przetwarzania danych (BackTestDataDownloader, HistoricalDataStorage).
   - Zweryfikuj logikę sygnalizacji w strategiach (LinearRegression, Tartaglia, Mona, MfiRsiEriTrend, Qiqi).

2. **Poprawki w Konfiguracji:**
   - Upewnij się, że `appsettings.json` zawiera poprawne dane konta (ApiKey/ApiSecret) oraz odpowiedni tryb (Live, Backtest, Optimizer).
   - Zweryfikuj i ujednolić ustawienia strategii i optymalizatora.

3. **Integracja Kontroli Jakości i Monitoringu:**
   - Sprawdź działanie endpointów health check (np. `/healthz`).
   - Upewnij się, że system logowania (ApplicationLogging) jest odpowiednio skonfigurowany.

4. **Testy Jednostkowe i Integracyjne:**
   - Uruchom wszystkie testy (np. BackTestDataDownloaderTest, BybitHistoricalTradesDownloaderTest, GridHelpersTest, Exchange tests).
   - Dodaj ewentualnie nowe testy integracyjne.

5. **Optymalizacja Modułu Optymalizacji:**
   - Przetestuj działanie algorytmu genetycznego (przez StrategyFitness, GeneticAlgorithmOptimizer).
   - Ulepsz funkcję fitness, aby lepiej odwzorowywała ryzyko i efektywność strategii.

6. **Budowanie i Wdrożenie Obrazu Docker:**
   - Skorzystaj z przygotowanego Dockerfile:
     - Budowanie: `docker build -t cryptoblade:latest .`
     - Uruchomienie: `docker run -d -p 80:80 --restart always --name cryptoblade cryptoblade:latest`
   - Sprawdź, czy kontener działa oraz endpoint `/healthz` odpowiada.

7. **Automatyzacja i Monitoring:**
   - Skonfiguruj automatyczny restart kontenera (restart policy `--restart always`).
   - Ustaw system monitoringu logów oraz health check, aby wykrywać potencjalne błędy.

8. **Wdrożenie do Środowiska Produkcyjnego/Demo:**
   - Wypchnij kod do dedykowanej gałęzi (np. `release/live`).
   - Skonfiguruj CI/CD (np. GitHub Actions, Azure DevOps) do automatycznego budowania obrazu Docker i wdrażania.

---

## Podsumowanie

Projekt CryptoBlade jest złożonym systemem tradingowym integrującym:
- Backtesting danych historycznych.
- Real-Time Trading z dynamicznym zarządzaniem portfelem.
- Optymalizację parametrów strategii przy użyciu algorytmów genetycznych.
- Monitorowanie i zdrowotne endpointy.

Aby osiągnąć główny cel (generowanie zysków na giełdzie Bybit), należy:
1. Dokonać przeglądu i refaktoryzacji kluczowych modułów.
2. Ujednolicić konfigurację oraz przetestować system przy użyciu testów jednostkowych i integracyjnych.
3. Zbudować obraz Docker i wdrożyć system do ciągłej pracy (24h) z automatycznym restartem oraz monitoringiem.

Realizacja powyższych kroków pozwoli na wdrożenie stabilnego, działającego systemu tradingowego, który na bieżąco analizuje rynek i automatycznie wykonuje zlecenia, maksymalizując zyski.

---


# ===== DOCUMENT: Parameters.md =====
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
If a dedicated parameter is introduced for a specific strategy, a separate sub-section with the strategyâ€™s name and its corresponding parameter description will be added. Global parameters apply universally to all strategies, whereas specific parameters may fine-tune individual strategy behavior.


# ===== DOCUMENT: Strategies.md =====
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


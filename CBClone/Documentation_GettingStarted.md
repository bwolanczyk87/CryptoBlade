# CryptoBlade – Overview and Action Plan

## Spis Treœci

- [1. Cel Projektu](#1-cel-projektu)
- [2. Struktura Projektu – G³ówne Modu³y i Ich Powi¹zania](#2-struktura-projektu--g³ówne-modu³y-i-ich-powi¹zania)
  - [A. Modu³ Backtestingu](#a-modu³-backtestingu)
  - [B. Modu³ Konfiguracji](#b-modu³-konfiguracji)
  - [C. Modu³ Optymalizacji](#c-modu³-optymalizacji)
  - [D. Modu³ Real-Time Tradingu](#d-modu³-real-time-tradingu)
  - [E. Modu³ Zdrowia i Monitorowania](#e-modu³-zdrowia-i-monitorowania)
  - [F. Modu³y Pomocnicze i Mapowania](#f-modu³y-pomocnicze-i-mapowania)
- [3. Mapa Po³¹czeñ i Referencji](#3-mapa-po³¹czeñ-i-referencji)
- [4. Kluczowe Procesy i Model Dzia³ania](#4-kluczowe-procesy-i-model-dzia³ania)
  - [A. Proces Backtestingu](#a-proces-backtestingu)
  - [B. Proces Optymalizacji Parametrów](#b-proces-optymalizacji-parametrów)
  - [C. Proces Real-Time Tradingu](#c-proces-real-time-tradingu)
  - [D. Proces Integracji i Wdro¿enia](#d-proces-integracji-i-wdro¿enia)
- [5. Konkretna Lista Kroków do Podjêcia](#5-konkretna-lista-kroków-do-podjêcia)
- [Podsumowanie](#podsumowanie)

---

## 1. Cel Projektu

**G³ówny cel:**
- Stworzenie zautomatyzowanego systemu tradingowego, który generuje zyski na gie³dzie Bybit.
- System oparty jest o strategie tradingowe napisane w .NET, z mo¿liwoœci¹ ich optymalizacji przy u¿yciu algorytmów genetycznych oraz backtestingu.
- Projekt ma za zadanie wysy³aæ requesty do API gie³dy, aby na bie¿¹co otwieraæ i zamykaæ pozycje oraz zarz¹dzaæ portfelem.

---

## 2. Struktura Projektu – G³ówne Modu³y i Ich Powi¹zania

### A. Modu³ Backtestingu
- **Funkcje:**
  - Pobieranie historycznych danych (candles, funding rate, ticki) z gie³d (Binance, Bybit).
  - Przetwarzanie danych w celu symulacji strategii.
  - Przechowywanie wyników backtestów (BacktestPerformanceResult) oraz monitorowanie efektywnoœci strategii.
- **Kluczowe klasy:**
  - `BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`
  - `BackTestDataDownloader`, `BackTestDataProcessor`
  - `BackTestExchange`, `BackTestPerformanceTracker`

### B. Modu³ Konfiguracji
- **Funkcje:**
  - Przechowywanie ustawieñ dla konta, strategii, backtestingu i optymalizacji.
  - Definicje zakresów wartoœci dla optymalizacji (np. `OptimizerFloatRange`, `OptimizerIntRange`).
- **Kluczowe elementy:**
  - `TradingBotOptions`, `BackTest`, `CriticalMode`, `DynamicBotCount`
  - Definicje strategii: `StrategyNames`, `StrategySelectPreference`

### C. Modu³ Optymalizacji
- **Funkcje:**
  - Optymalizacja parametrów strategii tradingowych przy u¿yciu algorytmu genetycznego.
  - Reprezentacja genetyczna strategii (chromosomy, geny).
  - Zarz¹dzanie populacj¹ i wyliczanie funkcji fitness.
- **Kluczowe klasy:**
  - `GeneticAlgorithmOptimizer`, `OptimizerBacktestExecutor`
  - Reprezentacja chromosomów: `TradingBotChromosome`, `ComplexChromosome` oraz ich specyficzne implementacje (np. `TartagliaChromosome`, `QiqiChromosome`).

### D. Modu³ Real-Time Tradingu
- **Funkcje:**
  - Realizacja strategii tradingowych na ¿ywo (oraz w trybie backtest).
  - Odbieranie danych rynkowych w czasie rzeczywistym (candles, tickery) za pomoc¹ socketów.
  - Generowanie sygna³ów wejœcia/wyjœcia i wysy³anie requestów do API (metody `ExecuteAsync`, `ExecuteUnstuckAsync`).
  - Zarz¹dzanie portfelem (klasa `WalletManager`).
- **Kluczowe klasy:**
  - `TradingStrategyBase` / `TradingStrategyCommonBase` – bazowe klasy strategii.
  - Konkretne strategie: `AutoHedgeStrategy`, `LinearRegressionStrategy`, `TartagliaStrategy`, `MonaStrategy`, `MfiRsiEriTrendTradingStrategy`, `QiqiStrategy`.
  - Mened¿ery strategii: `DefaultTradingStrategyManager`, `DynamicTradingStrategyManager`, `TradingHostedService`.

### E. Modu³ Zdrowia i Monitorowania
- **Funkcje:**
  - Monitorowanie stanu systemu (health checki dla backtestingu i egzekucji strategii).
- **Kluczowe klasy:**
  - `BacktestExecutionHealthCheck`, `TradeExecutionHealthCheck`

### F. Modu³y Pomocnicze i Mapowania
- **Funkcje:**
  - Mapowanie danych z API (Binance, Bybit) do wewnêtrznych modeli (np. `Candle`, `FundingRate`, `Order`, `Position`, `Ticker`).
  - Zestaw narzêdzi pomocniczych: obliczenia wskaŸników technicznych, operacje na danych rynkowych.
  - Definicje modeli danych u¿ywanych w ca³ym systemie.
  
---

## 3. Mapa Po³¹czeñ i Referencji

- **Backtesting** wykorzystuje:
  - Klasy pobieraj¹ce dane (`BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`).
  - Interfejsy do przechowywania danych (`IHistoricalDataStorage`).
  - Wyniki symulacji przekazywane s¹ do modu³u optymalizacji (np. przez `StrategyFitness`).

- **Optymalizacja:**
  - Bazuje na strategiach tradingowych – optymalizuje ich parametry przy u¿yciu algorytmów genetycznych.
  - Korzysta z danych backtestowych do oceny funkcji fitness.

- **Real-Time Trading:**
  - Integracja z API gie³d poprzez REST i Socket (klasy implementuj¹ce `ICbFuturesRestClient` i `ICbFuturesSocketClient`).
  - Strategia tradingowa przetwarza dane z rynków (przez `QuoteQueue` i subskrypcje) oraz generuje sygna³y do otwierania/zamykania pozycji.
  - Zarz¹dzanie portfelem przez `WalletManager`.

- **Mapowanie i Narzêdzia Pomocnicze:**
  - Umo¿liwiaj¹ spójn¹ komunikacjê miêdzy modu³ami, zapewniaj¹c standaryzacjê modeli danych oraz operacji obliczeniowych.

---

## 4. Kluczowe Procesy i Model Dzia³ania

### A. Proces Backtestingu
- **Cel:** Symulacja strategii tradingowych na danych historycznych.
- **Proces:**
  1. Pobieranie danych historycznych (candles, funding rates) z gie³d.
  2. Przechowywanie danych w systemie (np. `ProtoHistoricalDataStorage`).
  3. Przetwarzanie danych i symulacja strategii (klasa `BackTestExchange`).
  4. Generowanie raportów wynikowych (klasa `BackTestPerformanceTracker`).
- **Obszary ulepszeñ:** Optymalizacja algorytmów przetwarzania danych, poprawa spójnoœci danych, lepsze indeksowanie.

### B. Proces Optymalizacji Parametrów
- **Cel:** Wyznaczenie optymalnych parametrów strategii tradingowych.
- **Proces:**
  1. Reprezentacja parametrów jako geny (chromosomy).
  2. Uruchamianie backtestu dla ró¿nych konfiguracji (poprzez `OptimizerBacktestExecutor`).
  3. Ocena fitness (klasa `StrategyFitness`).
  4. Ewolucja populacji poprzez krzy¿owanie, mutacjê i selekcjê.
- **Obszary ulepszeñ:** Udoskonalenie funkcji fitness, optymalizacja parametrów mutacji/krzy¿owania.

### C. Proces Real-Time Tradingu
- **Cel:** Realizacja strategii tradingowych w czasie rzeczywistym.
- **Proces:**
  1. Odbiór danych rynkowych na ¿ywo (candles, tickery) za pomoc¹ socketów.
  2. Przetwarzanie danych i generowanie sygna³ów (metody `EvaluateSignalsInnerAsync`).
  3. Wysy³anie zleceñ do API gie³dy (metody `ExecuteAsync`, `ExecuteUnstuckAsync`).
  4. Zarz¹dzanie portfelem (klasa `WalletManager`).
- **Obszary ulepszeñ:** Optymalizacja logiki sygnalizacji, redukcja opóŸnieñ, usprawnienie obs³ugi b³êdów i reconnect.

### D. Proces Integracji i Wdro¿enia
- **Cel:** Uruchomienie systemu tradingowego 24h na lokalnym œrodowisku.
- **Proces:**
  1. Budowanie projektu przy u¿yciu Dockerfile.
  2. Konfiguracja ustawieñ (appsettings.json) dla œrodowiska Live.
  3. Wdro¿enie health checks i monitoringu.
  4. Automatyczne uruchomienie i restartowanie kontenera.
- **Obszary ulepszeñ:** Lepsza obs³uga logów, automatyczne skalowanie, integracja z narzêdziami monitoruj¹cymi.

---

## 5. Konkretna Lista Kroków do Podjêcia

1. **Audyt i Refaktoryzacja Kodów:**
   - Przejrzyj modu³y pobierania i przetwarzania danych (BackTestDataDownloader, HistoricalDataStorage).
   - Zweryfikuj logikê sygnalizacji w strategiach (LinearRegression, Tartaglia, Mona, MfiRsiEriTrend, Qiqi).

2. **Poprawki w Konfiguracji:**
   - Upewnij siê, ¿e `appsettings.json` zawiera poprawne dane konta (ApiKey/ApiSecret) oraz odpowiedni tryb (Live, Backtest, Optimizer).
   - Zweryfikuj i ujednoliæ ustawienia strategii i optymalizatora.

3. **Integracja Kontroli Jakoœci i Monitoringu:**
   - SprawdŸ dzia³anie endpointów health check (np. `/healthz`).
   - Upewnij siê, ¿e system logowania (ApplicationLogging) jest odpowiednio skonfigurowany.

4. **Testy Jednostkowe i Integracyjne:**
   - Uruchom wszystkie testy (np. BackTestDataDownloaderTest, BybitHistoricalTradesDownloaderTest, GridHelpersTest, Exchange tests).
   - Dodaj ewentualnie nowe testy integracyjne.

5. **Optymalizacja Modu³u Optymalizacji:**
   - Przetestuj dzia³anie algorytmu genetycznego (przez StrategyFitness, GeneticAlgorithmOptimizer).
   - Ulepsz funkcjê fitness, aby lepiej odwzorowywa³a ryzyko i efektywnoœæ strategii.

6. **Budowanie i Wdro¿enie Obrazu Docker:**
   - Skorzystaj z przygotowanego Dockerfile:
     - Budowanie: `docker build -t cryptoblade:latest .`
     - Uruchomienie: `docker run -d -p 80:80 --restart always --name cryptoblade cryptoblade:latest`
   - SprawdŸ, czy kontener dzia³a oraz endpoint `/healthz` odpowiada.

7. **Automatyzacja i Monitoring:**
   - Skonfiguruj automatyczny restart kontenera (restart policy `--restart always`).
   - Ustaw system monitoringu logów oraz health check, aby wykrywaæ potencjalne b³êdy.

8. **Wdro¿enie do Œrodowiska Produkcyjnego/Demo:**
   - Wypchnij kod do dedykowanej ga³êzi (np. `release/live`).
   - Skonfiguruj CI/CD (np. GitHub Actions, Azure DevOps) do automatycznego budowania obrazu Docker i wdra¿ania.

---

## Podsumowanie

Projekt CryptoBlade jest z³o¿onym systemem tradingowym integruj¹cym:
- Backtesting danych historycznych.
- Real-Time Trading z dynamicznym zarz¹dzaniem portfelem.
- Optymalizacjê parametrów strategii przy u¿yciu algorytmów genetycznych.
- Monitorowanie i zdrowotne endpointy.

Aby osi¹gn¹æ g³ówny cel (generowanie zysków na gie³dzie Bybit), nale¿y:
1. Dokonaæ przegl¹du i refaktoryzacji kluczowych modu³ów.
2. Ujednoliciæ konfiguracjê oraz przetestowaæ system przy u¿yciu testów jednostkowych i integracyjnych.
3. Zbudowaæ obraz Docker i wdro¿yæ system do ci¹g³ej pracy (24h) z automatycznym restartem oraz monitoringiem.

Realizacja powy¿szych kroków pozwoli na wdro¿enie stabilnego, dzia³aj¹cego systemu tradingowego, który na bie¿¹co analizuje rynek i automatycznie wykonuje zlecenia, maksymalizuj¹c zyski.

---

# CryptoBlade � Overview and Action Plan

## Spis Tre�ci

- [1. Cel Projektu](#1-cel-projektu)
- [2. Struktura Projektu � G��wne Modu�y i Ich Powi�zania](#2-struktura-projektu--g��wne-modu�y-i-ich-powi�zania)
  - [A. Modu� Backtestingu](#a-modu�-backtestingu)
  - [B. Modu� Konfiguracji](#b-modu�-konfiguracji)
  - [C. Modu� Optymalizacji](#c-modu�-optymalizacji)
  - [D. Modu� Real-Time Tradingu](#d-modu�-real-time-tradingu)
  - [E. Modu� Zdrowia i Monitorowania](#e-modu�-zdrowia-i-monitorowania)
  - [F. Modu�y Pomocnicze i Mapowania](#f-modu�y-pomocnicze-i-mapowania)
- [3. Mapa Po��cze� i Referencji](#3-mapa-po��cze�-i-referencji)
- [4. Kluczowe Procesy i Model Dzia�ania](#4-kluczowe-procesy-i-model-dzia�ania)
  - [A. Proces Backtestingu](#a-proces-backtestingu)
  - [B. Proces Optymalizacji Parametr�w](#b-proces-optymalizacji-parametr�w)
  - [C. Proces Real-Time Tradingu](#c-proces-real-time-tradingu)
  - [D. Proces Integracji i Wdro�enia](#d-proces-integracji-i-wdro�enia)
- [5. Konkretna Lista Krok�w do Podj�cia](#5-konkretna-lista-krok�w-do-podj�cia)
- [Podsumowanie](#podsumowanie)

---

## 1. Cel Projektu

**G��wny cel:**
- Stworzenie zautomatyzowanego systemu tradingowego, kt�ry generuje zyski na gie�dzie Bybit.
- System oparty jest o strategie tradingowe napisane w .NET, z mo�liwo�ci� ich optymalizacji przy u�yciu algorytm�w genetycznych oraz backtestingu.
- Projekt ma za zadanie wysy�a� requesty do API gie�dy, aby na bie��co otwiera� i zamyka� pozycje oraz zarz�dza� portfelem.

---

## 2. Struktura Projektu � G��wne Modu�y i Ich Powi�zania

### A. Modu� Backtestingu
- **Funkcje:**
  - Pobieranie historycznych danych (candles, funding rate, ticki) z gie�d (Binance, Bybit).
  - Przetwarzanie danych w celu symulacji strategii.
  - Przechowywanie wynik�w backtest�w (BacktestPerformanceResult) oraz monitorowanie efektywno�ci strategii.
- **Kluczowe klasy:**
  - `BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`
  - `BackTestDataDownloader`, `BackTestDataProcessor`
  - `BackTestExchange`, `BackTestPerformanceTracker`

### B. Modu� Konfiguracji
- **Funkcje:**
  - Przechowywanie ustawie� dla konta, strategii, backtestingu i optymalizacji.
  - Definicje zakres�w warto�ci dla optymalizacji (np. `OptimizerFloatRange`, `OptimizerIntRange`).
- **Kluczowe elementy:**
  - `TradingBotOptions`, `BackTest`, `CriticalMode`, `DynamicBotCount`
  - Definicje strategii: `StrategyNames`, `StrategySelectPreference`

### C. Modu� Optymalizacji
- **Funkcje:**
  - Optymalizacja parametr�w strategii tradingowych przy u�yciu algorytmu genetycznego.
  - Reprezentacja genetyczna strategii (chromosomy, geny).
  - Zarz�dzanie populacj� i wyliczanie funkcji fitness.
- **Kluczowe klasy:**
  - `GeneticAlgorithmOptimizer`, `OptimizerBacktestExecutor`
  - Reprezentacja chromosom�w: `TradingBotChromosome`, `ComplexChromosome` oraz ich specyficzne implementacje (np. `TartagliaChromosome`, `QiqiChromosome`).

### D. Modu� Real-Time Tradingu
- **Funkcje:**
  - Realizacja strategii tradingowych na �ywo (oraz w trybie backtest).
  - Odbieranie danych rynkowych w czasie rzeczywistym (candles, tickery) za pomoc� socket�w.
  - Generowanie sygna��w wej�cia/wyj�cia i wysy�anie request�w do API (metody `ExecuteAsync`, `ExecuteUnstuckAsync`).
  - Zarz�dzanie portfelem (klasa `WalletManager`).
- **Kluczowe klasy:**
  - `TradingStrategyBase` / `TradingStrategyCommonBase` � bazowe klasy strategii.
  - Konkretne strategie: `AutoHedgeStrategy`, `LinearRegressionStrategy`, `TartagliaStrategy`, `MonaStrategy`, `MfiRsiEriTrendTradingStrategy`, `QiqiStrategy`.
  - Mened�ery strategii: `DefaultTradingStrategyManager`, `DynamicTradingStrategyManager`, `TradingHostedService`.

### E. Modu� Zdrowia i Monitorowania
- **Funkcje:**
  - Monitorowanie stanu systemu (health checki dla backtestingu i egzekucji strategii).
- **Kluczowe klasy:**
  - `BacktestExecutionHealthCheck`, `TradeExecutionHealthCheck`

### F. Modu�y Pomocnicze i Mapowania
- **Funkcje:**
  - Mapowanie danych z API (Binance, Bybit) do wewn�trznych modeli (np. `Candle`, `FundingRate`, `Order`, `Position`, `Ticker`).
  - Zestaw narz�dzi pomocniczych: obliczenia wska�nik�w technicznych, operacje na danych rynkowych.
  - Definicje modeli danych u�ywanych w ca�ym systemie.
  
---

## 3. Mapa Po��cze� i Referencji

- **Backtesting** wykorzystuje:
  - Klasy pobieraj�ce dane (`BinanceHistoricalDataDownloader`, `BybitHistoricalDataDownloader`).
  - Interfejsy do przechowywania danych (`IHistoricalDataStorage`).
  - Wyniki symulacji przekazywane s� do modu�u optymalizacji (np. przez `StrategyFitness`).

- **Optymalizacja:**
  - Bazuje na strategiach tradingowych � optymalizuje ich parametry przy u�yciu algorytm�w genetycznych.
  - Korzysta z danych backtestowych do oceny funkcji fitness.

- **Real-Time Trading:**
  - Integracja z API gie�d poprzez REST i Socket (klasy implementuj�ce `ICbFuturesRestClient` i `ICbFuturesSocketClient`).
  - Strategia tradingowa przetwarza dane z rynk�w (przez `QuoteQueue` i subskrypcje) oraz generuje sygna�y do otwierania/zamykania pozycji.
  - Zarz�dzanie portfelem przez `WalletManager`.

- **Mapowanie i Narz�dzia Pomocnicze:**
  - Umo�liwiaj� sp�jn� komunikacj� mi�dzy modu�ami, zapewniaj�c standaryzacj� modeli danych oraz operacji obliczeniowych.

---

## 4. Kluczowe Procesy i Model Dzia�ania

### A. Proces Backtestingu
- **Cel:** Symulacja strategii tradingowych na danych historycznych.
- **Proces:**
  1. Pobieranie danych historycznych (candles, funding rates) z gie�d.
  2. Przechowywanie danych w systemie (np. `ProtoHistoricalDataStorage`).
  3. Przetwarzanie danych i symulacja strategii (klasa `BackTestExchange`).
  4. Generowanie raport�w wynikowych (klasa `BackTestPerformanceTracker`).
- **Obszary ulepsze�:** Optymalizacja algorytm�w przetwarzania danych, poprawa sp�jno�ci danych, lepsze indeksowanie.

### B. Proces Optymalizacji Parametr�w
- **Cel:** Wyznaczenie optymalnych parametr�w strategii tradingowych.
- **Proces:**
  1. Reprezentacja parametr�w jako geny (chromosomy).
  2. Uruchamianie backtestu dla r�nych konfiguracji (poprzez `OptimizerBacktestExecutor`).
  3. Ocena fitness (klasa `StrategyFitness`).
  4. Ewolucja populacji poprzez krzy�owanie, mutacj� i selekcj�.
- **Obszary ulepsze�:** Udoskonalenie funkcji fitness, optymalizacja parametr�w mutacji/krzy�owania.

### C. Proces Real-Time Tradingu
- **Cel:** Realizacja strategii tradingowych w czasie rzeczywistym.
- **Proces:**
  1. Odbi�r danych rynkowych na �ywo (candles, tickery) za pomoc� socket�w.
  2. Przetwarzanie danych i generowanie sygna��w (metody `EvaluateSignalsInnerAsync`).
  3. Wysy�anie zlece� do API gie�dy (metody `ExecuteAsync`, `ExecuteUnstuckAsync`).
  4. Zarz�dzanie portfelem (klasa `WalletManager`).
- **Obszary ulepsze�:** Optymalizacja logiki sygnalizacji, redukcja op�nie�, usprawnienie obs�ugi b��d�w i reconnect.

### D. Proces Integracji i Wdro�enia
- **Cel:** Uruchomienie systemu tradingowego 24h na lokalnym �rodowisku.
- **Proces:**
  1. Budowanie projektu przy u�yciu Dockerfile.
  2. Konfiguracja ustawie� (appsettings.json) dla �rodowiska Live.
  3. Wdro�enie health checks i monitoringu.
  4. Automatyczne uruchomienie i restartowanie kontenera.
- **Obszary ulepsze�:** Lepsza obs�uga log�w, automatyczne skalowanie, integracja z narz�dziami monitoruj�cymi.

---

## 5. Konkretna Lista Krok�w do Podj�cia

1. **Audyt i Refaktoryzacja Kod�w:**
   - Przejrzyj modu�y pobierania i przetwarzania danych (BackTestDataDownloader, HistoricalDataStorage).
   - Zweryfikuj logik� sygnalizacji w strategiach (LinearRegression, Tartaglia, Mona, MfiRsiEriTrend, Qiqi).

2. **Poprawki w Konfiguracji:**
   - Upewnij si�, �e `appsettings.json` zawiera poprawne dane konta (ApiKey/ApiSecret) oraz odpowiedni tryb (Live, Backtest, Optimizer).
   - Zweryfikuj i ujednoli� ustawienia strategii i optymalizatora.

3. **Integracja Kontroli Jako�ci i Monitoringu:**
   - Sprawd� dzia�anie endpoint�w health check (np. `/healthz`).
   - Upewnij si�, �e system logowania (ApplicationLogging) jest odpowiednio skonfigurowany.

4. **Testy Jednostkowe i Integracyjne:**
   - Uruchom wszystkie testy (np. BackTestDataDownloaderTest, BybitHistoricalTradesDownloaderTest, GridHelpersTest, Exchange tests).
   - Dodaj ewentualnie nowe testy integracyjne.

5. **Optymalizacja Modu�u Optymalizacji:**
   - Przetestuj dzia�anie algorytmu genetycznego (przez StrategyFitness, GeneticAlgorithmOptimizer).
   - Ulepsz funkcj� fitness, aby lepiej odwzorowywa�a ryzyko i efektywno�� strategii.

6. **Budowanie i Wdro�enie Obrazu Docker:**
   - Skorzystaj z przygotowanego Dockerfile:
     - Budowanie: `docker build -t cryptoblade:latest .`
     - Uruchomienie: `docker run -d -p 80:80 --restart always --name cryptoblade cryptoblade:latest`
   - Sprawd�, czy kontener dzia�a oraz endpoint `/healthz` odpowiada.

7. **Automatyzacja i Monitoring:**
   - Skonfiguruj automatyczny restart kontenera (restart policy `--restart always`).
   - Ustaw system monitoringu log�w oraz health check, aby wykrywa� potencjalne b��dy.

8. **Wdro�enie do �rodowiska Produkcyjnego/Demo:**
   - Wypchnij kod do dedykowanej ga��zi (np. `release/live`).
   - Skonfiguruj CI/CD (np. GitHub Actions, Azure DevOps) do automatycznego budowania obrazu Docker i wdra�ania.

---

## Podsumowanie

Projekt CryptoBlade jest z�o�onym systemem tradingowym integruj�cym:
- Backtesting danych historycznych.
- Real-Time Trading z dynamicznym zarz�dzaniem portfelem.
- Optymalizacj� parametr�w strategii przy u�yciu algorytm�w genetycznych.
- Monitorowanie i zdrowotne endpointy.

Aby osi�gn�� g��wny cel (generowanie zysk�w na gie�dzie Bybit), nale�y:
1. Dokona� przegl�du i refaktoryzacji kluczowych modu��w.
2. Ujednolici� konfiguracj� oraz przetestowa� system przy u�yciu test�w jednostkowych i integracyjnych.
3. Zbudowa� obraz Docker i wdro�y� system do ci�g�ej pracy (24h) z automatycznym restartem oraz monitoringiem.

Realizacja powy�szych krok�w pozwoli na wdro�enie stabilnego, dzia�aj�cego systemu tradingowego, kt�ry na bie��co analizuje rynek i automatycznie wykonuje zlecenia, maksymalizuj�c zyski.

---

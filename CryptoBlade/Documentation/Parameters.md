# Parameters.md

Poniżej znajduje się zestawienie wszystkich zmiennych występujących w pliku **Data_Strategies_Mona_Live_live.json**, wraz z opisem przeznaczenia i dwoma przykładowymi sposobami użycia każdej zmiennej.  
Dokument został przygotowany w formacie **Markdown**.

---

## 1. Accounts (tablica obiektów)

**Opis i przeznaczenie:**
- Zawiera listę konfiguracji kont giełdowych, na których bot może wykonywać operacje.
- Każdy obiekt w tablicy określa dane uwierzytelniające i parametry połączenia z danym kontem lub subkontem (API Key, ApiSecret, nazwa konta, giełda, tryb demo).

**Przykłady użycia:**
1. Dodanie nowego konta do tablicy, np. w celu obsługi innej giełdy, uzupełniając wartości `ApiKey` i `ApiSecret`.
2. Ustawienie `IsDemo = true` pozwala testować strategię na koncie testowym w trybie demonstracyjnym.

---

### Accounts -> Name

- **Opis**: Nazwa konta, identyfikująca je w konfiguracji.
- **Przykłady użycia**:
  1. Ustawienie `Name` na `"CryptoBlade_Subaccount"` w celu rozróżnienia głównego konta i subkonta.
  2. Zmiana nazwy na `"CryptoBlade_Test"` do szybkiej identyfikacji konta testowego.

### Accounts -> ApiKey

- **Opis**: Klucz API (publiczny) używany do autoryzacji na giełdzie.
- **Przykłady użycia**:
  1. Wpisanie klucza wygenerowanego na Bybit, by bot mógł składać zlecenia w naszym imieniu.
  2. Ustawienie pustego stringa, jeśli chcemy zablokować możliwości tradingowe (np. tymczasowe wyłączenie operacji).

### Accounts -> ApiSecret

- **Opis**: Sekretny klucz API wykorzystywany do podpisywania żądań i potwierdzenia autentyczności.
- **Przykłady użycia**:
  1. Wypełnienie `ApiSecret` zgodnie z danymi z giełdy Bybit, aby bot miał pełny dostęp do konta.
  2. Pozostawienie pustego pola w środowisku lokalnym, aby uniknąć ryzyka wycieku klucza w repozytorium.

### Accounts -> Exchange

- **Opis**: Nazwa giełdy, do której odnosi się dane API (np. Bybit).
- **Przykłady użycia**:
  1. Ustawienie na `"Bybit"` do handlu kontraktami USDT na Bybit.
  2. Rozszerzenie w przyszłości o `"Binance"`, gdyby bot miał także obsługiwać inną giełdę.

### Accounts -> IsDemo

- **Opis**: Informacja, czy konto jest trybem demo (true) czy produkcyjnym (false).
- **Przykłady użycia**:
  1. `IsDemo = true` podczas testowania, aby nie ryzykować prawdziwych środków.
  2. `IsDemo = false` na koncie głównym, aby strategia handlowała realnymi środkami.

---

## 2. AccountName

- **Opis**: Nazwa konta, z którego bot skorzysta domyślnie (musi pokrywać się z polem `Name` w którymś obiekcie `Accounts`).
- **Przykłady użycia**:
  1. Ustawienie wartości `"Demo"` w celu rozpoczęcia pracy na koncie demo.
  2. Przełączenie na `"CryptoBlade_Subaccount"`, aby aktywować pracę na subkoncie produkcyjnym.

---

## 3. BotMode

- **Opis**: Główny tryb pracy bota – np. `Live`, `Backtest` lub `Optimizer`.
- **Przykłady użycia**:
  1. Ustawienie `Live` do uruchomienia w czasie rzeczywistym.
  2. Ustawienie `Backtest` do uruchamiania backtestów offline (jeśli plik do tego służyłby).

---

## 4. StrategyName

- **Opis**: Nazwa aktywnej strategii, z której bot będzie korzystał (np. `"Mona"`).
- **Przykłady użycia**:
  1. Zmiana na `"AutoHedge"`, by użyć innej strategii w kodzie.
  2. Ustawienie `"Mona"` do działania z parametrami klastrowania, MFI, RSI itp.

---

## 5. TradingMode

- **Opis**: Szczegółowy tryb handlu (np. `Dynamic`), określa sposób zarządzania pozycjami.
- **Przykłady użycia**:
  1. `TradingMode = "Dynamic"` pozwala tworzyć wiele strategii jednocześnie w zależności od warunków rynkowych.
  2. `TradingMode = "Readonly"` (lub inny) może pozwolić wyłącznie monitorować rynek bez otwierania pozycji.

---

## 6. MaxRunningStrategies

- **Opis**: Maksymalna liczba strategii (instancji) działających równocześnie.
- **Przykłady użycia**:
  1. Ustawienie na 100, aby ograniczyć jednoczesne otwarte pozycje do 100 strategii.
  2. Zmniejszenie do 10 w celu bardziej zachowawczego zarządzania kapitałem.

---

## 7. DcaOrdersCount

- **Opis**: Liczba możliwych zleceń typu DCA (Dollar Cost Averaging) dla pojedynczej strategii.
- **Przykłady użycia**:
  1. Ustawienie na 1000, aby strategia mogła wielokrotnie dokładać do pozycji.
  2. Redukcja do 10 w strategii testowej, by ograniczyć liczbę dokupień.

---

## 8. DynamicBotCount (obiekt)

**Opis**: Parametry dotyczące dynamicznego zarządzania liczbą strategii oraz ekspozycją na long i short.

### DynamicBotCount -> TargetLongExposure

- **Opis**: Docelowa ekspozycja (w jednostkach waluty, np. USDT) dla pozycji long we wszystkich strategiach łącznie.
- **Przykłady użycia**:
  1. Ustawienie `300.0` przy posiadaniu 1000 USDT, aby łączna suma zaangażowanych środków w long nie przekraczała 300 USDT.
  2. Zmiana na mniejszą wartość typu 50, by ograniczyć ryzyko w trendzie spadkowym.

### DynamicBotCount -> TargetShortExposure

- **Opis**: Docelowa ekspozycja (waluta quote) dla pozycji short we wszystkich strategiach razem.
- **Przykłady użycia**:
  1. Ustawienie `300.0` w przypadku chęci agresywniejszego zabezpieczania portfela.
  2. Obniżenie do 100, jeśli spodziewamy się, że short stanowić ma tylko uzupełnienie strategii.

### DynamicBotCount -> MaxLongStrategies

- **Opis**: Maksymalna liczba jednoczesnych strategii w pozycji long.
- **Przykłady użycia**:
  1. `MaxLongStrategies = 20` oznacza, że nie będzie więcej niż 20 różnych symboli (lub instancji) w long.
  2. Zmiana na 5 przy skromniejszym portfelu.

### DynamicBotCount -> MaxShortStrategies

- **Opis**: Maksymalna liczba jednoczesnych strategii w pozycji short.
- **Przykłady użycia**:
  1. `MaxShortStrategies = 20` przy dużym wolumenie i tolerancji na shorty.
  2. Zmiana na 2, gdy shorty traktujemy tylko jako okazjonalne zabezpieczenie.

### DynamicBotCount -> MaxDynamicStrategyOpenPerStep

- **Opis**: Limit nowych strategii (otwarć) w danym przedziale czasowym (krok) dla dynamicznego trybu.
- **Przykłady użycia**:
  1. `MaxDynamicStrategyOpenPerStep = 20` pozwoli na agresywne otwieranie wielu pozycji w ciągu minuty.
  2. `MaxDynamicStrategyOpenPerStep = 1` gdy chcemy stopniowo dodawać kolejne pozycje.

### DynamicBotCount -> Step

- **Opis**: Czasowy interwał (np. `"00:01:00"`) w którym sprawdzany jest limit nowych strategii (MaxDynamicStrategyOpenPerStep).
- **Przykłady użycia**:
  1. Ustawienie `Step = "00:01:00"`, czyli co minutę jest liczony limit otwarć.
  2. `Step = "00:05:00"`, aby co 5 minut resetować możliwość otwierania nowych strategii.

---

## 9. WalletExposureLong

- **Opis**: Maksymalna ekspozycja w walucie quote na jedną pozycję long (lub sposób interpretacji łącznej ekspozycji w pewnych trybach).
- **Przykłady użycia**:
  1. `300.0` przy portfelu 1000 USDT, co pozwala jednej transakcji long użyć do 300 USDT.
  2. `50.0` jeśli chcemy, aby maksymalny rozmiar pozycji był mniejszy.

---

## 10. WalletExposureShort

- **Opis**: Maksymalna ekspozycja (w walucie quote, np. USDT) na jedną pozycję short.
- **Przykłady użycia**:
  1. Ustawienie `300.0` zapewnia, że short nie przekroczy 300 USDT w pojedynczej pozycji.
  2. Redukcja do 20.0, by otwierać niewielkie shorty w bardzo konserwatywnym stylu.

---

## 11. MinimumVolume

- **Opis**: Minimalny wymagany wolumen (np. dzienny) dla symbolu, aby strategia w ogóle rozważyła handel na tym rynku.
- **Przykłady użycia**:
  1. `15000.0` oznacza, że bot pominie pary z wolumenem niższym niż 15k USDT/24h.
  2. Zwiększenie do `100000.0` aby ograniczyć się do najbardziej płynnych rynków.

---

## 12. MinimumPriceDistance

- **Opis**: Minimalny wymagany "spread" cenowy lub minimalna odległość procentowa, aby zainicjować wejście.
- **Przykłady użycia**:
  1. `0.015` to 1.5% różnicy, co zapobiega otwieraniu pozycji w mikrowahaniach.
  2. `0.005` w strategiach skalpingowych z mniejszym oczekiwanym spreadem.

---

## 13. ForceMinQty

- **Opis**: Flaga (true/false), czy wymuszać minimalną wielkość zleceń wymaganych przez giełdę.
- **Przykłady użycia**:
  1. `true` w strategiach, które muszą zawsze otwierać pozycje na minimalny wymóg giełdy.
  2. `false` jeśli algorytm sam dobiera wielkość w zależności od wolumenów i kapitału.

---

## 14. QtyFactorLong

- **Opis**: Mnożnik wielkości pozycji długiej względem bazowej kalkulacji ilości.
- **Przykłady użycia**:
  1. `200.0` – każda pozycja long będzie 200 razy większa niż bazowo wyliczona (bardzo wysoka dźwignia kapitału).
  2. `1.0` – rozmiar pozycji ustalany jest bazowo, bez mnożenia.

---

## 15. QtyFactorShort

- **Opis**: Mnożnik wielkości pozycji krótkiej względem bazowej kalkulacji ilości.
- **Przykłady użycia**:
  1. `200` – każda pozycja short będzie wielokrotnie większa od standardowej wielkości.
  2. `0.5` – shorty będą o połowę mniejsze niż normalnie wyliczona kwota.

---

## 16. EnableRecursiveQtyFactorLong

- **Opis**: Określa, czy bot stosuje rekurencyjne obliczanie wielkości pozycji long (zachowania DCA) czy nie.
- **Przykłady użycia**:
  1. `true`, jeśli strategia ma dynamicznie zwiększać (dokładać) pozycje wraz z ruchem rynku.
  2. `false`, jeśli mamy stały przyrost pozycji bez zaawansowanej rekurencji.

---

## 17. EnableRecursiveQtyFactorShort

- **Opis**: Określa, czy bot stosuje rekurencyjne obliczanie wielkości pozycji short.
- **Przykłady użycia**:
  1. `true` – bot będzie wielokrotnie dokładał do short w określonych warunkach.
  2. `false` – shorty będą miały stałą wielkość zleceń.

---

## 18. PlaceOrderAttempts

- **Opis**: Liczba prób ponawiania zlecenia w razie niepowodzenia (np. order odrzucony).
- **Przykłady użycia**:
  1. `5` – bot pięć razy spróbuje wystawić zlecenie, jeśli nastąpi błąd sieci bądź giełdy.
  2. Zmiana na `1`, aby nie ponawiać i szybko przejść dalej w strategii.

---

## 19. MaxAbsFundingRate

- **Opis**: Maksymalna akceptowalna wartość stopy fundingu (np. 0.0002 = 0.02%) dla pozycji w futuresach.
- **Przykłady użycia**:
  1. `0.0002` blokuje otwarcie pozycji, gdy funding jest wyższy niż 0.02% w skali 8h.
  2. `0.0010` pozwala strategii godzić się na wyższy koszt utrzymywania pozycji.

---

## 20. MakerFeeRate

- **Opis**: Stawka prowizji (np. 0.0002 = 0.02%) dla zleceń typu maker.
- **Przykłady użycia**:
  1. `0.0002` to 0.02% prowizji dla zleceń w orderbooku.
  2. Zmiana na `0` przy rynkach z zerową prowizją maker (jeśli giełda oferuje promocję).

---

## 21. TakerFeeRate

- **Opis**: Stawka prowizji dla zleceń typu taker (zabierających płynność).
- **Przykłady użycia**:
  1. `0.00055` to 0.055% prowizji.
  2. Zmiana na `0.0010` w przypadku rynków o wyższej prowizji taker.

---

## 22. MinProfitRate

- **Opis**: Minimalny wymagany procent zysku (0.0006 = 0.06%) przy realizacji transakcji.
- **Przykłady użycia**:
  1. `0.0006`, strategia będzie celować w min. 0.06% zysku.
  2. `0.002`, gdy wymagamy co najmniej 0.2% zysku na każdej pozycji.

---

## 23. SpotRebalancingRatio

- **Opis**: Współczynnik rebalansowania spot (np. 0.0 oznacza brak rebalansowania).
- **Przykłady użycia**:
  1. `0.0` – nie wykonuje się rebalansowania portfela spot.
  2. `0.5` – być może docelowo 50% portfela jest rebalansowane (funkcjonalność zależna od implementacji).

---

## 24. StrategySelectPreference

- **Opis**: Preferencja wyboru strategii/symboli (np. `0` = Volume, `1` = NormalizedAverageTrueRange).
- **Przykłady użycia**:
  1. `0` – bot dobiera symbole głównie pod kątem najwyższego wolumenu.
  2. `1` – bot wybiera symbole o określonej zmienności (ATR).

---

## 25. NormalizedAverageTrueRangePeriod

- **Opis**: Okres do obliczeń znormalizowanego ATR (np. 14).
- **Przykłady użycia**:
  1. `14`, standardowa długość do liczenia ATR.
  2. `5`, jeśli chcemy szybsze i bardziej wrażliwe wskaźniki zmienności.

---

## 26. MinNormalizedAverageTrueRangePeriod

- **Opis**: Minimalny próg wartości ATR (zależy od strategii) do uznania symbolu za wystarczająco zmienny.
- **Przykłady użycia**:
  1. `1.0` – symbol musi mieć ATR powyżej 1, aby w ogóle otwierać tam pozycje.
  2. `0.5` – bardziej liberalne podejście do dopuszczenia symboli.

---

## 27. BackTest (obiekt)

- **Opis**: Parametry związane z backtestem (okres, początkowy balans, itp.).  
  Składa się z kilku pól:

### BackTest -> Start
- **Opis**: Data/godzina rozpoczęcia okresu testowego.
- **Przykłady użycia**:
  1. `"2023-01-01T00:00:00"` – test zaczyna się od 1 stycznia 2023.
  2. Przesunięcie na `"2022-12-01T00:00:00"`, aby objąć starsze dane.

### BackTest -> End
- **Opis**: Data/godzina zakończenia okresu testowego.
- **Przykłady użycia**:
  1. `"2023-08-19T00:00:00"` – test kończy się 19 sierpnia 2023.
  2. Ustawienie na `Now()` (bądź zbliżone) w celu testu do bieżącej daty.

### BackTest -> InitialBalance
- **Opis**: Początkowy kapitał (np. 1000 USDT) w symulacji backtestu.
- **Przykłady użycia**:
  1. `1000`, co oznacza, że test rozpoczyna z 1000 USDT.
  2. Zmiana na `5000`, by zasymulować większy portfel.

### BackTest -> StartupCandleData
- **Opis**: Czas trwania świec do wczytania zanim strategia zacznie faktycznie handlować (np. `1.00:00:00`).
- **Przykłady użycia**:
  1. `1.00:00:00` = 1 dzień wstecznych danych świec.
  2. `7.00:00:00` = tydzień wstecznych danych do rozgrzania wskaźników.

### BackTest -> InitialUntradableDays
- **Opis**: Ilość dni (na początku backtestu), w których strategia nie handluje (np. 0).
- **Przykłady użycia**:
  1. `0` – strategia może otwierać od razu po starcie testu.
  2. `5` – pierwsze 5 dni jest pomijane handlowo, aby zapełnić wskaźniki.

---

## 28. Unstucking (obiekt)

- **Opis**: Parametry związane z tzw. "unstuckingiem" – mechanizmem wychodzenia z pozycji stratnych.

### Unstucking -> Enabled
- **Opis**: Flaga włączająca/wyłączająca algorytm unstuck.
- **Przykłady użycia**:
  1. `false` – brak aktywnego mechanizmu wyrzucania złych pozycji.
  2. `true` – strategia zacznie zamykać/ograniczać pozycje przy dużej stracie.

### Unstucking -> SlowUnstuckThresholdPercent
- **Opis**: Próg procentowej straty portfela, od którego zaczyna się „wolne” unstuck.
- **Przykłady użycia**:
  1. `-0.3` -> przy spadku portfela o 30% rozpoczyna się powolny mechanic ustuck.
  2. Zmiana na `-0.1`, jeśli chcemy szybciej reagować na mniejsze straty.

### Unstucking -> SlowUnstuckPositionThresholdPercent
- **Opis**: Próg procentowy straty dla pojedynczej pozycji, przy którym zaczyna się powolne ograniczanie.
- **Przykłady użycia**:
  1. `-0.05` – jeśli pojedyncza pozycja spadnie o 5%, włącza się tryb slow-unstuck.
  2. `-0.1` – bardziej elastyczne podejście, dając pozycji większy margines.

### Unstucking -> ForceUnstuckThresholdPercent
- **Opis**: Próg portfela, przy którym włączany jest „siłowy” unstuck (drastyczniejsze zamykanie).
- **Przykłady użycia**:
  1. `-0.4` -> przy 40% spadku wartości portfela strategia może masowo ograniczyć straty.
  2. `-0.25`, jeśli nasza tolerancja ryzyka jest mniejsza.

### Unstucking -> ForceUnstuckPositionThresholdPercent
- **Opis**: Próg procentowy straty dla pozycji, po przekroczeniu którego strategia przymusowo zamyka (force).
- **Przykłady użycia**:
  1. `-0.2` -> każda pozycja spadająca 20% podlega wymuszonemu zamknięciu.
  2. `-0.3`, jeśli pozwalamy pozycji spaść jeszcze głębiej, zanim się poddamy.

### Unstucking -> SlowUnstuckPercentStep
- **Opis**: Wielkość kroków (procentowa) przy powolnym unstucku.
- **Przykłady użycia**:
  1. `0.2` oznacza, że bot co 20% odchylenia dokłada lub ogranicza.
  2. Zmiana na `0.05`, aby reagować częściej, ale mniejszymi porcjami.

### Unstucking -> ForceUnstuckPercentStep
- **Opis**: Wielkość kroków (procentowa) przy siłowym unstucku.
- **Przykłady użycia**:
  1. `0.1`, strategia przeprowadza dość agresywne cięcia przy 10% kolejnych ruchach.
  2. `0.25` – mniej częste, ale większe jednorazowe działania.

### Unstucking -> ForceKillTheWorst
- **Opis**: Flaga decydująca, czy strategia ma natychmiast "zabić" (zamknąć) najgorszą pozycję.
- **Przykłady użycia**:
  1. `false` – strategia raczej równo ogranicza straty w kilku pozycjach.
  2. `true` – najgorsza pozycja może zostać zamknięta od razu w celu ochrony kapitału.

---

## 29. Strategies (obiekt)

- **Opis**: Kontener strategii i ich parametrów.  
  Tutaj jest tylko `"Mona"` z określonymi polami.

### Strategies -> Mona -> MinReentryPositionDistanceLong
- **Opis**: Minimalna procentowa odległość ceny od średniej ceny pozycji, by strategia Mona dołożyła do long.
- **Przykłady użycia**:
  1. `0.025` – dokłada do long przy cofnięciu o 2.5% poniżej ostatniego wejścia.
  2. `0.01` – agresywniejsze dokupowanie co 1% spadku.

### Strategies -> Mona -> MinReentryPositionDistanceShort
- **Opis**: Minimalna procentowa odległość od średniej ceny short, by Mona dołożyła do short.
- **Przykłady użycia**:
  1. `0.025` – dokłada do short przy wzroście ceny o 2.5% ponad poprzedni entry.
  2. `0.04` – bardziej konserwatywne shorty, dokładając rzadziej.

### Strategies -> Mona -> ClusteringLength
- **Opis**: Długość historycznych danych (w świecach), wykorzystywana do klastrowania cen.
- **Przykłady użycia**:
  1. `480` – ~8 godzin przy świecach 1-minutowych.
  2. `720` – ~12 godzin dla bardziej rozbudowanego modelu klastrów cenowych.

### Strategies -> Mona -> BandwidthCoefficient
- **Opis**: Współczynnik szerokości "okna" dla algorytmu MeanShift (ustala czułość klastrowania).
- **Przykłady użycia**:
  1. `0.3` => umiarkowana precyzja klastrowania.
  2. `0.1` => węższe klastery, strategia będzie mieć bardziej szczegółowe punkty wsparcia/oporu.

### Strategies -> Mona -> MfiRsiLookback
- **Opis**: Określa, ile świec wstecz analizuje MFI/RSI do sygnałów (np. 2).
- **Przykłady użycia**:
  1. `2` => Bieżący i poprzedni okres w MFI/RSI.
  2. `5` => Bardziej wygładzone sygnały, dłuższa analiza trendu.

---

## 30. CriticalMode (obiekt)

**Opis**: Parametry aktywujące "tryb krytyczny" przy przekroczeniu danego poziomu wykorzystania portfela.

### CriticalMode -> EnableCriticalModeLong

- **Opis**: Flaga włączająca specjalne zachowanie, gdy portfel jest przepełniony pozycjami long.
- **Przykłady użycia**:
  1. `true` – jeśli chcemy zatrzymać otwieranie nowych long, gdy portfel już jest przeciążony.
  2. `false` – zawsze pozwalamy wchodzić w nowe long.

### CriticalMode -> EnableCriticalModeShort

- **Opis**: Flaga włączająca tryb krytyczny dla pozycji short.
- **Przykłady użycia**:
  1. `true` – włącza ograniczenia short, gdy zbytnio obciążone jest konto shortami.
  2. `false` – brak ograniczeń dla krótkich pozycji.

### CriticalMode -> WalletExposureThresholdLong

- **Opis**: Poziom ekspozycji long (np. 0.3 = 30%), powyżej którego włącza się tryb krytyczny.
- **Przykłady użycia**:
  1. `0.3` – jeśli 30% portfela jest w long, blokujemy kolejne.
  2. `0.5` – dopuszczamy do 50% w long, dopiero potem wchodzi tryb krytyczny.

### CriticalMode -> WalletExposureThresholdShort

- **Opis**: Poziom ekspozycji short, przy którym włącza się krytyczne ograniczenia.
- **Przykłady użycia**:
  1. `0.3` – przy 30% w shortach strategia blokuje dalsze shorty.
  2. `0.7` – pozwalamy na większą ekspozycję, zanim tryb się włączy.

---

## 31. SymbolVolumePreference (tablica)

- **Opis**: Preferowane kategorie wolumenu symbolu (np. `"MEDIUM", "HIGH"`).
- **Przykłady użycia**:
  1. Dodanie `"HIGH"` oznacza, że strategia woli pary o dużym wolumenie.
  2. Połączenie `"MEDIUM"` i `"HIGH"` – szeroki wybór rynków.

---

## 32. SymbolVolatilityPreference (tablica)

- **Opis**: Preferowane poziomy zmienności (np. `"MEDIUM", "HIGH"`).
- **Przykłady użycia**:
  1. Wpisanie `"HIGH"` przy strategiach lubiących duże wahania.
  2. `"LOW"` (gdyby było dostępne), aby preferować spokojniejsze rynki – w tym przypadku mamy `"MEDIUM"` i `"HIGH"`.

---

## 33. SymbolMaturityPreference (tablica)

- **Opis**: Określa „dojrzałość” projektu/symbole (np. `"MEDIUM"`, `"LARGE"`), by unikać świeżych, mało płynnych coinów.
- **Przykłady użycia**:
  1. Ustawienie `"MEDIUM", "LARGE"`, by unikać mikrokapitalizacji.
  2. Dodanie `"SMALL"` mogłoby poszerzyć zakres – ale tutaj mamy tylko `"MEDIUM"` i `"LARGE"`.

---

## 34. Whitelist (tablica)

- **Opis**: Lista symboli dopuszczonych do handlu.
- **Przykłady użycia**:
  1. Dodanie `"BTCUSDT"` i `"ETHUSDT"`, jeśli chcemy skupić się tylko na głównych kryptowalutach.
  2. Usunięcie części pozycji z listy, by zawęzić do kilku docelowych par.

---

## 35. Blacklist (tablica)

- **Opis**: Lista symboli zablokowanych dla handlu (pusta oznacza brak wykluczeń).
- **Przykłady użycia**:
  1. Dodanie `"1000PEPEUSDT"`, jeśli token jest zbyt ryzykowny.
  2. Pozostawienie pustej listy, by nie wykluczać żadnych rynków.

---

## 36. SymbolTradingModes (tablica)

- **Opis**: Lista reguł zmieniających `TradingMode` dla poszczególnych symboli (pusta = brak nadpisania).
- **Przykłady użycia**:
  1. Dodanie `{"Symbol": "BTCUSDT", "TradingMode": "Readonly"}` by BTCUSDT był tylko monitorowany.
  2. Pozostawienie pustej tablicy, jeśli nie rozróżniamy trybów między symbolami.

---

# Koniec

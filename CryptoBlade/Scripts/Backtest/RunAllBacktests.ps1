param(
    [int]$DaysToShow = 30
)

# Ścieżki
$scriptRoot = $PSScriptRoot
$globalConfigPath = "$scriptRoot/backtest_global.json"

# Wczytaj globalny config
$globalConfig = Get-Content $globalConfigPath | ConvertFrom-Json

# Mapowanie strategii na numer (zgodnie z RunStrategyContainer.ps1)
# Pobierz listę strategii z pliku StrategyNames.cs
$strategyNamesPath = "$scriptRoot/../../Strategies/StrategyNames.cs"
$strategyNames = @()
if (Test-Path $strategyNamesPath) {
    $fileContent = Get-Content $strategyNamesPath -Raw
    $pattern = 'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"'
    $matches = [regex]::Matches($fileContent, $pattern)
    foreach ($m in $matches) { $strategyNames += $m.Groups[1].Value }
} else {
    Write-Error "Nie znaleziono pliku StrategyNames.cs"
    exit 1
}
$strategyToNum = @{}
for ($i = 0; $i -lt $strategyNames.Count; $i++) { $strategyToNum[$strategyNames[$i]] = ($i+1).ToString() }

# Mapowanie BotMode i TradingMode na numer
function Get-EnumMap($filePath, $enumName) {
    $fileContent = Get-Content $filePath -Raw
    $pattern = "public\s+enum\s+$enumName\s*{(.*?)}"
    $match = [regex]::Match($fileContent, $pattern, "Singleline")
    $enumBlock = $match.Groups[1].Value
    $values = $enumBlock -split "," | ForEach-Object {
        $_ -replace "//.*", "" | ForEach-Object { $_.Trim() }
    } | Where-Object { $_ -ne "" }
    $map = @{}
    for ($i = 0; $i -lt $values.Count; $i++) { $map[$values[$i]] = ($i+1).ToString() }
    return $map
}
$botModeMap = Get-EnumMap "$scriptRoot/../../Configuration/BotMode.cs" "BotMode"
$tradingModeMap = Get-EnumMap "$scriptRoot/../../Configuration/TradingMode.cs" "TradingMode"

# Pobierz strategie z globalnego JSON-a
$strategies = $strategyNames
$strategies = $strategies | Where-Object { $_ -ne "Momentum" -and $_ -ne "MfiRsiEriTrend" }

# 1. Wstrzykiwanie konfiguracji do każdej strategii
foreach ($strategy in $strategies) {
    $strategyConfig = $globalConfig.PSObject.Copy()
    $strategyConfig | Add-Member -NotePropertyName "StrategyName" -NotePropertyValue $strategy
    $strategyConfig.Strategies = @{}

    # Dodaj config tylko dla tej strategii
    if ($globalConfig.Strategies.PSObject.Properties.Name -contains $strategy) {
        $strategyConfig.Strategies.$strategy = $globalConfig.Strategies.$strategy
    }

    # Specjalny przypadek: Qiqi potrzebuje też configu Recursive
    if ($strategy -eq "Qiqi" -and $globalConfig.Strategies.PSObject.Properties.Name -contains "Recursive") {
        $strategyConfig.Strategies.Recursive = $globalConfig.Strategies.Recursive
    }

    $outPath = "$scriptRoot/../../Data/Strategies/$strategy/Backtest/backtest.json"
    $strategyConfig | ConvertTo-Json -Depth 10 | Set-Content $outPath
}

#  2. Przed uruchomieniem backtestów:
$resultsState = @{}
foreach ($strategy in $strategies) {
    $resultsDir = "$scriptRoot/../../Data/Strategies/$strategy/Backtest/Results"
    $folders = @()
    if (Test-Path $resultsDir) {
        $folders = Get-ChildItem -Path $resultsDir -Directory | Select-Object -ExpandProperty Name
    }
    $resultsState[$strategy] = $folders
}

# 3. Uruchomienie backtestów dla każdej strategii
foreach ($strategy in $strategies) {
    $strategyNum = $strategyToNum[$strategy]
    $botModeNum = $botModeMap[$globalConfig.BotMode]
    $tradingModeNum = $tradingModeMap[$globalConfig.TradingMode]
    $code = "$strategyNum$botModeNum$tradingModeNum"
    Write-Host "Uruchamiam backtest dla $strategy (kod: $code)..."
    & "$scriptRoot/../RunStrategyContainer.ps1" -Code $code
}

# 4. Czekaj aż wszystkie kontenery zakończą pracę (monitorowanie rezultatów)
Write-Host "Czekam na zakończenie wszystkich backtestów..."
$resultsReady = @{}
while ($resultsReady.Count -lt $strategies.Count) {
    foreach ($strategy in $strategies) {
        if ($resultsReady.ContainsKey($strategy)) { continue }
        $resultsDir = "$scriptRoot/../../Data/Strategies/$strategy/Backtest/Results"
        if (Test-Path $resultsDir) {
            $currentFolders = Get-ChildItem -Path $resultsDir -Directory | Sort-Object Name
            $oldFolders = $resultsState[$strategy]
            $newFolders = $currentFolders | Where-Object { $oldFolders -notcontains $_.Name }
            if ($newFolders.Count -gt 0) {
                # Wybierz najnowszy folder (po dacie utworzenia)
                $latest = $newFolders | Sort-Object CreationTime -Descending | Select-Object -First 1
                $resultJson = Join-Path $latest.FullName "result.json"
                if (Test-Path $resultJson) {
                    $resultsReady[$strategy] = $resultJson
                    Write-Host "Nowy wynik dla $strategy => $resultJson"
                }
            }
        }
    }
    Start-Sleep -Seconds 5
}

# 5. Zbierz wyniki do zbiorczego JSON-a
$summary = @{
    Iterations = @()
}
foreach ($strategy in $strategies) {
    $resultPath = $resultsReady[$strategy]
    $json = Get-Content $resultPath | ConvertFrom-Json
    $iteration = @{
        Strategy = $strategy
        Date = (Split-Path -Leaf (Split-Path -Parent $resultPath))
        InitialBalance = $json.InitialBalance
        FinalBalance = $json.FinalBalance
        FinalEquity = $json.FinalEquity
        LowestEquityToBalance = $json.LowestEquityToBalance
        UnrealizedPnl = $json.UnrealizedPnl
        RealizedPnl = $json.RealizedPnl
        AverageDailyGainPercent = $json.AverageDailyGainPercent
        MaxDrawDown = $json.MaxDrawDown
        TotalDays = $json.TotalDays
        ExpectedDays = $json.ExpectedDays
        LossProfitRatio = $json.LossProfitRatio
        SpotBalance = $json.SpotBalance
        EquityBalanceNormalizedRooMeanSquareError = $json.EquityBalanceNormalizedRooMeanSquareError
        AdgNormalizedRootMeanSquareError = $json.AdgNormalizedRootMeanSquareError
        TotalFundingRateProfitOrLoss = $json.TotalFundingRateProfitOrLoss
    }
    $summary.Iterations += $iteration
}

# Dodaj całą konfigurację backtestu do zbiorczego pliku
$backtestGlobalPath = "$scriptRoot/backtest_global.json"
$backtestGlobal = Get-Content $backtestGlobalPath | ConvertFrom-Json
$summary.BacktestConfig = $backtestGlobal

# 6. Zapisz zbiorczy plik JSON z datą i GUID
$now = Get-Date -Format "yyyyMMddHHmmss"
$guid = [guid]::NewGuid().ToString("N")
$summaryFileName = "${now}-${guid}.json"
$summaryDir = "$scriptRoot/../../Data/Strategies/_globalBacktest"
if (!(Test-Path $summaryDir)) { New-Item -ItemType Directory -Path $summaryDir | Out-Null }
$summaryFullPath = Join-Path $summaryDir $summaryFileName
$summary | ConvertTo-Json -Depth 10 | Set-Content $summaryFullPath
Write-Host "Zbiorczy wynik zapisany do $summaryFullPath"

# 7. Uruchom serwer HTTP i otwórz stronę w przeglądarce
$projectRoot = Resolve-Path "$scriptRoot/../.."
$portUsed = (ss -tln | Select-String ":8080").Length -gt 0
if (-not $portUsed) {
    Write-Host "Uruchamiam serwer HTTP na porcie 8080..."
    Start-Process python3 -ArgumentList "-m http.server 8080" -WorkingDirectory $projectRoot
    Start-Sleep -Seconds 2
} else {
    Write-Host "Serwer HTTP na porcie 8080 już działa."
}
Start-Process "xdg-open" "http://localhost:8080/Scripts/Backtest/backtest_summary.html"
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
    $match = [regex]::Matches($fileContent, $pattern, "Singleline")
    foreach ($m in $match) { $strategyNames += $m.Groups[1].Value }
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
$botModeMap     = Get-EnumMap "$scriptRoot/../../Configuration/BotMode.cs" "BotMode"
$tradingModeMap = Get-EnumMap "$scriptRoot/../../Configuration/TradingMode.cs" "TradingMode"

# Pobierz strategie z globalnego JSON-a
$strategies = $strategyNames
$strategies = $strategies | Where-Object { $_ -ne "Momentum" -and $_ -ne "MfiRsiEriTrend" }

# 1. Wstrzykiwanie konfiguracji do każdej strategii
foreach ($strategy in $strategies) {
    $strategyConfig = $globalConfig.PSObject.Copy()
    $strategyConfig | Add-Member -NotePropertyName "StrategyName" -NotePropertyValue $strategy
    $strategyConfig.Strategies = @{}

    if ($globalConfig.Strategies.PSObject.Properties.Name -contains $strategy) {
        $strategyConfig.Strategies.$strategy = $globalConfig.Strategies.$strategy
    }
    if ($strategy -eq "Qiqi" -and $globalConfig.Strategies.PSObject.Properties.Name -contains "Recursive") {
        $strategyConfig.Strategies.Recursive = $globalConfig.Strategies.Recursive
    }

    $outPath = "$scriptRoot/../../Data/Strategies/$strategy/Backtest/backtest.json"
    $outDir  = Split-Path -Parent $outPath
    if (!(Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
    $strategyConfig | ConvertTo-Json -Depth 10 | Set-Content $outPath
}

#  2. Snapshot stanu wyników
$resultsState = @{}
foreach ($strategy in $strategies) {
    $resultsDir = "$scriptRoot/../../Data/Strategies/$strategy/Backtest/Results"
    $folders = @()
    if (Test-Path $resultsDir) {
        $folders = Get-ChildItem -Path $resultsDir -Directory | Select-Object -ExpandProperty Name
    }
    $resultsState[$strategy] = $folders
}

#3. Uruchom backtesty
foreach ($strategy in $strategies) {
    $strategyNum   = $strategyToNum[$strategy]
    $botModeNum    = $botModeMap[$globalConfig.BotMode]
    $tradingModeNum= $tradingModeMap[$globalConfig.TradingMode]
    $code = "$strategyNum$botModeNum$tradingModeNum"
    Write-Host "Uruchamiam backtest dla $strategy (kod: $code)..."
    & "$scriptRoot/../RunStrategyContainer.ps1" -Code $code
}

# 4. Czekanie na wyniki
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

# 5. Zbiorczy JSON
$summary = @{ Iterations = @() }
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

# 5a. Dołącz metadane Backtestu (Start/End/InitialBalance) do summary
$backtestGlobalPath = "$scriptRoot/backtest_global.json"
$backtestGlobal = Get-Content $backtestGlobalPath | ConvertFrom-Json

if ($null -eq $backtestGlobal.BackTest) {
    Write-Warning "Brak sekcji BackTest w backtest_global.json — kolumna 'Backtest' będzie pusta."
} else {
    $bt = $backtestGlobal.BackTest
    $summary.BacktestConfig = @{
        BackTest = @{
            Start          = $bt.Start
            End            = $bt.End
            InitialBalance = $bt.InitialBalance
        }
    }
}

# 6. Zapis zbiorczego pliku
$now = Get-Date -Format "yyyyMMddHHmmss"
$guid = [guid]::NewGuid().ToString("N")
$summaryFileName = "${now}-${guid}.json"
$summaryDir = "$scriptRoot/../../Data/Strategies/_globalBacktest"
if (!(Test-Path $summaryDir)) { New-Item -ItemType Directory -Path $summaryDir -Force | Out-Null }
$summaryFullPath = Join-Path $summaryDir $summaryFileName
$summary | ConvertTo-Json -Depth 10 | Set-Content $summaryFullPath
Write-Host "Zbiorczy wynik zapisany do $summaryFullPath"

# Zbuduj manifest index.json (lista wszystkich *.json, najnowsze pierwsze)
$manifestPath = Join-Path $summaryDir 'index.json'
$files = Get-ChildItem -Path $summaryDir -File -Filter '*.json' |
         Sort-Object LastWriteTime -Descending |
         Select-Object -ExpandProperty Name

@{ files = $files } | ConvertTo-Json -Depth 3 | Set-Content $manifestPath

# Dodatkowo wygodny alias na najnowszy plik
Copy-Item -Force $summaryFullPath (Join-Path $summaryDir 'latest.json')


# ===================== 7. Serwer + otwarcie przeglądarki =====================

function Get-OS {
    if ($env:OS -eq 'Windows_NT' -or $PSVersionTable.PSEdition -eq 'Desktop') { return 'Windows' }
    if ($IsMacOS) { return 'macOS' }
    if ($env:WSL_DISTRO_NAME -or $IsLinux) { return 'Linux' }
    return 'Linux'
}

# UWAGA: parametr 'Host' zmieniony na 'Address', by nie kolidować z $Host
function Test-TcpPortOpen {
    param(
        [int]$Port,
        [string]$Address = '127.0.0.1',
        [int]$TimeoutMs = 500
    )
    try {
        $client = [System.Net.Sockets.TcpClient]::new()
        $iar = $client.BeginConnect($Address, $Port, $null, $null)
        $ok = $iar.AsyncWaitHandle.WaitOne($TimeoutMs)
        if ($ok) {
            $client.EndConnect($iar)
            $client.Close()
            return $true
        } else {
            $client.Close()
            return $false
        }
    } catch { return $false }
}

function Get-PythonCommand {
    $os = Get-OS
    if ($os -eq 'Windows') {
        $options = @(
            @{ cmd = "py";      args = "-3 -m http.server {PORT}" },
            @{ cmd = "python";  args = "-m http.server {PORT}" },
            @{ cmd = "python3"; args = "-m http.server {PORT}" }
        )
    } else {
        $options = @(
            @{ cmd = "python3"; args = "-m http.server {PORT}" },
            @{ cmd = "python";  args = "-m http.server {PORT}" }
        )
    }
    foreach ($opt in $options) {
        if (Get-Command $opt.cmd -ErrorAction SilentlyContinue) { return $opt }
    }
    return $null
}

function Start-StaticServerPython {
    param([string]$Root, [int]$Port, [int]$WaitSeconds = 15)
    $py = Get-PythonCommand
    if (-not $py) { return $null }
    $args = $py.args.Replace("{PORT}", "$Port")

    Write-Host "Startuję Python HTTP server: $($py.cmd) $args (root: $Root)"
    $proc = Start-Process -FilePath $py.cmd -ArgumentList $args -WorkingDirectory $Root -WindowStyle Hidden -PassThru

    $deadline = (Get-Date).AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-TcpPortOpen -Port $Port) { return $proc }
        if ($proc.HasExited) {
            Write-Warning "Python server zakończył się (ExitCode=$($proc.ExitCode)) zanim port $Port wstał."
            return $null
        }
        Start-Sleep -Milliseconds 300
    }
    Write-Warning "Python server nie nasłuchuje na porcie $Port po ${WaitSeconds}s."
    return $null
}

# Fallback: prosty serwer statyczny na HttpListener
function Start-EmbeddedServer {
    param([string]$Root, [int]$Port)
    $script = @'
param($Root,$Port)
$listener = New-Object System.Net.HttpListener
$prefix = "http://localhost:$Port/"
$listener.Prefixes.Add($prefix)
$listener.Start()
try {
    while ($true) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request
        $resp = $ctx.Response

        $path = $req.Url.AbsolutePath
        if ($path -eq "/") { $path = "/Scripts/Backtest/backtest_summary.html" }

        $fsPath = [IO.Path]::GetFullPath((Join-Path $Root ($path.TrimStart('/').Replace('/', [IO.Path]::DirectorySeparatorChar))))
        $rootFull = [IO.Path]::GetFullPath($Root)
        if (-not $fsPath.StartsWith($rootFull)) { $resp.StatusCode=403; $resp.Close(); continue }

        if (Test-Path $fsPath -PathType Container) { $fsPath = Join-Path $fsPath "index.html" }

        if (Test-Path $fsPath) {
            try {
                $bytes = [IO.File]::ReadAllBytes($fsPath)
                $ext = [IO.Path]::GetExtension($fsPath).ToLower()
                $ct = switch ($ext) {
                    ".html" { "text/html" }
                    ".htm"  { "text/html" }
                    ".json" { "application/json" }
                    ".js"   { "application/javascript" }
                    ".css"  { "text/css" }
                    ".png"  { "image/png" }
                    ".jpg"  { "image/jpeg" }
                    ".jpeg" { "image/jpeg" }
                    ".svg"  { "image/svg+xml" }
                    default { "application/octet-stream" }
                }
                $resp.ContentType = $ct
                $resp.OutputStream.Write($bytes,0,$bytes.Length)
            } catch { $resp.StatusCode=500 }
        } else {
            $resp.StatusCode=404
        }
        $resp.OutputStream.Close()
        $resp.Close()
    }
} finally {
    $listener.Stop()
}
'@
    Start-Job -ScriptBlock ([ScriptBlock]::Create($script)) -ArgumentList $Root,$Port | Out-Null

    $deadline = (Get-Date).AddSeconds(5)
    while ((Get-Date) -lt $deadline) {
        if (Test-TcpPortOpen -Port $Port) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Open-UrlCrossPlatform {
    param([string]$Url)
    $os = Get-OS
    if ($os -eq 'Windows') {
        Start-Process $Url | Out-Null
    } elseif ($os -eq 'macOS') {
        Start-Process open $Url | Out-Null
    } else {
        $xdg = Get-Command xdg-open -ErrorAction SilentlyContinue
        if ($xdg) { Start-Process xdg-open $Url | Out-Null }
        else { Write-Host "Otwórz ręcznie: $Url" }
    }
}

$projectRoot = (Resolve-Path "$scriptRoot/../..").Path
$port = 8080

# 1) Python, jeśli dostępny
$proc = $null
if (-not (Test-TcpPortOpen -Port $port)) {
    Write-Host "Uruchamiam serwer HTTP na porcie $port (Python, jeśli dostępny)..."
    $proc = Start-StaticServerPython -Root $projectRoot -Port $port -WaitSeconds 15
}

# 2) Fallback na HttpListener, jeśli port nie wstał
if (-not (Test-TcpPortOpen -Port $port)) {
    Write-Warning "Fallback: start wbudowanego serwera (HttpListener)."
    $ok = Start-EmbeddedServer -Root $projectRoot -Port $port
    if (-not $ok) {
        Write-Error "Nie udało się wystartować żadnego serwera na porcie $port."
        exit 1
    }
}

$summaryUrl = "http://localhost:$port/Scripts/Backtest/backtest_summary.html"
Write-Host "Otwieram: $summaryUrl"
Open-UrlCrossPlatform -Url $summaryUrl

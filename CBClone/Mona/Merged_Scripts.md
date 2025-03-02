
# ===== SCRIPT: BuildDocker.ps1 =====
docker build -f "CryptoBlade/Dockerfile" -t cryptoblade:latest .
docker save -o cryptoblade.tar cryptoblade:latest

# ===== SCRIPT: CopyFilesForBot.ps1 =====
Param(
    [Parameter(Mandatory = $true)]
    [int]$StrategyNumber,
    [string]$Version = "1.0.0",
    [switch]$ExtendedIndex
)

# Mapowanie numerów strategii
$strategyMap = @{
    1 = "AutoHedge"
    2 = "MfiRsiCandlePrecise"
    3 = "MfiRsiEriTrend"
    4 = "LinearRegression"
    5 = "Tartaglia"
    6 = "Mona"
    7 = "Qiqi"
}

# Pobranie strategii
if ($strategyMap.ContainsKey($StrategyNumber)) {
    $StrategyName = $strategyMap[$StrategyNumber]
} else {
    Write-Host "Nieprawidłowy numer strategii. Dostępne numery to 1-7."
    exit
}

$SourceDir = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade"
$MainDestDir = "C:\Users\bwola\source\repos\CryptoBlade\CBCLone"

$DestDir = Join-Path -Path $MainDestDir -ChildPath $StrategyName
if (Test-Path $DestDir) {
    Remove-Item -Path $DestDir\* -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $DestDir | Out-Null
}

if ($SourceDir[-1] -ne '\') {
    $SourceDir += "\"
}

#########################################
# Krok 1: Filtrowanie plików źródłowych #
#########################################
$filteredFiles = Get-ChildItem -Path $SourceDir -Recurse -File | Where-Object { 
    ($_.FullName -notmatch '(?i)\.sln$') -and
    ($_.FullName -notmatch '(?i)cryptoblade\.tar$') -and
    ($_.FullName -notmatch '(?i)Dockerfile$') -and
    ($_.FullName -notmatch '(?i)\\bin(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\obj(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTest(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Optimizer(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\wwwroot(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Pages(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\appsettings.Development.json(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\docker-compose(\\|$)')
}

##################################################
# Krok 2: Łączenie plików .cs według modułów     #
##################################################
$csFiles = $filteredFiles | Where-Object { $_.Extension -eq ".cs" }
$mergedCode = @()

foreach ($file in $csFiles) {
    $content = Get-Content -Path $file.FullName -Raw
    $mergedCode += "`n// ===== FILE: $($file.Name) =====`n" + $content
}

$mergedCodePath = Join-Path -Path $DestDir -ChildPath "CryptoBlade_Merged.cs"
$mergedCode -join "`n" | Out-File -FilePath $mergedCodePath -Encoding utf8
Write-Host "Scalono wszystkie pliki C# w: $mergedCodePath"

##################################################
# Krok 3: Łączenie plików skryptów (Scripts)    #
##################################################
$scriptFiles = Get-ChildItem -Path "$SourceDir\Scripts" -Recurse -File -Filter "*.ps1"
$mergedScripts = @()

foreach ($script in $scriptFiles) {
    $content = Get-Content -Path $script.FullName -Raw
    $mergedScripts += "`n# ===== SCRIPT: $($script.Name) =====`n" + $content
}

$mergedScriptsPath = Join-Path -Path $DestDir -ChildPath "Merged_Scripts.ps1"
$mergedScripts -join "`n" | Out-File -FilePath $mergedScriptsPath -Encoding utf8
Write-Host "Scalono wszystkie pliki skryptów w: $mergedScriptsPath"

##################################################
# Krok 4: Łączenie dokumentacji Markdown        #
##################################################
$markdownFiles = Get-ChildItem -Path $SourceDir -Recurse -File -Filter "*.md"
$mergedDocs = @()

foreach ($doc in $markdownFiles) {
    $content = Get-Content -Path $doc.FullName -Raw
    $mergedDocs += "`n# ===== DOCUMENT: $($doc.Name) =====`n" + $content
}

$mergedDocsPath = Join-Path -Path $DestDir -ChildPath "Merged_Documentation.md"
$mergedDocs -join "`n" | Out-File -FilePath $mergedDocsPath -Encoding utf8
Write-Host "Scalono całą dokumentację w: $mergedDocsPath"

##################################################
# Krok 5: Kopiowanie pozostałych plików          #
##################################################
$otherFiles = $filteredFiles | Where-Object { $_.Extension -notin @(".cs", ".ps1", ".md") }
foreach ($file in $otherFiles) {
    $relativePath = $file.FullName.Substring($SourceDir.Length)
    $newFilePath = Join-Path -Path $DestDir -ChildPath ($relativePath -replace '[\\\/]', '_')
    
    Write-Host "Kopiowanie:" $file.FullName "->" $newFilePath
    Copy-Item -Path $file.FullName -Destination $newFilePath
}

Write-Host "Proces kopiowania zakończony!"


# ===== SCRIPT: RunContainer.ps1 =====
<# 
    deploy.ps1 - Skrypt do dekodowania trzycyfrowego kodu, dynamicznego pobierania mappingów
    z plików źródłowych (Strategies/StrategyNames.cs, Configuration/BotMode.cs, Configuration/TradingMode.cs),
    aktualizacji plików JSON (np. live.json, backtest.json, optimizer.json) z relatywną ścieżką,
    kopiowania wartości ApiKey i ApiSecret z appsettings.json (w trybie demo kopiujemy wszystkie konta,
    a w produkcyjnym tylko wybrane konto), generowania zmiennych środowiskowych do sekcji environment w 
    pliku docker-compose.yml oraz uruchomienia kontenerów (docker login + docker compose up).

    Parametry:
      - Code             : trzycyfrowy kod (np. "123")
      - StrategiesCount  : (opcjonalnie) liczba strategii losowanych z dostępnej listy (domyślnie 0)
      - Demo             : (opcjonalnie) flaga bool, jeśli $true – tryb demo (kopiujemy wszystkie konta i ustawiamy konto demo), domyślnie $false

    Użycie:
        .\deploy.ps1 -Code "123" [-StrategiesCount 2] [-Demo $true]
#>

param (
    [Parameter(Mandatory = $true)]
    [string]$Code,

    [Parameter(Mandatory = $false)]
    [int]$StrategiesCount = 0,

    [Parameter(Mandatory = $false)]
    [bool]$Demo = $false
)

# Walidacja kodu
if ($Code -notmatch '^\d{3}$') {
    Write-Error "Kod musi składać się z dokładnie 3 cyfr."
    exit 1
}

# Funkcja: Pobiera wartości enum z pliku źródłowym
function Get-EnumMapping {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string]$EnumName
    )
    if (-not (Test-Path $FilePath)) {
        Write-Error "Plik $FilePath nie istnieje."
        exit 1
    }
    $fileContent = Get-Content $FilePath -Raw
    $pattern = "public\s+enum\s+$EnumName\s*{(.*?)}"
    $match = [regex]::Match($fileContent, $pattern, "Singleline")
    if (-not $match.Success) {
        Write-Error "Nie znaleziono definicji enum $EnumName w pliku $FilePath."
        exit 1
    }
    $enumBlock = $match.Groups[1].Value
    $values = $enumBlock -split "," | ForEach-Object {
        $_ -replace "//.*", "" | ForEach-Object { $_.Trim() }
    } | Where-Object { $_ -ne "" }
    return $values
}

# Funkcja: Pobiera stałe string z klasy (np. StrategyNames)
function Get-ConstantMapping {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [Parameter(Mandatory = $true)]
        [string]$ClassName
    )
    if (-not (Test-Path $FilePath)) {
        Write-Error "Plik $FilePath nie istnieje."
        exit 1
    }
    $fileContent = Get-Content $FilePath -Raw
    $pattern = 'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"'
    $matches = [regex]::Matches($fileContent, $pattern)
    $values = @()
    foreach ($m in $matches) {
        $values += $m.Groups[1].Value
    }
    return $values
}

# Dynamiczne mapowania:
# Pobieramy nazwy strategii z pliku Strategies/StrategyNames.cs
$strategyValues = Get-ConstantMapping -FilePath "$PSScriptRoot\..\Strategies\StrategyNames.cs" -ClassName "StrategyNames"
$strategyMap = @{}
for ($i = 0; $i -lt $strategyValues.Count; $i++) {
    $key = ($i + 1).ToString()
    $strategyMap[$key] = $strategyValues[$i]
}

# Pobieramy BotMode z Configuration/BotMode.cs
$botModeValues = Get-EnumMapping -FilePath "$PSScriptRoot\..\Configuration\BotMode.cs" -EnumName "BotMode"
$botModeMap = @{}
for ($i = 0; $i -lt $botModeValues.Count; $i++) {
    $key = ($i + 1).ToString()
    $botModeMap[$key] = $botModeValues[$i]
}

# Pobieramy TradingMode z Configuration/TradingMode.cs
$tradingModeValues = Get-EnumMapping -FilePath "$PSScriptRoot\..\Configuration\TradingMode.cs" -EnumName "TradingMode"
$tradingModeMap = @{}
for ($i = 0; $i -lt $tradingModeValues.Count; $i++) {
    $key = ($i + 1).ToString()
    $tradingModeMap[$key] = $tradingModeValues[$i]
}

# Dekodowanie cyfr:
$botModeDigit = $Code.Substring(1,1)
$tradingModeDigit = $Code.Substring(2,1)
if (-not $botModeMap.ContainsKey($botModeDigit) -or -not $tradingModeMap.ContainsKey($tradingModeDigit)) {
    Write-Error "Niepoprawny kod – brak mapowania dla BotMode lub TradingMode."
    exit 1
}
$BotMode = $botModeMap[$botModeDigit]
$TradingMode = $tradingModeMap[$tradingModeDigit]

# Wybór strategii:
if ($StrategiesCount -gt 0) {
    $availableStrategies = $strategyMap.Values
    if ($StrategiesCount -gt $availableStrategies.Count) {
        $StrategiesCount = $availableStrategies.Count
    }
    $selectedStrategies = Get-Random -InputObject $availableStrategies -Count $StrategiesCount
    $StrategyName = $selectedStrategies -join ","
    Write-Host "Wybrane losowo strategie: $StrategyName"
} else {
    $strategyDigit = $Code.Substring(0,1)
    if (-not $strategyMap.ContainsKey($strategyDigit)) {
        Write-Error "Niepoprawny kod – brak mapowania dla strategii."
        exit 1
    }
    $StrategyName = $strategyMap[$strategyDigit]
}

Write-Host "Dekodowano:" -ForegroundColor Cyan
Write-Host "  Strategia: $StrategyName"
Write-Host "  BotMode: $BotMode"
Write-Host "  TradingMode: $TradingMode"

# Wybór pliku JSON na podstawie BotMode – zakładamy, że plik znajduje się w:
# "$PSScriptRoot\..\Data\Strategies\<primaryStrategy>\<botmode>.json"
$primaryStrategy = ($StrategyName -split ",")[0].Trim()
$jsonFile = "$PSScriptRoot\..\Data\Strategies\$primaryStrategy\$($BotMode.ToLower()).json"
Write-Host "Sprawdzam ścieżkę: $jsonFile"
if (-not (Test-Path $jsonFile)) {
    Write-Error "Plik $jsonFile nie istnieje!"
    exit 1
}

# Ładowanie pliku JSON i aktualizacja pól
$jsonContent = Get-Content $jsonFile -Raw | ConvertFrom-Json
$jsonContent.StrategyName = $StrategyName
$jsonContent.BotMode = $BotMode
$jsonContent.TradingMode = $TradingMode

# Kopiowanie ApiKey/ApiSecret z appsettings.json – plik znajduje się w katalogu nadrzędnym
$appSettingsFile = "$PSScriptRoot\..\appsettings.json"
if (-not (Test-Path $appSettingsFile)) {
    Write-Warning "Plik appsettings.json nie został znaleziony. Dane ApiKey/ApiSecret nie zostaną zaktualizowane."
} else {
    $appSettings = Get-Content $appSettingsFile -Raw | ConvertFrom-Json
    if ($appSettings.TradingBot -and $appSettings.TradingBot.Accounts) {
        if ($Demo) {
            # Tryb demo: kopiujemy wszystkie konta i ustawiamy AccountName na pierwsze konto demo
            $jsonContent.Accounts = $appSettings.TradingBot.Accounts
            $demoAccount = $appSettings.TradingBot.Accounts | Where-Object { $_.IsDemo -eq $true } | Select-Object -First 1
            if ($demoAccount) {
                $jsonContent.AccountName = $demoAccount.Name
                Write-Host "Demo mode: Skopiowano wszystkie konta i ustawiono konto demo: $($demoAccount.Name)."
            } else {
                Write-Warning "Demo mode: Nie znaleziono konta demo w appsettings.json."
            }
        } else {
            # Produkcyjny: kopiujemy tylko konto wskazane w AccountName
            $selectedAccount = $appSettings.TradingBot.Accounts | Where-Object { $_.Name -eq $jsonContent.AccountName }
            if ($selectedAccount) {
                $jsonContent.Accounts = @($selectedAccount)
                Write-Host "Skopiowano ApiKey i ApiSecret z appsettings.json dla konta $($jsonContent.AccountName)."
            } else {
                Write-Warning "Nie znaleziono konta o nazwie $($jsonContent.AccountName) w appsettings.json."
            }
        }
    } else {
        Write-Warning "Struktura appsettings.json nie zawiera TradingBot.Accounts."
    }
}

# Zapisanie zaktualizowanego JSONa (nadpisanie oryginalnego pliku)
$jsonContent | ConvertTo-Json -Depth 10 | Set-Content $jsonFile
Write-Host "Plik $jsonFile został zaktualizowany."

# Funkcja rekurencyjnego spłaszczania obiektu JSON z obsługą tablic – rozwija zagnieżdżone elementy używając separatora __
function Flatten-Json {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Data,
        [string]$Prefix = ""
    )
    $result = @{}
    # Jeżeli obiekt jest tablicą
    if ($Data -is [System.Array]) {
        $index = 0
        foreach ($item in $Data) {
            $newPrefix = if ($Prefix) { "$Prefix`__$index" } else { $index.ToString() }
            $result += Flatten-Json -Data $item -Prefix $newPrefix
            $index++
        }
    }
    # Jeżeli obiekt jest obiektem (custom) – przetwarzamy tylko właściwości typu NoteProperty
    elseif ($Data -is [psobject] -and $Data.PSObject.Properties.Count -gt 0) {
        foreach ($prop in $Data.PSObject.Properties) {
            if ($prop.MemberType -ne 'NoteProperty') { continue }
            $newPrefix = if ($Prefix) { "$Prefix`__$($prop.Name)" } else { $prop.Name }
            $result += Flatten-Json -Data $prop.Value -Prefix $newPrefix
        }
    }
    else {
        $result[$Prefix] = $Data.ToString()
    }
    return $result
}

$flattened = Flatten-Json -Data $jsonContent

# Generowanie linii zmiennych środowiskowych w formacie "CB_TradingBot__<Key>=<Value>"
# Dodajemy dwa tabulatory (8 spacji) przed każdą linią.
$envLines = $flattened.GetEnumerator() | Sort-Object Key | ForEach-Object {
    "        - CB_TradingBot__$($_.Key)=$($_.Value -replace ',', '.')"
}


# Aktualizacja pliku docker-compose.yml – ścieżka:
# "$PSScriptRoot\..\Data\Strategies\$StrategyName\Docker\docker-compose.yml"
$composeFile = "$PSScriptRoot\..\Data\Strategies\$StrategyName\Docker\docker-compose.yml"
if (-not (Test-Path $composeFile)) {
    Write-Error "Plik docker-compose.yml nie został znaleziony!"
    exit 1
}

$composeContent = Get-Content $composeFile -Raw
$pattern = "(?ms)(environment:\s*\n(?:\s+- .*\n)+)"
if ($composeContent -match $pattern) {
    $composeContent = $composeContent -replace $pattern, "environment:`n$($envLines -join "`n")`n"
} else {
    $composeContent = $composeContent -replace "(cryptoblade:\s*\n)", "`$1    environment:`n$($envLines -join "`n")`n"
}
Set-Content $composeFile $composeContent
Write-Host "Plik docker-compose.yml został zaktualizowany."

# Uruchomienie docker login i docker compose up
Write-Host "Logowanie do Docker..."
docker login
if ($LASTEXITCODE -ne 0) {
    Write-Error "Błąd logowania do Docker."
    exit 1
}

Set-Location "$PSScriptRoot\..\Data\Strategies\$StrategyName\Docker"
Write-Host "Uruchamianie docker compose up..."
docker compose up -d
if ($LASTEXITCODE -ne 0) {
    Write-Error "Błąd przy uruchamianiu docker compose."
    exit 1
}

Write-Host "Kontenery zostały uruchomione, a wyniki backtestu będą zapisywane zgodnie z konfiguracją."


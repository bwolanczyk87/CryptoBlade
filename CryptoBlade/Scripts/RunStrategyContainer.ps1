<# 
    deploy.ps1 - Skrypt do dekodowania trzycyfrowego kodu, dynamicznego pobierania mappingów
    z plików źródłowych (Strategies/StrategyNames.cs, Configuration/BotMode.cs, Configuration/TradingMode.cs),
    aktualizacji plików JSON (np. live.json, backtest.json, optimizer.json) z relatywną ścieżką,
    generowania zmiennych środowiskowych do sekcji environment w 
    pliku docker-compose.yml oraz uruchomienia kontenerów (docker login + docker compose up).

    Parametry:
      - Code             : trzycyfrowy kod (np. "123")
      - Demo             : (opcjonalnie) flaga bool, jeśli $true – tryb demo (kopiujemy wszystkie konta i ustawiamy konto demo), domyślnie $false

    Użycie:
        .\deploy.ps1 -Code "123" [-Demo $true]
#>

param (
    [Parameter(Mandatory = $true)]
    [string]$Code,

    [Parameter(Mandatory = $false)]
    [bool]$Demo = $true
)

# Walidacja kodu
if ($Code -notmatch '^\d{3}$') {
    Write-Error "Kod musi składać się z dokładnie 3 cyfr."
    exit 1
}

# Sprawdź status Dockera i uruchom jeśli nie działa
Write-Host "Sprawdzam status Dockera..."
$dockerStatus = & systemctl is-active docker 2>$null

if ($dockerStatus -ne "active") {
    Write-Host "Docker nie jest aktywny. Próbuję uruchomić..."

    # Automatyczne wykrywanie socketa Docker
    if (Test-Path "$HOME/.docker/desktop/docker.sock") {
        $env:DOCKER_HOST = "unix://$HOME/.docker/desktop/docker.sock"
        Write-Host "Używam socketa Docker Desktop: $HOME/.docker/desktop/docker.sock"
    } 
    elseif (Test-Path "/var/run/docker.sock") {
        $env:DOCKER_HOST = "unix:///var/run/docker.sock"
        Write-Host "Używam klasycznego socketa: /var/run/docker.sock"
    }
    else {
        Write-Warning "Nie znaleziono socketa Docker. Sprawdź, czy Docker jest uruchomiony."
    }

    try {
        sudo systemctl start docker
        systemctl --user start docker-desktop
        Start-Sleep -Seconds 3
        $dockerStatus = & systemctl is-active docker 2>$null
        if ($dockerStatus -eq "active") {
            Write-Host "Docker został uruchomiony."

            # Uruchomienie docker login
            Write-Host "Logowanie do Docker..."
            $accountsConfigPath = "$scriptRoot/../../appsettings.Accounts.json"
            $accountsConfig = Get-Content $accountsConfigPath | ConvertFrom-Json
            $dockerToken = $accountsConfig.Docker.Token
            $dockerLogin = $accountsConfig.Docker.Login 

            if (-not $dockerToken -or -not $dockerLogin) {
                Write-Error "Nie znaleziono tokena lub loginu do Dockera w pliku appsettings.Accounts.json."
                exit 1
            }
            else {
                $dockerToken | docker login --username $dockerLogin --password-stdin
            }

            if ($LASTEXITCODE -ne 0) {
                Write-Error "Błąd logowania do Docker."
                exit 1
            }
        } else {
            Write-Error "Nie udało się uruchomić Dockera. Sprawdź uprawnienia lub zainstaluj Docker."
            exit 1
        }
    } catch {
        Write-Error "Błąd podczas uruchamiania Dockera: $_"
        exit 1
    }
} else {
    Write-Host "Docker jest aktywny."
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
$strategyDigit = $Code.Substring(0,1)
if (-not $strategyMap.ContainsKey($strategyDigit)) {
    Write-Error "Niepoprawny kod – brak mapowania dla strategii."
    exit 1
}
$StrategyName = $strategyMap[$strategyDigit]

# Wybór pliku JSON na podstawie BotMode – zakładamy, że plik znajduje się w:
# "$PSScriptRoot\..\Data\Strategies\<primaryStrategy>\<botmode>\<botmode>.json"
$primaryStrategy = ($StrategyName -split ",")[0].Trim()
$jsonFile = "$PSScriptRoot\..\Data\Strategies\$primaryStrategy\$BotMode\$($BotMode.ToLower()).json"
Write-Host "Sprawdzam ścieżkę: $jsonFile"
if (-not (Test-Path $jsonFile)) {
    Write-Error "Plik $jsonFile nie istnieje!"
    exit 1
}

# Ładowanie pliku JSON i aktualizacja pól
$jsonContent = Get-Content $jsonFile -Raw | ConvertFrom-Json
$jsonContent.StrategyName = $StrategyName
$jsonContent.BotMode = $BotMode

if ($BotMode -eq "Backtest") {
    $jsonContent.TradingMode = "DynamicBackTest"
} else {
    $jsonContent.TradingMode = $TradingMode
}

Write-Host "Dekodowano:" -ForegroundColor Cyan
Write-Host "  Strategia: $StrategyName"
Write-Host "  BotMode: $BotMode"
Write-Host "  TradingMode: $TradingMode"

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
    if ($Data -is [System.Array]) {
        $index = 0
        foreach ($item in $Data) {
            $newPrefix = if ($Prefix) { "$Prefix`__$index" } else { $index.ToString() }
            $result += Flatten-Json -Data $item -Prefix $newPrefix
            $index++
        }
    }
    elseif ($Data -is [psobject] -and $Data.PSObject.Properties.Count -gt 0) {
        foreach ($prop in $Data.PSObject.Properties) {
            if ($prop.MemberType -ne 'NoteProperty') { continue }
            $newPrefix = if ($Prefix) { "$Prefix`__$($prop.Name)" } else { $prop.Name }
            $result += Flatten-Json -Data $prop.Value -Prefix $newPrefix
        }
    }
    else {
        if ($Data -is [DateTime]) {
            $result[$Prefix] = $Data.ToString("yyyy-MM-ddTHH:mm:ss")
        } else {
            $result[$Prefix] = $Data.ToString()
        }
    }
    return $result
}

$jsonContent = Get-Content $jsonFile -Raw | ConvertFrom-Json
$flattened = Flatten-Json -Data $jsonContent

# Generowanie linii zmiennych środowiskowych w formacie "CB_TradingBot__<Key>=<Value>"
# Dodajemy dwa tabulatory (8 spacji) przed każdą linią.
$envLines = $flattened.GetEnumerator() | Sort-Object Key | ForEach-Object {
    "        - CB_TradingBot__$($_.Key)=$($_.Value -replace ',', '.')"
}


# Aktualizacja pliku docker-compose.yml – ścieżka:
# "$PSScriptRoot\..\Data\Strategies\$StrategyName\Docker\docker-compose.yml"
$composeFile = "$PSScriptRoot\..\Data\Strategies\$StrategyName\_docker\docker-compose.yml"
if (-not (Test-Path $composeFile)) {
    Write-Error "Plik docker-compose.yml nie został znaleziony!"
    exit 1
}

$composeContent = Get-Content $composeFile -Raw
$pattern = "(?ms)(^\s*environment:\s*\n(?:\s+- .*\n)*)"
if ($composeContent -match $pattern) {
    $composeContent = $composeContent -replace $pattern, "    environment:`n$($envLines -join "`n")`n"
} else {
    $composeContent = $composeContent -replace "(environment:\s*\n)", "    environment:`n$($envLines -join "`n")`n"
}

$containerName = "$($StrategyName.ToLower())_$($BotMode.ToLower())"
if ($composeContent -match "container_name:\s*\S+") {
    # Zastępujemy istniejącą wartość
    $composeContent = $composeContent -replace "container_name:\s*\S+", "container_name: $containerName"
} else {
    # Jeżeli nie ma pola container_name, wstawiamy je pod sekcją usługi (przyjmujemy, że usługa ma nazwę 'cryptoblade:')
    $composeContent = $composeContent -replace "(cryptoblade:\s*\n)", "`$1    container_name: $containerName`n"
}

Set-Content $composeFile $composeContent
Write-Host "Plik docker-compose.yml został zaktualizowany."

$config = Get-Content "$PSScriptRoot\..\appsettings.Accounts.json" | ConvertFrom-Json
$account = $config.TradingBot.Accounts | Where-Object { $_.Name -eq $jsonContent.AccountName }
$env:CB_TradingBot__Accounts__0__Name=$account.Name
$env:CB_TradingBot__Accounts__0__ApiKey=$account.ApiKey
$env:CB_TradingBot__Accounts__0__ApiSecret=$account.ApiSecret
$env:CB_TradingBot__Accounts__0__Exchange=$account.Exchange
$env:CB_TradingBot__Accounts__0__IsDemo=$account.IsDemo

$secondaryAccount = $($config.TradingBot.Accounts | Where-Object { !$_.IsDemo })[0]
$env:CB_TradingBot__Accounts__1__Name=$secondaryAccount.Name
$env:CB_TradingBot__Accounts__1__ApiKey=$secondaryAccount.ApiKey
$env:CB_TradingBot__Accounts__1__ApiSecret=$secondaryAccount.ApiSecret
$env:CB_TradingBot__Accounts__1__Exchange=$secondaryAccount.Exchange
$env:CB_TradingBot__Accounts__1__IsDemo=$secondaryAccount.IsDemo

Set-Location "$PSScriptRoot\..\Data\Strategies\$StrategyName\_docker"
Write-Host "Uruchamianie docker compose up..."

docker network rm "$($containerName)_default"
docker rm -f $containerName
docker-compose -p $containerName up -d --force-recreate
if ($LASTEXITCODE -ne 0) {
    Write-Error "Błąd przy uruchamianiu docker compose."
    exit 1
}


Write-Host "Kontenery zostały uruchomione, a wyniki backtestu będą zapisywane zgodnie z konfiguracją."

Param(
    [Parameter(Mandatory = $true)]
    [string]$StrategyName
)

$SourceDir = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade"
$DestDir   = "C:\Users\bwola\source\repos\CryptoBlade\CBCLone"

# Upewnij się, że folder docelowy istnieje
if (!(Test-Path -Path $DestDir)) {
    New-Item -ItemType Directory -Path $DestDir | Out-Null
}

if ($SourceDir[-1] -ne '\') {
    $SourceDir += "\"
}

# Krok 1: Pobierz pliki ze źródłowego folderu, stosując filtry
$filteredFiles = Get-ChildItem -Path $SourceDir -Recurse -File | Where-Object { 
    ($_.FullName -notmatch '(?i)cryptoblade\.tar$') -and
    ($_.FullName -notmatch '(?i)Dockerfile$') -and
    ($_.FullName -notmatch '(?i)\\bin(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\obj(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\HistoricalData(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTest(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTests(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTesting(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Optimizer(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\OptimizerResults(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\wwwroot(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Pages(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\appsettings.Development.json(\\|$)')
}

# Krok 2: Wczytaj listę strategii z pliku StrategyNames.cs
$csFilePath = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade\Strategies\StrategyNames.cs"
$pattern = 'public const string\s+\w+\s*=\s*"([^"]+)"'
$strategyNames = Select-String -Path $csFilePath -Pattern $pattern | ForEach-Object {
    $_.Matches[0].Groups[1].Value
}

# Filtrowanie plików według strategii:
# Jeśli plik nie zawiera żadnej z nazw strategii – jest dozwolony.
# Jeśli zawiera którąś nazwę strategii, to dozwolony tylko, jeśli wszystkie wystąpienia są zgodne z parametrem $StrategyName.
# $filteredFiles = $filteredFiles | Where-Object {
#     $fileLower = $_.FullName.ToLower()
#     $matches = $strategyNames | Where-Object { $fileLower.Contains($_.ToLower()) }
    
#     if ($matches.Count -eq 0) {
#         $true
#     }
#     else {
#         $allChosen = $matches | Where-Object { $_.ToLower() -eq $StrategyName.ToLower() }
#         $allChosen.Count -eq $matches.Count
#     }
# }

# Krok 3: Przetwórz dynamiczne foldery z wynikami backtestów
$backTestsRoot = Join-Path $SourceDir "Samples\BackTest\BackTests"
if (Test-Path $backTestsRoot) {
    # Pobierz wszystkie foldery (zakładamy, że nazwy są tworzone dynamicznie, np. oparte na timestamp)
    $backTestFolders = Get-ChildItem -Path $backTestsRoot -Directory
    $validBackTestFolders = @()
    
    foreach ($folder in $backTestFolders) {
        $appSettingsPath = Join-Path $folder.FullName "appsettings.json"
        if (Test-Path $appSettingsPath) {
            try {
                $jsonContent = Get-Content -Path $appSettingsPath -Raw | ConvertFrom-Json
                # Sprawdź, czy pole StrategyName w JSON odpowiada przekazanemu parametrowi
                if ($jsonContent.StrategyName -and ($jsonContent.StrategyName.ToLower() -eq $StrategyName.ToLower())) {
                    $validBackTestFolders += $folder
                }
            } catch {
                Write-Host "Błąd wczytywania JSON z $appSettingsPath"
            }
        }
    }
    
    if ($validBackTestFolders.Count -gt 0) {
        # Wybierz najnowszy folder (na podstawie LastWriteTime)
        $latestFolder = $validBackTestFolders | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        Write-Host "Wybrano folder backtest: $($latestFolder.FullName)"
        # Pobierz wszystkie pliki z wybranego folderu, pomijając pliki PNG
        $backTestFiles = Get-ChildItem -Path $latestFolder.FullName -File | Where-Object { $_.Extension.ToLower() -ne ".png" }
        # Dodaj te pliki do listy do skopiowania
        $filteredFiles += $backTestFiles
    }
}
$filteredFiles.Count
# Krok 4: Skopiuj wszystkie wybrane pliki do folderu docelowego, generując nową nazwę na bazie relatywnej ścieżki
$filteredFiles | ForEach-Object {
    $RelativePath = $_.FullName.Substring($SourceDir.Length)
    $NewFileName = $RelativePath -replace '[\\\/]', '_'
    $DestFilePath = Join-Path -Path $DestDir -ChildPath $NewFileName

    Write-Host $_.FullName
    # Odkomentuj poniższą linię, aby faktycznie kopiować pliki:
    # Copy-Item -Path $_.FullName -Destination $DestFilePath
}

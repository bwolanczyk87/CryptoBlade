#!/usr/bin/env pwsh

Param(
    [Parameter(Mandatory = $true)]
    [int]$StrategyNumber,
    [string]$Version = "1.0.0",
    [switch]$ExtendedIndex
)

# Mapowanie numerów strategii (bez zmian)
$strategyMap = @{
    1 = "AutoHedge"
    2 = "MfiRsiCandlePrecise"
    3 = "MfiRsiEriTrend"
    4 = "LinearRegression"
    5 = "Tartaglia"
    6 = "Mona"
    7 = "Qiqi"
    8 = "Momentum"
}

# Walidacja numeru strategii
if (-not $strategyMap.ContainsKey($StrategyNumber)) {
    Write-Host "Nieprawidłowy numer strategii. Dostępne numery to 1-8."
    exit
}
$StrategyName = $strategyMap[$StrategyNumber]

# ZMIANA: Ścieżki Linuxowe
$SourceDir = "/home/inclusion/Repositories/CryptoBlade/CryptoBlade/"
$MainDestDir = "/home/inclusion/Repositories/CryptoBlade/CBClone"

# ZMIANA: Sprawdzanie końcowego slash-a dla Linuxa
if ($SourceDir[-1] -ne '/') { $SourceDir += "/" }

# Przygotowanie folderu docelowego
$DestDir = Join-Path -Path $MainDestDir -ChildPath $StrategyName
if (Test-Path $DestDir) {
    Remove-Item -Path "$DestDir/*" -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $DestDir | Out-Null
}

# Filtrowanie plików
$filteredFiles = Get-ChildItem -Path $SourceDir -Recurse -File | Where-Object { 
    ($_.FullName -notmatch '\.sln$') -and
    ($_.FullName -notmatch '\.vscode_launch$') -and
    ($_.FullName -notmatch 'cryptoblade\.tar$') -and
    ($_.FullName -notmatch 'Dockerfile$') -and
    # ZMIANA: Separatory ścieżek
    ($_.FullName -notmatch '/bin(/|$)') -and
    ($_.FullName -notmatch '/obj(/|$)') -and
    ($_.FullName -notmatch '/HistoricalData(/|$)') -and
    ($_.FullName -notmatch '/Documentation(/|$)') -and
    ($_.FullName -notmatch '/Scripts(/|$)') -and
    ($_.FullName -notmatch '/Backtest(/|$)') -and
    ($_.FullName -notmatch '/BackTesting(/|$)') -and
    ($_.FullName -notmatch '/Optimizer(/|$)') -and
    ($_.FullName -notmatch '/wwwroot(/|$)') -and
    ($_.FullName -notmatch '/Pages(/|$)') -and
    ($_.FullName -notmatch '/appsettings.Development.json$') -and
    ($_.FullName -notmatch '/CryptoBlade.csproj$') -and
    ($_.FullName -notmatch '/Properties/launchSettings.json$') -and
    ($_.FullName -notmatch '/docker-compose') -and
    ($_.FullName -notmatch '/appsettings.Accounts.json$')
}

# Wczytywanie nazw strategii
$csFilePath = "/home/inclusion/Repositories/CryptoBlade/CryptoBlade/Strategies/StrategyNames.cs"
$strategyNames = Select-String -Path $csFilePath -Pattern 'public const string\s+\w+\s*=\s*"([^"]+)"' | 
                 ForEach-Object { $_.Matches[0].Groups[1].Value }

# Filtrowanie po nazwach strategii
$filteredFiles = $filteredFiles | Where-Object {
    $fileLower = $_.FullName.ToLower()
    $matches = $strategyNames | Where-Object { $fileLower.Contains($_.ToLower()) }
    ($matches.Count -eq 0) -or ($matches.ForEach({ $_.ToLower() }) -contains $StrategyName.ToLower())
}

# Przetwarzanie wyników backtestów
$backTestsRoot = Join-Path $SourceDir "Data/Strategies/${StrategyName}/BackTests/Results"
$combinedAppSettings = @{}
$combinedResults = @{}

if (Test-Path $backTestsRoot) {
    Get-ChildItem -Path $backTestsRoot -Directory | ForEach-Object {
        $appSettingsPath = Join-Path $_.FullName "appsettings.json"
        $resultPath = Join-Path $_.FullName "result.json"
        
        if (Test-Path $appSettingsPath) {
            try {
                $json = Get-Content $appSettingsPath -Raw | ConvertFrom-Json
                if ($json.StrategyName -eq $StrategyName) {
                    $json.PSObject.Properties.Remove("Accounts")
                    $combinedAppSettings[$_.Name] = $json
                    
                    if (Test-Path $resultPath) {
                        $combinedResults[$_.Name] = Get-Content $resultPath -Raw | ConvertFrom-Json
                    }
                }
            } catch { Write-Host "Błąd JSON: $appSettingsPath" }
        }
    }

    if ($combinedAppSettings.Count -gt 0) {
        $combinedAppSettings | ConvertTo-Json -Depth 10 | 
        Out-File (Join-Path $DestDir "Backtest_AppSettings.json") -Encoding utf8
    }

    if ($combinedResults.Count -gt 0) {
        $combinedResults | ConvertTo-Json -Depth 10 | 
        Out-File (Join-Path $DestDir "Backtest_Result.json") -Encoding utf8
    }
}

# Scalanie plików .cs
$csFiles = $filteredFiles | Where-Object Extension -eq ".cs"
if ($csFiles) {
    $groupedFiles = $csFiles | ForEach-Object {
        $relativePath = $_.FullName.Substring($SourceDir.Length)
        $group = ($relativePath -split '/')[0]
        if (-not $group) { $group = "CryptoBlade" }
        [PSCustomObject]@{File=$_; Group=$group}
    } | Group-Object Group

    $dependencyMap = @{}
    
    foreach ($group in $groupedFiles) {
        $groupNamespace = if ($group.Name -eq "CryptoBlade") { "CryptoBlade" } 
                          else { "CryptoBlade.$($group.Name)" }
        $destFileName = if ($group.Name -eq "CryptoBlade") { "CryptoBlade.cs" } 
                         else { "CryptoBlade_$($group.Name).cs" }

        $processedContents = @()
        $indexEntries = @()
        $fileCounter = 1

        foreach ($item in $group.Group) {
            $content = Get-Content $item.File.FullName -Raw
            $contentNoUsing = $content -replace '(?m)^\s*using\s+.*?;\s*(\r?\n)?', ''
            
            if (-not ($contentNoUsing -match 'namespace\s+')) {
                $contentNoUsing = "namespace $groupNamespace {`n$contentNoUsing`n}"
            }

            $indexEntries += "$fileCounter. $($item.File.Name)"
            $processedContents += "// ==== FILE #$fileCounter => $($item.File.Name) ====`n$contentNoUsing"
            $fileCounter++
        }

        $finalContent = @"
// *** METADATA ***
// Version: $Version
// Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
// Module: $groupNamespace
// ****************

$($processedContents -join "`n`n// -----------------------------`n`n")
"@
        $finalContent | Out-File (Join-Path $DestDir $destFileName) -Encoding utf8
        $dependencyMap[$group.Name] = $indexEntries
    }

    $dependencyMap | ConvertTo-Json | Out-File (Join-Path $DestDir "DependencyMap.json") -Encoding utf8
}

# Kopiowanie pozostałych plików
$filteredFiles | Where-Object Extension -ne ".cs" | ForEach-Object {
    $NewFileName = $_.FullName.Substring($SourceDir.Length).Replace("/", "_")
    Copy-Item $_.FullName (Join-Path $DestDir $NewFileName)
}

# ZMIANA: Ścieżki do zasobów
$appsettingsPath = Join-Path $SourceDir "appsettings.json"
if (Test-Path $appsettingsPath) {
    Get-Content $appsettingsPath | ConvertFrom-Json | 
    ConvertTo-Json -Depth 10 | Out-File (Join-Path $DestDir "appsettings.json") -Encoding utf8
}

# ZMIANA: Linuxowe ścieżki do dodatkowych zasobów
$botNotesPath = "/home/inclusion/Repositories/CryptoBlade/CBClone/bot_notes.md"
if (Test-Path $botNotesPath) {
    Copy-Item $botNotesPath (Join-Path $DestDir "bot_notes.md")
}

# Scalanie dokumentacji
$docsPath = "/home/inclusion/Repositories/CryptoBlade/CryptoBlade/Documentation"
if (Test-Path $docsPath) {
    $mergedDocs = Get-ChildItem $docsPath -Recurse -Filter "*.md" | 
                  ForEach-Object { "# ==== $($_.Name) ====`n$(Get-Content $_.FullName -Raw)" }
    $mergedDocs -join "`n" | Out-File (Join-Path $DestDir "Merged_Documentation.md") -Encoding utf8
}

Write-Host "Skrypt zakończony pomyślnie! Wynik w: $DestDir"
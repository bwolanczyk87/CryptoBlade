Param(
    [Parameter(Mandatory = $true)]
    [string]$StrategyName
)

# Ścieżki źródłowe i główny folder docelowy
$SourceDir = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade"
$MainDestDir = "C:\Users\bwola\source\repos\CryptoBlade\CBCLone"

# Folder docelowy z nazwą strategii (tworzony lub czyszczony)
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
    ($_.FullName -notmatch '(?i)cryptoblade\.tar$') -and
    ($_.FullName -notmatch '(?i)Dockerfile$') -and
    ($_.FullName -notmatch '(?i)\\bin(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\obj(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\HistoricalData(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTest(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTests(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTesting(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Optimizer(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\wwwroot(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Pages(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\appsettings.Development.json(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\CryptoBlade.csproj(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Properties\\launchSettings.json(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\appsettings.json(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\docker-compose(\\|$)')
}

# Wczytanie listy strategii z pliku StrategyNames.cs
$csFilePath = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade\Strategies\StrategyNames.cs"
$pattern = 'public const string\s+\w+\s*=\s*"([^"]+)"'
$strategyNames = Select-String -Path $csFilePath -Pattern $pattern | ForEach-Object {
    $_.Matches[0].Groups[1].Value
}

# Filtrowanie wg strategii:
# Jeśli plik nie zawiera żadnej z nazw strategii – jest dozwolony.
# Jeśli zawiera którąś nazwę, to dozwolony tylko, gdy wszystkie wystąpienia są zgodne z parametrem.
if (![string]::IsNullOrEmpty($StrategyName)) {
    $filteredFiles = $filteredFiles | Where-Object {
        $fileLower = $_.FullName.ToLower()
        $matches = $strategyNames | Where-Object { $fileLower.Contains($_.ToLower()) }
        
        if ($matches.Count -eq 0) {
            $true
        }
        else {
            $allChosen = $matches | Where-Object { $_.ToLower() -eq $StrategyName.ToLower() }
            $allChosen.Count -eq $matches.Count
        }
    }
}

#######################################################
# Krok 1.5: Przetwarzanie folderów BackTests (Samples\BackTest\BackTests)
#######################################################
# Utwórz puste mapy na scalone dane z appsettings.json i result.json
$combinedAppSettings = @{}
$combinedResults = @{}

$backTestsRoot = Join-Path $SourceDir "Samples\BackTest\BackTests"
if (Test-Path $backTestsRoot) {
    # Pobierz wszystkie foldery (nazwy dynamiczne, np. timestamp)
    $backTestFolders = Get-ChildItem -Path $backTestsRoot -Directory
    foreach ($folder in $backTestFolders) {
        # Ścieżki do plików appsettings.json i result.json (pomijamy result_detailed.json)
        $appSettingsPath = Join-Path $folder.FullName "appsettings.json"
        $resultPath = Join-Path $folder.FullName "result.json"
        
        # Przetwarzanie appsettings.json
        if (Test-Path $appSettingsPath) {
            try {
                $jsonContent = Get-Content -Path $appSettingsPath -Raw | ConvertFrom-Json
                if ($jsonContent.StrategyName -and ($jsonContent.StrategyName.ToLower() -eq $StrategyName.ToLower())) {
                    # Usuń klucz "Accounts" jeśli istnieje
                    if ($jsonContent.PSObject.Properties.Name -contains "Accounts") {
                        $jsonContent.PSObject.Properties.Remove("Accounts")
                    }
                    $combinedAppSettings[$folder.Name] = $jsonContent

                    # Przetwarzanie result.json (scalamy, jeżeli appsettings pasuje)
                    if (Test-Path $resultPath) {
                        try {
                            $jsonResult = Get-Content -Path $resultPath -Raw | ConvertFrom-Json
                            $combinedResults[$folder.Name] = $jsonResult
                        } catch {
                            Write-Host "Błąd wczytywania JSON z $resultPath"
                        }
                    }
                }
            } catch {
                Write-Host "Błąd wczytywania JSON z $appSettingsPath"
            }
        }
    }
    
    # Zapis scalonych plików JSON (jeśli cokolwiek zostało zebrane)
    if ($combinedAppSettings.Keys.Count -gt 0) {
        $appSettingsOutPath = Join-Path -Path $DestDir -ChildPath "Backtest_AppSettings.json"
        $combinedAppSettings | ConvertTo-Json -Depth 10 | Out-File -FilePath $appSettingsOutPath -Encoding utf8
        Write-Host "Zapisano scalony plik appsettings:" $appSettingsOutPath
    }
    if ($combinedResults.Keys.Count -gt 0) {
        $resultOutPath = Join-Path -Path $DestDir -ChildPath "Backtest_Result.json"
        $combinedResults | ConvertTo-Json -Depth 10 | Out-File -FilePath $resultOutPath -Encoding utf8
        Write-Host "Zapisano scalony plik result:" $resultOutPath
    }
}

#######################################################
# Krok 2: Podział plików na .cs i pozostałe
#######################################################
$csFiles = $filteredFiles | Where-Object { $_.Extension -eq ".cs" }
$otherFiles = $filteredFiles | Where-Object { $_.Extension -ne ".cs" }

###########################################################
# Krok 3: Scalanie plików .cs wg namespace i zapis scalonych
###########################################################
if ($csFiles) {
    $filesByNamespace = $csFiles | ForEach-Object {
        $content = Get-Content $_.FullName -Raw
        # Wyszukaj deklarację namespace (zakładamy standardowy format)
        $namespaceMatch = [regex]::Match($content, 'namespace\s+([\w\.]+)')
        if ($namespaceMatch.Success) {
            [PSCustomObject]@{
                File      = $_
                Namespace = $namespaceMatch.Groups[1].Value
                Content   = $content
            }
        }
    } | Group-Object -Property Namespace

    foreach ($group in $filesByNamespace) {
        $ns = $group.Name

        $usingStatements = @()
        $innerContents = @()
        
        foreach ($item in $group.Group) {
            # Pobierz linie zaczynające się od "using"
            $usings = Select-String -InputObject $item.Content -Pattern '^\s*using\s+[^;]+;' -AllMatches |
                      ForEach-Object { $_.Matches } | ForEach-Object { $_.Value.Trim() }
            $usingStatements += $usings

            # Usuń dyrektywy using
            $contentNoUsing = $item.Content -replace '(^\s*using\s+[^;]+;\s*\r?\n)+', ''

            # Wyciągnij zawartość wewnątrz bloku namespace
            $nsPattern = 'namespace\s+[\w\.]+\s*\{([\s\S]*)\}\s*$'
            $m = [regex]::Match($contentNoUsing, $nsPattern)
            if ($m.Success) {
                $innerContent = $m.Groups[1].Value.Trim()
            } else {
                $innerContent = $contentNoUsing.Trim()
            }
            $innerContents += $innerContent
        }

        # Unikalne dyrektywy using
        $uniqueUsings = $usingStatements | Select-Object -Unique

        # Połącz zawartość wewnętrzną (wszystkie treści scalone)
        $combinedInner = $innerContents -join "`n`n"

        # Owijamy wynik w blok namespace
        $combinedFileContent = ($uniqueUsings -join "`n") + "`n`n" +
                               "namespace $ns {" + "`n" +
                               $combinedInner + "`n" +
                               "}"

        # Nazwa wynikowego pliku oparta o namespace (zamiana kropek na podkreślenia)
        $destFileName = ($ns -replace '\.', '_') + ".cs"
        $destFilePath = Join-Path -Path $DestDir -ChildPath $destFileName

        Write-Host "Tworzę scalony plik:" $destFilePath
        $combinedFileContent | Out-File -FilePath $destFilePath -Encoding utf8
    }
}

##################################################
# Krok 4: Kopiowanie pozostałych (nie .cs) plików
##################################################
if ($otherFiles) {
    $otherFiles | ForEach-Object {
        $RelativePath = $_.FullName.Substring($SourceDir.Length)
        $NewFileName = $RelativePath -replace '[\\\/]', '_'
        $DestFilePath = Join-Path -Path $DestDir -ChildPath $NewFileName

        Write-Host "Kopiowanie:" $_.FullName "->" $DestFilePath
        Copy-Item -Path $_.FullName -Destination $DestFilePath
    }
}

Param(
    [Parameter(Mandatory = $true)]
    [int]$StrategyNumber,
    [string]$Version = "1.0.0",
    [switch]$ExtendedIndex  # opcjonalnie dodaje wyszukiwanie klas i interfejsów
)

# Mapowanie numerów na nazwy strategii
$strategyMap = @{
    1 = "AutoHedge"
    2 = "MfiRsiCandlePrecise"
    3 = "MfiRsiEriTrend"
    4 = "LinearRegression"
    5 = "Tartaglia"
    6 = "Mona"
    7 = "Qiqi"
}

# Pobranie nazwy strategii na podstawie numeru
if ($strategyMap.ContainsKey($StrategyNumber)) {
    $StrategyName = $strategyMap[$StrategyNumber]
} else {
    Write-Host "Nieprawidłowy numer strategii. Dostępne numery to 1-7."
    exit
}
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
    ($_.FullName -notmatch '(?i)\.sln$') -and
    ($_.FullName -notmatch '(?i)\.vscode_launch$') -and
    ($_.FullName -notmatch '(?i)cryptoblade\.tar$') -and
    ($_.FullName -notmatch '(?i)Dockerfile$') -and
    ($_.FullName -notmatch '(?i)\\bin(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\obj(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\HistoricalData(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Documentation(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Scripts(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Backtest(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\BackTesting(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Optimizer(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\wwwroot(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Pages(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\appsettings.Development.json(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\CryptoBlade.csproj(\\|$)') -and
    ($_.FullName -notmatch '(?i)\\Properties\\launchSettings.json(\\|$)') -and
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
# Krok 1.5: Przetwarzanie folderów Results (Data\Strategies\${StrategyName}\BackTests\Results)
#######################################################
# Utwórz puste mapy na scalone dane z appsettings.json i result.json
$combinedAppSettings = @{}
$combinedResults = @{}

$backTestsRoot = Join-Path $SourceDir "Data\Strategies\${StrategyName}\BackTests\Results"
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

# --------------------------------------------------
# Krok 3: Scalanie plików .cs w oddzielne pliki wg głównych folderów
# --------------------------------------------------
if ($csFiles) {
    # Grupujemy pliki .cs według głównego folderu (pierwszy poziom zagnieżdżenia względem $SourceDir).
    $groupedFiles = $csFiles | ForEach-Object {
        $relativePath = $_.FullName.Substring($SourceDir.Length)
        $parts = $relativePath -split '[\\/]'
        if ($parts.Length -gt 1 -and $parts[0] -ne '') {
            $group = $parts[0]
        } else {
            $group = "CryptoBlade"
        }
        [PSCustomObject]@{
            File  = $_
            Group = $group
        }
    } | Group-Object -Property Group

    # Pusta kolekcja do mapy zależności
    $dependencyMap = @{}

    foreach ($group in $groupedFiles) {
        # Ustal pełny namespace oraz nazwę pliku docelowego.
        if ($group.Name -eq "CryptoBlade") {
            $groupNamespace = "CryptoBlade"
            $destFileName = "CryptoBlade.cs"
        } else {
            $groupNamespace = "CryptoBlade." + $group.Name
            $destFileName = "CryptoBlade_" + $group.Name + ".cs"
        }

        $allUsings = @()
        $processedContents = @()
        $indexEntries = @()
        $fileCounter = 1

        foreach ($item in $group.Group) {
            $file = $item.File
            $content = Get-Content $file.FullName -Raw

            # Dodaj informację, jeśli plik wygląda jak interfejs (nazwa zaczyna się na "I")
            $interfaceComment = ""
            if ($file.Name -match "^I[A-Z]") {
                $interfaceComment = "// Uwaga: Ten plik zawiera interfejs – zadbaj o pełną dokumentację!" + "`n"
            }

            # Wyciągamy dyrektywy using
            $usingMatches = Select-String -InputObject $content -Pattern '^\s*using\s+[^;]+;' -AllMatches |
                            ForEach-Object { $_.Matches } | ForEach-Object { $_.Value.Trim() }
            $allUsings += $usingMatches

            # Usuwamy dyrektywy using z treści
            $contentNoUsing = $content -replace '(^\s*using\s+[^;]+;\s*\r?\n)+', ''

            # Jeśli opcja ExtendedIndex jest ustawiona, wyszukaj definicje klas i interfejsów
            $extraIndexInfo = ""
            if ($ExtendedIndex) {
                $classMatches = [regex]::Matches($contentNoUsing, 'class\s+(\w+)')
                $interfaceMatches = [regex]::Matches($contentNoUsing, 'interface\s+(\w+)')
                $names = @()
                foreach ($m in $classMatches) { $names += "class " + $m.Groups[1].Value }
                foreach ($m in $interfaceMatches) { $names += "interface " + $m.Groups[1].Value }
                if ($names.Count -gt 0) {
                    $extraIndexInfo = " [" + ($names -join ", ") + "]"
                }
            }
            
            # Dodajemy separator z numeracją i nazwą pliku
            $separator = "// ==== FILE #$($fileCounter): $($file.Name)$extraIndexInfo ===="
            $blockHeader = $separator
            # Dodajemy wpis do indeksu: numer i nazwa pliku (oraz dodatkowe informacje, jeśli dostępne)
            $indexEntries += "$fileCounter. $($file.Name)$extraIndexInfo"
            
            # Sprawdzamy, czy plik zawiera deklarację namespace
            if ($contentNoUsing -match '^\s*namespace\s+') {
                # Jeśli tak – zachowujemy oryginalny blok namespace
                $processedBlock = $blockHeader + "`n" + $interfaceComment + $contentNoUsing.Trim()
            } else {
                # Jeśli nie – opakowujemy całość w namespace odpowiadający danej grupie
                $wrappedContent = "namespace $groupNamespace {" + "`n" + $interfaceComment + $contentNoUsing.Trim() + "`n" + "}"
                $processedBlock = $blockHeader + "`n" + $wrappedContent
            }
            $processedContents += $processedBlock
            $fileCounter++
        }

        # Sortujemy dyrektywy using alfabetycznie oraz usuwamy duplikaty
        $uniqueUsings = $allUsings | Sort-Object -Unique
        $combinedUsings = $uniqueUsings -join "`n"

        # Tworzymy blok indeksu
        $indexBlock = "// *** INDEX OF INCLUDED FILES ***" + "`n" + ($indexEntries -join "`n") + "`n" + "// *******************************" + "`n`n"

        # Blok metadanych na początku pliku
        $metadataBlock = "// *** METADATA ***" + "`n" +
                         "// Version: $Version" + "`n" +
                         "// Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss UTC')" + "`n" +
                         "// Module: $groupNamespace" + "`n" +
                         "// ****************" + "`n`n"

        # Łączymy przetworzone treści plików – oddzielone ustalonym separatorem
        $separatorBetween = "`n`n// -----------------------------`n`n"
        $combinedContent = $processedContents -join $separatorBetween

        # Finalny wynik: metadane, indeks, usingi na początku, a następnie scalona zawartość
        $finalContent = $metadataBlock + $indexBlock + $combinedUsings + "`n`n" + $combinedContent

        $destFilePath = Join-Path -Path $DestDir -ChildPath $destFileName
        Write-Host "Tworzę scalony plik:" $destFilePath " (scalone pliki:" $indexEntries.Count ")"
        $finalContent | Out-File -FilePath $destFilePath -Encoding utf8

        # Dodajemy informacje o zależnościach do mapy
        $dependencyMap[$group.Name] = $indexEntries
    }

    # Generacja pliku mapy zależności
    $dependencyMapPath = Join-Path -Path $DestDir -ChildPath "DependencyMap.json"
    $dependencyMap | ConvertTo-Json -Depth 5 | Out-File -FilePath $dependencyMapPath -Encoding utf8
    Write-Host "Utworzono mapę zależności w:" $dependencyMapPath
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

##################################################
# Krok 5: Przetwarzanie i kopiowanie pliku appsettings.json
##################################################
$appsettingsPath = Join-Path -Path $SourceDir -ChildPath "appsettings.json"
if (Test-Path $appsettingsPath) {
    Write-Host "Przetwarzanie pliku:" $appsettingsPath
    try {
        $jsonContent = Get-Content -Path $appsettingsPath -Raw | ConvertFrom-Json
        $destinationAppsettingsPath = Join-Path -Path $DestDir -ChildPath "appsettings.json"
        $jsonContent | ConvertTo-Json -Depth 10 | Out-File -FilePath $destinationAppsettingsPath -Encoding utf8
        Write-Host "Zapisano zmodyfikowany plik appsettings.json w:" $destinationAppsettingsPath
    }
    catch {
        Write-Host "Błąd przetwarzania pliku appsettings.json:" $_.Exception.Message
    }
}
else {
    Write-Host "Plik appsettings.json nie został znaleziony w:" $SourceDir
}

##################################################
# Krok 6: Przetwarzanie i kopiowanie pliku bot_notes.md
##################################################
$botNotesPath = "C:\Users\bwola\source\repos\CryptoBlade\CBClone\bot_notes.md"
if (Test-Path $botNotesPath) {
    $destBotNotesPath = Join-Path -Path $DestDir -ChildPath "bot_notes.md"
    Write-Host "Kopiowanie bot_notes.md:" $botNotesPath "->" $destBotNotesPath
    Copy-Item -Path $botNotesPath -Destination $destBotNotesPath
} else {
    Write-Host "Plik bot_notes.md nie został znaleziony w:" $botNotesPath
}

##################################################
# Krok 7: Łączenie dokumentacji Markdown        #
##################################################
$docsPath = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade\Documentation"
$markdownFiles = Get-ChildItem -Path $docsPath -Recurse -File -Filter "*.md"
$mergedDocs = @()

foreach ($doc in $markdownFiles) {
    $content = Get-Content -Path $doc.FullName -Raw
    $mergedDocs += "`n# ===== DOCUMENT: $($doc.Name) =====`n" + $content
}

$mergedDocsPath = Join-Path -Path $DestDir -ChildPath "Merged_Documentation.md"
$mergedDocs -join "`n" | Out-File -FilePath $mergedDocsPath -Encoding utf8
Write-Host "Scalono całą dokumentację w: $mergedDocsPath"

##################################################
# Krok 7: Łączenie skryptów                      #
##################################################
$scriptsPath = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade\Scripts"
$scripts = Get-ChildItem -Path $scriptsPath -Recurse -File -Filter "*.ps1"
$mergedScripts = @()

foreach ($sc in $scripts) {
    $content = Get-Content -Path $sc.FullName -Raw
    $mergedScripts += "`n# ===== DOCUMENT: $($sc.Name) =====`n" + $content
}

$mergedScriptsPath = Join-Path -Path $DestDir -ChildPath "Merged_Scripts.txt"
$mergedScripts -join "`n" | Out-File -FilePath $mergedScriptsPath -Encoding utf8
Write-Host "Scalono wszystkie skrypty w: $mergedScriptsPath"
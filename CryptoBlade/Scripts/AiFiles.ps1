$SourceDir = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade"
$DestDir   = "C:\Users\bwola\source\repos\CryptoBlade\CBCLone"

if (!(Test-Path -Path $DestDir)) {
    New-Item -ItemType Directory -Path $DestDir | Out-Null
}

Get-ChildItem -Path $SourceDir -Recurse -File | ForEach-Object {
    $ParentFolder = $_.Directory.Name
    $NewFileName = "$ParentFolder" + "_" + $_.Name
    
    # Okre�l pe�n� �cie�k� pliku docelowego
    $DestFilePath = Join-Path -Path $DestDir -ChildPath $NewFileName

    # Skopiuj plik do lokalizacji docelowej z now� nazw�
    Copy-Item -Path $_.FullName -Destination $DestFilePath
}

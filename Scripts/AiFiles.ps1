$SourceDir = "C:\Users\bwola\source\repos\CryptoBlade\CryptoBlade"
$DestDir   = "C:\Users\bwola\source\repos\CryptoBlade\CBCLone"

if (!(Test-Path -Path $DestDir)) {
    New-Item -ItemType Directory -Path $DestDir | Out-Null
}

Get-ChildItem -Path $SourceDir -Recurse -File | ForEach-Object {
    $ParentFolder = $_.Directory.Name
    $NewFileName = "$ParentFolder" + "_" + $_.Name
    
    # Okreœl pe³n¹ œcie¿kê pliku docelowego
    $DestFilePath = Join-Path -Path $DestDir -ChildPath $NewFileName

    # Skopiuj plik do lokalizacji docelowej z now¹ nazw¹
    Copy-Item -Path $_.FullName -Destination $DestFilePath
}

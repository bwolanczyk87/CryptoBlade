param(
    [switch]$GitPull,
    [switch]$Build
)

Write-Host "Kopiuję docker-compose.yml na VM..."
scp -i ~/.ssh/id_rsa ~/Repositories/CryptoBlade/VmOracle/docker-compose.yml ubuntu@79.76.101.1:/home/ubuntu/docker-compose.yml


$remoteCommands = @("cd /home/ubuntu")
if ($GitPull) { $remoteCommands += "git pull" }
if ($Build)   { $remoteCommands += "docker compose build --no-cache" }
$remoteCommands += "docker compose -p momentum up -d --force-recreate"

$remoteScript = $remoteCommands -join "; "

Write-Host "Łączę się z VM i wykonuję operacje..."
ssh -i ~/.ssh/id_rsa ubuntu@79.76.101.1

Write-Host "Gotowe!"


# Спира API-то, уеб интерфейса и MySQL контейнера.
$ErrorActionPreference = 'Continue'
$Root    = Split-Path -Parent $PSScriptRoot
$PidFile = Join-Path $Root '.porachki.pids'
Set-Location $Root

Write-Host '> Спиране на API и уеб интерфейса' -ForegroundColor Cyan
if (Test-Path $PidFile) {
    Get-Content $PidFile | Where-Object { $_ -match '^\d+$' } | ForEach-Object { taskkill /PID $_ /T /F *> $null }
    Remove-Item $PidFile -Force
}
# Ако са пуснати ръчно - спираме процесите, които слушат на портовете на приложението
foreach ($port in 5080, 5173) {
    Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        ForEach-Object { taskkill /PID $_.OwningProcess /T /F *> $null }
}

Write-Host '> Спиране на MySQL' -ForegroundColor Cyan
docker compose stop db

Write-Host ''
Write-Host 'Всичко е спряно. Данните в базата се запазват.' -ForegroundColor Green
Start-Sleep 3

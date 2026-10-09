# Стартира всичко с един клик: MySQL (Docker), API (.NET) и уеб интерфейса (Vite),
# след което отваря приложението в браузъра. Спиране: stop.bat
$ErrorActionPreference = 'Continue'   # в PowerShell 5.1 'Stop' превръща stderr на docker/dotnet в грешки
$Root    = Split-Path -Parent $PSScriptRoot
$ApiDir  = Join-Path $Root 'backend\Porachki.Api'
$WebDir  = Join-Path $Root 'frontend'
$PidFile = Join-Path $Root '.porachki.pids'
$ApiUrl  = 'http://localhost:5080/health'
$WebUrl  = 'http://localhost:5173'
Set-Location $Root
$Host.UI.RawUI.WindowTitle = 'Обществени поръчки - стартиране'

function Step($text) { Write-Host ''; Write-Host "> $text" -ForegroundColor Cyan }
function Ok($text)   { Write-Host "  $text" -ForegroundColor Green }
function Fail($text) {
    Write-Host ''
    Write-Host "ГРЕШКА: $text" -ForegroundColor Red
    Read-Host 'Натиснете Enter за изход' | Out-Null
    exit 1
}
function Test-Url($url) {
    try { Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 2 | Out-Null; return $true } catch { return $false }
}

# Вече работи? Само отваряме браузъра.
if ((Test-Url $ApiUrl) -and (Test-Url $WebUrl)) {
    Ok 'Приложението вече работи - отварям браузъра.'
    Start-Process $WebUrl
    Start-Sleep 2
    exit 0
}

# 1) Инструменти
Step 'Проверка на инструментите'
$tools = @(
    @('docker', 'Docker Desktop: https://www.docker.com/products/docker-desktop'),
    @('dotnet', '.NET 9 SDK: https://dotnet.microsoft.com/download'),
    @('npm',    'Node.js 20+: https://nodejs.org')
)
foreach ($t in $tools) {
    if (-not (Get-Command $t[0] -ErrorAction SilentlyContinue)) { Fail "Липсва $($t[0]). Инсталирайте $($t[1])" }
}
Ok 'docker, dotnet и npm са налични'

# 2) Docker и MySQL
Step 'Стартиране на MySQL (Docker, порт 3307)'
docker info *> $null
if ($LASTEXITCODE -ne 0) {
    $desktop = Join-Path $Env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (Test-Path $desktop) { Write-Host '  Стартирам Docker Desktop...'; Start-Process $desktop }
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep 3
        docker info *> $null
        if ($LASTEXITCODE -eq 0) { break }
    }
    if ($LASTEXITCODE -ne 0) { Fail 'Docker не отговаря. Стартирайте Docker Desktop и опитайте отново.' }
}
docker compose up -d db
if ($LASTEXITCODE -ne 0) { Fail 'MySQL контейнерът не стартира (вижте съобщението по-горе).' }

$dbReady = $false
for ($i = 0; $i -lt 60; $i++) {
    docker compose exec -T db mysqladmin ping -h localhost -proot --silent *> $null
    if ($LASTEXITCODE -eq 0) { $dbReady = $true; break }
    Start-Sleep 2
}
if (-not $dbReady) { Fail 'MySQL не отговори за 2 минути.' }
Ok 'MySQL работи'

# 3) Anthropic API ключ за AI анализа (по желание)
$secrets = (dotnet user-secrets list --project $ApiDir 2>$null) -join "`n"
if ($secrets -notmatch 'Estimates:AnthropicApiKey' -and -not $Env:Estimates__AnthropicApiKey) {
    Step 'AI анализ'
    Write-Host '  За AI анализа е нужен Anthropic API ключ (https://platform.claude.com -> API Keys).' -ForegroundColor Yellow
    $key = Read-Host '  Поставете ключа или натиснете Enter, за да пропуснете'
    if ($key) {
        dotnet user-secrets set 'Estimates:AnthropicApiKey' $key --project $ApiDir | Out-Null
        Ok 'Ключът е запазен в user-secrets'
    } else {
        Write-Host '  Пропуснато - приложението работи, но бутонът "AI анализ" ще покаже, че липсва ключ.'
    }
}

# 4) Зависимости на фронтенда (само първия път)
if (-not (Test-Path (Join-Path $WebDir 'node_modules'))) {
    Step 'Инсталиране на npm пакетите (само първия път)'
    Push-Location $WebDir
    npm install --no-audit --no-fund
    $code = $LASTEXITCODE
    Pop-Location
    if ($code -ne 0) { Fail 'npm install не успя.' }
}

# 5) API и уеб в отделни прозорци
Step 'Стартиране на API и уеб интерфейса'
$api = Start-Process powershell -PassThru -WorkingDirectory $ApiDir -ArgumentList @(
    '-NoExit', '-NoProfile', '-Command', "`$Host.UI.RawUI.WindowTitle = 'Porachki API'; dotnet run")
$web = Start-Process powershell -PassThru -WorkingDirectory $WebDir -ArgumentList @(
    '-NoExit', '-NoProfile', '-Command', "`$Host.UI.RawUI.WindowTitle = 'Porachki Web'; npm run dev")
"$($api.Id)`n$($web.Id)" | Set-Content $PidFile

Write-Host '  Изчакване на API-то (първия път компилира проекта - 1-2 минути)...'
$deadline = (Get-Date).AddMinutes(5)
while (-not (Test-Url $ApiUrl)) {
    if ((Get-Date) -gt $deadline) { Fail 'API-то не стартира. Вижте прозореца "Porachki API" за грешката.' }
    if ($api.HasExited) { Fail 'Прозорецът "Porachki API" беше затворен.' }
    Start-Sleep 3
}
Ok 'API: http://localhost:5080 (Swagger: /swagger)'

$deadline = (Get-Date).AddMinutes(2)
while (-not (Test-Url $WebUrl)) {
    if ((Get-Date) -gt $deadline) { Fail 'Уеб интерфейсът не стартира. Вижте прозореца "Porachki Web".' }
    Start-Sleep 2
}
Ok "Уеб: $WebUrl"

Start-Process $WebUrl
Write-Host ''
Write-Host 'Готово! Приложението е отворено в браузъра.' -ForegroundColor Green
Write-Host 'За спиране на всичко: stop.bat. Този прозорец ще се затвори след 10 секунди.'
Start-Sleep 10

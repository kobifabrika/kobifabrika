<#
.SYNOPSIS
    KobiFabrika geliştirme ortamını tek komutla başlatır (Windows).

.DESCRIPTION
    1. Docker'ın çalıştığını kontrol eder (kapalıysa Docker Desktop'ı açıp bekler).
    2. docker compose up -d ile SQL Server ve Mailpit'i başlatır, SQL Server hazır olana kadar bekler.
    3. Veritabanı migration'larını uygular.
    4. API'yi ve frontend'i iki ayrı PowerShell penceresinde başlatır.
    5. İkisi de hazır olunca tarayıcıda http://localhost:3000 adresini açar.

    Bir servis ancak bu betiğin daha önce açtığı pencere hâlâ açıksa yeniden kullanılır. Portta sahipsiz
    kalmış bir dotnet/node süreci varsa (kapanmış bir terminalden veya başka bir araçtan kalan) durdurulup
    yeniden başlatılır: çıktısını okuyan kalmamış bir Next.js dev sunucusu "write EPIPE" ile bozulur,
    sayfalar dakikalarca "Rendering" der ve tarayıcıda "Failed to fetch" görünür.

    Durdurmak için: .\durdur.ps1

.PARAMETER TarayiciAcma
    Hazır olunca tarayıcıyı açma.

.PARAMETER Hizli
    Frontend'i production modunda açar (npm run build + npm start). İlk açılış derleme yüzünden 1-3 dakika
    sürer, sonra sayfalar anında açılır; elle deneme için. Kod değişiklikleri otomatik yansımaz (tekrar
    .\baslat.ps1 -Hizli). Varsayılan dev modudur (npm run dev, anlık yenileme).

.EXAMPLE
    .\baslat.ps1
    .\baslat.ps1 -Hizli
#>
[CmdletBinding()]
param(
    [switch]$TarayiciAcma,
    [switch]$Hizli
)

# Native komutlar (docker, dotnet, npm) $LASTEXITCODE ile kontrol edilir; 'Stop' Windows PowerShell 5.1'de
# stderr'e yazan her native komutu hataya çevirdiği için kullanılmaz.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
Set-Location $root

$apiProject = 'backend/src/KobiFabrika.Api'
$infraProject = 'backend/src/KobiFabrika.Infrastructure'
$apiUrl = 'http://localhost:5080/health'
$webUrl = 'http://localhost:3000'
$pidFile = Join-Path $root '.baslat.pids'

function Write-Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "    $text" -ForegroundColor Green }
function Stop-WithError([string]$text) {
    Write-Host "`nHATA: $text" -ForegroundColor Red
    exit 1
}

# Adres 200 dönüyor mu? (Windows PowerShell 5.1'de 4xx/5xx exception fırlatır.)
function Test-Url([string]$url) {
    try {
        $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
        return $response.StatusCode -eq 200
    } catch {
        return $false
    }
}

function Wait-Until([scriptblock]$condition, [int]$timeoutSeconds, [string]$what) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return }
        Start-Sleep -Seconds 2
    }
    Stop-WithError "$what $timeoutSeconds saniyede hazır olmadı."
}

# Porta dinleyen süreçlerin PID'leri.
function Get-PortOwners([int]$port) {
    @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)
}

# Portu tutan dotnet/node süreçlerini alt süreçleriyle durdurur; port başka bir programdaysa hata verir.
function Clear-Port([int]$port, [string]$what) {
    foreach ($owner in Get-PortOwners $port) {
        $process = Get-Process -Id $owner -ErrorAction SilentlyContinue
        if (-not $process) { continue }
        if ($process.ProcessName -notmatch '^(dotnet|node|KobiFabrika\.Api)$') {
            Stop-WithError "Port $port başka bir programda: $($process.ProcessName) (PID $owner). Kapatıp tekrar deneyin."
        }
        Write-Host "    Port $port'ta sahipsiz $what süreci var ($($process.ProcessName), PID $owner); yeniden başlatılıyor." -ForegroundColor Yellow
        taskkill /PID $owner /T /F *> $null
    }
    Wait-Until { (Get-PortOwners $port).Count -eq 0 } 20 "Port $port"
}

function Test-Alive($processId) {
    return [bool]($processId -and (Get-Process -Id $processId -ErrorAction SilentlyContinue))
}

function Test-Docker {
    docker info *> $null
    return $LASTEXITCODE -eq 0
}

# --- 0. Ön kontroller -------------------------------------------------------
Write-Step 'Ön kontroller'

foreach ($tool in 'docker', 'dotnet', 'npm') {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Stop-WithError "'$tool' bulunamadı. Kurulum adımları: README.md → Gereksinimler."
    }
}

dotnet ef --version *> $null
if ($LASTEXITCODE -ne 0) {
    Stop-WithError "dotnet-ef aracı yok. Kurmak için: dotnet tool install --global dotnet-ef"
}

if (-not (Test-Path (Join-Path $root '.env'))) {
    Stop-WithError ".env dosyası yok. Önce: Copy-Item .env.example .env (ve şifreyi değiştirin). README.md → İlk çalıştırma, 1. adım."
}

$secrets = dotnet user-secrets list --project $apiProject 2>$null | Out-String
if ($secrets -notmatch 'ConnectionStrings:Default') {
    Stop-WithError "API bağlantı dizesi (user-secrets) yok. README.md → İlk çalıştırma, 3. adım."
}
Write-Ok 'Araçlar, .env ve bağlantı dizesi tamam.'

# --- 1. Docker --------------------------------------------------------------
Write-Step 'Docker kontrol ediliyor'
if (-not (Test-Docker)) {
    $dockerDesktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
    if (-not (Test-Path $dockerDesktop)) {
        Stop-WithError 'Docker çalışmıyor. Docker Desktop''ı açıp "Engine running" yazısını bekleyin, sonra tekrar deneyin.'
    }
    Write-Host '    Docker çalışmıyor; Docker Desktop açılıyor (1-2 dakika sürebilir)...'
    Start-Process -FilePath $dockerDesktop
    Wait-Until { Test-Docker } 180 'Docker'
}
Write-Ok 'Docker çalışıyor.'

# --- 2. SQL Server + Mailpit ------------------------------------------------
Write-Step 'SQL Server ve Mailpit başlatılıyor (docker compose up -d)'
docker compose up -d
if ($LASTEXITCODE -ne 0) { Stop-WithError 'docker compose up -d başarısız oldu.' }

$sqlContainer = (docker compose ps -q sqlserver | Out-String).Trim()
if (-not $sqlContainer) { Stop-WithError 'sqlserver konteyneri bulunamadı (docker compose ps).' }

Write-Host '    SQL Server''ın hazır olması bekleniyor...'
Wait-Until {
    (docker inspect --format '{{.State.Health.Status}}' $sqlContainer 2>$null | Out-String).Trim() -eq 'healthy'
} 180 'SQL Server'
Write-Ok 'SQL Server hazır. Mailpit: http://localhost:8025'

# --- 3. Migration'lar -------------------------------------------------------
# Pencere PID'leri durdur.ps1 için saklanır. Yeniden kullanılan servisin önceki PID'i korunur.
$pids = @{}
if (Test-Path $pidFile) {
    try {
        (Get-Content $pidFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $pids[$_.Name] = $_.Value }
    } catch {
        $pids = @{}
    }
}

# API sadece bu betiğin açtığı pencere hâlâ açıksa yeniden kullanılır; portta sahipsiz süreç varsa kapatılır.
$apiAlreadyRunning = (Test-Alive $pids.api) -and (Test-Url $apiUrl)
if (-not $apiAlreadyRunning) {
    if (Test-Alive $pids.api) { taskkill /PID $pids.api /T /F *> $null }
    Clear-Port 5080 'API'
}

Write-Step 'Veritabanı migration''ları uygulanıyor'
$efArgs = @('ef', 'database', 'update', '-p', $infraProject, '-s', $apiProject)
if ($apiAlreadyRunning) {
    # Çalışan API DLL'leri kilitler (Windows); derlemeden mevcut çıktıyla uygulanır.
    Write-Host '    API zaten çalışıyor; derlemeden uygulanıyor (--no-build).'
    $efArgs += '--no-build'
}
# Geliştirme ayarı EF'in SQL komutlarını loglar; burada sadece uyarı ve hatalar görünsün.
$efLogLevel = 'Serilog__MinimumLevel__Override__Microsoft.EntityFrameworkCore.Database.Command'
[Environment]::SetEnvironmentVariable($efLogLevel, 'Warning', 'Process')
& dotnet @efArgs
$efExitCode = $LASTEXITCODE
[Environment]::SetEnvironmentVariable($efLogLevel, $null, 'Process')
if ($efExitCode -ne 0) { Stop-WithError 'Migration uygulanamadı. Yukarıdaki hatayı inceleyin.' }
Write-Ok 'Veritabanı güncel.'

# --- 4. API ve frontend pencereleri -----------------------------------------
if (-not (Test-Path (Join-Path $root 'frontend/node_modules'))) {
    Write-Step 'Frontend paketleri kuruluyor (npm install, ilk seferde)'
    Push-Location (Join-Path $root 'frontend')
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { Stop-WithError 'npm install başarısız oldu.' }
    } finally {
        Pop-Location
    }
}

# Yeni pencereler bu betiği çalıştıran PowerShell ile açılır (powershell.exe veya pwsh.exe).
$shell = (Get-Process -Id $PID).Path

function Start-Window([string]$title, [string]$workingDirectory, [string]$command) {
    $script = "`$Host.UI.RawUI.WindowTitle = '$title'; Set-Location -LiteralPath '$workingDirectory'; $command"
    $process = Start-Process -FilePath $shell -ArgumentList '-NoExit', '-NoProfile', '-Command', $script -PassThru
    return $process.Id
}

Write-Step 'API başlatılıyor (yeni pencere)'
if ($apiAlreadyRunning) {
    Write-Ok 'API zaten çalışıyor; yeni pencere açılmadı.'
} else {
    $pids.api = Start-Window 'KobiFabrika API' $root "dotnet run --project $apiProject"
}

$webMode = if ($Hizli) { 'prod' } else { 'dev' }
$webModeLabel = if ($Hizli) { 'production: npm run build + npm start' } else { 'dev: npm run dev' }
Write-Step "Frontend başlatılıyor ($webModeLabel, yeni pencere)"
$webReusable = (Test-Alive $pids.web) -and ($pids.webMode -eq $webMode) -and (Test-Url "$webUrl/giris")
if ($webReusable) {
    Write-Ok 'Frontend zaten bu modda çalışıyor; yeni pencere açılmadı.'
} else {
    if (Test-Alive $pids.web) {
        Write-Host '    Önceki frontend penceresi kapatılıyor (mod değişti veya yanıt vermiyor).'
        taskkill /PID $pids.web /T /F *> $null
    }
    Clear-Port 3000 'frontend'
    $webCommand = if ($Hizli) {
        "npm run build; if (`$LASTEXITCODE -eq 0) { npm start } else { Write-Host 'Build başarısız; yukarıdaki hatayı inceleyin.' -ForegroundColor Red }"
    } else {
        'npm run dev'
    }
    $pids.web = Start-Window 'KobiFabrika Frontend' (Join-Path $root 'frontend') $webCommand
    $pids.webMode = $webMode
}

$pids | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8

# --- 5. Hazır olunca tarayıcı -----------------------------------------------
Write-Step 'API ve frontend''in hazır olması bekleniyor'
Wait-Until { Test-Url $apiUrl } 180 "API ($apiUrl)"
Write-Ok "API hazır: http://localhost:5080 (doküman: /scalar)"
# Production build ilk seferde birkaç dakika sürebilir.
$webTimeout = if ($Hizli) { 600 } else { 180 }
if ($Hizli -and -not $webReusable) { Write-Host '    Production build alınıyor (1-3 dakika)...' }
Wait-Until { Test-Url "$webUrl/giris" } $webTimeout "Frontend ($webUrl)"
Write-Ok "Frontend hazır: $webUrl"

if (-not $TarayiciAcma) {
    Start-Process $webUrl
}

Write-Host "`nKobiFabrika çalışıyor. Durdurmak için: .\durdur.ps1" -ForegroundColor Green

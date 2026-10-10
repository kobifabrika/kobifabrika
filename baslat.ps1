<#
.SYNOPSIS
    KobiFabrika geliştirme ortamını tek komutla başlatır (Windows).

.DESCRIPTION
    1. Docker'ın çalıştığını kontrol eder (kapalıysa Docker Desktop'ı açıp bekler).
    2. docker compose up -d ile SQL Server ve Mailpit'i başlatır, SQL Server hazır olana kadar bekler.
    3. Veritabanı migration'larını uygular.
    4. API'yi ve frontend'i iki ayrı PowerShell penceresinde başlatır.
    5. İkisi de hazır olunca tarayıcıda http://localhost:3000 adresini açar.

    Durdurmak için: .\durdur.ps1

.PARAMETER TarayiciAcma
    Hazır olunca tarayıcıyı açma.

.EXAMPLE
    .\baslat.ps1
#>
[CmdletBinding()]
param(
    [switch]$TarayiciAcma
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
Write-Step 'Veritabanı migration''ları uygulanıyor'
$apiAlreadyRunning = Test-Url $apiUrl
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

# Pencere PID'leri durdur.ps1 için saklanır. Zaten çalışan bir servis atlanırsa önceki PID'i korunur.
$pids = @{}
if (Test-Path $pidFile) {
    try {
        (Get-Content $pidFile -Raw | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $pids[$_.Name] = $_.Value }
    } catch {
        $pids = @{}
    }
}

Write-Step 'API başlatılıyor (yeni pencere)'
if ($apiAlreadyRunning) {
    Write-Ok 'API zaten çalışıyor; yeni pencere açılmadı.'
} else {
    $pids.api = Start-Window 'KobiFabrika API' $root "dotnet run --project $apiProject"
}

Write-Step 'Frontend başlatılıyor (yeni pencere)'
if (Test-Url "$webUrl/giris") {
    Write-Ok 'Frontend zaten çalışıyor; yeni pencere açılmadı.'
} else {
    $pids.web = Start-Window 'KobiFabrika Frontend' (Join-Path $root 'frontend') 'npm run dev'
}

$pids | ConvertTo-Json | Set-Content -Path $pidFile -Encoding UTF8

# --- 5. Hazır olunca tarayıcı -----------------------------------------------
Write-Step 'API ve frontend''in hazır olması bekleniyor'
Wait-Until { Test-Url $apiUrl } 180 "API ($apiUrl)"
Write-Ok "API hazır: http://localhost:5080 (doküman: /scalar)"
Wait-Until { Test-Url "$webUrl/giris" } 180 "Frontend ($webUrl)"
Write-Ok "Frontend hazır: $webUrl"

if (-not $TarayiciAcma) {
    Start-Process $webUrl
}

Write-Host "`nKobiFabrika çalışıyor. Durdurmak için: .\durdur.ps1" -ForegroundColor Green

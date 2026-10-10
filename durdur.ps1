<#
.SYNOPSIS
    baslat.ps1 ile açılan KobiFabrika geliştirme ortamını durdurur (Windows).

.DESCRIPTION
    1. API ve frontend pencerelerini (ve içlerinde çalışan dotnet/node süreçlerini) kapatır.
    2. 5080 ve 3000 portlarında hâlâ dinleyen dotnet/node süreci kalırsa onları da durdurur.
    3. docker compose stop ile SQL Server ve Mailpit'i durdurur. Veriler silinmez (volume kalır).

.PARAMETER DockerKalsin
    SQL Server ve Mailpit konteynerlerini durdurma (sadece API ve frontend kapanır).

.EXAMPLE
    .\durdur.ps1
    .\durdur.ps1 -DockerKalsin
#>
[CmdletBinding()]
param(
    [switch]$DockerKalsin
)

# Native komutlar (docker, dotnet, npm) $LASTEXITCODE ile kontrol edilir; 'Stop' Windows PowerShell 5.1'de
# stderr'e yazan her native komutu hataya çevirdiği için kullanılmaz.
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
Set-Location $root
$pidFile = Join-Path $root '.baslat.pids'

function Write-Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Write-Ok([string]$text) { Write-Host "    $text" -ForegroundColor Green }

# Süreci alt süreçleriyle birlikte kapatır (pencere → dotnet/node).
function Stop-Tree([int]$processId, [string]$name) {
    if (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
        taskkill /PID $processId /T /F *> $null
        Write-Ok "$name kapatıldı (PID $processId)."
    }
}

Write-Step 'API ve frontend durduruluyor'
if (Test-Path $pidFile) {
    try {
        $pids = Get-Content $pidFile -Raw | ConvertFrom-Json
        if ($pids.api) { Stop-Tree $pids.api 'API penceresi' }
        if ($pids.web) { Stop-Tree $pids.web 'Frontend penceresi' }
    } catch {
        Write-Host "    $pidFile okunamadı; portlara bakılacak." -ForegroundColor Yellow
    }
    Remove-Item $pidFile -Force -ErrorAction SilentlyContinue
}

# baslat.ps1 dışında açılmış (veya pencere kapanıp sahipsiz kalmış) süreçler: sadece dotnet/node ise durdurulur.
foreach ($port in 5080, 3000) {
    $listeners = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($owner in $listeners) {
        $process = Get-Process -Id $owner -ErrorAction SilentlyContinue
        if ($process -and $process.ProcessName -match '^(dotnet|node|KobiFabrika\.Api)$') {
            Stop-Tree $owner "$($process.ProcessName) (port $port)"
        } elseif ($process) {
            Write-Host "    Port $port başka bir programda: $($process.ProcessName) (PID $owner). Dokunulmadı." -ForegroundColor Yellow
        }
    }
}
Write-Ok 'API ve frontend durdu.'

if ($DockerKalsin) {
    Write-Host "`nSQL Server ve Mailpit açık bırakıldı." -ForegroundColor Green
    exit 0
}

Write-Step 'SQL Server ve Mailpit durduruluyor (docker compose stop)'
docker info *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Ok 'Docker çalışmıyor; durdurulacak konteyner yok.'
} else {
    docker compose stop
    if ($LASTEXITCODE -ne 0) {
        Write-Host '    docker compose stop başarısız oldu.' -ForegroundColor Red
        exit 1
    }
    Write-Ok 'Konteynerler durdu. Veriler duruyor; tekrar başlatmak için: .\baslat.ps1'
}

<#
.SYNOPSIS
    KobiFabrika'nın bütün testlerini tek komutla çalıştırır ve Türkçe özet verir.

.DESCRIPTION
    Sırayla:
      1. Docker kontrolü (SQL Server + Mailpit: docker compose up -d)
      2. Backend testleri (xUnit + Testcontainers, Release)
      3. Frontend lint, birim testleri (Vitest) ve build (E2E için production derlemesi, .next-e2e)
      4. Uçtan uca testler (Playwright: masaüstü 1280 px, telefon 375 px Chromium + WebKit)

    E2E ayrı portlarda (API 5180, frontend 3100) ve ayrı veritabanında (KobiFabrika_E2E, her çalıştırmada sıfırdan) çalışır;
    baslat.ps1 açıkken de çakışmaz, geliştirme veritabanına dokunmaz. Sonunda bölüm bazında geçen/kalan/süre tablosu,
    kalan testlerin adı, kısa nedeni ve ekran görüntüsünün yolu yazılır. Bir test kalırsa çıkış kodu 1.
    Bitince açtığı bütün süreçleri kapatır; 5180 ve 3100 portları boş kalır.

.PARAMETER Hizli
    Sadece birim testleri: backend testleri ve frontend birim testleri (lint, build ve E2E yok).

.PARAMETER E2E
    Sadece uçtan uca testler (API ve frontend kendisi derlenir).

.PARAMETER Gorunur
    E2E'de tarayıcı görünür ve yavaş çalışır (izlemek için); testler tek tek koşar.

.PARAMETER Rapor
    Bitince Playwright HTML raporunu açar (ekran görüntüleri, hata kayıtları).

.EXAMPLE
    .\test.ps1
    .\test.ps1 -Hizli
    .\test.ps1 -E2E -Gorunur
    .\test.ps1 -Rapor
#>
[CmdletBinding()]
param(
    [switch]$Hizli,
    [switch]$E2E,
    [switch]$Gorunur,
    [switch]$Rapor
)

# Native komutların çıkış kodu $LASTEXITCODE ile kontrol edilir ('Stop' 5.1'de stderr'e yazan her komutu hataya çevirir).
$ErrorActionPreference = 'Continue'
$root = $PSScriptRoot
$frontend = Join-Path $root 'frontend'
$out = Join-Path $root 'TestResults/test-ps1'
$e2ePorts = 5180, 3100
$onWindows = $env:OS -eq 'Windows_NT'
Set-Location $root

if ($Hizli -and $E2E) {
    Write-Host 'HATA: -Hizli ve -E2E birlikte kullanılamaz.' -ForegroundColor Red
    exit 1
}

$results = New-Object System.Collections.Generic.List[object]

function Write-Step([string]$text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

function Format-Duration([TimeSpan]$t) {
    if ($t.TotalMinutes -ge 1) { return '{0} dk {1} sn' -f [int][Math]::Floor($t.TotalMinutes), $t.Seconds }
    return '{0} sn' -f [int][Math]::Round($t.TotalSeconds)
}

function Add-Result([string]$name, $summary, [TimeSpan]$duration) {
    $results.Add([pscustomobject]@{
            Bolum    = $name
            Gecen    = [int]$summary.passed
            Kalan    = [int]$summary.failed
            Sure     = $duration
            Failures = @($summary.failures)
        })
}

function New-Failure([string]$name, [string]$reason) {
    return [pscustomobject]@{ passed = 0; failed = 1; failures = @([pscustomobject]@{ name = $name; reason = $reason; screenshot = $null }) }
}

# Sonuç dosyasını frontend/e2e/scripts/ozet.mjs ile özetler: { passed, failed, failures[] }.
function Read-Summary([string]$kind, [string]$file) {
    Push-Location $frontend
    try {
        $json = node e2e/scripts/ozet.mjs $kind $file | Out-String
        return $json | ConvertFrom-Json
    } finally {
        Pop-Location
    }
}

# Portu dinleyen süreçler (Windows: Get-NetTCPConnection, Mac/Linux: lsof).
function Get-PortOwners([int]$port) {
    if ($onWindows) {
        return @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
                Select-Object -ExpandProperty OwningProcess -Unique)
    }
    return @(lsof -ti "tcp:$port" -sTCP:LISTEN 2>$null)
}

# E2E portlarında kalmış dotnet/node süreçlerini (önceki yarım kalmış çalıştırma) kapatır.
function Clear-E2EPorts {
    foreach ($port in $e2ePorts) {
        foreach ($owner in Get-PortOwners $port) {
            $process = Get-Process -Id $owner -ErrorAction SilentlyContinue
            if (-not $process) { continue }
            if ($process.ProcessName -notmatch '^(dotnet|node|KobiFabrika\.Api)$') {
                Write-Host "    Port $port başka bir programda: $($process.ProcessName) (PID $owner)." -ForegroundColor Yellow
                continue
            }
            if ($onWindows) { taskkill /PID $owner /T /F *> $null } else { Stop-Process -Id $owner -Force }
        }
    }
}

function Test-Docker {
    docker info *> $null
    return $LASTEXITCODE -eq 0
}

function Test-Url([string]$url) {
    try { return (Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5).StatusCode -eq 200 } catch { return $false }
}

function Wait-Until([scriptblock]$condition, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $condition) { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

$started = Get-Date
$buildOk = $false
$apiBuilt = $false
$exitCode = 0

try {
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Force $out | Out-Null
    Clear-E2EPorts

    # --- 1. Docker -------------------------------------------------------------------------------
    Write-Step 'Docker kontrol ediliyor (SQL Server + Mailpit)'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $dockerProblem = $null
    if (-not (Test-Docker)) {
        $desktop = if ($onWindows) { Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe' } else { $null }
        if ($desktop -and (Test-Path $desktop)) {
            Write-Host '    Docker çalışmıyor; Docker Desktop açılıyor (1-2 dakika)...'
            Start-Process -FilePath $desktop
            if (-not (Wait-Until { Test-Docker } 180)) { $dockerProblem = 'Docker 3 dakikada açılmadı.' }
        } else {
            $dockerProblem = 'Docker çalışmıyor. Docker Desktop''ı açın.'
        }
    }
    if (-not $dockerProblem -and -not (Test-Path (Join-Path $root '.env'))) {
        $dockerProblem = '.env yok. Önce: Copy-Item .env.example .env (README → İlk çalıştırma).'
    }
    if (-not $dockerProblem) {
        docker compose up -d *> $null
        if ($LASTEXITCODE -ne 0) { $dockerProblem = 'docker compose up -d başarısız oldu.' }
    }
    if (-not $dockerProblem) {
        $sql = (docker compose ps -q sqlserver | Out-String).Trim()
        $healthy = Wait-Until { (docker inspect --format '{{.State.Health.Status}}' $sql 2>$null) -eq 'healthy' } 180
        if (-not $healthy) { $dockerProblem = 'SQL Server 3 dakikada hazır olmadı.' }
        elseif (-not (Wait-Until { Test-Url 'http://localhost:8025/api/v1/info' } 60)) { $dockerProblem = 'Mailpit (8025) yanıt vermiyor.' }
    }
    $watch.Stop()
    if ($dockerProblem) {
        Add-Result 'Docker' (New-Failure 'Docker' $dockerProblem) $watch.Elapsed
        throw 'docker'
    }
    Add-Result 'Docker' ([pscustomobject]@{ passed = 1; failed = 0; failures = @() }) $watch.Elapsed

    # --- 2. Backend testleri ---------------------------------------------------------------------
    if (-not $E2E) {
        Write-Step 'Backend testleri (dotnet test, Release)'
        $watch = [Diagnostics.Stopwatch]::StartNew()
        dotnet test backend -c Release --results-directory $out --report-xunit-ctrf --report-xunit-ctrf-filename backend.json
        $watch.Stop()
        $file = Join-Path $out 'backend.json'
        $summary = if (Test-Path $file) { Read-Summary 'ctrf' $file } else { New-Failure 'Backend' 'Derleme veya test çalıştırması başarısız (çıktıya bakın).' }
        $apiBuilt = Test-Path $file
        Add-Result 'Backend testleri' $summary $watch.Elapsed
    }

    Push-Location $frontend
    try {
        # --- 3. Frontend lint, birim testleri, build -----------------------------------------------
        if (-not $E2E -and -not $Hizli) {
            Write-Step 'Frontend lint (ESLint)'
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $file = Join-Path $out 'eslint.json'
            node node_modules/eslint/bin/eslint.js --format json --output-file $file
            $watch.Stop()
            Add-Result 'Frontend lint' (Read-Summary 'eslint' $file) $watch.Elapsed
        }

        if (-not $E2E) {
            Write-Step 'Frontend birim testleri (Vitest)'
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $file = Join-Path $out 'vitest.json'
            node node_modules/vitest/vitest.mjs run --reporter=default --reporter=json "--outputFile.json=$file"
            $watch.Stop()
            Add-Result 'Frontend birim testleri' (Read-Summary 'vitest' $file) $watch.Elapsed
        }

        if (-not $E2E -and -not $Hizli) {
            Write-Step 'Frontend build (production, E2E için .next-e2e)'
            $watch = [Diagnostics.Stopwatch]::StartNew()
            $env:NEXT_DIST_DIR = '.next-e2e'
            $env:NEXT_PUBLIC_API_URL = 'http://localhost:5180'
            $env:NEXT_TELEMETRY_DISABLED = '1'
            node node_modules/next/dist/bin/next build
            $buildOk = $LASTEXITCODE -eq 0
            Remove-Item Env:NEXT_DIST_DIR, Env:NEXT_PUBLIC_API_URL -ErrorAction SilentlyContinue
            $watch.Stop()
            $summary = if ($buildOk) { [pscustomobject]@{ passed = 1; failed = 0; failures = @() } } else { New-Failure 'next build' 'Derleme başarısız (çıktıya bakın).' }
            Add-Result 'Frontend build' $summary $watch.Elapsed
        }

        # --- 4. E2E --------------------------------------------------------------------------------
        if (-not $Hizli) {
            if (-not $E2E -and -not $buildOk) {
                Add-Result 'E2E (Playwright)' (New-Failure 'E2E' 'Frontend derlenemediği için çalıştırılmadı.') ([TimeSpan]::Zero)
            } else {
                Write-Step 'Uçtan uca testler (Playwright: masaüstü, telefon Chromium ve WebKit)'
                $watch = [Diagnostics.Stopwatch]::StartNew()
                if ($buildOk) { $env:E2E_SKIP_WEB_BUILD = '1' }
                if ($apiBuilt) { $env:E2E_SKIP_API_BUILD = '1' }
                if ($Gorunur) { $env:E2E_HEADED = '1' }
                node node_modules/@playwright/test/cli.js test
                Remove-Item Env:E2E_SKIP_WEB_BUILD, Env:E2E_SKIP_API_BUILD, Env:E2E_HEADED -ErrorAction SilentlyContinue
                $watch.Stop()
                Add-Result 'E2E (Playwright)' (Read-Summary 'playwright' (Join-Path $frontend 'test-results/results.json')) $watch.Elapsed
            }
        }
    } finally {
        Pop-Location
    }
} catch {
    if ("$_" -ne 'docker') { Write-Host "`nBEKLENMEYEN HATA: $_" -ForegroundColor Red; $exitCode = 1 }
} finally {
    # Açılan sunucuların hiçbiri açık kalmasın (Playwright kapatır; yarıda kesilirse burada kapanır).
    Clear-E2EPorts
}

# --- Özet --------------------------------------------------------------------------------------------
$total = (Get-Date) - $started
Write-Host "`n==================== TEST ÖZETİ ====================" -ForegroundColor Cyan
'{0,-26} {1,6} {2,6}  {3}' -f 'Bölüm', 'Geçen', 'Kalan', 'Süre' | Write-Host
'{0,-26} {1,6} {2,6}  {3}' -f ('-' * 26), '-----', '-----', '----------' | Write-Host
foreach ($r in $results) {
    $color = if ($r.Kalan -gt 0) { 'Red' } else { 'Green' }
    '{0,-26} {1,6} {2,6}  {3}' -f $r.Bolum, $r.Gecen, $r.Kalan, (Format-Duration $r.Sure) | Write-Host -ForegroundColor $color
}
$passed = ($results | Measure-Object -Property Gecen -Sum).Sum
$failed = ($results | Measure-Object -Property Kalan -Sum).Sum
'{0,-26} {1,6} {2,6}  {3}' -f 'TOPLAM', $passed, $failed, (Format-Duration $total) | Write-Host

$failures = @($results | Where-Object { $_.Kalan -gt 0 })
if ($failures.Count -gt 0) {
    Write-Host "`nKALAN TESTLER" -ForegroundColor Red
    foreach ($r in $failures) {
        foreach ($f in $r.Failures) {
            Write-Host "- [$($r.Bolum)] $($f.name)" -ForegroundColor Red
            if ($f.reason) { Write-Host "    Neden: $($f.reason)" }
            if ($f.screenshot) { Write-Host "    Ekran görüntüsü: $($f.screenshot)" }
        }
    }
    if (Test-Path (Join-Path $frontend 'playwright-report/index.html')) {
        Write-Host "`nE2E raporu (ekranlar, trace, video): frontend/playwright-report/index.html  (.\test.ps1 -Rapor ile açılır)"
    }
    $exitCode = 1
} elseif ($exitCode -eq 0) {
    Write-Host "`nHepsi yeşil." -ForegroundColor Green
}

if ($Rapor) {
    $report = Join-Path $frontend 'playwright-report/index.html'
    if (Test-Path $report) {
        if ($onWindows) { Start-Process $report } else { & open $report }
    } else {
        Write-Host 'Playwright raporu yok (E2E çalışmadı).' -ForegroundColor Yellow
    }
}

exit $exitCode

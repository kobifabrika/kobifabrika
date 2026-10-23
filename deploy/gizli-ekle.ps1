# Canlı sunucunun .env dosyasına gizli bir değer ekler/değiştirir ve ilgili servisi yeniden başlatır (docs/canli-ortam.md → Gizli değerler).
# Değer gösterilmeden sorulur (Read-Host -AsSecureString), ssh'nin standart girdisinden sunucudaki bin/gizli-yaz.sh'ye gider:
# komut satırına, ekrana, kayda veya geçmişe yazılmaz.
#
#   .\deploy\gizli-ekle.ps1                    # adı sorar
#   .\deploy\gizli-ekle.ps1 BREVO_SMTP_KEY     # sadece değeri sorar
param(
    [string]$Ad,
    [string]$Sunucu = "kf"
)
$ErrorActionPreference = "Stop"

$izinli = [ordered]@{
    "BREVO_SMTP_USER"      = "Brevo SMTP kullanıcı adı (api yeniden başlar)"
    "BREVO_SMTP_KEY"       = "Brevo SMTP anahtarı (api)"
    "SENTRY_DSN"           = "Sentry DSN (api)"
    "R2_ACCOUNT_ID"        = "Cloudflare hesap kimliği (yedek)"
    "R2_ACCESS_KEY_ID"     = "R2 erişim anahtarı (yedek)"
    "R2_SECRET_ACCESS_KEY" = "R2 gizli anahtarı (yedek)"
    "R2_BUCKET"            = "R2 kova adı (yedek)"
    "YEDEK_SIFRE"          = "R2'ye giden yedeklerin şifresi (yedek)"
    "APP_DB_PASSWORD"      = "API'nin veritabanı şifresi (kf_app güncellenir, api)"
}

if (-not $Ad) {
    Write-Host "Değişkenler:"
    $izinli.GetEnumerator() | ForEach-Object { Write-Host ("  {0,-22} {1}" -f $_.Key, $_.Value) }
    $Ad = (Read-Host "Değişken adı").Trim()
}
$Ad = $Ad.ToUpperInvariant()
if (-not $izinli.Contains($Ad)) {
    Write-Host "Bilinmeyen değişken: $Ad" -ForegroundColor Red
    exit 2
}

$guvenli = Read-Host "$Ad değeri (ekranda görünmez)" -AsSecureString
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($guvenli)
try {
    $baytlar = [Text.Encoding]::UTF8.GetBytes([Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) + "`n")
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
}
if ($baytlar.Length -le 1) {
    Write-Host "Boş değer; bir şey yazılmadı." -ForegroundColor Yellow
    exit 2
}

# PowerShell borusu yerine doğrudan süreç girdisi: kodlama ve satır sonu değişmez.
$psi = New-Object Diagnostics.ProcessStartInfo "ssh", "$Sunucu sudo /opt/kobifabrika/bin/gizli-yaz.sh $Ad"
$psi.UseShellExecute = $false
$psi.RedirectStandardInput = $true
# Girdi kodlaması BOM'suz UTF-8: aksi halde .NET girdinin başına BOM yazar (sunucu ayrıca temizler).
$eskiKodlama = [Console]::InputEncoding
[Console]::InputEncoding = New-Object Text.UTF8Encoding($false)
try {
    $surec = [Diagnostics.Process]::Start($psi)
} finally {
    [Console]::InputEncoding = $eskiKodlama
}
$surec.StandardInput.BaseStream.Write($baytlar, 0, $baytlar.Length)
$surec.StandardInput.Close()
[Array]::Clear($baytlar, 0, $baytlar.Length)
$surec.WaitForExit()
exit $surec.ExitCode

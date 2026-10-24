#!/usr/bin/env bash
# Yedeğin şifreli kopyasını Cloudflare R2'ye gönderir (yedek-al.sh çağırır; R2 anahtarları .env'de doluysa).
# Şifre: .env YEDEK_SIFRE (AES-256, PBKDF2). R2'de 30 günden eski kopyaları R2'nin yaşam döngüsü kuralı siler (panelden).
# Açmak: openssl enc -d -aes-256-cbc -pbkdf2 -iter 200000 -pass env:YEDEK_SIFRE -in X.bak.zst.enc | zstd -d > X.bak
. /opt/kobifabrika/bin/ortak.sh
dosya=$1
gecici="$dosya.enc"
trap 'rm -f "$gecici"' EXIT
openssl enc -aes-256-cbc -pbkdf2 -iter 200000 -salt -pass env:YEDEK_SIFRE -in "$dosya" -out "$gecici"
export RCLONE_CONFIG_R2_TYPE=s3 RCLONE_CONFIG_R2_PROVIDER=Cloudflare RCLONE_CONFIG_R2_NO_CHECK_BUCKET=true
export RCLONE_CONFIG_R2_ACCESS_KEY_ID="$R2_ACCESS_KEY_ID" RCLONE_CONFIG_R2_SECRET_ACCESS_KEY="$R2_SECRET_ACCESS_KEY"
export RCLONE_CONFIG_R2_ENDPOINT="https://$R2_ACCOUNT_ID.r2.cloudflarestorage.com"
# Değerler komut satırına girmez: -e sadece adı geçirir, değer bu kabuğun ortamından okunur.
docker run --rm -v "$KF/yedek:/yedek:ro"   -e RCLONE_CONFIG_R2_TYPE -e RCLONE_CONFIG_R2_PROVIDER -e RCLONE_CONFIG_R2_NO_CHECK_BUCKET   -e RCLONE_CONFIG_R2_ACCESS_KEY_ID -e RCLONE_CONFIG_R2_SECRET_ACCESS_KEY -e RCLONE_CONFIG_R2_ENDPOINT   rclone/rclone:1.75.1 copy "/yedek/$(basename "$gecici")" "r2:$R2_BUCKET/veritabani/"
kayit "R2'ye gönderildi: $R2_BUCKET/veritabani/$(basename "$gecici")"

#!/usr/bin/env bash
# Veritabanı yedeği (docs/canli-ortam.md → Yedek): gece 03:30 İstanbul (kobifabrika-yedek.timer) ve her dağıtımda migration'dan
# hemen önce. Yedek /opt/kobifabrika/yedek/KobiFabrika-<UTC zaman>-<etiket>.bak.zst; 7 günden eskiler silinir.
# R2 anahtarları .env'de doluysa şifreli kopya R2'ye de gider (bin/r2-gonder.sh); boşsa bu adım atlanır.
# Kullanım: sudo /opt/kobifabrika/bin/yedek-al.sh [gece|dagitim|elle]
. /opt/kobifabrika/bin/ortak.sh
etiket=${1:-elle}

if ! db_var_mi; then
  kayit "KobiFabrika veritabanı yok (ilk kurulum); yedek atlandı."
  exit 0
fi

ad="KobiFabrika-$(date -u +%Y%m%d-%H%M%S)-$etiket.bak"
# Express sürümü yerleşik yedek sıkıştırmasını desteklemiyor (WITH COMPRESSION hata verir): yedek zstd ile sıkıştırılır.
sql "BACKUP DATABASE [KobiFabrika] TO DISK = N'/var/opt/mssql/backup/$ad' WITH INIT, FORMAT, CHECKSUM, STATS = 50" > /dev/null
zstd -q -T0 -10 --rm "$KF/yedek/$ad"
chmod 640 "$KF/yedek/$ad.zst"
kayit "Yedek alındı: yedek/$ad.zst ($(du -h "$KF/yedek/$ad.zst" | cut -f1))"

find "$KF/yedek" -maxdepth 1 -name 'KobiFabrika-*.bak.zst' -mtime +6 -print -delete | sed 's/^/silindi (7 günden eski): /'

if [ -n "${R2_ACCESS_KEY_ID:-}" ] && [ -n "${R2_SECRET_ACCESS_KEY:-}" ] && [ -n "${R2_ACCOUNT_ID:-}" ] && [ -n "${R2_BUCKET:-}" ]; then
  "$KF/bin/r2-gonder.sh" "$KF/yedek/$ad.zst"
else
  kayit "R2 anahtarları boş: uzak kopya pasif."
fi

#!/usr/bin/env bash
# .env'ye tek değer yazar ve ilgili servisi yeniden kurar. Değer stdin'den okunur, ekrana/loga/komut satırına yazılmaz.
# PC'den: deploy/gizli-ekle.ps1 (değeri göstermeden sorar). Sunucuda: sudo /opt/kobifabrika/bin/gizli-yaz.sh AD  (sonra değeri yapıştır, Enter)
set -euo pipefail
KF=/opt/kobifabrika
ad=${1:?değişken adı gerekli}

declare -A servis=(
  [BREVO_SMTP_USER]=api [BREVO_SMTP_KEY]=api [SENTRY_DSN]=api [SENTRY_DSN_WEB]=web
  [R2_ACCOUNT_ID]=- [R2_ACCESS_KEY_ID]=- [R2_SECRET_ACCESS_KEY]=- [R2_BUCKET]=- [YEDEK_SIFRE]=-
  [APP_DB_PASSWORD]=api
)
if [ -z "${servis[$ad]+x}" ]; then
  echo "Bilinmeyen değişken: $ad. İzin verilenler: ${!servis[*]}" >&2
  echo "(MSSQL_SA_PASSWORD ve DP_CERT_PASSWORD bu yolla değişmez: docs/canli-ortam.md → Gizli değerler)" >&2
  exit 2
fi

IFS= read -r deger || true
# Windows'tan gelen satır sonu, baştaki UTF-8 BOM ve kenar boşlukları atılır.
deger=${deger%$'\r'}
deger=${deger#$'\xef\xbb\xbf'}
deger=$(printf '%s' "$deger" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')
case "$deger" in *"'"*|*$'\n'*) echo "Değer tek tırnak veya satır sonu içeremez." >&2; exit 2 ;; esac
if [[ "$ad" = SENTRY_DSN* ]] && [ -n "$deger" ] && ! [[ "$deger" =~ ^https://[^@[:space:]]+@[^/[:space:]]+/[0-9]+$ ]]; then
  echo "$ad biçimi https://<anahtar>@<sunucu>/<proje no> olmalı; yazılmadı." >&2
  exit 2
fi

tmp=$(mktemp "$KF/.env.XXXXXX")
chmod 600 "$tmp"
# Değer awk'a ortamdan geçer (komut satırına girmez); .env'de tek tırnak içinde: compose ve bash için birebir.
DEGER="$deger" awk -v ad="$ad" 'BEGIN{d=ENVIRON["DEGER"]; y=0}
  $0 ~ "^"ad"=" {print ad"='\''"d"'\''"; y=1; next} {print}
  END{if(!y) print ad"='\''"d"'\''"}' "$KF/.env" > "$tmp"
mv "$tmp" "$KF/.env"
chown deploy:deploy "$KF/.env"; chmod 600 "$KF/.env"
unset deger DEGER
echo "$ad yazıldı (uzunluk gizli)."

if [ "$ad" = APP_DB_PASSWORD ]; then "$KF/bin/sql-kullanici.sh"; fi
hedef=${servis[$ad]}
if [ "$hedef" != - ]; then
  cd "$KF"
  if docker compose up -d --wait --wait-timeout 120 "$hedef" > /dev/null 2>&1; then
    echo "$hedef yeniden kuruldu, sağlıklı."
  else
    echo "UYARI: $hedef sağlıklı açılmadı. Kayıt: docker compose logs --tail 50 $hedef. Önceki değeri bu betikle geri yaz." >&2
    exit 1
  fi
else
  echo "Servis yeniden başlatılmadı (yedek betiği her çalışmada .env'yi okur)."
fi

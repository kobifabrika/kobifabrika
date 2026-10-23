#!/usr/bin/env bash
# Yeni sunucuda bir kez (docs/canli-ortam.md → Sıfırdan kurulum): klasörler, .env (rastgele şifreler, ekrana basılmaz),
# Data Protection sertifikası, yedek zamanlayıcısı. Var olan .env ve sertifikaya dokunmaz. Kullanım: sudo /opt/kobifabrika/bin/ilk-kurulum.sh
set -euo pipefail
KF=/opt/kobifabrika
cd "$KF"
install -d -m 750 -o root -g deploy "$KF/sertifika"
# SQL Server konteyneri 10001 (mssql) kullanıcısıyla yazar; deploy grubu okur.
install -d -m 2770 -o 10001 -g deploy "$KF/yedek"

sifre() { printf 'Kf%s7x' "$(openssl rand -base64 36 | tr -d '/+=\n')"; }
if [ ! -f "$KF/.env" ]; then
  umask 077
  {
    echo "# Canlı gizli değerler (docs/canli-ortam.md → Gizli değerler). Git'e girmez, ekrana basılmaz."
    echo "MSSQL_SA_PASSWORD='$(sifre)'"
    echo "APP_DB_PASSWORD='$(sifre)'"
    echo "DP_CERT_PASSWORD='$(sifre)'"
    echo "YEDEK_SIFRE='$(sifre)'"
    echo "WEB_VARIANT=site"
    echo "BREVO_SMTP_USER=''"
    echo "BREVO_SMTP_KEY=''"
    echo "SENTRY_DSN=''"
    echo "R2_ACCOUNT_ID=''"
    echo "R2_ACCESS_KEY_ID=''"
    echo "R2_SECRET_ACCESS_KEY=''"
    echo "R2_BUCKET=''"
  } > "$KF/.env"
  echo ".env oluşturuldu."
fi
chown deploy:deploy "$KF/.env"; chmod 600 "$KF/.env"

if [ ! -f "$KF/sertifika/dp.pfx" ]; then
  set -a; . "$KF/.env"; set +a
  tmp=$(mktemp -d)
  openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj "/CN=KobiFabrika Data Protection" \
    -keyout "$tmp/k.pem" -out "$tmp/c.pem" 2> /dev/null
  openssl pkcs12 -export -inkey "$tmp/k.pem" -in "$tmp/c.pem" -passout env:DP_CERT_PASSWORD -out "$KF/sertifika/dp.pfx"
  rm -rf "$tmp"
  # API konteyneri 1654 (app) kullanıcısıyla okur.
  chown 1654:deploy "$KF/sertifika/dp.pfx"; chmod 440 "$KF/sertifika/dp.pfx"
  echo "Data Protection sertifikası oluşturuldu."
fi

install -m 644 "$KF/systemd/kobifabrika-yedek.service" "$KF/systemd/kobifabrika-yedek.timer" /etc/systemd/system/
systemctl daemon-reload
systemctl enable --now kobifabrika-yedek.timer > /dev/null
echo "Yedek zamanlayıcısı: $(systemctl list-timers kobifabrika-yedek.timer --no-legend | awk '{print $1, $2, $3}')"

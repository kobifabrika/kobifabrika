#!/usr/bin/env bash
# Yayın modu: site = sadece tanıtım sitesi (NEXT_PUBLIC_SITE_ONLY=1, kayıt/giriş "Çok yakında"), tam = site + panel.
# İki imaj da her dağıtımda gelir; geçiş derleme istemez. Kullanım: sudo /opt/kobifabrika/bin/yayin-modu.sh site|tam
set -euo pipefail
KF=/opt/kobifabrika
mod=${1:?site veya tam}
case "$mod" in site|tam) ;; *) echo "site veya tam" >&2; exit 2 ;; esac
docker image inspect "kobifabrika-web-$mod:current" > /dev/null
if grep -q '^WEB_VARIANT=' "$KF/.env"; then sed -i "s/^WEB_VARIANT=.*/WEB_VARIANT=$mod/" "$KF/.env"; else echo "WEB_VARIANT=$mod" >> "$KF/.env"; fi
cd "$KF" && docker compose up -d --wait web
echo "Yayın modu: $mod ($(docker inspect -f '{{.Config.Image}}' "$(docker compose ps -q web)"))"

#!/usr/bin/env bash
# GitHub Actions dağıtım anahtarının tek girişi (deploy kullanıcısının authorized_keys'inde command="..." ile zorunlu).
# Kabuk, port yönlendirme ve pty yok (restrict); sadece üç komut:
#   yukle          stdin'den `docker save` akışı (düz, gzip veya zstd; docker load kendisi açar)
#   dosyalar       stdin'den tar.gz (compose.yml, Caddyfile, bin/, systemd/) → /opt/kobifabrika (.env, yedek, sertifika'ya dokunmaz)
#   dagit <etiket> bin/dagit.sh
set -euo pipefail
read -r -a komut <<< "${SSH_ORIGINAL_COMMAND:-}"
case "${komut[0]:-}" in
  yukle)
    docker load
    ;;
  dosyalar)
    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    tar -xzf - -C "$tmp" compose.yml Caddyfile bin systemd
    install -m 644 "$tmp/compose.yml" "$tmp/Caddyfile" /opt/kobifabrika/
    install -m 755 "$tmp"/bin/*.sh /opt/kobifabrika/bin/
    install -m 644 "$tmp"/systemd/* /opt/kobifabrika/systemd/
    if ! cmp -s /opt/kobifabrika/systemd/kobifabrika-yedek.timer /etc/systemd/system/kobifabrika-yedek.timer \
      || ! cmp -s /opt/kobifabrika/systemd/kobifabrika-yedek.service /etc/systemd/system/kobifabrika-yedek.service; then
      sudo install -m 644 /opt/kobifabrika/systemd/kobifabrika-yedek.* /etc/systemd/system/
      sudo systemctl daemon-reload
    fi
    echo "dosyalar güncellendi"
    ;;
  dagit)
    [[ "${komut[1]:-}" =~ ^[a-z0-9-]{1,40}$ ]] || { echo "geçersiz etiket" >&2; exit 2; }
    exec /opt/kobifabrika/bin/dagit.sh "${komut[1]}"
    ;;
  *)
    echo "izin verilmeyen komut" >&2
    exit 2
    ;;
esac

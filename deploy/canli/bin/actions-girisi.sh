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
    install -m 644 "$tmp/compose.yml" /opt/kobifabrika/
    # Caddyfile konteynere tek dosya olarak bağlı: yerinde yazılır (install yeni inode açar, konteyner eskisini görmeye devam eder).
    cat "$tmp/Caddyfile" > /opt/kobifabrika/Caddyfile
    install -m 755 "$tmp"/bin/*.sh /opt/kobifabrika/bin/
    install -m 644 "$tmp"/systemd/* /opt/kobifabrika/systemd/
    degisti=0
    for birim in /opt/kobifabrika/systemd/*; do
      if ! cmp -s "$birim" "/etc/systemd/system/$(basename "$birim")"; then
        sudo install -m 644 "$birim" /etc/systemd/system/
        degisti=1
      fi
    done
    if [ "$degisti" = 1 ]; then
      sudo systemctl daemon-reload
      # [Install] bölümü olan birimler (zamanlayıcılar, açılış servisi) etkinleştirilir.
      for birim in $(grep -l '^\[Install\]' /opt/kobifabrika/systemd/*); do
        sudo systemctl enable --now "$(basename "$birim")" > /dev/null
      done
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

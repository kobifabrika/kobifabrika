#!/usr/bin/env bash
# 80/443'ü sadece Cloudflare'in IP aralıklarına açar (docs/canli-ortam.md → Cloudflare). Root ile çalışır.
# Liste resmi kaynaktan: https://www.cloudflare.com/ips-v4 ve /ips-v6. İndirilemez veya biçimi bozuksa son kaydedilen liste
# (/opt/kobifabrika/cloudflare-ip/) kullanılır; hiç liste yoksa hiçbir şeye dokunmaz.
#
# İki katman:
#   DOCKER-USER  Caddy'nin yayınlanan portları Docker'ın DNAT'ı ile FORWARD'dan geçer, ufw'ye hiç uğramaz. Asıl kısıt burada:
#                dış arayüzden 80/tcp, 443/tcp, 443/udp'ye gelen yeni bağlantı KF-CLOUDFLARE zincirine gider; Cloudflare
#                aralığındaysa devam eder, değilse düşer. Konteynerler arası ve sunucunun kendi trafiği etkilenmez.
#   ufw          INPUT yolu (docker-proxy) için aynı aralıklar, "cloudflare" yorumlu kurallar; herkese açık 80/443 kaldırılır.
# SSH (22) ve diğer ufw kurallarına dokunulmaz. Değişiklik yoksa ufw kuralları yeniden yazılmaz.
#
# Kullanım: sudo /opt/kobifabrika/bin/cloudflare-ip.sh          indir + uygula (haftalık zamanlayıcı ve açılışta da çalışır)
#           sudo /opt/kobifabrika/bin/cloudflare-ip.sh --yerel  indirmeden kayıtlı listeyi uygula
#           sudo /opt/kobifabrika/bin/cloudflare-ip.sh --kaldir kısıtı kaldır, 80/443'ü herkese aç (turuncu buluttan çıkarken)
set -euo pipefail
KLASOR=/opt/kobifabrika/cloudflare-ip
ZINCIR=KF-CLOUDFLARE
kayit() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }
[ "$(id -u)" = 0 ] || { echo "root ile çalıştır (sudo)" >&2; exit 1; }
mod=${1:-indir}
arayuz=$(ip route show default | awk '{print $5; exit}')
[ -n "$arayuz" ] || { echo "varsayılan ağ arayüzü bulunamadı" >&2; exit 1; }

# DOCKER-USER'daki KF-CLOUDFLARE atlamalarını kaldırır (tekrar tekrar eklenmesin).
atlamalari_sil() {
  local ipt=$1
  { $ipt -S DOCKER-USER 2> /dev/null || true; } | { grep -- "-j $ZINCIR" || true; } | sed 's/^-A /-D /' | while read -r kural; do
    # shellcheck disable=SC2086
    $ipt $kural
  done
}

ufw_cloudflare_sil() {
  local no
  for no in $(ufw status numbered | grep '# cloudflare' | sed -E 's/^\[ *([0-9]+)\].*/\1/' | sort -rn); do
    ufw --force delete "$no" > /dev/null
  done
}

if [ "$mod" = --kaldir ]; then
  for ipt in iptables ip6tables; do
    atlamalari_sil "$ipt"
    $ipt -F "$ZINCIR" 2> /dev/null && $ipt -X "$ZINCIR" || true
  done
  ufw allow 80/tcp > /dev/null
  ufw allow 443/tcp > /dev/null
  ufw allow 443/udp > /dev/null
  ufw_cloudflare_sil
  rm -f "$KLASOR/ufw-uygulanan"
  kayit "Kısıt kaldırıldı: 80/443 herkese açık"
  exit 0
fi

install -d -m 755 "$KLASOR"

if [ "$mod" = indir ]; then
  tmp=$(mktemp -d)
  trap 'rm -rf "$tmp"' EXIT
  if curl -fsS --max-time 20 https://www.cloudflare.com/ips-v4 -o "$tmp/ips-v4" \
    && curl -fsS --max-time 20 https://www.cloudflare.com/ips-v6 -o "$tmp/ips-v6"; then
    # Biçim kontrolü: her satır bir CIDR; sayı makul (bozuk/boş yanıt herkesi kilitlemesin).
    # Cloudflare'in dosyaları son satır sonu olmadan gelir: eklenir (yoksa read son aralığı atlar).
    sed -i -e 's/\r$//' -e '/^$/d' -e '$a\' "$tmp/ips-v4" "$tmp/ips-v6"
    if ! grep -qvE '^([0-9]{1,3}\.){3}[0-9]{1,3}/[0-9]{1,2}$' "$tmp/ips-v4" \
      && ! grep -qvE '^[0-9a-f:]+/[0-9]{1,3}$' "$tmp/ips-v6" \
      && [ "$(wc -l < "$tmp/ips-v4")" -ge 10 ] && [ "$(wc -l < "$tmp/ips-v6")" -ge 5 ]; then
      for f in ips-v4 ips-v6; do
        if ! cmp -s "$tmp/$f" "$KLASOR/$f"; then
          install -m 644 "$tmp/$f" "$KLASOR/$f"
          kayit "$f güncellendi ($(wc -l < "$KLASOR/$f") aralık)"
        fi
      done
    else
      kayit "UYARI: indirilen liste biçimsiz; kayıtlı liste kullanılıyor"
    fi
  else
    kayit "UYARI: liste indirilemedi; kayıtlı liste kullanılıyor"
  fi
fi

[ -s "$KLASOR/ips-v4" ] && [ -s "$KLASOR/ips-v6" ] || { kayit "Kayıtlı liste yok; hiçbir şey değişmedi"; exit 1; }

# 1) DOCKER-USER (asıl kısıt). Zincir önce doldurulur, atlama en son eklenir: arada açık ya da kapalı kalma olmaz.
for ipt in iptables ip6tables; do
  $ipt -S DOCKER-USER > /dev/null 2>&1 || { kayit "$ipt: DOCKER-USER yok (Docker çalışıyor mu?), atlandı"; continue; }
  liste=$KLASOR/ips-v4
  [ "$ipt" = ip6tables ] && liste=$KLASOR/ips-v6
  $ipt -N "$ZINCIR" 2> /dev/null || true
  $ipt -F "$ZINCIR"
  while read -r aralik; do
    $ipt -A "$ZINCIR" -s "$aralik" -j RETURN
  done < "$liste"
  $ipt -A "$ZINCIR" -j DROP
  atlamalari_sil "$ipt"
  $ipt -I DOCKER-USER -i "$arayuz" -p udp -m conntrack --ctstate NEW --ctorigdstport 443 -j "$ZINCIR"
  $ipt -I DOCKER-USER -i "$arayuz" -p tcp -m conntrack --ctstate NEW --ctorigdstport 443 -j "$ZINCIR"
  $ipt -I DOCKER-USER -i "$arayuz" -p tcp -m conntrack --ctstate NEW --ctorigdstport 80 -j "$ZINCIR"
done

# 2) ufw (INPUT yolu). Liste değişmediyse dokunulmaz.
if ! cat "$KLASOR/ips-v4" "$KLASOR/ips-v6" | cmp -s - "$KLASOR/ufw-uygulanan"; then
  # Eski "cloudflare" kuralları yenileriyle değişir, sonra herkese açık 80/443 silinir (trafik DOCKER-USER'dan geçtiği için
  # arada kesinti olmaz).
  ufw_cloudflare_sil
  cat "$KLASOR/ips-v4" "$KLASOR/ips-v6" | while read -r aralik; do
    ufw allow proto tcp from "$aralik" to any port 80,443 comment cloudflare > /dev/null
    ufw allow proto udp from "$aralik" to any port 443 comment cloudflare > /dev/null
  done
  for kural in 80/tcp 443/tcp 443/udp; do ufw delete allow "$kural" > /dev/null 2>&1 || true; done
  cat "$KLASOR/ips-v4" "$KLASOR/ips-v6" > "$KLASOR/ufw-uygulanan"
  kayit "ufw güncellendi"
fi

kayit "Cloudflare kısıtı uygulandı: $(wc -l < "$KLASOR/ips-v4") IPv4 + $(wc -l < "$KLASOR/ips-v6") IPv6 aralık, arayüz $arayuz"

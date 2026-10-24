#!/usr/bin/env bash
# Dağıtım (GitHub Actions "Dağıtım" işi çağırır; elle de çalışır): docs/canli-ortam.md → Dağıtım.
# Önkoşul: kobifabrika-api:<etiket>, kobifabrika-web-site:<etiket>, kobifabrika-web-tam:<etiket> imajları yüklü (docker load).
# Sıra: yedek → migration (aynı API imajındaki bundle, sa ile) → uygulama girişi (kf_app) → yeni imajlar → /health.
# Sağlık kontrolü geçmezse önceki imajlara döner ve hata verir. Her imajın son 2 sürümü (current, onceki) kalır.
# Kullanım: /opt/kobifabrika/bin/dagit.sh <etiket>
. /opt/kobifabrika/bin/ortak.sh
etiket=${1:?etiket gerekli}
imajlar=(kobifabrika-api kobifabrika-web-site kobifabrika-web-tam)

for i in "${imajlar[@]}"; do
  docker image inspect "$i:$etiket" > /dev/null || { kayit "Eksik imaj: $i:$etiket"; exit 1; }
done

docker compose up -d --wait sqlserver

kayit "1/5 Yedek (migration öncesi)"
sudo "$KF/bin/yedek-al.sh" dagitim

kayit "2/5 Migration"
# Bundle uygulamanın ayarlarını okur; bağlantı dizesi ortamdan adıyla geçer (komut satırına şifre girmez).
export ConnectionStrings__Default="Server=sqlserver,1433;Database=KobiFabrika;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True"
docker run --rm --network kobifabrika_default -e ConnectionStrings__Default -e ASPNETCORE_ENVIRONMENT=Production \
  --entrypoint /app/efbundle "kobifabrika-api:$etiket"
unset ConnectionStrings__Default

kayit "3/5 Uygulama veritabanı girişi (kf_app, db_owner)"
"$KF/bin/sql-kullanici.sh"

kayit "4/5 Yeni imajlar"
onceki_var=0
if docker image inspect kobifabrika-api:current > /dev/null 2>&1; then
  onceki_var=1
  for i in "${imajlar[@]}"; do
    docker image inspect "$i:current" > /dev/null 2>&1 && docker tag "$i:current" "$i:onceki"
  done
fi
for i in "${imajlar[@]}"; do docker tag "$i:$etiket" "$i:current"; done
docker compose up -d --remove-orphans || kayit "compose up hata verdi; sağlık kontrolüne bırakılıyor"

dis_saglik() {
  # İlk kurulumda sertifika birkaç saniye sonra gelir: 60 sn denenir.
  for _ in $(seq 1 20); do
    curl -fsS -o /dev/null --max-time 10 https://api.kobifabrika.com/health && return 0
    sleep 3
  done
  return 1
}

saglikli() {
  for _ in $(seq 1 40); do
    local api web
    api=$(docker inspect -f '{{.State.Health.Status}}' "$(docker compose ps -q api)" 2>/dev/null || echo yok)
    web=$(docker inspect -f '{{.State.Health.Status}}' "$(docker compose ps -q web)" 2>/dev/null || echo yok)
    if [ "$api" = healthy ] && [ "$web" = healthy ]; then return 0; fi
    sleep 3
  done
  kayit "Sağlık: api=$api web=$web"
  return 1
}

kayit "5/5 Sağlık kontrolü"
if ! saglikli || ! dis_saglik; then
  docker compose logs --tail 80 api web
  if [ "$onceki_var" = 1 ]; then
    kayit "Sağlık kontrolü geçmedi: önceki imajlara dönülüyor (migration geri alınmaz; gerekirse yedekten dönüş, docs/canli-ortam.md)."
    for i in "${imajlar[@]}"; do docker tag "$i:onceki" "$i:current"; done
    docker compose up -d
    saglikli && kayit "Önceki sürüm ayakta." || kayit "Önceki sürüm de sağlıksız!"
  fi
  exit 1
fi

# Caddyfile değişmiş olabilir: yeniden yükle (sertifikalar korunur). Konteyner dosyanın eski inode'unu görüyorsa
# (dosya yerinde yazılmamışsa) reload eskisini okur: o durumda Caddy yeniden oluşturulur (birkaç saniye).
if docker compose exec -T caddy cat /etc/caddy/Caddyfile | cmp -s - Caddyfile; then
  docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile > /dev/null 2>&1 || true
else
  kayit "Caddyfile konteynerdekinden farklı: Caddy yeniden oluşturuluyor"
  docker compose up -d --force-recreate --no-deps caddy
fi

# Son 2 sürüm kalır: current ve onceki dışındaki etiketler kaldırılır.
for i in "${imajlar[@]}"; do
  docker image ls "$i" --format '{{.Tag}}' | grep -vxE 'current|onceki' | xargs -r -I{} docker rmi "$i:{}" > /dev/null 2>&1 || true
done
docker image prune -f > /dev/null
kayit "Dağıtım tamam: $etiket ($(docker compose ps --format '{{.Service}}={{.Status}}' | tr '\n' ' '))"

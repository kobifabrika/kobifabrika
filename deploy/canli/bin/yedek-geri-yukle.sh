#!/usr/bin/env bash
# Yedekten geri yükleme (docs/canli-ortam.md → Yedekten dönme).
#   Deneme (canlıya dokunmaz):  sudo /opt/kobifabrika/bin/yedek-geri-yukle.sh deneme [yedek/X.bak.zst]
#     → KobiFabrika_GeriYuklemeDeneme olarak açar, tablo/satır sayılarını canlıyla karşılaştırır, sonra siler.
#   Canlıya dönüş:              sudo /opt/kobifabrika/bin/yedek-geri-yukle.sh canli yedek/X.bak.zst
#     → API'yi durdurur, önce şimdiki hali yedekler, KobiFabrika'yı yedekle değiştirir, API'yi açar.
. /opt/kobifabrika/bin/ortak.sh
mod=${1:?deneme veya canli}
dosya=${2:-$(ls -1t "$KF"/yedek/KobiFabrika-*.bak.zst | head -1)}
[ -f "$dosya" ] || dosya="$KF/$dosya"
[ -f "$dosya" ] || { kayit "Yedek bulunamadı: $dosya"; exit 1; }

acik="$KF/yedek/geri-yukle.bak"
zstd -q -d -f "$dosya" -o "$acik"
chown 10001 "$acik"
trap 'rm -f "$acik"' EXIT
kayit "Yedek: $(basename "$dosya")"

# Mantıksal dosya adları canlı veritabanından (yedekler hep aynı veritabanından alınır).
mantiksal() { sql "SET NOCOUNT ON; SELECT name FROM sys.master_files WHERE database_id = DB_ID('KobiFabrika') AND type = $1"; }
veri=$(mantiksal 0); gunluk=$(mantiksal 1)

geri_yukle() { # $1 = hedef veritabanı
  sql "RESTORE DATABASE [$1] FROM DISK = N'/var/opt/mssql/backup/geri-yukle.bak' WITH REPLACE, CHECKSUM,
    MOVE N'$veri' TO N'/var/opt/mssql/data/$1.mdf', MOVE N'$gunluk' TO N'/var/opt/mssql/data/$1_log.ldf'" > /dev/null
}
sayim() { sql "SET NOCOUNT ON; SELECT COUNT(*) FROM [$1].sys.tables; SELECT CAST(SUM(p.rows) AS bigint) FROM [$1].sys.partitions p JOIN [$1].sys.tables t ON t.object_id = p.object_id WHERE p.index_id IN (0,1); SELECT COUNT(*) FROM [$1].dbo.[__EFMigrationsHistory]" | tr '
' ' '; }

case "$mod" in
  deneme)
    hedef=KobiFabrika_GeriYuklemeDeneme
    geri_yukle "$hedef"
    kayit "Geri yüklendi: $hedef (tablo, toplam satır, migration) = $(sayim "$hedef")"
    kayit "Canlı:          KobiFabrika (tablo, toplam satır, migration) = $(sayim KobiFabrika)"
    sql "SET NOCOUNT ON; SELECT CONCAT('  paket: ', Name) FROM [$hedef].dbo.Plans ORDER BY SortOrder" || true
    sql "ALTER DATABASE [$hedef] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$hedef]" > /dev/null
    kayit "$hedef silindi."
    ;;
  canli)
    docker compose stop api
    "$KF/bin/yedek-al.sh" geri-yukleme-oncesi
    sql "ALTER DATABASE [KobiFabrika] SET SINGLE_USER WITH ROLLBACK IMMEDIATE" > /dev/null
    geri_yukle KobiFabrika
    sql "ALTER DATABASE [KobiFabrika] SET MULTI_USER" > /dev/null
    "$KF/bin/sql-kullanici.sh"
    docker compose up -d --wait api
    kayit "Canlı veritabanı yedekten döndü: $(sayim KobiFabrika)"
    ;;
  *) echo "deneme veya canli" >&2; exit 2 ;;
esac

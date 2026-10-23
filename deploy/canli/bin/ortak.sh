# Sunucu betiklerinin ortak parçası (source edilir). Gizli değerler .env'den ortama okunur, hiçbir zaman ekrana yazılmaz.
set -euo pipefail
KF=/opt/kobifabrika
cd "$KF"
set -a
# shellcheck disable=SC1091
. "$KF/.env"
set +a

kayit() { printf '[%s] %s\n' "$(date -u +%H:%M:%S)" "$*"; }

# SQL Server'da sa ile komut: şifre konteynerin kendi ortamından (SQLCMDPASSWORD), komut satırına girmez.
# Ek ortam değişkenleri ($(DEGISKEN) betik değişkeni olarak) -e ile adıyla geçirilir: sql "..." [-e AD]...
sql() {
  local query=$1; shift
  docker compose exec -T "$@" sqlserver sh -c 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -b -h -1 -W -Q "$0"' "$query"
}

db_var_mi() { [ "$(sql "SET NOCOUNT ON; SELECT CASE WHEN DB_ID('KobiFabrika') IS NULL THEN 0 ELSE 1 END")" = "1" ]; }

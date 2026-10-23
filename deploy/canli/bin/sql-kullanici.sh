#!/usr/bin/env bash
# API'nin veritabanı girişi kf_app (sa değil): yoksa açar, şifresini .env APP_DB_PASSWORD ile eşitler, db_owner yapar
# (Hangfire şemasını kendisi kurar). dagit.sh her dağıtımda çağırır; tekrar çalıştırmak zararsız.
# Şifre sqlcmd betik değişkeni olarak ortamdan okunur ($(APP_DB_PASSWORD)); komut satırına girmez.
. /opt/kobifabrika/bin/ortak.sh
sorgu=$(cat <<'SQL'
SET NOCOUNT ON;
IF SUSER_ID('kf_app') IS NULL
  CREATE LOGIN [kf_app] WITH PASSWORD = '$(APP_DB_PASSWORD)', CHECK_POLICY = OFF, DEFAULT_DATABASE = [KobiFabrika];
ELSE
  ALTER LOGIN [kf_app] WITH PASSWORD = '$(APP_DB_PASSWORD)';
USE [KobiFabrika];
IF USER_ID('kf_app') IS NULL CREATE USER [kf_app] FOR LOGIN [kf_app];
ALTER ROLE [db_owner] ADD MEMBER [kf_app];
SQL
)
sql "$sorgu" -e APP_DB_PASSWORD > /dev/null
kayit "kf_app hazır."

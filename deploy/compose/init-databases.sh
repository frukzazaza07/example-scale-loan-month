#!/bin/sh
set -eu
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v webhook_password="$WEBHOOK_DB_PASSWORD" -v loan_password="$LOAN_DB_PASSWORD" \
  -v notification_password="$NOTIFICATION_DB_PASSWORD" -v simulator_password="$SIMULATOR_DB_PASSWORD" <<'SQL'
CREATE ROLE webhook LOGIN PASSWORD :'webhook_password';
CREATE ROLE loan LOGIN PASSWORD :'loan_password';
CREATE ROLE notification LOGIN PASSWORD :'notification_password';
CREATE ROLE simulator LOGIN PASSWORD :'simulator_password';
CREATE DATABASE webhook OWNER webhook;
CREATE DATABASE loan OWNER loan;
CREATE DATABASE notification OWNER notification;
CREATE DATABASE simulator OWNER simulator;
REVOKE CONNECT ON DATABASE webhook, loan, notification, simulator FROM PUBLIC;
GRANT CONNECT ON DATABASE webhook TO webhook;
GRANT CONNECT ON DATABASE loan TO loan;
GRANT CONNECT ON DATABASE notification TO notification;
GRANT CONNECT ON DATABASE simulator TO simulator;
SQL

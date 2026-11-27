#!/bin/sh
# Tool: Claude Code (Claude Opus 5.5)
# Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
# Prompt Summary: "PA3 hardening: separate owner and runtime database roles, API health check, field key as a secret file, SonarQube smells, test first"
# AI Contribution: First-start database setup: an owner role that owns the resourcehub database and schema (used only by the migration job) and a separate runtime login role for the API that may only connect; neither has superuser, role or database rights (CWE-250, CWE-269, CWE-1392) (~100%)
# Human Modifications: None yet
# Verification: ComposeConfigTests.Database_belongs_to_the_owner_role_not_the_app_role; ComposeStackTests (migration job, runtime role restrictions, role flags)
# Confidence: High
#
# Runs once, when the postgres:18 image initialises an empty data volume. The superuser
# (POSTGRES_USER / POSTGRES_PASSWORD) is used only here. Table privileges for the runtime role are
# granted by the migration job (DatabaseStartup.MigrateAsOwnerAsync) after it creates the tables.
set -eu

: "${OWNER_DB_USER:?OWNER_DB_USER must be set}"
: "${OWNER_DB_PASSWORD:?OWNER_DB_PASSWORD must be set}"
: "${APP_DB_USER:?APP_DB_USER must be set}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD must be set}"
: "${APP_DB_NAME:?APP_DB_NAME must be set}"

if [ "$OWNER_DB_USER" = "$APP_DB_USER" ]; then
    echo "OWNER_DB_USER and APP_DB_USER must be different roles" >&2
    exit 1
fi

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -v owner_user="$OWNER_DB_USER" -v owner_password="$OWNER_DB_PASSWORD" \
    -v app_user="$APP_DB_USER" -v app_password="$APP_DB_PASSWORD" -v app_db="$APP_DB_NAME" <<'SQL'
-- Owns the schema; only the one-shot migration job logs in as it.
CREATE ROLE :"owner_user" LOGIN PASSWORD :'owner_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT CONNECTION LIMIT 5;
-- The API's role: owns nothing, so it cannot ALTER or DROP tables or disable their triggers.
CREATE ROLE :"app_user" LOGIN PASSWORD :'app_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT CONNECTION LIMIT 50;
CREATE DATABASE :"app_db" OWNER :"owner_user";
REVOKE ALL ON DATABASE :"app_db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"app_db" TO :"app_user";
\connect :"app_db"
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO :"owner_user";
GRANT USAGE ON SCHEMA public TO :"app_user";
SQL

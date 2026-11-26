#!/bin/sh
# Tool: Claude Code (Claude Opus 5.5)
# Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
# Prompt Summary: "PA3: harden the Resource Hub with local RS256 JWT auth, row-level authz, AES-GCM field encryption, audit and rate limits, test first"
# AI Contribution: First-start database setup: a login role for the API with no superuser, role or database rights, owning only the resourcehub database (CWE-250, CWE-1392) (~100%)
# Human Modifications: None yet
# Verification: ComposeStackTests.Database_is_not_reachable_from_the_host_and_app_role_is_not_superuser; ComposeConfigTests
# Confidence: High
#
# Runs once, when the postgres:18 image initialises an empty data volume. The superuser
# (POSTGRES_USER / POSTGRES_PASSWORD) is used only here; the API never gets its password.
set -eu

: "${APP_DB_USER:?APP_DB_USER must be set}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD must be set}"
: "${APP_DB_NAME:?APP_DB_NAME must be set}"

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
    -v app_user="$APP_DB_USER" -v app_password="$APP_DB_PASSWORD" -v app_db="$APP_DB_NAME" <<'SQL'
CREATE ROLE :"app_user" LOGIN PASSWORD :'app_password'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS NOINHERIT CONNECTION LIMIT 50;
CREATE DATABASE :"app_db" OWNER :"app_user";
REVOKE ALL ON DATABASE :"app_db" FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE :"app_db" TO :"app_user";
\connect :"app_db"
REVOKE ALL ON SCHEMA public FROM PUBLIC;
ALTER SCHEMA public OWNER TO :"app_user";
SQL

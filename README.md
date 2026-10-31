# Resource Allocation & Help Desk Hub

ASP.NET Core Web API (.NET 10) + PostgreSQL 18. See [spec.md](spec.md) for the full design.

**Current state: barebones.** Only `GET /api/v1/health` exists. It checks the PostgreSQL
connection through an EF Core `DbContext` (no tables yet).

## Layout

```
global.json                  # pins .NET SDK 10
Directory.Build.props        # turns on NuGet lock files (packages.lock.json)
.github/workflows/devsecops.yml  # CI: build, test, image, Trivy, Semgrep, SonarQube Cloud
Dockerfile                   # multi-stage: SDK build -> ASP.NET runtime
docker-compose.yml           # api (:8080) + db (PostgreSQL 18, :5432)
ResourceHub.slnx
src/ResourceHub.Api/         # Web API (Controllers/, Data/, Program.cs)
tests/ResourceHub.Tests/     # xUnit integration tests (WebApplicationFactory)
```

## Prerequisites

- .NET SDK 10 (for local build/test)
- Docker with Compose v2

## Run with Docker Compose

```bash
docker compose up --build -d
```

```bash
curl -i http://localhost:8080/api/v1/health
```

Expected when the database is up:

```
HTTP/1.1 200 OK
{"status":"ok","database":"ok"}
```

Stop the database to see the unhealthy response:

```bash
docker compose stop db
```

```bash
curl -i http://localhost:8080/api/v1/health
```

```
HTTP/1.1 503 Service Unavailable
{"status":"unhealthy","database":"unreachable"}
```

Start it again with `docker compose start db`. Shut everything down with:

```bash
docker compose down
```

Add `-v` to also delete the database volume.

The database password defaults to `resourcehub_dev`, a local development value. Override it by
setting `POSTGRES_PASSWORD` in your shell or in a `.env` file next to `docker-compose.yml`
(`.env` is git-ignored). Changing it after the volume exists needs `docker compose down -v`.

## Build and run locally (without the api container)

```bash
docker compose up -d db
```

```bash
dotnet run --project src/ResourceHub.Api
```

Local runs use the `Development` environment, which reads the connection string from
`src/ResourceHub.Api/appsettings.Development.json` (`localhost:5432`). The URL is printed on
start-up (default `http://localhost:5184/api/v1/health`).

## Test

```bash
dotnet test
```

Without a database, the PostgreSQL-dependent test is skipped and the rest run against an
unreachable connection string (checks the `503` path). To also run the `200` test, start the db
container and point the tests at it:

```bash
docker compose up -d db
```

```bash
RESOURCEHUB_TEST_DB="Host=localhost;Port=5432;Database=resourcehub;Username=resourcehub;Password=resourcehub_dev" dotnet test
```

## Configuration

| Setting | Where | Notes |
|---|---|---|
| `ConnectionStrings:HubDb` | env `ConnectionStrings__HubDb` (compose) or `appsettings.Development.json` | Required; app fails on first DB use without it |
| `POSTGRES_PASSWORD` | shell / `.env` | Used by both containers; default `resourcehub_dev` |
| `ASPNETCORE_HTTP_PORTS` | Dockerfile | `8080` |

## NuGet lock files

Each project has a `packages.lock.json` that pins every direct and transitive package. CI and the
Dockerfile restore with `--locked-mode`, so a restore that would change the lock file fails. After
adding or upgrading a package, run `dotnet restore` and commit the updated lock files. Trivy also
reads these files to find NuGet dependencies; without them it detects none.

## CI (GitHub Actions)

`.github/workflows/devsecops.yml` runs on every push to `main` and on pull requests.

| Job | Steps |
|---|---|
| Build, test, image, Trivy, SonarQube | Restore, SonarQube begin, build (Release), test (with a PostgreSQL 18 service container, coverage), SonarQube end, `docker build`, Trivy dependency scan, Trivy image scan |
| Semgrep | Open-source Semgrep CLI with the public `p/csharp` rules (no account or token) |

Only a broken build, a failing test or a failing Docker build fails the run. Scanner findings
are reported in the logs and uploaded as artifacts (`trivy-reports`, `semgrep-reports`,
`sonarqube-report`, `test-results`) without failing the run. SonarQube Cloud results are on
the SonarQube Cloud dashboard; it needs the `SONAR_TOKEN` repository secret and is skipped
when the secret is not available (for example, pull requests from forks).

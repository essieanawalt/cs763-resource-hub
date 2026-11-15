# Resource Allocation & Help Desk Hub

ASP.NET Core Web API (.NET 10) + PostgreSQL 18 + EF Core. See [spec-domain2.md](spec-domain2.md) for the full design.

The hub manages a pool of physical lab equipment, software license keys and cloud credit
vouchers, and the help desk tickets that request them: submit, IT director sign-off,
administrator allocation, device return, close. All endpoints in spec section 5 are
implemented, plus the existing `GET /api/v1/health`.

## Layout

```
global.json                  # pins .NET SDK 10
Directory.Build.props        # turns on NuGet lock files (packages.lock.json)
.github/workflows/devsecops.yml  # CI: build, test, image, Trivy, Semgrep, SonarQube Cloud
Dockerfile                   # multi-stage: SDK build -> ASP.NET runtime
docker-compose.yml           # api (:8080) + db (PostgreSQL 18, :5432)
ResourceHub.slnx
functional-codebase/dotnet-tools.json            # local dotnet-ef tool (migrations)
functional-codebase/src/ResourceHub.Api/
  Controllers/   # health, assets, tickets (+ allocate), allocations
  Services/      # AssetService, TicketService, AllocationService
  Domain/        # entities and enums
  Contracts/     # request/response DTOs and validation
  Data/          # HubDbContext, MockData (seed), DatabaseMigrator, Migrations/
  Security/      # X-User-Id authentication handler and roles
functional-codebase/tests/ResourceHub.Tests/     # xUnit integration tests (WebApplicationFactory)
```

## Calling the API

Every endpoint except `/api/v1/health` needs an `X-User-Id` header naming one of the seeded
mock users. A missing or unknown id gets `401`; the wrong role gets `403`. **This header is a
stand-in for real authentication, not authentication itself:** the service trusts whoever
sends it. Replace it with a signed token before any real deployment.

| User | Id | Role |
|---|---|---|
| Dr. Maya Chen | `40000000-0000-4000-8000-000000000001` | Course instructor (CS501 A1) |
| Prof. Daniel Okafor | `40000000-0000-4000-8000-000000000002` | Course instructor (EE580 B1) |
| Dr. Lucas Ferreira | `40000000-0000-4000-8000-000000000003` | Lab advisor (Data Science Lab) |
| Dr. Priya Raman | `40000000-0000-4000-8000-000000000004` | Lab advisor (Embedded Systems Lab) |
| Jordan Blake | `40000000-0000-4000-8000-000000000005` | IT director |
| Sam Rivera | `40000000-0000-4000-8000-000000000006` | Administrator |

Courses, labs, projects and the four starting assets are seeded with the ids in spec section 7.

| Endpoint | Who | Notes |
|---|---|---|
| `POST /api/v1/assets` | administrator | `type` is `physical` (`totalUnits`, `defaultLoanDays`), `license` (`licenseKeys`) or `voucher` (`totalCredit`) |
| `GET /api/v1/assets` | any user | Filters: `category`, `departmentProgram`, `type`, `availability` (`available`, `reserved`, `checkedout`, `overdue`), `page`, `pageSize` |
| `GET/PUT/DELETE /api/v1/assets/{id}` | read: any user; change: administrator | License keys are shown masked (`****Q9XA`); PUT can add keys or remove unissued ones |
| `POST /api/v1/assets/{id}/return` | administrator | Body `{"token":"HW-..."}`: returns one device |
| `GET /api/v1/assets/allocations` | own course/lab, or staff | Needs `courseId` or `researchLabId` |
| `POST /api/v1/assets/tickets` | course instructor, lab advisor | For your own course or lab; items have `quantity` (physical, license) or `amount` (voucher) |
| `GET /api/v1/assets/tickets` | own course/lab, or staff | Filters: `status`, `assetCategory`, `courseId`, `researchLabId`, `projectId`, `page`, `pageSize` |
| `GET /api/v1/assets/tickets/{id}` | own course/lab, or staff | Includes events and timings |
| `PUT /api/v1/assets/tickets/{id}` | requester (contents, while pending); IT director (`approved`/`rejected`); administrator (`closed`) | Send either `{"status":...,"comment":...}` or new contents, not both |
| `DELETE /api/v1/assets/tickets/{id}` | requester or administrator | Pending tickets only |
| `POST /api/v1/assets/tickets/{id}/allocate` | administrator | All or nothing. `409` with a `shortages` list if stock is short; the ticket stays approved |

Errors are returned as ProblemDetails (`application/problem+json`). Enum values are snake_case
strings (`course_instructor`, `allocation_failed`).

Example: Dr. Chen requests two laptops and one IDE license for CS501:

```bash
curl -s -X POST http://localhost:8080/api/v1/assets/tickets \
  -H 'Content-Type: application/json' \
  -H 'X-User-Id: 40000000-0000-4000-8000-000000000001' \
  -d '{"courseId":"10000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":2},{"assetId":"50000000-0000-4000-8000-000000000003","quantity":1}]}'
```

## Prerequisites

- .NET SDK 10 (for local build/test)
- Docker with Compose v2

## Run with Docker Compose

```bash
docker compose up --build -d
```

On start-up the API applies the EF Core migrations, which create the schema and seed the mock
data. If the database is not reachable yet it retries every 5 seconds; health answers `503`
meanwhile.

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
dotnet run --project functional-codebase/src/ResourceHub.Api
```

Local runs use the `Development` environment, which reads the connection string from
`functional-codebase/src/ResourceHub.Api/appsettings.Development.json` (`localhost:5432`). The URL is printed on
start-up (default `http://localhost:5184/api/v1/health`).

## Test

```bash
dotnet test
```

Without a database, only the two health tests that use an unreachable connection string run;
the 24 tests that need PostgreSQL are skipped. To run everything, start the db container and
point the tests at it. Each test class creates its own throwaway database
(`resourcehub_test_<guid>`), migrates and seeds it, and drops it afterwards, so the
`resourcehub` database itself is not touched:

```bash
docker compose up -d db
```

```bash
RESOURCEHUB_TEST_DB="Host=localhost;Port=5432;Database=resourcehub;Username=resourcehub;Password=resourcehub_dev" dotnet test
```

The user in that connection string needs permission to create databases (the compose `db`
user has it).

## Database migrations

The schema and seed data live in `functional-codebase/src/ResourceHub.Api/Data/Migrations/`.
After changing the model, add a migration with the local `dotnet-ef` tool:

```bash
cd functional-codebase && dotnet tool restore
```

```bash
cd functional-codebase/src/ResourceHub.Api && ConnectionStrings__HubDb="Host=localhost" dotnet tool run dotnet-ef migrations add <Name> --output-dir Data/Migrations
```

The connection string only has to be present; it is not used to create a migration.

## Configuration

| Setting | Where | Notes |
|---|---|---|
| `ConnectionStrings:HubDb` | env `ConnectionStrings__HubDb` (compose) or `appsettings.Development.json` | Required; app fails on first DB use without it |
| `POSTGRES_PASSWORD` | shell / `.env` | Used by both containers; default `resourcehub_dev` |
| `Database:MigrateOnStartup` | env `Database__MigrateOnStartup` | Default `true`; the tests set it to `false` and migrate their own database |
| `RESOURCEHUB_TEST_DB` | shell | Connection string for the database tests; they skip without it |
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

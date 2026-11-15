# Resource Allocation & Help Desk Hub

ASP.NET Core Web API (.NET 10) + PostgreSQL 18. See [spec-domain2.md](spec-domain2.md) for the full design.

Manages lab equipment, software license keys and cloud credit vouchers, and the help desk
tickets that request them: submit → IT director sign-off → administrator allocation → return
(physical devices) → close. All endpoints in spec section 5 are implemented, following the
clarifications in section 6, with the section 7 mock data seeded under its fixed ids.

Run the commands below from the repository root: the shared `Dockerfile`, `docker-compose.yml` and solution file live there.

## Layout

```
global.json                  # pins .NET SDK 10
Directory.Build.props        # turns on NuGet lock files (packages.lock.json)
.github/workflows/devsecops.yml  # CI: build, test, image, Trivy, Semgrep, SonarQube Cloud
Dockerfile                   # multi-stage: SDK build -> ASP.NET runtime
docker-compose.yml           # api (:8080) + db (PostgreSQL 18, :5432)
ResourceHub.slnx
PA2/functional-codebase/dotnet-tools.json        # local dotnet-ef tool (migrations)
PA2/functional-codebase/src/ResourceHub.Api/     # Web API
  Program.cs                 #   services, JSON options, startup migration, routes
  Domain/                    #   entities and enums
  Data/                      #   HubDbContext, SeedData (spec section 7), Migrations/
  Contracts/                 #   request/response records
  Endpoints/                 #   Health, Asset, Ticket, Allocation route groups
  Services/                  #   AllocationService, validation, caller identity, views
PA2/functional-codebase/tests/ResourceHub.Tests/ # xUnit API integration tests (WebApplicationFactory + PostgreSQL)
```

## API

All routes are under `/api/v1`. Bodies and responses are JSON; enum values are lower snake case
(`physical`, `license`, `voucher`; `pending`, `approved`, `rejected`, `allocated`, `closed`).
Errors are RFC 7807 problem details (`400` validation, `401`, `403`, `404`, `409` state or stock conflicts).

| Route | Verb | Who (`X-User-Id`) | What |
|---|---|---|---|
| `/health` | GET | anyone | `200` if the database is reachable, `503` if not |
| `/assets` | POST | administrator | Add an asset. Physical: `units`, `defaultLoanDays`. License: `licenseKeys`. Voucher: `balance` |
| `/assets` | GET | anyone | Filters (combinable): `category`, `departmentProgram`, `availability` (`available`/`reserved`/`checkedout`/`overdue`), `type`, `search`; `page`, `pageSize` (≤100); newest first |
| `/assets/{asset_id}` | GET, PUT, DELETE | GET anyone; PUT/DELETE administrator | PUT sets total stock (cannot drop below what is handed out). DELETE refused (`409`) once tickets refer to the asset |
| `/assets/{asset_id}/return` | POST | administrator | Body `{"token": "HW-…"}`: returns one device, stock +1, records the return time |
| `/assets/allocations` | GET | the course's instructor / lab's advisor, IT director, administrator | `courseId` or `researchLabId`: unreturned devices, keys and credits on tickets not closed, plus a summary |
| `/assets/tickets` | POST | course instructor (own course) or lab advisor (own lab) | `courseId` or `researchLabId`, optional `projectId`, `notes`, `items: [{assetId, quantity \| amount, loanDays?}]` |
| `/assets/tickets` | GET | anyone | Filters: `status` (comma-separated), `assetCategory`, `courseId`, `researchLabId`, `projectId`; paged, newest first |
| `/assets/tickets/{ticket_id}` | GET | anyone | Ticket with `events` and `metrics` (seconds between steps, allocation attempts) |
| `/assets/tickets/{ticket_id}` | PUT | requester (content, while pending); IT director (`{"status":"approved"\|"rejected","reason":…}`); administrator (`{"status":"closed"}`) | Content edit **or** state change, not both |
| `/assets/tickets/{ticket_id}` | DELETE | requester or administrator | Pending tickets only |
| `/assets/tickets/{ticket_id}/allocate` | POST | administrator | Atomic, all-or-nothing: issues keys, deducts credits, hands out devices with tokens and due dates. Short stock → `409` with `shortages`, ticket stays `approved` |

**Caller identity.** Authentication is out of scope, so callers send a seeded user's id in the
`X-User-Id` header (unknown or missing → `401`, wrong role → `403`):

| User | Role | Id |
|---|---|---|
| Dr. Maya Chen | Course instructor, CS501 A1 | `40000000-0000-4000-8000-000000000001` |
| Prof. Daniel Okafor | Course instructor, EE580 B1 | `40000000-0000-4000-8000-000000000002` |
| Dr. Lucas Ferreira | Lab advisor, Data Science Lab | `40000000-0000-4000-8000-000000000003` |
| Dr. Priya Raman | Lab advisor, Embedded Systems Lab | `40000000-0000-4000-8000-000000000004` |
| Jordan Blake | IT director | `40000000-0000-4000-8000-000000000005` |
| Sam Rivera | Administrator | `40000000-0000-4000-8000-000000000006` |

Course, lab, project and asset ids are in spec section 7.

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
{"status":"healthy","database":"reachable"}
```

On start-up the api applies the EF Core migrations, which create the tables and seed the
mock data. If the database is down at that moment the api keeps running and reports `503`;
restart the api container once the database is back.

### Example flow

```bash
B=http://localhost:8080/api/v1
```

Dr. Chen asks for two laptops (10-day loan), one IDE key and 250 credits for CS501 A1:

```bash
curl -s -X POST $B/assets/tickets -H "Content-Type: application/json" -H "X-User-Id: 40000000-0000-4000-8000-000000000001" -d '{"courseId":"10000000-0000-4000-8000-000000000001","projectId":"30000000-0000-4000-8000-000000000001","items":[{"assetId":"50000000-0000-4000-8000-000000000002","quantity":2,"loanDays":10},{"assetId":"50000000-0000-4000-8000-000000000003","quantity":1},{"assetId":"50000000-0000-4000-8000-000000000004","amount":250.00}]}'
```

The IT director approves it (use the ticket `id` from the response):

```bash
curl -s -X PUT $B/assets/tickets/<ticket-id> -H "Content-Type: application/json" -H "X-User-Id: 40000000-0000-4000-8000-000000000005" -d '{"status":"approved"}'
```

The administrator allocates it. The response holds the issued key and the device tokens:

```bash
curl -s -X POST $B/assets/tickets/<ticket-id>/allocate -H "X-User-Id: 40000000-0000-4000-8000-000000000006"
```

Return one laptop, then close the ticket:

```bash
curl -s -X POST $B/assets/50000000-0000-4000-8000-000000000002/return -H "Content-Type: application/json" -H "X-User-Id: 40000000-0000-4000-8000-000000000006" -d '{"token":"<HW-token>"}'
```

```bash
curl -s -X PUT $B/assets/tickets/<ticket-id> -H "Content-Type: application/json" -H "X-User-Id: 40000000-0000-4000-8000-000000000006" -d '{"status":"closed"}'
```

`GET $B/assets/tickets/<ticket-id>` shows the events and timings.

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
dotnet run --project PA2/functional-codebase/src/ResourceHub.Api
```

Local runs use the `Development` environment, which reads the connection string from
`PA2/functional-codebase/src/ResourceHub.Api/appsettings.Development.json` (`localhost:5432`). The URL is printed on
start-up (default `http://localhost:5184/api/v1/health`).

## Build

```bash
dotnet restore ResourceHub.slnx --locked-mode
```

```bash
dotnet build ResourceHub.slnx --configuration Release --no-restore
```

## Test

The tests are API integration tests: they start the api in memory (`WebApplicationFactory`)
and call it over HTTP against a real PostgreSQL. Each test class creates its own database
(`rh_test_<guid>`, migrated and seeded on start-up) on the server named in `RESOURCEHUB_TEST_DB`
and drops it afterwards, so the user in that connection string needs permission to create
databases (the compose and CI `resourcehub` user has it).

Start the db container and point the tests at it:

```bash
docker compose up -d db
```

```bash
RESOURCEHUB_TEST_DB="Host=localhost;Port=5432;Database=resourcehub;Username=resourcehub;Password=resourcehub_dev" dotnet test ResourceHub.slnx
```

Without `RESOURCEHUB_TEST_DB`, only the `503` health test runs (against an unreachable
server) and every database test is reported as skipped. CI sets the variable and runs:

```bash
dotnet test ResourceHub.slnx --configuration Release --no-build --logger trx --collect "XPlat Code Coverage;Format=opencover"
```

## Database migrations

The schema lives in EF Core migrations under `src/ResourceHub.Api/Data/Migrations` and is
applied at start-up. After changing the model, add a migration with the local tool (no database
needed):

```bash
cd PA2/functional-codebase && dotnet tool restore
```

```bash
dotnet tool run dotnet-ef migrations add <Name> --project src/ResourceHub.Api --output-dir Data/Migrations
```

## Configuration

| Setting | Where | Notes |
|---|---|---|
| `ConnectionStrings:HubDb` | env `ConnectionStrings__HubDb` (compose) or `appsettings.Development.json` | Required; the app refuses to start without it |
| `RESOURCEHUB_TEST_DB` | env (tests only) | PostgreSQL server for the integration tests; tests needing it are skipped when unset |
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

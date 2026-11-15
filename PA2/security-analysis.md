# security-analysis.md — PA2 naive baseline

## AI Deliverable Metadata Block

| Item | Detail |
|---|---|
| Conceptual AI contribution | AI: _%_ / HUMAN: _%_ |
| My part | _to fill_ |
| AI part (Claude chat assistant) | _to fill_ |
| Design-aid prompts | `.ai_logs/2026-11-15_security-analysis-log.md` (in this folder) |

## 1. Scope

- **System analyzed:** the Domain 2 Resource Allocation & Help Desk Hub in `PA2/functional-codebase/` (C#/.NET 10 minimal API, EF Core, PostgreSQL 18, Docker Compose). Code as scanned at `4d39740`; later commits change file headers and line breaks only
- **Inputs:** `spec-domain2.md`; `sast-triage-report.md` (scan results, validated vulnerabilities V1–V3, candidates M1–M9, existing protections P1–P9); `evidence/manual-validation-2026-11-15.txt`
- _In and out of scope: to fill_

### 1.1 Confirmed vulnerabilities carried in from the triage

Validated on 2026-11-15 against the running compose stack. Full write-ups in `sast-triage-report.md` §4; none was flagged by Semgrep or SonarQube.

| # | Vulnerability | Layer | CWE |
|---|---|---|---|
| V1 | Caller identity is the `X-User-Id` header, accepted as given: anyone who knows a user id can act as that user, and the event log records the impersonated user as the actor | Authentication | CWE-290 |
| V2 | Ticket list and detail are open to anyone and return requester ids, notes and event actor ids, which supply the ids V1 needs; together V1 and V2 give anyone administrator access | Authorization / data exposure | CWE-862, CWE-200 |
| V3 | License keys stored in plain text and returned as-is by allocate and the allocations listing | Data storage | CWE-312 |

### 1.2 Candidates not yet confirmed

From the triage review and my design-time notes. CWE ids are first guesses.

| # | Candidate | Where | Likely CWE (check) |
|---|---|---|---|
| M4 | Dev database password in config, and as the compose default (`resourcehub_dev`) | `appsettings.Development.json`, `docker-compose.yml` | CWE-798 |
| M5 | Full device token written to the log on return | `Endpoints/AssetEndpoints.cs` L77 | CWE-532 |
| M6 | Floating image and runner tags (`sdk:10.0`, `aspnet:10.0`, `postgres:18`, `ubuntu-latest`) | `Dockerfile`, `docker-compose.yml`, `devsecops.yml` | CWE-1357 / CWE-829 |
| M7 | Plain HTTP on 8080; header and keys travel unencrypted | `Dockerfile`, `docker-compose.yml` | CWE-319 |
| M8 | 401 message tells the caller how to get through ("Send the id of a known user in the X-User-Id header.") | `Services/Actors.cs` L31 | CWE-209 |
| M9 | Possible missing null checks when building asset responses | `Services/AssetViews.cs` | CWE-476 |
| G1 | `AllowedHosts: "*"` accepts any Host header | `appsettings.json` | CWE-644 |
| G2 | Migrations run at startup, so the app's database user can change the schema | `Program.cs` | CWE-250 / CWE-272 |
| G3 | Asset list and detail open to anyone (counts only, no keys) | `Endpoints/AssetEndpoints.cs` | CWE-862 (low) |
| G4 | PostgreSQL port 5432 published to the host, not only to the API container | `docker-compose.yml` | CWE-668 |
| G5 | No rate limiting seen (carried from my PA1 notes; not checked against this code) | all endpoints | CWE-770 |

## 2. Security requirements (defensive baseline)

_Requirements the system should meet, each with an id (SR1…) so later sections can refer to them._

## 3. Abuse cases

_Malicious-user scenarios (AC1…): actor, goal, how, which requirement it breaks._

## 4. Attack trees

_At least three, one per chosen abuse case._

### 4.1 Attack tree: _AC?_

### 4.2 Attack tree: _AC?_

### 4.3 Attack tree: _AC?_

## 5. Attack surface map

Routes and caller rules read from the code (`Endpoints/*.cs`). "Any known user" means any id that exists in the users table, sent in `X-User-Id` (V1). _Risk ratings and highest-risk vectors: to fill._

### 5.1 HTTP entry points (port 8080, plain HTTP)

| Method and route | Caller rule in the code | What it takes in | What it returns | Related findings |
|---|---|---|---|---|
| `GET /api/v1/health` | none | — | status | |
| `POST /api/v1/assets` | Administrator | body | asset (counts, no keys) | V1 |
| `GET /api/v1/assets` | none | paging and filter query | asset list (counts, no keys) | G3 |
| `GET /api/v1/assets/{assetId}` | none | route id | asset (counts, no keys) | G3 |
| `PUT /api/v1/assets/{assetId}` | Administrator | route id, body | asset | V1 |
| `DELETE /api/v1/assets/{assetId}` | Administrator | route id | — | V1 |
| `POST /api/v1/assets/{assetId}/return` | Administrator | route id, device token in body | — | V1, M5 |
| `POST /api/v1/assets/tickets` | Course instructor or lab advisor | body | ticket | V1 |
| `GET /api/v1/assets/tickets` | none | paging and filter query | tickets with requester ids and notes | V2 |
| `GET /api/v1/assets/tickets/{ticketId}` | none | route id | ticket with requester id, notes and event actor ids | V2 |
| `PUT /api/v1/assets/tickets/{ticketId}` | any known user; contents: requester only; approve and reject: IT director; close: administrator | route id, body | ticket | V1 |
| `DELETE /api/v1/assets/tickets/{ticketId}` | requester or administrator | route id | — | V1 |
| `POST /api/v1/assets/tickets/{ticketId}/allocate` | Administrator | route id | allocation, including license keys and device tokens | V1, V3 |
| `GET /api/v1/assets/allocations` | any known user; results only for the course's instructor, the lab's advisor, the IT director or an administrator | course or lab id query | active allocations, including license keys and device tokens | V1, V3 |

Inputs common to all routes: the `X-User-Id` header (V1, M8), route ids (GUIDs, typed by the route), query values (page size capped at 100, P8), JSON bodies (validated, P4).

### 5.2 Other entry points

| Entry point | Detail | Related findings |
|---|---|---|
| PostgreSQL | Port 5432 published to the host; password from an environment variable with the default `resourcehub_dev` | G4, M4, V3 |
| Configuration | `appsettings.json`, `appsettings.Development.json`, compose environment | M4, G1 |
| Logs | Application log receives the full device token on return | M5 |
| Container images and CI | Floating base image and runner tags; the CI pipeline builds and scans the code | M6 |
| Startup | Migrations applied by the app at startup | G2 |

## 6. STRIDE threat model

### 6.1 Data flow diagram

_Diagram and trust boundaries: to draw._

Elements identified so far, from the code and compose file:
- **External entity:** API caller (any HTTP client; the role is whatever `X-User-Id` claims)
- **Processes:** API endpoints → services (identity stand-in `Actors`, validation, allocation, ticket transitions) → EF Core
- **Data stores:** PostgreSQL (users, assets, license keys, devices, tickets, ticket events, allocations); application log
- **Supporting:** container images, CI pipeline (build, tests, Semgrep, SonarQube, Trivy)

### 6.1a Existing controls (from the triage)

Added by the agent without being asked; full table in `sast-triage-report.md` §5. These go in the STRIDE matrix as controls already in place.

| # | Control | Where |
|---|---|---|
| P1 | Identity stand-in: 401 for unknown callers, 403 for the wrong role, per-role rules on writes (not real authentication, see V1) | `Services/Actors.cs`, endpoint files |
| P2 | The agent labelled its own identity check as not real authentication | `Services/Actors.cs`, `PA2/README.md` |
| P3 | Device tokens from a cryptographically secure random generator | `Services/AllocationService.cs` |
| P4 | Request body validation, with tests | `Services/Validation.cs` |
| P5 | Allocations listing limited by role and course or lab | `Endpoints/AllocationEndpoints.cs` |
| P6 | Unissued license keys never returned (no key field in asset responses) | `Services/AssetViews.cs`, `Contracts/AssetContracts.cs` |
| P7 | Parameterized SQL for row locks; all-or-nothing allocation with conditional stock updates | `Services/AllocationService.cs` and endpoints |
| P8 | Page size capped at 100 | `Services/Query.cs` |
| P9 | Errors as problem details, no stack traces | `Program.cs` |

### 6.2 STRIDE matrix

_Every DFD element against S, T, R, I, D, E._

## 7. Proposed mitigations

_A technical plan for every vulnerability and threat above, traced back to its id._

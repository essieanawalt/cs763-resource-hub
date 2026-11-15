# spec-domain2.md — Resource Allocation & Help Desk Hub (Domain 2)
> this spec was built from updated, PA2 requirements

## AI Usage Log

| Item | Detail |
|---|---|
| Conceptual AI contribution | ~35% AI / ~65% me |
| My part | Copied the standardized Domain 2 text (section 5) unchanged; walked through each requirement and endpoint and made every interpretation decision in section 6; directed which PA1 spec sections to carry over (overview, tech stack, deployment, layout, roles, out of scope) and how to adapt them |
| AI part (Claude chat assistant) | Listed open questions and options for each requirement and endpoint; drafted the wording of section 6 and the mock data from my decisions; adapted the carried-over PA1 sections to the standardized spec |
| Design-aid prompts | `.ai_logs/2026-11-14_spec-walkthrough-log.md` (in this folder) |

## 1. Overview

The Resource Allocation & Help Desk Hub is one backend service of the Project Portal (PP). It manages a pool of physical lab equipment, software license keys and cloud credit vouchers, and handles requests for them from courses and research labs through a help desk ticket flow: request, sign-off, allocation, and for physical devices, return.

The hub runs in its own containers, owns its own database and makes no calls to other Project Portal services. Data owned elsewhere (courses, labs, projects, users) comes from static mock data (section 7).

Section 5 is the standardized specification for this service, copied unchanged. Section 6 clarifies the parts of it that are open to more than one reading.

## 2. Tech stack and deployment

- C# / ASP.NET Core Web API (.NET 10)
- PostgreSQL 18
- Entity Framework Core (Npgsql provider)
- Docker + Docker Compose
- JSON REST API under `/api/v1`

The service runs with the existing `Dockerfile` and `docker-compose.yml` at the repository root: an `api` container (ASP.NET Core, port 8080) and a `db` container (PostgreSQL 18). The api container receives the database connection string as `ConnectionStrings__HubDb` (configuration key `ConnectionStrings:HubDb`). In the CI pipeline, tests that need the database receive a connection string in the `RESOURCEHUB_TEST_DB` environment variable.

The service also provides `GET /api/v1/health`, which returns 200 when the database is reachable and 503 when it is not.

## 3. Repository layout

- Application code: project `ResourceHub.Api` in `PA2/functional-codebase/src/ResourceHub.Api/`
- Tests: project `ResourceHub.Tests` in `PA2/functional-codebase/tests/ResourceHub.Tests/`
- Build and run files (`ResourceHub.slnx`, `Dockerfile`, `docker-compose.yml`, `global.json`, `Directory.Build.props`) stay at the repository root and expect these project names and paths. `Directory.Build.props` turns on NuGet lock files (`packages.lock.json`), which the Dockerfile and pipeline restore in locked mode

## 4. Roles

| Role | What they do in this service |
|---|---|
| Course instructor | Submits resource request tickets for their course |
| Research lab faculty advisor | Submits resource request tickets for their research lab |
| IT director | Signs off on tickets (approves or rejects) |
| Administrator | Manages the asset catalog and runs the allocation that issues keys, vouchers and hardware |

## 5. Standardized specification (verbatim)

### Domain #2 The Resource Allocation & Help Desk Hub:

This service handles the distribution of critical project assets, logging tracking parameters, software license keys, and cloud credit vouchers across active course groups and research labs.

**Functional Requirements:**

1. Asset Lifecycle & Inventory Allocation: Programmatically manage and monitor a pool of physical lab equipment (hardware units, laptops, IoT nodes), software license keys, and cloud credit vouchers. Each unique asset must be assigned a UUID, name, category, total stock, and availability metrics. Assets are allocated directly to an overarching academic course or research lab.
2. Polymorphic Balance and Status Tracking: Process transactional balance logic for heterogeneous assets. For cloud vouchers, it must manage strict numeric deduction checks (preventing balances from dropping below zero) and log high-value alphanumeric license tokens. For physical items, it must calculate active check-out duration tracking rules and maintain status flags (available, reserved, checkedout, overdue).
3. Help Desk Ticketing Pipeline: Process incoming resource requests from course instructor or research lab faculty advisor. The service must parse multi-tiered requests, track ticket lifecycles (pending, approved, rejected, allocated, closed), update project asset allocations dynamically upon IT director sign-off, and output comprehensive logging metrics.
4. Explicit Inventory Allocation Engine: The system must decouple administrative ticket review from physical inventory modification. Inventory values must only alter when a separate, dedicated allocation execution command is processed by an administrator, triggering atomic database deductions and issuing keys or hardware assignment tokens.
5. Complex Multi-Field Query: Support rich administrative and group queries. Users can search and filter the asset catalog and ticket logs simultaneously by status, asset category, requesting research lab or course.

**REST APIs**

| Endpoints | Verbs | Description |
|---|---|---|
| `/api/v1/assets` | POST, GET | Add an inventory asset item. Supplying multi-field search query parameters allows administrators and teams to filter active inventory listings by department program or availability state. |
| `/api/v1/assets/{asset_id}` | GET, PUT, DELETE | Read, update, and delete an inventory asset item. |
| `/api/v1/assets/{asset_id}/return` | POST | Processes the return of a checked-out physical device, increments the pool's remaining capacity stock, and records the return timestamp. |
| `/api/v1/assets/allocations` | GET | Read all active resource allocations, checked-out inventory metrics, and assigned software keys for a specific requesting research lab or course. |
| `/api/v1/assets/tickets` | POST, GET | Submit a new help desk resource request ticket or read a listing of all active requests filtered dynamically by status (pending/approved/allocated) or course section or project. |
| `/api/v1/assets/tickets/{ticket_id}` | PUT, DELETE, GET | Update a ticket's contents, delete a pending request, or fetch specific ticket metrics. A PUT request targeting this route handles the operational approval state transition. |
| `/api/v1/assets/tickets/{ticket_id}/allocate` | POST | Executes the real physical database mutation. This endpoint processes an approved ticket, atomically subtracts items from the main asset pool inventory, and establishes the active user allocation record. |

## 6. Clarifications

Where section 5 is open to more than one reading, build to these.

1. **Kinds of asset:** each asset has a **type** (`physical`, `license`, `voucher`), which decides how its balance and status work, and a **category** (for example laptop, IoT node, IDE license, cloud credits), which is a descriptive label used for searching.
2. **Asset data:** UUID, name, type, category, home department program, total stock and availability. By type: a physical asset has a number of units and a default loan period in days; a license asset holds its license keys; a voucher asset holds a credit balance.
3. **Availability:** physical: number of units available, plus counts by status flag. License: keys not yet issued. Voucher: remaining balance. The availability filter on `GET /api/v1/assets` uses the status flags in requirement 2; license and voucher assets count as available while some stock or balance remains.
4. **When stock changes:** total stock is set when an asset is added or updated. Remaining stock and balances change only when a ticket is allocated (`POST .../allocate`) or a physical device is returned (`POST .../return`). The below-zero check in requirement 2 is made at allocation.
5. **License keys:** keys are stored with their asset, and each issued key is recorded with the ticket it was issued for and the time it was issued.
6. **Check-out duration:** a physical device's due date is its allocation date plus the loan period. A ticket may give a loan period for each physical asset it requests; otherwise the asset's default applies. A device is overdue once its due date has passed and it has not been returned.
7. **Ticket content:** a requester (a course instructor or a research lab faculty advisor), the course or the research lab the request is for, an optional project id, and one or more requested assets, each with a quantity (physical and license assets) or an amount (voucher assets). "Multi-tiered" means one ticket can request several assets.
8. **Ticket changes:** a ticket's contents can be updated, and the ticket deleted, only while it is `pending`. A PUT on `/api/v1/assets/tickets/{ticket_id}` also changes its state: `pending` to `approved` or `rejected` (IT director sign-off), and `allocated` to `closed`. Only `POST .../allocate` moves a ticket from `approved` to `allocated`.
9. **Not enough stock:** allocation is all or nothing. If any requested asset does not have enough stock or balance, nothing is allocated and the ticket stays `approved`, so allocation can be run again later.
10. **What allocation issues:** license: keys from the asset's pool. Voucher: the requested amount, deducted from the balance. Physical: one hardware assignment token for each device handed out, with its due date.
11. **Return:** one device per call, identified by its hardware assignment token. The return adds one to the asset's remaining stock and records the return time.
12. **Active allocations:** physical devices not yet returned, and license and voucher allocations whose ticket is not `closed`.
13. **Logging metrics:** an event is recorded for every ticket state change and every allocation attempt, including attempts that fail for lack of stock. `GET /api/v1/assets/tickets/{ticket_id}` returns the ticket with its events and timings (for example time from submission to approval and from approval to allocation).
14. **Filters:** query parameters can be combined. Assets: category, department program, availability. Tickets: status, asset category, course, research lab, project. Listings are paged and sorted newest first by creation date by default.
15. **Mock data:** courses (each including its section), research labs, projects, users and a few starting assets are seeded as static mock data with fixed ids, listed below, so tests and manual calls can use them. Each user has one of the four roles named in section 4: course instructor, research lab faculty advisor, IT director, administrator. No other service is called.

## 7. Mock data

The ids below are fixed so tests and manual calls can use them.

**Courses**

| id | Course and section | Department program |
|---|---|---|
| `10000000-0000-4000-8000-000000000001` | CS501 A1 — Software Engineering | Computer Science |
| `10000000-0000-4000-8000-000000000002` | EE580 B1 — Embedded Systems Design | Electrical Engineering |

**Research labs**

| id | Lab | Department program |
|---|---|---|
| `20000000-0000-4000-8000-000000000001` | Data Science Lab | Computer Science |
| `20000000-0000-4000-8000-000000000002` | Embedded Systems Lab | Electrical Engineering |

**Projects**

| id | Project | Course or lab |
|---|---|---|
| `30000000-0000-4000-8000-000000000001` | Course Analytics Dashboard | CS501 A1 |
| `30000000-0000-4000-8000-000000000002` | Smart Parking Prototype | Embedded Systems Lab |

**Users**

| id | Name | Role | Course or lab |
|---|---|---|---|
| `40000000-0000-4000-8000-000000000001` | Dr. Maya Chen | Course instructor | CS501 A1 |
| `40000000-0000-4000-8000-000000000002` | Prof. Daniel Okafor | Course instructor | EE580 B1 |
| `40000000-0000-4000-8000-000000000003` | Dr. Lucas Ferreira | Research lab faculty advisor | Data Science Lab |
| `40000000-0000-4000-8000-000000000004` | Dr. Priya Raman | Research lab faculty advisor | Embedded Systems Lab |
| `40000000-0000-4000-8000-000000000005` | Jordan Blake | IT director | — |
| `40000000-0000-4000-8000-000000000006` | Sam Rivera | Administrator | — |

**Starting assets**

| id | Name | Type | Category | Department program | Stock |
|---|---|---|---|---|---|
| `50000000-0000-4000-8000-000000000001` | Raspberry Pi 5 kit | physical | IoT node | Electrical Engineering | 10 units, default loan period 14 days |
| `50000000-0000-4000-8000-000000000002` | Dell Latitude 7450 laptop | physical | Laptop | Computer Science | 5 units, default loan period 30 days |
| `50000000-0000-4000-8000-000000000003` | JetBrains IDE license | license | IDE license | Computer Science | 3 keys: `JBIDE-7K2M-Q9XA`, `JBIDE-3PLR-8WVN`, `JBIDE-M4TD-2HCE` |
| `50000000-0000-4000-8000-000000000004` | Cloud credit pool | voucher | Cloud credits | Computer Science | balance 5000.00 |

## 8. Out of scope (this version)

- Calls to other Project Portal services
- Email or other notifications
- A user interface
- Expiry dates for license keys and cloud credit vouchers

# security-analysis.md — PA2 naive baseline

## AI Deliverable Metadata Block

| Item | Detail |
|---|---|
| Conceptual AI contribution | AI: _%_ / HUMAN: _%_ (to set once sections 3–7 are written) |
| My part | Wrote the security requirements from the spec's business rules, including the review gaps, and chose to judge each on its own logic; checked every CWE against MITRE CWE 5.0; ran every test and check against the stack (`\du`, the network login, the requirement runs, the abuse case checks); decided which findings were confirmed and promoted (V4, V5, V6) and asked for M9 to be closed; asked for risk ratings and for data minimization, deny by default, the event log in the database and response headers to be explored; raised hoarding and asked to add it; decided on the renumbering; wrote AC1–AC6 and the coverage tables, and asked for AC7; caught the SR9 contradiction and proposed the fix; wrote the attack trees; found the SR11 scope gap and set out the two options, then chose to widen it; caught stale and broken references and the assistant overstating its part |
| AI part (Claude chat assistant) | Section skeleton; carried in the triage findings and protections; attack-surface routes read from the code; reviews of the requirements, abuse cases and attack trees against the code and evidence; test scripts and check commands; status updates from the results; V4–V6 write-ups (suggested testing the V6 chain); risk-rating scale and ratings; drafts of SR14, SR16, SR18 and SR7 (fair share); AC7 draft; renumbering; tree layout fixes; SR11 rewrite after she widened it |
| Design-aid prompts | `.ai_logs/2026-11-15_security-analysis-log.md` (in this folder) |

## 1. Scope

- **System analyzed:** the Domain 2 Resource Allocation & Help Desk Hub in `PA2/functional-codebase/` (C#/.NET 10 minimal API, EF Core, PostgreSQL 18, Docker Compose). Code as scanned at `4d39740`; later commits change file headers and line breaks only
- **Inputs:** `spec-domain2.md`; `sast-triage-report.md` (scan results, validated vulnerabilities V1–V6, candidates, existing protections P1–P10); evidence in `evidence/`: `manual-validation-2026-11-15.txt`, `sr-validation-2026-11-15.txt`, `sr-validation-2-2026-11-15.txt`, `db-role-check-2026-11-15.txt`
- _In and out of scope: to fill_

### 1.1 Confirmed vulnerabilities

Validated on 2026-11-15 against the running compose stack. V1–V3 are carried in from the triage (full write-ups in `sast-triage-report.md` §4); none was flagged by Semgrep or SonarQube. V4 and V5 were confirmed while testing the security requirements in section 2 (`evidence/sr-validation-2026-11-15.txt`), and V6 by chaining three candidates (`evidence/db-role-check-2026-11-15.txt`).

| # | Vulnerability | Layer | CWE | Risk |
|---|---|---|---|---|
| V1 | Caller identity is the `X-User-Id` header, accepted as given: anyone who knows a user id can act as that user, and the event log records the impersonated user as the actor | Authentication | CWE-290 | High |
| V2 | Ticket list and detail are open to anyone and return requester ids, notes and event actor ids, which supply the ids V1 needs; together V1 and V2 give anyone administrator access | Authorization / data exposure | CWE-862 | High |
| V3 | License keys stored in plain text and returned as-is by allocate and the allocations listing | Data storage | CWE-312 | High |
| V4 | Ticket event history deleted with the ticket: deleting a pending ticket through the API removed its submission event (0 left), because events cascade-delete with their ticket (`Data/HubDbContext.cs` L114). Combined with V1, anyone can erase the record that a request was made (was G6; SR11) | Audit / database | CWE-221 (a Class; its nearest Base child, CWE-223, covers not recording an event, not deleting one already recorded) | Medium |
| V5 | Full device token written to the application log on return (`Endpoints/AssetEndpoints.cs` L77). The token is all `/return` needs, so anyone who can read the logs holds working tokens. SonarQube flagged the same line only for logging performance (CA1873, triage S1), not for the token (was M5; SR22) | Logging | CWE-532 | Medium |
| V6 | Database open to the network as superuser with the committed password: no `.env` exists, so the stack uses the compose fallback password `resourcehub_dev` (was M4, also committed in `appsettings.Development.json`); port 5432 is published on all host interfaces (was G4); and that user is a superuser (was G2). A separate container outside the compose network logged in with it and got `usesuper = t`. Full control of the data without the API: every license key (V3), the event log (SR11) and stock. Tested through the host, not from a second machine. G2 and G4 come from the PA1 compose file, not the PA2 build | Deployment / database | CWE-1392 (default password), with CWE-1327 (unrestricted bind) and CWE-250 (excess privilege); CWE-260 for the copy in the config file | High |

**How risk is rated:** impact if exploited, weighed against how easy it is to reach.
- **High:** anyone who can reach the API or the database can do it with no special access, and it exposes secrets or gives control (V1, V2, V3, V6). V6 is rated for the stack as it runs; it drops if the database port is closed in deployment
- **Medium:** real damage, but it needs some access first (log access for V5, the requester's identity or V1 for V4), or it weakens the system without being an attack on its own (M6, M7, G5)
- **Low:** little to gain on its own (M8 only repeats what V1 already allows; G3 shows counts, no keys)

### 1.2 Candidates not yet confirmed

From the triage review and my design-time notes. CWE ids checked against MITRE CWE 5.0 (released 2026-10-28) on 2026-11-15.

| # | Candidate | Where | CWE | Risk |
|---|---|---|---|---|
| M6 | Floating image and runner tags (`sdk:10.0`, `aspnet:10.0`, `postgres:18`, `ubuntu-latest`) | `Dockerfile`, `docker-compose.yml`, `devsecops.yml` | CWE-494 (secondary: CWE-1357) | Medium |
| M7 | Plain HTTP on 8080; header and keys travel unencrypted | `Dockerfile`, `docker-compose.yml` | CWE-319 | Medium |
| M8 | 401 message tells the caller how to get through ("Send the id of a known user in the X-User-Id header.") | `Services/Actors.cs` L31 | CWE-210 | Low |
| G3 | Asset list and detail open to anyone (counts only, no keys) | `Endpoints/AssetEndpoints.cs` | CWE-862 (low) | Low |
| G5 | No rate limiting: nothing in the source configures a rate limiter, and 60 fast requests all got 200 (carried from my PA1 notes, confirmed 2026-11-15, `evidence/sr-validation-2026-11-15.txt`, SR22) | all endpoints | CWE-770 | Medium |

**Reviewed, not a finding:** G1, `AllowedHosts: "*"` in `appsettings.json`. It's the ASP.NET template default, and nothing in the code reads the Host header or builds a link or redirect from it, so there's nothing to exploit here.

**Reviewed, not a finding:** M9, possible missing null checks in `Services/AssetViews.cs`. Every nullable stock and balance value is read with a default (`?? 0`, L58–L74), and the database has check constraints that keep stock and balances within range (`Data/HubDbContext.cs` L72–75), so there's no null to dereference.

## 2. Security requirements (defensive baseline)

Each requirement says what the system must guarantee, whatever the current code does. The findings are the evidence of whether it is met. Group A comes from the business rules in `spec-domain2.md`, reshaped to hold even when someone is trying to break them, plus one gap I found in review (SR10), and follows a ticket's journey from request to return. Group B covers the controls everything else depends on, ordered by layer; the spec leaves security out on purpose, so their sources are the assets they protect.

Each requirement is judged on its own logic, assuming the requirements it depends on hold; a failed dependency (most often SR12, through V1) is carried by the "Depends on" line, not repeated in every status.

**Read SR12 first.** Authentication is listed in group B with the other cross-cutting controls, but almost every other requirement depends on it.

**Numbering:** renumbered on 2026-11-15 to group the requirements by topic. The two evidence runs label their steps with the earlier numbers, so where a status cites "`evidence/sr-validation-2026-11-15.txt`, SRn", SRn is that run's step label. Earlier → current: 7→8, 8→9, 9→10, 10→11, 11→12, 12→13, 13→15, 14→17, 15→19, 16→20, 17→21, 18→22, 19→23, 20→24, 21→25, 22→26, 23→16, 24→14, 25→18, 26→7 (1–6 unchanged).

Status values: **Met (tested)**, **Met (review)** (read in the code, not yet run), **Partly met**, **Not met**, **Not checked**, each with its evidence (a V, M, G or P id, a code location, or a test).

### 2.1 Group A: business rules

#### A1 Requests and approval

**SR1 — Authorization: request scope.** A course instructor shall be able to submit tickets only for a course they teach, and a research lab faculty advisor only for their own lab.
- Source: spec clarification 7, roles in section 4, mock users in section 7
- Depends on: SR12
- How to verify: as the CS501 instructor, submit a ticket for EE580
- Status: **Met (tested).** Dr. Chen submitting for EE580 got a 403 (`evidence/sr-validation-2026-11-15.txt`, SR1). Enforced in `Services/TicketContent.cs` L129–135. SR13 is the general form of this rule

**SR2 — Integrity: ticket state machine.** The server shall allow only these transitions, each by its role: pending → approved or rejected (IT director), approved → allocated (administrator, through allocate only), allocated → closed (administrator). Content changes and deletes shall be allowed only while the ticket is pending. A ticket shall be allocated at most once, and any other transition shall be rejected and logged as SR11 describes.
- Source: spec requirement 3, clarification 8
- Depends on: SR12
- How to verify: allocate a rejected ticket; allocate the same ticket twice; edit an approved ticket
- Status: **Partly met.** Tested: editing an approved ticket got a 409, a wrong-role approval a 403, and a second allocate a 409 (`evidence/sr-validation-2026-11-15.txt`, SR2 and SR3). Not met: the refused approval left no event (only the submission event is in `ticket_events`) and nothing in the application log (SR22). Only failed allocations are recorded (`Services/AllocationService.cs` L52, L65). In practice the role checks also fail through SR12 (V1)

#### A2 Allocation

**SR3 — Integrity: all-or-nothing allocation.** An allocation shall either complete fully (stock and balances reduced, keys and tokens issued, allocation recorded) or change nothing, leaving the ticket approved. Each license key shall be issued to at most one allocation.
- Source: spec requirement 4, clarifications 9, 10
- How to verify: existing concurrent allocation test (P7); an allocation where one requested asset is short
- Status: **Met (tested).** A second allocate of the same ticket got a 409 (`evidence/sr-validation-2026-11-15.txt`, SR3). One key per allocation: allocate takes only unissued keys while holding a lock on the asset row (`Services/AllocationService.cs` L120); the key table shows each issued key on one ticket and allocation. This run didn't make two allocations compete for the same keys, so that part rests on the code review and the P7 test

**SR4 — Integrity: voucher balance.** At allocation, the server shall reject any deduction that would take a voucher balance below zero, including when two allocations arrive at the same time. The check and the deduction shall happen in one transaction, and a rejected attempt shall be logged as SR11 describes.
- Source: spec requirement 2, clarification 4
- Depends on: SR12 (who can run allocate)
- How to verify: two concurrent allocations that together exceed the balance; one succeeds, one is rejected, balance never below zero
- Status: **Met (tested).** Two simultaneous allocations of 3000.00 against a balance of 5000.00: one 200, one 409 with the shortfall, balance left at 2000.00 (`evidence/sr-validation-2026-11-15.txt`, SR4)

**SR5 — Integrity: single path to remaining stock.** Remaining stock and balances shall never go negative or above total. Apart from the recalculation when total stock changes, they shall change only through allocate (decrease) and return (increase), and only when run by an authenticated administrator. Changing total stock through `PUT /api/v1/assets/{asset_id}` shall never leave remaining stock negative or above total.
- Source: spec requirement 4, clarification 4
- Depends on: SR12
- How to verify: lower total stock below what is already handed out, then read the asset
- Status: **Met (tested).** Lowering laptop total stock to 1 with 2 out got a 409 (`evidence/sr-validation-2026-11-15.txt`, SR5). The code also refuses lowering a voucher total below what is used and removing issued keys (`Endpoints/AssetEndpoints.cs` L259–263, L278–282); those two weren't run

**SR6 — Accountability: issued secrets recorded.** The service shall record which license key or hardware token went to which ticket and when. Keeping the values themselves out of the application log is SR22.
- Source: spec requirement 2 ("log high-value alphanumeric license tokens"), clarification 5
- How to verify: allocate, then query the issued keys and devices for the ticket id and time
- Status: **Met (tested).** The issued key shows its ticket, allocation and issue time, and each device its ticket and check-out time (`evidence/sr-validation-2026-11-15.txt`, SR3 / SR6). The spec wording asks for the tokens to be logged, which reads as the weakness itself; I took clarification 5 as the safe reading, recording the issuance and not the value

**SR7 — Availability: fair share of stock.** One requester, or one department program, shall not be able to tie up a disproportionate share of an asset through the normal request flow. Limits on quantity per ticket, loan length and open tickets per requester or department shall be set by policy and enforced by the server, not left to the approver alone.
- Source: the hoarding abuse case (section 3). The spec sets no such limits, so the actual numbers are a policy decision; this requirement only says they must exist and be enforced
- Depends on: SR12 (otherwise a limit per requester means nothing)
- How to verify: as one instructor, ask for more than the stock on a 365-day loan, and open several tickets; expect a refusal past the limit
- Status: **Not met** (tested). Dr. Chen's ticket for 1000 Raspberry Pi kits (stock 10) on a 365-day loan plus all three license keys was accepted (201) while she had other tickets pending; the probe ticket was then deleted (`evidence/sr-validation-2-2026-11-15.txt`). The only caps are per ticket (20 items, 1000 each, 365 days), not per requester or department

#### A3 Devices and returns

**SR8 — Integrity: status flags and due dates.** The server shall work out each device's status (available, reserved, checkedout, overdue) and due date from the allocation date and loan period. A caller shall not be able to set either directly.
- Source: spec requirement 2, clarification 6
- How to verify: send a status or due date in an update body and check that it is ignored or rejected
- Status: **Met (tested).** An update carrying `status: overdue` and `remainingStock: 999` returned 200 with remaining stock unchanged at 3 and the device status unchanged (`evidence/sr-validation-2026-11-15.txt`, SR7 / SR16). The extra fields are silently ignored, not rejected. SR20 is the general form of this rule

**SR9 — Integrity: returns.** A hardware assignment token shall be accepted for return only while its device is checked out, and only once. A return shall never push remaining stock above total.
- Source: spec clarifications 6, 11
- Depends on: SR12
- How to verify: return the same token twice; return a made-up token
- Status: **Met (tested).** Returning a token got a 200, the same token again a 409, and a made-up token a 404 (`evidence/sr-validation-2026-11-15.txt`, SR8)

#### A4 Catalog and records

**SR10 — Integrity: asset deletion.** The server shall reject deleting an asset that any ticket or allocation refers to, so active allocations, issued keys and tokens, and their history stay intact.
- Source: a gap I found in review, not from the spec. The spec allows `DELETE /api/v1/assets/{asset_id}` but doesn't say what happens to an asset that is in use, so this is my own clarification
- Depends on: SR12 (only an administrator can delete)
- How to verify: delete an asset with a checked-out device or an issued key and expect a 409; delete an unused asset and expect a 204
- Status: **Met (tested).** Deleting the laptop while in use got a 409; an unused asset was deleted with a 204 (`evidence/sr-validation-2026-11-15.txt`, SR9). The agent added this without being asked (P10)

**SR11 — Accountability: trustworthy event log.** Every change to tickets, stock and devices shall be recorded with the verified identity of the caller who made it, the time and the outcome: ticket submissions, edits and state changes, every allocation attempt (failed ones included), device returns, and creating, updating or deleting an asset. The log shall be append-only, both through the API and in the database: the service's database user can add events but not change or delete them. Other requirements that say "logged" mean this.
- Source: spec requirement 3 and clarification 13, which cover tickets and allocation attempts. I widened it to returns and asset changes because those change stock and devices just as much, and AC4 and AC7 rely on them leaving no record
- Depends on: SR12
- How to verify: make a change, read the ticket's events, check the actor; return a device and change an asset, then look for an event; try to edit or delete an event through the API and in the database
- Status: **Not met**, for three separate reasons:
  - **Scope:** returns and asset create, update and delete record no event, only an application log line. There's no event type for them (`TicketEventType` in `Domain/Entities.cs` has only Submitted, Updated, Approved, Rejected, AllocationSucceeded, AllocationFailed, Closed), and events are written only in `Endpoints/TicketEndpoints.cs` (L97, L168, L297) and `Services/AllocationService.cs` (L52, L65, L149). Met (review) for what clarification 13 asks: ticket changes and allocation attempts are recorded with actor and time
  - **Append-only through the API:** deleting pending ticket T3 removed its submission event from `ticket_events` (V4; 0 left; `evidence/sr-validation-2026-11-15.txt`, SR10), because events cascade-delete with their ticket (`Data/HubDbContext.cs` L114)
  - **Append-only in the database:** through V6, deleting 6 events succeeded inside a transaction that was then rolled back (`evidence/sr-validation-2-2026-11-15.txt`). The update in the same test matched no rows because it used an event type that doesn't exist (`Allocated` instead of `AllocationSucceeded`), so only deletion is shown

  The actor is also only as trustworthy as SR12, which fails (V1)

**Gaps found in review, to decide later.** These aren't from the spec. Most came from asking whether everything the service hands out has a way to close, return or end. They are business-process gaps more than security requirements, but a record that never closes is also an audit and availability problem, so I'm keeping them here to decide on:
- **Lost devices:** a physical device that never comes back stays overdue forever. There's no way to write it off and adjust total stock
- **Leaked license keys:** an issued key is never returned to the pool, and expiry is out of scope (spec section 8), but there's also no way to revoke or replace a key if it leaks (V3). Revocation is a different thing from expiry, so this one leans toward a security requirement
- **Approved tickets that can't be filled:** a ticket short on stock stays approved so allocation can run later (clarification 9), but clarification 8 gives no way to cancel or close it, so it can stay approved forever
- **Separation of duties:** the user who submits a ticket shouldn't be the one who approves it. The spec doesn't ask for it and no mock user holds two roles, so nothing can break it today. I'm keeping it here rather than as a requirement with a status. The wider version is that an administrator can allocate, return and delete with no second person involved (AC7)
- **Returns trust the token alone:** a return is accepted for any valid token sent by an administrator, with nothing confirming the device came back. SR9 makes each token work only once, but doesn't cover this (AC4, AC7)

### 2.2 Group B: cross-cutting controls

#### B1 Identity and access

**SR12 — Authentication.** Every request other than health shall carry proof of identity that the caller can't forge and that expires. A header value the caller picks doesn't count.
- Source: spec section 4 roles; every role rule in group A depends on knowing who the caller is
- How to verify: send a request with another user's id and no other proof; send an expired proof
- Status: **Not met** (V1). The `X-User-Id` header also never expires, so even a caller who got it legitimately keeps it forever

**SR13 — Authorization: by role and by object on every route.** SR1 is the create-time case of this rule. Every route shall check both the caller's role and whether the record belongs to them: a requester sees and changes only their own tickets, and allocations are shown only to the course's instructor, the lab's advisor, the IT director or an administrator. Reads are covered as well as writes.
- Source: protects ticket contents and user ids (V2), the asset catalog (G3) and allocations (P5)
- Depends on: SR12
- How to verify: with no header, and as a different requester, read and edit someone else's ticket
- Status: **Not met.** Ticket reads are open to anyone (V2) and asset reads too (G3). Writes and the allocations listing have role and ownership checks (P1, P5), but they rest on V1

**SR14 — Authorization: deny by default.** A route shall be closed to every caller until it has an explicit caller rule. Only the health check is public on purpose.
- Source: V2 and G3 exist because reads were left open; nothing makes a new route closed by default
- Depends on: SR12
- How to verify: call every route with no header and list which ones answer
- Status: **Not met** (tested). With no header, five read routes answered 200: health (intended), asset list and detail (G3), ticket list and detail (V2); only the allocations listing refused (401) (`evidence/sr-validation-2-2026-11-15.txt`). Caller rules are added route by route (`Actors.RequireAsync`), with no default policy

#### B2 Protecting data

**SR15 — Confidentiality: license keys and tokens.** License keys and hardware tokens shall be protected at rest, so a database read doesn't expose them, and returned only to callers SR13 allows.
- Source: protects license keys and hardware tokens, the highest-value data in the service
- Depends on: SR12, SR13
- How to verify: query the `license_keys` table directly; search the database volume's files on the host for a known key (`docker-compose.yml` L11, L31; section 5.2); read allocations as a caller who shouldn't see them
- Status: **Not met.** Keys are stored and returned in plain text (V3), and anyone using V1 can read them. Reading the database volume's files directly found a known key in two table data files and in the write-ahead log (`evidence/abuse-case-checks-2026-11-15.txt`, check 3), so the keys are exposed at rest even without the port or a password

**SR16 — Confidentiality: data minimization.** Each response shall carry only what that caller needs for that route. User ids of other people, notes and event actor ids shall not be returned to callers who don't need them, and secrets (SR15) only on the routes that issue them.
- Source: V2 shows that even a correct authorization check (SR13) leaves this open: the ticket responses hand out the ids that V1 needs
- Depends on: SR13
- How to verify: read a ticket and the ticket list and list every user id in the response
- Status: **Not met** (tested; V2). With no header, one ticket's events handed out the ids of the requester, the IT director and the administrator (`evidence/sr-validation-2-2026-11-15.txt`), exactly the ids V1 needs

**SR17 — Confidentiality: transport.** All traffic to the API shall be encrypted in transit, so the identity proof, license keys and tokens can't be read on the network.
- Source: protects the identity proof (SR12) and the secrets in SR15 while they travel
- How to verify: call the API over plain HTTP and check it's refused or redirected
- Status: **Not met.** The API serves plain HTTP on 8080 (M7)

**SR18 — Response headers.** Responses that carry secrets shall tell clients and proxies not to store them, and every response shall declare its content type and stop the client guessing it.
- Source: protects the keys and tokens in SR15 after they leave the API
- How to verify: read the headers on the allocate and allocations responses
- Status: **Not met** (tested). The allocations listing, which returns license keys and device tokens, came back with only `Content-Type`, `Date`, `Server: Kestrel` and `Transfer-Encoding`: no `Cache-Control: no-store` and no `X-Content-Type-Options: nosniff`, and the `Server` header names the web server (`evidence/sr-validation-2-2026-11-15.txt`)

#### B3 Input

**SR19 — Input handling.** Every request body, query value and route id shall be checked on the server before use, and database queries shall stay parameterized.
- Source: protects data integrity and the database
- How to verify: existing validation tests; send malformed bodies and SQL-like strings in filters
- Status: **Met (tested).** Broken JSON, wrong types, a negative quantity, over-long notes, an unknown status, an empty body and a non-GUID route id all got 400 or 404, and a text body 415; none returned a stack trace (`evidence/sr-validation-2-2026-11-15.txt`). Search text containing `%`, `_` or `' OR 1=1--` matched nothing, so it isn't treated as a pattern or as SQL. Body validation also has tests (P4), and queries are parameterized (P7)

**SR20 — Request binding.** A request body shall only be able to set the fields that route is meant to change, never stock, actor ids or server-worked-out values. SR8 is the device case of this rule.
- Source: protects server-owned fields
- How to verify: add `remainingUnits` or `requesterId` to an update body and check they're ignored or rejected
- Status: **Met (tested).** A ticket submitted with `requesterId` set to the administrator was saved with Dr. Chen as requester, and extra fields on an asset update changed nothing (`evidence/sr-validation-2026-11-15.txt`, SR16 and SR7 / SR16). Bodies bind to fixed request types, and unknown fields are silently ignored rather than rejected. `TicketRequest`'s `Status` field is how the transitions in SR2 are requested, so it's meant to be there

#### B4 Errors and logging

**SR21 — Error messages.** Errors shall tell the caller what went wrong without revealing internals, other users' data or how to get past a check.
- Source: keeps error responses from helping an attacker
- How to verify: trigger 400, 401, 403, 404, 409 and a server error, and read each body
- Status: **Partly met.** Every malformed request above came back as problem details with no stack trace (P9; `evidence/sr-validation-2-2026-11-15.txt`), but the 401 tells the caller exactly which header to send (M8, shown again in the same run)

**SR22 — Security logging.** Refused and failed attempts (401, 403, 409) shall be written to the application log with the caller, route and time, and no key, token or password value shall ever be written there. This is separate from the business event log in SR11.
- Source: lets someone notice an attack and work out what happened afterwards
- How to verify: make refused calls and read the log; search the log for key and token values
- Status: **Not met** (tested). The returned device token is in the application log in full (V5), and none of the refused requests in the run (two 403s, 409s) appear in the log at all (`evidence/sr-validation-2026-11-15.txt`, SR18). The identity check (`Services/Actors.cs`) has no logging

#### B5 Platform

**SR23 — Secrets management.** No password, key or other secret shall be committed to the repository or used as a built-in default. Secrets shall be supplied at deployment and the service shall refuse to start without them.
- Source: protects the database credentials
- How to verify: search the repository for the database password; start the stack without `POSTGRES_PASSWORD` set
- Status: **Not met** (V6). The password is committed in `appsettings.Development.json` and is the compose fallback (M4)

**SR24 — Least privilege.** The API's database user shall have only the rights it needs on its own tables, and shall not be able to change the schema or manage roles. The database shall be reachable only by the API, not from the host's network.
- Source: limits the damage if the API or its connection is compromised
- How to verify: `\du` for the app user's role; connect to port 5432 from another machine on the network
- Status: **Not met** (V6). The app user is a superuser (G2, confirmed with `\du`, `evidence/db-role-check-2026-11-15.txt`) and port 5432 is published on all host interfaces (G4)

**SR25 — Build and supply chain integrity.** Base images shall be pinned by digest and the CI runner to a fixed version, dependencies locked and scanned, and pipeline actions pinned.
- Source: protects what gets built and deployed
- How to verify: read the `Dockerfile`, `docker-compose.yml` and `devsecops.yml` for floating tags; check the Trivy results
- Status: **Partly met.** Lock files, Trivy scans and pinned action and scanner versions are in place from PA1, but the base images and runner are floating tags (M6)

**SR26 — Availability: limits.** The service shall limit how many requests a caller can make in a period and how much one request can ask for, so one caller can't exhaust it.
- Source: keeps the service usable for everyone else
- How to verify: send requests in a fast loop and watch for 429s; ask for a page size over 100
- Status: **Partly met.** Request size is limited: a page size of 500 was refused with a 400 ("Must be between 1 and 100"; `evidence/sr-validation-2026-11-15.txt`, follow-up) (P8). No rate limiting: 60 fast requests all got 200, no 429 (`evidence/sr-validation-2026-11-15.txt`, SR22), and nothing in the source configures a rate limiter (G5)

**Considered, not applicable now:**
- **CSRF and CORS:** there's no browser client and no cookies, so neither applies yet. They come back if the identity proof ever moves into a cookie
- **Container hardening:** the PA1 Dockerfile already runs the API as a non-root user. A read-only filesystem would be the next step, but that's a choice for the hardening phase

## 3. Abuse cases

Each case is told from the attacker's side, using the findings in section 1 and the requirements in section 2. I kept seven cases, drawn from five kinds of attacker (an outsider, a legitimate requester, a trusted administrator, someone with log access, anyone who can reach the database port); some attackers appear in more than one case. Together they break every requirement group except input handling, which held up in testing, and devices and returns, where the cases use a gap the requirements don't cover rather than breaking one. "Shown" records what I demonstrated: **Demonstrated** (run end to end), **Partly demonstrated** (steps shown separately) or **Not demonstrated**.

### AC1 — Steal license keys
- **Attacker:** an outsider on the network with no account
- **Goal:** working license keys to use or sell
- **Preconditions:** network access to port 8080 (or 5432 for the database route)
- **Steps:**
  1. Call any protected route; the 401 says which header to send (M8)
  2. Read any ticket with no header and copy the administrator's id from its events (V2)
  3. Send that id as `X-User-Id` and read the allocations listing (V1)
  4. Take the license keys from the response, in plain text (V3)
  5. Or skip the API: log in to PostgreSQL with the fallback password and read `license_keys` as superuser (V6)
- **Weaknesses used:** V1, V2, V3, V6, M8 (M7 if the traffic is read on the network)
- **Requirements broken:** SR12, SR13, SR14, SR15, SR16, SR21; SR17 if the traffic is read on the network; SR23 and SR24 on the database route
- **Impact:** paid licenses lost, and there's no way to revoke or replace an issued key (gaps note in section 2), so they stay lost
- **Risk:** High
- **Shown:** demonstrated through the API. The administrator's id came from an open ticket read (triage evidence, step 4), and acting as the administrator, the allocations listing returned the license key and a device token in plain text (`evidence/abuse-case-checks-2026-11-15.txt`, check 1). The database route is shown in parts: the superuser login from outside the compose network (V6) and the keys readable in the database (V3, step 5)

### AC2 — Approve your own request
- **Attacker:** a legitimate course instructor or lab advisor
- **Goal:** get their own ticket approved and allocated without the IT director's sign-off
- **Preconditions:** a real requester role, and the IT director's and administrator's ids (from a ticket's events, V2)
- **Steps:**
  1. Submit a ticket normally
  2. Send the approval with the IT director's id as `X-User-Id` (V1)
  3. Send the allocate with the administrator's id (V1)
  4. Collect the keys, credit or devices
- **Weaknesses used:** V1, V2
- **Requirements broken:** SR2, SR11, SR12, SR14, SR16
- **Impact:** the sign-off the whole spec is built around stops meaning anything, and the event log names the IT director and the administrator as the ones who did it
- **Risk:** High
- **Shown:** demonstrated. The triage evidence is this attack end to end: Dr. Chen submits (step 1), I send the approval with the IT director's id (step 2) and the allocate with the administrator's id (step 3), and the event log names them, not me (step 4). The server can't tell those requests from the real people's

### AC3 — Cover your tracks
- **Attacker:** an insider or outsider who has already done something they don't want traced
- **Goal:** remove or falsify the record of what happened
- **Preconditions:** a pending ticket to delete, or access to port 5432
- **Steps:**
  1. Probe first: refused requests aren't written to the application log (SR22), so trying things leaves nothing behind
  2. Act under someone else's id (V1), so the event log names the wrong person
  3. Delete the pending ticket through the API, which deletes its events with it (V4)
  4. Or delete event rows directly in the database as superuser (V6)
- **Weaknesses used:** V1, V4, V6
- **Requirements broken:** SR11, SR12, SR22, SR24
- **Impact:** no trustworthy answer to "who did this", which weakens every other finding after the fact
- **Risk:** High
- **Shown:** partly demonstrated; each step was shown separately, but I didn't delete someone else's ticket while acting as them. Deleting a pending ticket left 0 events (V4), deleting 6 events through the database succeeded before the rollback (`evidence/sr-validation-2-2026-11-15.txt`), the log records the impersonated actor (triage evidence, step 4), and refused requests don't appear in the application log (SR22)

### AC4 — Fake a device return
- **Attacker:** an outsider acting as the administrator, or someone with read access to the application logs
- **Goal:** make a device that is still out look returned, to hide a missing device or free the stock
- **Preconditions:** a device token, and the administrator's id (V2)
- **Steps:**
  1. Get a token: from the allocations listing while acting as the administrator (V1), or from the application log, which holds every returned token in full (V5)
  2. Send it to `/return` as the administrator (V1); the token is all the route needs
  3. The stock count goes up and the device drops off the overdue list while it is still out
- **Weaknesses used:** V1, V2, V5
- **Gaps used:** returns trust the token alone (gaps note in section 2). SR9 holds as written, since each token is accepted once, but nothing checks the device actually came back
- **Requirements broken:** SR11 (the return records no event), SR12, SR13, SR15, SR22
- **Impact:** inventory that doesn't match reality, and a lost or stolen device that never shows as overdue
- **Risk:** Medium
- **Shown:** partly demonstrated. The allocations listing returns device tokens (`evidence/abuse-case-checks-2026-11-15.txt`, check 1), the token is in the log (V5), and a token returns with a 200 (`evidence/sr-validation-2026-11-15.txt`, SR8); I didn't return a device I knew was still out

### AC5 — Rewrite inventory directly
- **Attacker:** anyone who can reach port 5432 on the host
- **Goal:** change stock, balances or records without going through the API
- **Preconditions:** network access to the host; nothing else, since the password is the committed fallback
- **Steps:**
  1. Log in to PostgreSQL with `resourcehub_dev` (V6)
  2. Update stock in `assets` or the voucher balance directly
  3. Every rule the API enforces (SR3, SR4, SR5, SR8, SR10) lives in the application, so none of them apply. The database's check constraints still keep stock and balances in range (`Data/HubDbContext.cs` L72–75), but a superuser can drop them
- **Weaknesses used:** V6
- **Requirements broken:** SR4, SR5, SR11, SR23, SR24
- **Impact:** inventory and balances can't be trusted, and nothing records the change
- **Risk:** High
- **Shown:** demonstrated. The superuser login from outside the compose network (V6), then through the same login an update setting the Raspberry Pi kit's remaining stock to 0 succeeded (`UPDATE 1`) before it was rolled back (`evidence/abuse-case-checks-2026-11-15.txt`, check 2)

### AC6 — Hoard stock (insider misuse)
- **Attacker:** a legitimate instructor or advisor, or a group of them in one department program
- **Goal:** tie up stock other courses and labs need, through the normal request flow
- **Preconditions:** a real requester role; nothing else
- **Steps:**
  1. Request the maximum: 20 items, up to 1000 each, on a 365-day loan
  2. Keep several tickets open at once; nothing limits one requester's or one department's share
  3. Rely on volume to wear down the IT director's approval, the only gate (or skip it through V1)
  4. Never return the devices, since an overdue device can't be written off; or drain the license keys or the voucher balance
- **Weaknesses used:** no limit per requester or department (SR7; CWE-770)
- **Requirements broken:** SR7
- **Impact:** availability for everyone else, through something no one would call an attack
- **Risk:** Medium
- **Shown:** demonstrated. A request for 1000 kits against a stock of 10, on a 365-day loan, plus all three license keys, was accepted with a 201 while the same requester had other tickets pending (`evidence/sr-validation-2-2026-11-15.txt`)

### AC7 — Misuse a real administrator role (privileged insider)
- **Attacker:** a real administrator, using only the access the role gives; no V1 needed
- **Goal:** take resources for themselves or a friend, and make it look like routine work
- **Preconditions:** the administrator role
- **Steps:**
  1. Allocate an approved ticket; the allocate response hands the administrator every key and device token issued (SR15 allows it: the role needs to run allocate)
  2. Keep a device and mark it returned with its token; the return route checks only the token, so stock goes up and the device is never overdue (as in AC4, but with no impersonation)
  3. Delete other requesters' pending tickets they don't want filled; an administrator can delete any pending ticket, and its history goes with it (V4)
  4. Change total stock through `PUT /api/v1/assets/{id}` to make the numbers fit
- **Weaknesses used:** V4
- **Gaps used:** no separation of duties or second approval for allocation, returns or deletes, and returns trust the token alone (both in the gaps note in section 2)
- **Requirements broken:** SR11 (the event log is the only check on the administrator, and it can be emptied)
- **Impact:** a trusted role can take stock and remove the trail with no other person involved, so the controls that hold against outsiders (SR1, SR3, SR10) don't help here
- **Risk:** Medium: it needs a trusted role, but nothing in the system would catch it
- **Shown:** partly demonstrated. The administrator received the keys and tokens on allocate and returned a device with its token (`evidence/sr-validation-2026-11-15.txt`, SR3 and SR8); the code lets an administrator delete any pending ticket (`Endpoints/TicketEndpoints.cs` L192); deleting a pending ticket removed its events (V4). I didn't delete another requester's ticket as the administrator

**Left out:** flooding the API (G5) and a poisoned base image (M6). Both are real, but they're generic to any service rather than shaped by this one, and the second is from review only. They stay as findings and are picked up in the STRIDE matrix.

### 3.1 Coverage check

| Finding | Abuse cases |
|---|---|
| V1 | AC1, AC2, AC3, AC4 |
| V2 | AC1, AC2, AC4 |
| V3 | AC1 |
| V4 | AC3, AC7 |
| V5 | AC4 |
| V6 | AC1, AC3, AC5 |
| M7, M8 | AC1 |
| M6, G5 | none (left out above) |

Every High-risk finding (V1, V2, V3, V6) appears in at least one case.

| Requirement group | Broken by |
|---|---|
| A1 Requests and approval | AC2 (SR2) |
| A2 Allocation | AC5 (SR4, SR5), AC6 (SR7) |
| A3 Devices and returns | none broken; AC4 and AC7 use the token-only return gap instead |
| A4 Catalog and records | AC2, AC3, AC4, AC5, AC7 (SR11) |
| B1 Identity and access | AC1, AC2, AC3, AC4 |
| B2 Protecting data | AC1, AC2, AC4 |
| B3 Input | none; held up in testing |
| B4 Errors and logging | AC1 (SR21), AC3, AC4 (SR22) |
| B5 Platform | AC1, AC3, AC5 (SR23, SR24) |

**What this shows:** the requirements that tested as met (SR1, SR3–SR6, SR8–SR10, SR19, SR20) aren't broken by any case through the API, but AC5's database route goes around all of them, because they live in the application and V6 skips the application.

## 4. Attack trees

Three trees, one each for AC1, AC2 and AC3. Each root is the attacker's goal from the abuse case; each branch is one way to get there.

**How to read them:**
- **[OR]**: any one child is enough. **[AND]**: every child is needed
- Each leaf is a concrete action tied to a finding, with three tags: **Shown** (D demonstrated, P partly, N not, as in section 3) · **Effort** (Low, Med, High) · **Needs** (none, a role, log access, network access)
- **Needs** counts only what's beyond reaching the API on port 8080, which every attack assumes. "network" means a further foothold: the database port, or a position on the path between a client and the API
- **→ cheapest path** marks the route an attacker would most likely take
- **[see T0]** points to the shared subtree below instead of repeating it

### T0 — Shared subtree: act as a privileged user

Used by all three trees. It covers acting as someone inside the API; reaching the database directly (V6) goes around the API, so each tree has it as its own branch.

```
Act as a privileged user: IT director or administrator [OR]
├── Impersonate them [AND]                                            → cheapest path
│   ├── Learn the target's id [OR]
│   │   ├── Read any ticket with no header (V2)                       D · Low  · none
│   │   └── Use the fixed seeded ids (spec section 7, …0005, …0006)   D · Low  · none
│   └── Send the id as X-User-Id (V1)                                 D · Low  · none
└── Have a real one act for me [OR]
    ├── Be one: misuse my own real role (AC7)                         P · —    · role
    └── Get one to go along knowingly (collusion)                     N · High · none
```

The seeded ids are published in the spec and used unchanged by the running stack; every test in the evidence files sent them without first reading a ticket, so V1 works even if V2 is fixed.

### 4.1 Attack tree: AC1 — Steal license keys

```
Steal license keys [OR]
├── From the API [AND]                                               → cheapest path
│   ├── Act as the administrator [see T0]
│   └── Read the keys in plain text (V3) [OR]
│       ├── The allocations listing returns them                     D · Low  · none
│       └── Allocate an approved ticket; the response returns them   D · Low  · none
├── From the database [AND]
│   ├── Reach port 5432 on the host (G4, part of V6)                 D · Low  · network
│   ├── Log in with the fallback password (V6)                       D · Low  · none
│   └── Read license_keys, stored in plain text (V3)                 D · Low  · none
└── From the network [AND]
    ├── Sit on the path between a client and the API (M7)            N · Med  · network
    └── Capture a plain-HTTP response that carries keys (M7)         N · Med  · network
```

### 4.2 Attack tree: AC2 — Approve your own request

```
Get my own ticket approved and allocated without a real review by the IT director [AND]
├── Submit my own ticket                                         D · Low  · role
├── Get it approved [OR]
│   ├── Act as the IT director [see T0]                          → cheapest path
│   └── A real IT director approves it without looking,
│       worn down by volume (as in AC6)                          N · Med  · none
└── Get it allocated [OR]
    ├── Act as the administrator [see T0]
    └── Wait: once approved, a real administrator allocates it   → cheapest path
        as routine, and nothing marks it as unusual              N · Low  · none
```

T0's "Be one" branch doesn't apply here: a requester can't also be the IT director or the administrator.

Waiting is marked as the cheapest way to get the allocation: once a ticket is approved, allocation is the administrator's normal next step, so it needs no impersonation and leaves the attacker out of the event log for that step.

The database route (V6) is left out on purpose. Someone with superuser access could set the status and write allocation rows directly, but they'd skip the ticket altogether and read the keys (AC1), so it isn't a realistic way to reach this goal.

### 4.3 Attack tree: AC3 — Cover your tracks

```
Leave no trustworthy record of what I did [OR]
├── Hide who did it: send another user's id as X-User-Id (V1)   D · Low  · none   → cheapest path
├── Erase the record [OR]
│   ├── Delete a pending ticket; its events go with it (V4) [AND]
│   │   ├── The ticket is still pending                         D · Low  · none
│   │   └── Have the right to delete it [OR]
│   │       ├── Be its requester                                D · Low  · role
│   │       └── Act as the administrator [see T0]               N · Low  · none
│   └── Delete or edit event rows in the database [AND]
│       ├── Reach port 5432 on the host (G4, part of V6)        D · Low  · network
│       ├── Log in with the fallback password (V6)              D · Low  · none
│       └── Change the rows [OR]
│           ├── Delete event rows                               D · Low  · none
│           └── Edit event rows                                 N · Low  · none
└── Leave nothing to record [OR]
    ├── Probe freely: refused attempts aren't logged (SR22)     D · Low  · none
    └── Act outside tickets: returns and asset create, update and
        delete write no event, only an application log line     N · Low  · role
```

Editing event rows is marked N because the update in the assistant's test script used an event type that doesn't exist (`Allocated` instead of `AllocationSucceeded`) and matched no rows; only the delete is shown (`evidence/sr-validation-2-2026-11-15.txt`). The last leaf is confirmed in the code but not run: events are only written for ticket submission, updates, transitions and allocation attempts (`Endpoints/TicketEndpoints.cs` L97, L168, L297; `Services/AllocationService.cs` L149, L184), so returns and asset changes leave no event at all.

## 5. Attack surface map

Routes and caller rules read from the code (`Endpoints/*.cs`). "Any known user" means any id that exists in the users table, sent in `X-User-Id` (V1). Risk uses the scale in section 1.1. "No event" means the route changes stock, devices or the catalog without writing to the event log (SR11).

**Highest-risk vectors:** the `X-User-Id` header on every write (V1); the five read routes that answer with no header at all (SR14), two of which hand out the ids V1 needs (V2); the two routes that return license keys and device tokens (allocate and the allocations listing, V3); and the database port (V6), which goes around every rule the API enforces (AC5).

### 5.1 HTTP entry points (port 8080, plain HTTP)

| Method and route | Caller rule in the code | What it takes in | What it returns | Related findings | Abuse cases | Risk |
|---|---|---|---|---|---|---|
| `GET /api/v1/health` | none (public on purpose) | — | status | | | Low |
| `POST /api/v1/assets` | Administrator | body | asset (counts, no keys) | V1; no event (SR11) | AC7 | Medium |
| `GET /api/v1/assets` | none; answers with no header (SR14) | paging and filter query | asset list (counts, no keys) | G3 | | Low |
| `GET /api/v1/assets/{assetId}` | none; answers with no header (SR14) | route id | asset (counts, no keys) | G3 | | Low |
| `PUT /api/v1/assets/{assetId}` | Administrator | route id, body | asset | V1; no event (SR11) | AC7 | Medium |
| `DELETE /api/v1/assets/{assetId}` | Administrator | route id | — | V1; no event (SR11) | | Medium |
| `POST /api/v1/assets/{assetId}/return` | Administrator | route id, device token in body | — | V1, V5; no event (SR11); accepts any valid token (token-only return gap) | AC4, AC7 | Medium |
| `POST /api/v1/assets/tickets` | Course instructor or lab advisor | body | ticket | V1; no share limit (SR7) | AC2, AC6 | Medium |
| `GET /api/v1/assets/tickets` | none; answers with no header (SR14) | paging and filter query | tickets with requester ids and notes | V2 | AC1, AC2, AC4 | High |
| `GET /api/v1/assets/tickets/{ticketId}` | none; answers with no header (SR14) | route id | ticket with requester id, notes and event actor ids | V2 | AC1, AC2, AC4 | High |
| `PUT /api/v1/assets/tickets/{ticketId}` | any known user; contents: requester only; approve and reject: IT director; close: administrator | route id, body | ticket | V1; refused transitions leave no event (SR2) | AC2 | High |
| `DELETE /api/v1/assets/tickets/{ticketId}` | requester or administrator | route id | — | V1, V4 | AC3, AC7 | Medium |
| `POST /api/v1/assets/tickets/{ticketId}/allocate` | Administrator | route id | allocation, including license keys and device tokens | V1, V3 | AC1, AC2, AC7 | High |
| `GET /api/v1/assets/allocations` | any known user; results only for the course's instructor, the lab's advisor, the IT director or an administrator; the only read route that refuses a missing header (401) | course or lab id query | active allocations, including license keys and device tokens | V1, V3 | AC1, AC4 | High |

Port 8080 is published on all host interfaces (`docker-compose.yml` L26). That is intended for an API and is not a finding on its own; the risk on this port is plain HTTP (M7).

Common to all routes:
- **Inputs:** the `X-User-Id` header (V1, M8), route ids (GUIDs, typed by the route), query values (page size capped at 100, P8), JSON bodies (validated, P4; extra fields ignored, SR20)
- **The ids for V1 are public anyway:** the seeded user ids are fixed in the spec (section 7) and unchanged in the running stack, so V1 works even without V2
- **Every response:** no `Cache-Control: no-store` and no `X-Content-Type-Options: nosniff`, and `Server: Kestrel` names the web server (SR18). Tested on the allocations listing; nothing in the code sets these headers on any route (the only header code is the `X-User-Id` read in `Services/Actors.cs`); errors are problem details with no stack trace (P9), but the 401 explains how to get through (M8)
- **No limits per caller:** no rate limiting (G5, SR26), and refused attempts aren't written to the application log (SR22)

### 5.2 Other entry points

| Entry point | Detail | Related findings | Abuse cases | Risk |
|---|---|---|---|---|
| PostgreSQL | Port 5432 published on all host interfaces; the stack uses the fallback password `resourcehub_dev`; the app's user is a superuser. Anyone who reaches it bypasses the API, and with it every rule in group A except the database's own check constraints, which a superuser can drop | V6, V3 | AC1, AC3, AC5 | High |
| Configuration | `appsettings.json`; `appsettings.Development.json`, committed with the database password; compose environment with the fallback password | V6 (CWE-260 for the config file copy) | AC5 | Medium |
| Logs | Application log receives the full device token on every return; returns and asset changes are recorded only here, not in the event log; refused attempts aren't recorded at all | V5; SR11, SR22 | AC3, AC4 | Medium |
| Database volume | The data lives in a named volume on the host (`docker-compose.yml` L11, L31). Needs access to the host's files; anyone with that can read the license keys there in plain text without going through the port. Shown: a known key was found in two table data files and in the write-ahead log (`evidence/abuse-case-checks-2026-11-15.txt`, check 3) | V3 | | Medium |
| Container images and CI | Floating base image and runner tags; the CI pipeline builds and scans the code, using `SONAR_TOKEN` from GitHub secrets (`devsecops.yml` L50), a scanner token rather than app data | M6 | | Medium |
| Startup | Migrations applied by the app at startup, using the superuser connection | V6 | | Medium |

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
| P7 | Parameterized SQL for row locks; all-or-nothing allocation: rows locked (`FOR UPDATE`), then checked and deducted in one transaction | `Services/AllocationService.cs` and endpoints |
| P8 | Page size capped at 100 | `Services/Query.cs` |
| P9 | Errors as problem details, no stack traces | `Program.cs` |
| P10 | Asset delete refused (409) while any ticket or allocation refers to it; database restricts as well | `Endpoints/AssetEndpoints.cs` (`DeleteAsync`), `Data/HubDbContext.cs` |

### 6.2 STRIDE matrix

_Every DFD element against S, T, R, I, D, E._

## 7. Proposed mitigations

_A technical plan for every vulnerability and threat above, traced back to its id._

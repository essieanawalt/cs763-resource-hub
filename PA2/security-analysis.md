# security-analysis.md — PA2 naive baseline

## AI Deliverable Metadata Block

| Item | Detail |
|---|---|
| Conceptual AI contribution | AI: _%_ / HUMAN: _%_ (to set once sections 3–7 are written) |
| My part | Wrote the security requirements from the spec's business rules, including the review gaps; checked every CWE against MITRE CWE 5.0; confirmed the database superuser (`\du`); ran the requirement tests; decided which findings were confirmed and promoted (V4, V5) |
| AI part (Claude chat assistant) | Section skeleton; carried in the triage findings and protections; attack-surface routes read from the code; review of the requirements against the code; test script; status updates from the test results; V4/V5 write-ups |
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

**SR11 — Accountability: trustworthy event log.** Every ticket state change and every allocation attempt, failed ones included, shall be recorded with the verified identity of the caller who made it, the time and the outcome. The log shall be append-only, both through the API and in the database: the service's database user can add events but not change or delete them. Other requirements that say "logged" mean this.
- Source: spec requirement 3, clarification 13
- Depends on: SR12
- How to verify: make a change, read the ticket's events, check the actor; try to edit or delete an event
- Status: **Not met.** In the database too: through V6, deleting 6 events succeeded inside a transaction that was then rolled back (`evidence/sr-validation-2-2026-11-15.txt`; the update in the same test matched no rows, so only deletion is shown). Recording works: state changes, edits and allocation attempts are recorded with actor and time (`Endpoints/TicketEndpoints.cs` L97, L168; `Services/AllocationService.cs` L52, L65). But the log isn't append-only: deleting pending ticket T3 through the API removed its submission event from `ticket_events` (V4; 0 left; `evidence/sr-validation-2026-11-15.txt`, SR10), because events cascade-delete with their ticket (`Data/HubDbContext.cs` L114). The actor is also only as trustworthy as SR12, which fails (V1)

**Gaps found in review, to decide later.** These aren't from the spec. Most came from asking whether everything the service hands out has a way to close, return or end. They are business-process gaps more than security requirements, but a record that never closes is also an audit and availability problem, so I'm keeping them here to decide on:
- **Lost devices:** a physical device that never comes back stays overdue forever. There's no way to write it off and adjust total stock
- **Leaked license keys:** an issued key is never returned to the pool, and expiry is out of scope (spec section 8), but there's also no way to revoke or replace a key if it leaks (V3). Revocation is a different thing from expiry, so this one leans toward a security requirement
- **Approved tickets that can't be filled:** a ticket short on stock stays approved so allocation can run later (clarification 9), but clarification 8 gives no way to cancel or close it, so it can stay approved forever
- **Separation of duties:** the user who submits a ticket shouldn't be the one who approves it. The spec doesn't ask for it and no mock user holds two roles, so nothing can break it today. I'm keeping it here rather than as a requirement with a status

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
- How to verify: query the `license_keys` table directly; read allocations as a caller who shouldn't see them
- Status: **Not met.** Keys are stored and returned in plain text (V3), and anyone using V1 can read them

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

_Malicious-user scenarios (AC1…): actor, goal, how, which requirement it breaks._

**Candidates noted so far:**
- **Fake device return:** the allocations listing shows device tokens, and a token is all `/return` needs. Acting as an administrator through V1, an attacker copies a token from the listing and marks a device returned while it is still out, which raises the stock count and hides a missing device. Touches SR9, SR12, SR13, SR15
- **Hoarding (insider misuse):** a legitimate instructor or advisor, or a group of them in one department program, uses the normal request flow to tie up stock others need. Variants: maximum quantities (20 items, up to 1000 each), maximum loans (365 days, then never returning, since overdue devices can't be written off), draining license keys (issued keys never return to the pool) or the voucher balance. Nothing limits one requester's or one department's share; the IT director's approval is the only gate, and it can be worn down by volume or skipped through V1. Breaks availability for everyone else (CWE-770). Covered by SR7. Shown in testing: a request for 1000 kits against a stock of 10 on a 365-day loan was accepted (`evidence/sr-validation-2-2026-11-15.txt`)

## 4. Attack trees

_At least three, one per chosen abuse case._

### 4.1 Attack tree: _AC?_

### 4.2 Attack tree: _AC?_

### 4.3 Attack tree: _AC?_

## 5. Attack surface map

Routes and caller rules read from the code (`Endpoints/*.cs`). "Any known user" means any id that exists in the users table, sent in `X-User-Id` (V1). Risk uses the scale in section 1.1. **Highest-risk vectors:** the `X-User-Id` header on every write (V1), the open ticket reads that supply the ids for it (V2), the two routes that return license keys and device tokens (allocate and the allocations listing, V3), and the database port (V6).

### 5.1 HTTP entry points (port 8080, plain HTTP)

| Method and route | Caller rule in the code | What it takes in | What it returns | Related findings | Risk |
|---|---|---|---|---|---|
| `GET /api/v1/health` | none | — | status | | Low |
| `POST /api/v1/assets` | Administrator | body | asset (counts, no keys) | V1 | Medium |
| `GET /api/v1/assets` | none | paging and filter query | asset list (counts, no keys) | G3 | Low |
| `GET /api/v1/assets/{assetId}` | none | route id | asset (counts, no keys) | G3 | Low |
| `PUT /api/v1/assets/{assetId}` | Administrator | route id, body | asset | V1 | Medium |
| `DELETE /api/v1/assets/{assetId}` | Administrator | route id | — | V1 | Medium |
| `POST /api/v1/assets/{assetId}/return` | Administrator | route id, device token in body | — | V1, M5 | Medium |
| `POST /api/v1/assets/tickets` | Course instructor or lab advisor | body | ticket | V1 | Medium |
| `GET /api/v1/assets/tickets` | none | paging and filter query | tickets with requester ids and notes | V2 | High |
| `GET /api/v1/assets/tickets/{ticketId}` | none | route id | ticket with requester id, notes and event actor ids | V2 | High |
| `PUT /api/v1/assets/tickets/{ticketId}` | any known user; contents: requester only; approve and reject: IT director; close: administrator | route id, body | ticket | V1 | High |
| `DELETE /api/v1/assets/tickets/{ticketId}` | requester or administrator | route id | — | V1 | Medium |
| `POST /api/v1/assets/tickets/{ticketId}/allocate` | Administrator | route id | allocation, including license keys and device tokens | V1, V3 | High |
| `GET /api/v1/assets/allocations` | any known user; results only for the course's instructor, the lab's advisor, the IT director or an administrator | course or lab id query | active allocations, including license keys and device tokens | V1, V3 | High |

Port 8080 is published on all host interfaces (`docker-compose.yml` L26). That is intended for an API and is not a finding on its own; the risk on this port is plain HTTP (M7).

Inputs common to all routes: the `X-User-Id` header (V1, M8), route ids (GUIDs, typed by the route), query values (page size capped at 100, P8), JSON bodies (validated, P4).

### 5.2 Other entry points

| Entry point | Detail | Related findings | Risk |
|---|---|---|---|
| PostgreSQL | Port 5432 published on all host interfaces; password from an environment variable with the fallback `resourcehub_dev`; the app's user is a superuser | G4, M4, G2, V3 | High |
| Configuration | `appsettings.json`, `appsettings.Development.json`, compose environment | M4 | Medium |
| Logs | Application log receives the full device token on return | M5 | Medium |
| Container images and CI | Floating base image and runner tags; the CI pipeline builds and scans the code | M6 | Medium |
| Startup | Migrations applied by the app at startup, using the superuser connection | G2 | Medium |

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

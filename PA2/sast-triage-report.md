# sast-triage-report.md — PA2 naive baseline

## AI Deliverable Metadata Block

| Item | Detail |
|---|---|
| Conceptual AI contribution | AI: 50% / HUMAN: 50% |
| My part | Collected the pipeline and SonarCloud evidence; caught that Semgrep only partly parsed three files and raised it with the facilitator from PA1 experience; reviewed every generated file (header notes), including spotting that roles can be spoofed; ran the manual validation of M1–M3 against the running stack, the security requirement tests that confirmed V4 and V5, and the database checks that confirmed V6 (`\du` and the network login); decided verdicts and which findings count; rewrote drafts |
| AI part (Claude chat assistant) | Report structure; scan facts summarized from the evidence; first-draft ledger rows; manual-review candidates (M1–M8) from chat notes; validation commands; test script for the requirement checks; V4–V6 write-ups (suggested testing the V6 chain) |
| Design-aid prompts | `.ai_logs/2026-11-14_review-log.md` and `.ai_logs/2026-11-15_review-log.md` (in this folder) |

## 1. Scope and evidence

- Code scanned: `PA2/functional-codebase/` (test-first build, commit `cd539b8`; final scanned code `4d39740` after the parse fix)
- **Note on later formatting:** during my review on 2026-11-15, my editor's format-on-save split long lines in `Data/SeedData.cs`, `Endpoints/AssetEndpoints.cs`, `Services/AllocationService.cs` and `Services/TicketContent.cs`. I checked that the code is identical apart from line breaks (no behaviour change) and kept it; the file headers record it. Line numbers in this report refer to the scanned commit `4d39740`, so they may be shifted in those four files
- Pipeline run: `43671417710` on commit `bf6674d` (2026-11-15 03:14 UTC). Evidence in `evidence/scan-reports-pa2-build/` and `evidence/pipeline-run-pa2-build.txt`
- Rescan after the parse fix: run `43736626360` (2026-11-15 16:39 UTC). Evidence in `evidence/scan-reports-pa2-parsefix/` and `evidence/pipeline-run-pa2-parsefix.txt`
- Pre-build baseline for comparison: `evidence/scan-reports-pa2-prebuild/`
- Tools: Semgrep 1.180.0 (`p/csharp`, no account), SonarQube Cloud (free plan), Trivy 0.75.0 (dependencies and image)
- Tests in CI: 123 passed, 0 failed, 0 skipped (database tests ran against the pipeline's PostgreSQL service)

### Scan coverage (read before the findings)

| Tool | Coverage | Gap |
|---|---|---|
| Semgrep | Final code (`4d39740`): 23 targets, about 100% of lines parsed, 0 errors. First scan (`bf6674d`): about 99.9%, with 3 files only partly parsed (primary constructors in `Data/HubDbContext.cs` and `Services/AllocationService.cs`, and `public partial class Program`), fixed by the agent on request (see section 2) | **Test files not scanned** (same as PA1; cause not confirmed, likely Semgrep's default ignore list) |
| SonarQube Cloud | Final analysis of `main` at `4d39740` (private project). Covers source and test files; test coverage 95.4%, duplication 0.0% | Results are from the .NET (roslyn) analyzers SonarQube imports; no SonarQube security rule fired and **0 security hotspots** were raised. Raw output exported from the SonarCloud web API (token-authenticated, private project): `issues.json`, `hotspots.json`, `measures.json`, `analyses.json` in `evidence/sonarqube-pa2-build/` (revision `bf6674d406`, before the parse fix; dashboard summary screenshot alongside) and `evidence/sonarqube-pa2-parsefix/` (revision `4d39740771`, final code) |
| Trivy dependencies | Both lock files under `PA2/functional-codebase/` | |
| Trivy image | `resourcehub-api:ci` (Ubuntu 24.04, .NET runtime 10.0.15) | |

## 2. Raw results

| Tool | Result |
|---|---|
| Semgrep | 0 findings, 3 `PartialParsing` errors (above). Facilitator guidance (2026-11-15): fixing testability is fine as long as it is logged and attributed; security fixes wait for PA3. The agent rewrote the two primary constructors and removed the `partial class Program` line when asked (2026-11-15, prompt 03; behaviour unchanged, 123 tests passing). Local Semgrep run with the pipeline's image and options: 23 files, 0 errors, about 100% parsed, **still 0 findings**. Confirmed by pipeline run `43736626360`: 23 targets, about 100% parsed, 0 errors, 0 findings (`evidence/scan-reports-pa2-parsefix/`) |
| SonarQube Cloud | Quality gate **passed** (Sonar way). Final code (`4d39740`): 18 open issues, all Maintainability / Medium; 0 bugs or vulnerabilities; 0 security hotspots. Before the parse fix (`bf6674d`): 19 issues, the extra one being ASP0027 (S3) |
| Trivy dependencies | 0 vulnerabilities |
| Trivy image | 36 OS package vulnerabilities (31 medium, 5 low, 0 high/critical), the same as the pre-build baseline, so none come from the generated code |

## 3. Triage ledger

SonarQube issues from `issues.json`: 19 at `bf6674d` (matches the dashboard), 18 at the final commit `4d39740` once the parse fix removed S3. Line numbers below are from `bf6674d`; only `AllocationService.cs` moved in the final code (noted in the row). The API classes all 19 as `CODE_SMELL` with severity `INFO`; the dashboard shows them as Maintainability / Medium impact. Semgrep raised none.

| # | Tool | Rule / finding | Location (`src/ResourceHub.Api/` or `tests/ResourceHub.Tests/`) | Count | Verdict | Reason | CWE |
|---|---|---|---|---|---|---|---|
| S1 | SonarQube (roslyn CA1873) | Log argument evaluated even when logging is disabled | `Endpoints/AssetEndpoints.cs` L77, L205, L296, L325; `Endpoints/TicketEndpoints.cs` L101, L171, L204, L302; `Services/AllocationService.cs` L138 (L149 in the final code) | 9 | True positive, not a security issue | Performance / maintainability. **But L77 also writes the full device token to the log, which the rule does not consider; see M5** | — (M5: CWE-532) |
| S2 | SonarQube (roslyn CA1862) | Culture-sensitive case-insensitive string comparison; use `StringComparison.OrdinalIgnoreCase` | `Endpoints/AssetEndpoints.cs` L113, L118, L123; `Endpoints/TicketEndpoints.cs` L250 | 4 | True positive, not a security issue | All four are list filters (asset category, department program, name search; ticket asset category) that lower-case the input and the column before comparing. They decide which public catalog rows are listed, not who may access what. Worst case: a filter misses rows under some cultures. A common analyzer finding in my experience; teams often apply it for consistency even where it adds little | — |
| S3 | SonarQube (roslyn, ASP0027) | `public partial class Program` no longer needed | `Program.cs` L60 | 1 | True positive, not a security issue. **Resolved** in `4d39740` | Leftover for test access, and one of the lines Semgrep could not parse; removed by the agent in the parse fix | — |
| S4 | SonarQube (roslyn CA1861) | Prefer `static readonly` fields over constant array arguments | Tests: `AllocationTests.cs` L220; `AssetCrudTests.cs` L53, L160, L167; `TicketQueryTests.cs` L86 | 5 | True positive, not a security issue | Test-code style | — |

Totals from `measures.json`: at `bf6674d`, 19 code smells, coverage 95.3%, 1,978 lines of code; at the final commit `4d39740`, **18 code smells, 0 bugs, 0 vulnerabilities, 0 security hotspots, coverage 95.4%, 1,990 lines of code**.

**Expected false positive that did not appear:** `FromSql($"… {id} …")` in `AssetEndpoints.cs`, `TicketEndpoints.cs` and `AllocationService.cs` looks like string-built SQL but is parameterized by EF Core. Neither tool flagged it, including after the parse fix, when Semgrep could read every one of those calls. **No false positives were raised in this scan**: all 18 remaining SonarQube issues are true positives, none of them security issues.

**What the scanners did not raise:** no finding or hotspot for the `X-User-Id` identity check, the open ticket reads, plain-text license keys, the config password, or token logging (beyond S1's performance note). See section 4.

## 4. Vulnerabilities missed by the scanners (manual review)

Candidates from the code review (chat review on 2026-11-14, plus my own review notes). **M1–M3 are validated below** (V1–V3), M5 as V5, and M4 with G2 and G4 as V6; M6–M8 remain candidates (M9 was reviewed and isn't a finding) for the security analysis and PA3. G2 and G4 come from my review of the root `docker-compose.yml`, which `spec-domain2.md` (section 2) told the agent to run the service with, so these two were already in place before the PA2 build rather than written by the agent during it. `git log -- docker-compose.yml` shows the file was last changed in PA1 (commit `29c340a`, 2026-11-01) and no PA2 commit touches it. At least 3 distinct, multi-layer vulnerabilities with CWE and root cause are required.

I checked every CWE id against MITRE CWE 5.0 (released 2026-10-28) on 2026-11-15.

| # | Layer | Candidate | Where | CWE |
|---|---|---|---|---|
| M1 | Authentication | Caller identity is an `X-User-Id` header trusted as given | `Services/Actors.cs` | CWE-290 |
| M2 | Authorization / data exposure | Ticket list and detail open to anyone, returning requester ids, notes and event actor ids, which supply the ids M1 needs | `Endpoints/TicketEndpoints.cs`, `Services/TicketViews.cs` | CWE-862 |
| M3 | Data storage | License keys stored and returned in plain text | `Domain/Entities.cs`, `Services/AllocationService.cs`, `Endpoints/AllocationEndpoints.cs` | CWE-312 |
| M4 | Configuration | (validated as V6) Database password `resourcehub_dev` in two places: committed in `appsettings.Development.json` (written by the agent through a shell command), which the compose stack doesn't load because it runs as Production, and as the compose fallback when `POSTGRES_PASSWORD` is unset, which is the password the running stack actually uses | `appsettings.Development.json`, `docker-compose.yml` L8, L24 | CWE-260 (config file), CWE-1392 (compose default) |
| M5 | Logging | Full device token logged on return (validated as V5) | `Endpoints/AssetEndpoints.cs` line 77 | CWE-532 |
| M6 | Deployment | Unpinned base images and runner (`sdk:10.0`, `aspnet:10.0`, `postgres:18`, `ubuntu-latest`) | `Dockerfile`, `docker-compose.yml`, `devsecops.yml` | CWE-494 (secondary: CWE-1357) |
| M7 | Transport | Plain HTTP on 8080 | `Dockerfile`, `docker-compose.yml` | CWE-319 |
| M8 | Error handling | The 401 response tells the caller how to get through: "Send the id of a known user in the X-User-Id header." | `Services/Actors.cs` line 31 | CWE-210 |
| M9 | Robustness | Possible missing null checks when building asset responses (noted in my review). Reviewed, not a finding: every nullable value has a default and the database has check constraints; see `security-analysis.md` §1.2 | `Services/AssetViews.cs` | CWE-476 |
| G2 | Database privilege | (validated as V6) The API connects as `resourcehub`, the same user compose sets as `POSTGRES_USER`, and the official postgres image creates that user as a superuser, so the app holds full superuser rights on the database (it also applies migrations at startup). I confirmed the superuser role with `\du` on 2026-11-15 | `docker-compose.yml` L7, L24; `Program.cs` | CWE-250 |
| G4 | Deployment / network | (validated as V6) PostgreSQL port 5432 is published on all host interfaces (`"5432:5432"`, no host IP), not only to the API container | `docker-compose.yml` L10 | CWE-1327 |

### Validated vulnerabilities

Confirmed on 2026-11-15 against the local compose stack built from `4d39740`. Commands and full outputs: `evidence/manual-validation-2026-11-15.txt` (V1–V3), `evidence/sr-validation-2026-11-15.txt` (V4–V5, found while testing the security requirements in `security-analysis.md`) and `evidence/db-role-check-2026-11-15.txt` (V6).

### V1 — Caller identity can be spoofed (from M1)
- Layer: Authentication
- CWE: CWE-290 (Authentication Bypass by Spoofing)
- Location: src/ResourceHub.Api/Services/Actors.cs (RequireAsync)
- Root cause: the API takes the user id from a request header and looks it up, with no proof the caller is that user
- How I confirmed it: first spotted in my code review ("roles can be spoofed", `AllocationEndpoints.cs` header). Allocate with no header returned `401`; the same request with the administrator's id, sent by me, returned `200 allocated` (evidence step 3)
- Scanner result: not flagged by Semgrep or SonarQube
- Impact: anyone who knows a user id can act as that user, including the IT director and the administrator. The event log then records the impersonated user as the actor (step 4 shows the administrator, `…006`, as the allocator), so the audit trail is forged as well

### V2 — Ticket data, including user ids, readable by anyone (from M2)
- Layer: Authorization / data exposure
- CWE: CWE-862 (Missing Authorization). I first had CWE-200 here too, but MITRE marks it as discouraged for mapping, because the exposure is the impact and the missing check is the cause, so CWE-862 covers it
- Location: src/ResourceHub.Api/Endpoints/TicketEndpoints.cs (ListAsync, GetAsync), Services/TicketViews.cs
- Root cause: the ticket list and detail routes have no caller check, and the response includes the requester's id, notes and each event's actor id
- How I confirmed it: finding followed V1; `GET /api/v1/assets/tickets/{id}` with no header returned `200` with `requesterId` and events carrying the IT director's and administrator's ids (evidence step 4)
- Scanner result: not flagged by Semgrep or SonarQube
- Chain with V1: these responses provide the ids v1 needs, so together they give anyone admin-level access

### V3 — License keys stored and returned in plain text (from M3)
- Layer: Data storage
- CWE: CWE-312 (Cleartext Storage of Sensitive Information)
- Location: `license_keys` table (Domain/Entities.cs, Data/HubDbContext.cs); returned by Services/AllocationService.cs and Endpoints/AllocationEndpoints.cs
- Root cause: keys are stored as plain strings and copied into the allocate and allocations responses as-is
- How I confirmed it: on review; the allocate response contained `JBIDE-7K2M-Q9XA` (evidence step 3), and a direct database query showed all three keys in readable form, one linked to my ticket (step 5)
- Scanner result: not flagged by Semgrep or SonarQube
- Impact: anyone with db access or who uses V1 to act as an admin gets valid license keys

### V4 — Ticket event history deleted with the ticket (found in requirement testing)
- Layer: Audit / database
- CWE: CWE-221 (Information Loss or Omission). It's a Class, which MITRE allows with careful review; its nearest Base child, CWE-223 (Omission of Security-relevant Information), covers not recording an event, not deleting one already recorded, so the Class is the closer fit
- Location: Data/HubDbContext.cs L114 (events cascade-delete with their ticket); Endpoints/TicketEndpoints.cs (`DeleteAsync`, pending tickets only)
- Root cause: ticket events are stored as children of the ticket and set to delete with it, so the event log is not append-only
- How I confirmed it: submitted a ticket, saw its submission event in `ticket_events`, deleted the ticket as the requester (204), then queried the table: 0 events left (`sr-validation`, step SR10)
- Scanner result: not flagged by Semgrep or SonarQube
- Impact: a requester, or anyone using V1 to act as one, can erase the record that a request was ever made

### V5 — Device token written to the application log (from M5)
- Layer: Logging
- CWE: CWE-532 (Insertion of Sensitive Information into Log File)
- Location: Endpoints/AssetEndpoints.cs L77 (`ReturnAsync`)
- Root cause: the return writes the full hardware assignment token into its log message
- How I confirmed it: returned a device, then searched the API container log for its token and found the full value (`sr-validation`, step SR18)
- Scanner result: not flagged as a security issue. SonarQube flagged the same line only for logging performance (CA1873, S1)
- Impact: the token is all `/return` needs, so anyone who can read the logs holds working tokens; with V1 they can mark devices returned that are still out

### V6 — Database open to the network as superuser with the committed password (from M4, G2, G4)
- Layer: Deployment / database
- CWE: CWE-1392 (Use of Default Credentials); contributing CWE-1327 (Binding to an Unrestricted IP Address) and CWE-250 (Execution with Unnecessary Privileges); CWE-260 for the copy in `appsettings.Development.json`
- Location: `docker-compose.yml` L7–L10, L24 (fallback password, published port, superuser `POSTGRES_USER`); `appsettings.Development.json`
- Root cause: three settings that are each minor combine: no `.env`, so the fallback password `resourcehub_dev` is the real one; port 5432 is published on all host interfaces; and the app's user is the image's superuser
- How I confirmed it: `\du` showed `resourcehub` as Superuser; then a separate container outside the compose network logged in on the published port with the fallback password and got `usesuper = t` (`evidence/db-role-check-2026-11-15.txt`). Reached through the host; a login from a second machine was not tried
- Scanner result: not flagged by Semgrep or SonarQube. Trivy scans the image and dependencies, not the compose file
- Origin: G2 and G4 come from the PA1 compose file, which the PA2 spec told the agent to run with; the committed config-file copy of the password was written by the PA2 agent
- Impact: anyone who can reach port 5432 has full control of the data without the API: every license key (V3), the event log (V4, SR11 in `security-analysis.md`) and stock

## 5. Protections the agent added without being asked

Not findings, but they shape what the scanners saw. None was in the prompt or the spec. Paths are under `PA2/functional-codebase/`.

| # | What the agent added | Where | Noted by |
|---|---|---|---|
| P1 | Caller identity stand-in: `X-User-Id` header, 401 for unknown callers, 403 for the wrong role, and per-role rules for writes | `src/ResourceHub.Api/Services/Actors.cs` and the endpoint files | Me |
| P2 | Labelled its own identity check as not real authentication, in the code comment and in the README ("authentication is out of scope") | `Services/Actors.cs`, `PA2/README.md` (Caller identity) | Me |
| P3 | Hardware tokens from a cryptographically secure random generator (`RandomNumberGenerator`, the reason for the `System.Security.Cryptography` import) | `Services/AllocationService.cs` (`NewToken`) | Me |
| P4 | Input validation on request bodies, with tests for the 400 responses | `Services/Validation.cs`; tests | Me |
| P5 | Allocations listing limited to the course's instructor or lab's advisor, the IT director and administrators | `Endpoints/AllocationEndpoints.cs` | Chat review |
| P6 | Unissued license keys never returned: asset responses carry only total and remaining stock. The asset response type has no field for keys at all, so this is protection by omission | `Services/AssetViews.cs` (builds the response, L82), `Contracts/AssetContracts.cs` (`AssetDto`, L38) | Me, following up on agent comment post-build |
| P7 | Parameterized SQL for row locks (`FromSql` with interpolated values) and all-or-nothing allocation (rows locked with `FOR UPDATE`, then checked and deducted in one transaction), with a test for two allocations at once | `Services/AllocationService.cs`, `Endpoints/AssetEndpoints.cs`, `Endpoints/TicketEndpoints.cs`; `tests/…/AllocationTests.cs` | Chat review |
| P8 | Page size capped at 100 | `Services/Query.cs` (`MaxPageSize`) | Chat review |
| P9 | Errors returned as problem details, no stack traces | `Program.cs` | Chat review |
| P10 | Asset delete refused (409) while any ticket item or allocation refers to the asset, so issued keys, tokens and their history can't be deleted with it; the database restricts those deletes as well | `src/ResourceHub.Api/Endpoints/AssetEndpoints.cs` (`DeleteAsync`), `Data/HubDbContext.cs` | Chat review |

**Not protected:** asset and ticket reads are open to anyone, unlike attempt 1, and ticket reads return the user ids that P1 relies on (see M2).

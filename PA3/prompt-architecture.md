# PA3 PROMPT ARCHITECTURE (prompt-architecture.md)

* Student Name: Essie Anawalt
* Assigned Domain Module: Domain 2: Resource Allocation & Help Desk Hub
* Core Technology Stack: C# / ASP.NET Core (.NET 10) / Entity Framework Core / PostgreSQL 18 / Docker Compose
* Own Repository URL: https://github.com/essieanawalt/cs763-resource-hub
* Peer Repository URL (your fork or copy, where pa3-measured is tagged): https://github.com/essieanawalt/artifact-indexer
* Peer Domain Module and Stack: Domain 1: Project Artifact & Repository Indexer, Python / FastAPI / PostgreSQL / uv
* Use these headings and metadata fields as given. The prompt content in Section 3 is your own.

================================================================================

AI DELIVERABLE METADATA BLOCK

================================================================================
* Conceptual AI Contribution to this document: ~40% 
  * human: decisions, scope and review mine; 
  * assistant: copying and capturing the run data by the assistant, plus redrafting, which saved me time
* Summary of design-aid prompts: I set the scope, chose the threats (mine and the peer's), made the setup, key-handling, test and role decisions, and reviewed every section, asking for trims, accuracy checks and two additions to the prompt. The Claude chat assistant (claude.ai, Claude Opus 5.5) proposed options, wrote the prompt text and this document's wording from those decisions, and copied the prompts in unchanged from the frozen files.
* Chat share link(s) or appendix path in ./.ai_logs/: `PA3/.ai_logs/2026-11-26_prompt-architecture-log.md`

================================================================================

SECTION 1: ENGINE AND RUN METADATA

================================================================================

1.1 Engine and Model

| Field | Value |
|---|---|
| Agent tool and version | Claude Code 2.1.330 (Claude desktop app 6.28899.6, macOS 27.2.1, arm64), the same for both runs |
| Engine / provider | Anthropic |
| Model string | `claude-opus-5-5`, effort Medium (confirmed in the session info of both runs) |
| Operational mode | Local, on my machine. Sessions started in Manual and were switched to **Auto** before the prompt was sent, so each iteration ran without approvals. Sandbox off, no MCP servers. Each run started in its working folder (own: `PA3/secure-codebase/`; peer: `PA2/functional-codebase/`) with a local, uncommitted `.claude/settings.local.json` that pins the model, turns off auto-updates and denies reads of my logs, documents, the other repository, the token issuer and my prompts; `/add-dir` added only the task-notes folder (`MyAI/PA3`, `MyAI/PA3-peer`) |
| Hyperparameters changed from PA1 Agent_environment.md | Model pinned to the full id instead of the `opus` alias, and auto-updates off (both since PA2; now set in the local settings file, because the repository's settings don't load when Claude Code starts in a subfolder); Claude Code 2.1.283 in PA1 → 2.1.330 (it changed even with auto-updates off, likely with the desktop app); permission mode Manual → Auto for the measured runs; the `caveman` plugin off (since PA1 prompt 3). Effort Medium unchanged. Temperature and other sampling settings can't be set in Claude Code |

1.2 Test/Production Generation Order

Order used: Test-first
Brief reason: Same order as my PA2 rebuild, so the phases compare, and the tests for each control are written before the code that has to pass them.

1.3 Iteration Count (measured run, maximum 2 per repository)

| Repository | Starting tag | Iterations used | Ending tag | Build result at pa3-measured |
|---|---|---|---|---|
| Own | pa2 (commit 5211272) | 1 | pa3-measured (on `bd9772c`) | Builds (local Docker Compose and CI) |
| Peer | assignment-2-v1.0.0 (commit 1a094d6) | 1 | pa3-measured (on `fd093ca`, fork branch `pa3-peer`) | Builds (Docker Compose) |

Starting points: the own run started on branch `pa3-own` from `pa2`, plus two setup commits with no code changes (an unchanged copy of `PA2/functional-codebase/` into `PA3/secure-codebase/`, and deny rules). The peer run started on `pa3-peer` at `assignment-2-v1.0.0` with no commits; its deny rules were local and uncommitted.

================================================================================

SECTION 2: REMEDIATION SCOPE

================================================================================

2.1 Confirmed CWEs (all of them, from your sast-triage-report.md; add rows as needed)

| # | CWE ID | Location (file / endpoint) | Short description |
|---|---|---|---|
| 1 | CWE-290 | `Services/Actors.cs` | The `X-User-Id` header is accepted as given, so anyone can act as any user |
| 2 | CWE-862 | `Endpoints/TicketEndpoints.cs`, `Services/TicketViews.cs` | Ticket list and detail are open to anyone and return user ids and notes |
| 3 | CWE-312 | `license_keys` table; `Services/AllocationService.cs`, `Endpoints/AllocationEndpoints.cs` | License keys are stored and returned in plain text |
| 4 | CWE-221 | `Data/HubDbContext.cs` L114; `Endpoints/TicketEndpoints.cs` (`DeleteAsync`) | A ticket's event history is deleted with the ticket |
| 5 | CWE-532 | `Endpoints/AssetEndpoints.cs` L77 | The full device token is written to the application log on return |
| 6 | CWE-1392  | `docker-compose.yml` L7–L10, L24; `appsettings.Development.json` | The database is reachable from the network as superuser with the committed fallback password (also: CWE-1327, CWE-250, CWE-260)|

2.2 STRIDE Threats (exactly 3, from your security-analysis.md)

| # | STRIDE category | DFD component | One-line risk rationale |
|---|---|---|---|
| T1 | Tampering (PS4-T) | PS4 Allocation service (device return) | A return trusts the device token alone, so a device can be marked returned while it is still out (AC4) |
| T2 | Repudiation (PS3-R, PS4-R, DS2-R) | DS2 Event log, written by PS3 Ticket logic and PS4 Allocation service | Asset changes and returns leave no event and no verified identity, so changes can't be traced to who made them (SR11) |
| T3 | Denial of service (PS1-D) | PS1 API endpoints | No rate limiting, so one caller can flood the API and push older log lines out (G5, CWE-770; 60 requests got no 429) |

Peer targets: my Semgrep baseline on the peer's PA2 tag found 0 issues and 0 errors, so I had no triaged peer CWEs to work from. I set the peer targets in 3.4.b as:
- three threats I chose from STRIDE candidates the assistant drew from the Domain 1 spec, reworded so each mirrors my own T1–T3: PT1 lost updates to a project or its artifacts, PT2 no record of who changed, deleted or downloaded (kept after deletes), and PT3 unbounded upload size, made worse by the zip of all manuscripts
- the five minimum controls
- CWEs mapped from the missing controls (CWE-306, CWE-862, CWE-312), which are my interpretation from the definitions and the peer's README, not triage

Putting the peer targets in 3.4 is also my interpretation of "one labeled service-context section".

================================================================================

SECTION 3: SECURE-BY-DESIGN PROMPT ARCHITECTURE

================================================================================

Record the prompts exactly as given to the agent. Sections 3.1 to 3.3 are applied unchanged to both repositories. Section 3.4 is the one labeled service-context section, and it is the only part that may differ between repositories. Section 3.5 records the scan output added in iteration 2, if used.

3.1 System / Role Prompt

Sent as one message per run: 3.1–3.3 followed by that repository's 3.4. The text is copied unchanged from the frozen files `prompts/PA3/2026-11-26_01_secure-by-design_own.txt` and `_peer.txt` (frozen 2026-11-26 11:58); 3.1–3.3 are byte-identical in both.

```text
## 3.1 Role

You are a defensive security engineer hardening a working web service that was built without security requirements. Make it secure by design and keep its functional behaviour, following 3.2 (security constraints), 3.3 (output and verification) and 3.4 (service context). You may refactor or rewrite any code in your working folder.

Work only in your working folder, the notes folder added to this session and any file 3.4 names. Don't read, search or list anything else. If you need something outside those places, record it in your task note instead.

If anything is unclear, make the most reasonable choice, record it in your task note and carry on. Don't stop to ask.
```


3.2 Security Constraints

Show where your prompt covers each minimum requirement.

| Requirement | Prompt section / line that enforces it |
|---|---|
| (1) RS256 or ES256 verification with algorithm allowlist (reject "none" and HS256) | 3.2 item 1: "Read the public keys from the JWKS file at JWT_JWKS_PATH. Choose the key by the token's `kid`…" and "Allow only RS256. Reject `none`, HS256 and every other algorithm." Tested by the token cases in 3.3 item 2 |
| (2) exp, iss, and aud claim validation | 3.2 item 1: "Require `exp`. Validate `exp`, `nbf` (when present), `iss` against JWT_ISSUER and `aud` against JWT_AUDIENCE…"; values fixed in 3.3 item 3 (`http://localhost:9000`, `cs763-api`) |
| (3) Row-level authorization on the primary resource | 3.2 item 2, with the primary resource and its rules in 3.4 ("Primary resource: …") |
| (4) Application-layer encryption of the sensitive field, key from environment variable, never committed | 3.2 item 3 (AES-256-GCM, FIELD_ENCRYPTION_KEY, placeholder only in `.env.example`), the field in 3.4 ("Sensitive field: …"), and 3.3 item 3 (key passed through Docker Compose) |
| (5) Input validation on all write endpoints | 3.2 item 4 |
| Remediation of each CWE in 2.1 | 3.2 item 5 and 3.4.a "Weaknesses to fix" (all six, with files); peer: 3.4.b "Weaknesses to fix" (mapped CWEs) |
| Mitigation of each threat in 2.2 | 3.2 item 5 and 3.4.a "Threats to mitigate" (T1–T3); peer: 3.4.b (PT1–PT3) |

```text
## 3.2 Security constraints

These apply to every endpoint except the health check, which stays public.

1. Authentication: JSON Web Tokens verified locally, with no identity provider and no network calls.
   - Read the public keys from the JWKS file at JWT_JWKS_PATH. Choose the key by the token's `kid` and reject an unknown `kid`. Use only keys from that file; ignore any key or key location in the token header.
   - Allow only RS256. Reject `none`, HS256 and every other algorithm.
   - Require `exp`. Validate `exp`, `nbf` (when present), `iss` against JWT_ISSUER and `aud` against JWT_AUDIENCE (a string, or an array that includes it). Allow at most 60 seconds of clock skew.
   - Missing or invalid tokens get 401.
   - Identity is the `sub` claim; roles are the `roles` claim (a JSON array). Never take identity, ownership or role from the request body, query string or other headers.
2. Row-level authorization on the primary resource in 3.4. Every read, update and delete of a record checks the caller against that record, not only their role. Lists and searches return only records the caller may read. A record the caller may not access returns 404, like one that doesn't exist. A role not allowed an action at all gets 403.
3. Application-layer encryption of the sensitive field in 3.4. Encrypt it in the service before storing it; decrypt it only for a caller allowed to see it. Use AES-256-GCM with a unique random nonce per value and the key from FIELD_ENCRYPTION_KEY. Never commit a key: only a placeholder in `.env.example`, `.env` in `.gitignore`, the key passed to the container through Docker Compose. Encrypt every stored value, including seed data.
4. Validate input on every write endpoint and reject invalid input with 400 or 422.
5. Fix every weakness and mitigate every threat in 3.4. Where 3.4 leaves a choice to you, keep it simple and explain it in your task note.
6. Don't weaken or remove protections that already exist.
```

3.3 Output and Verification Constraints

(e.g., required tests, Docker Compose build, PA2 integration tests updated for authentication, file header and commit tag rules)

```text
## 3.3 Output and verification

1. Work test-first: write or update the tests for each change before the production code.
2. Tests:
   - Update the existing tests to authenticate with tokens and keep them passing.
   - Tests create a throwaway RSA key pair in test code, write a test JWKS file and sign their own tokens. Don't use or look for real key files.
   - Token cases: valid; another user's valid token (accepted, but refused this user's records); expired; expired by 2 minutes; not yet valid; missing `exp`; wrong issuer; wrong audience; audience array that includes ours (accepted); `alg: none`; HS256 signed with the public key; an algorithm not on the allowlist; bad signature; payload changed after signing; unknown `kid`; `jku` header pointing elsewhere; malformed.
   - Also cover row-level authorization, role refusals, encryption at rest and on return, input validation, and each weakness and threat in 3.4.
3. The service builds and starts with Docker Compose (see 3.4) and the full test suite passes. Compose mounts the JWKS file from the host path JWKS_HOST_PATH, read-only, and sets JWT_JWKS_PATH, JWT_ISSUER, JWT_AUDIENCE and FIELD_ENCRYPTION_KEY from the environment or `.env`. Tokens come from issuer `http://localhost:9000` with audience `cs763-api`; use these as the configured values. Tests that run against the Compose stack point JWKS_HOST_PATH at the test JWKS file. List every variable in `.env.example`, placeholders only.
4. Update the README in your working folder: how to configure and run the service with tokens; file paths and line ranges of the token signature verification and of the row-level authorization checks; the 404 / 403 rule; where encryption happens.
5. Put this header at the top of every source file you create or change, in its comment syntax (skip JSON, lock files, .md, .gitignore). Language stack is in 3.4. Replace any older one-line `AI Contribution Weight` header.
   // Tool: Claude Code (Claude Opus 5.5)
   // Language stack: <from 3.4>
   // Prompt Summary: "<one-line summary of this task>"
   // AI Contribution: <what you wrote> (~<percent>%)
   // Human Modifications: None yet
   // Verification: <tests or checks that cover this file>
   // Confidence: <Low / Medium / High>
6. When you finish, write a task note in the notes folder added to this session, named `YYYY-MM-DD_NN_short-name.md`, with these headings: What I was asked, What I did, Files created / changed, Decisions I made (and why), Problems / things I couldn't do, Commands run / test results. List each weakness and threat from 3.4 and where it is fixed, or why it isn't.
7. Don't commit, push, tag or change git settings. Stop when the work is done and the tests pass, or when you can't go further, and say which.
```

3.4 Service-Context Section (the only labeled section that changes per repository)

Include that repository's domain, stack, primary resource, and designated sensitive field (see PA3_domain_security_definitions.md in the Google Drive folder).

3.4.a Own repository

```text
## 3.4 Service context

Service: Resource Allocation & Help Desk Hub. Help desk tickets request hardware, software licenses and cloud credit vouchers for courses and research labs; approved tickets are allocated from asset pools.
Language stack: C# / ASP.NET Core (.NET 10), PostgreSQL 18, EF Core
Working folder: `PA3/secure-codebase/` (service in `src/`, tests in `tests/`). The build files at the repository root (`Dockerfile`, `docker-compose.yml`, `global.json`, `Directory.Build.props`, `ResourceHub.slnx`) and `CLAUDE.md` belong to an earlier version: read them if needed, don't change them. Create a Dockerfile and `docker-compose.yml` in your working folder that build and run this copy.

Identity and roles:
- Token `sub` values are the ids of the seeded users. Course and lab association comes from the seeded data.
- Token roles: `admin` (the spec's IT director and administrator), `faculty_instructor` (course instructor and research lab advisor). Any other authenticated caller has no role-based rights.

Primary resource: help desk ticket (`/api/v1/assets/tickets/{ticket_id}`) and the allocations it produces.
- Any authenticated caller may read and search the asset catalog.
- A `faculty_instructor` may create tickets and may read only the tickets they submitted. They may update or delete their own tickets only while the ticket is pending.
- A `faculty_instructor` sees only allocations for the courses or labs they are associated with.
- Only `admin` may approve or reject a ticket (the PUT state transition), run `/allocate`, create, update or delete assets, and process returns.
- The ticket listing and the allocation listing return only rows the caller may read.

Sensitive field: the software license key value, wherever it is stored. Return it decrypted only to `admin` and to the requester of the allocation it belongs to. Keys still unallocated are returned decrypted only to `admin`. Asset search doesn't need to search the key value.

Weaknesses to fix (paths relative to `src/ResourceHub.Api/` unless stated):
- CWE-290, `Services/Actors.cs`: the `X-User-Id` header is accepted as given, so anyone can act as any user.
- CWE-862, `Endpoints/TicketEndpoints.cs`, `Services/TicketViews.cs`: ticket list and detail are open to anyone and return user ids and notes.
- CWE-312, `license_keys` table, `Services/AllocationService.cs`, `Endpoints/AllocationEndpoints.cs`: license keys are stored and returned in plain text.
- CWE-221, `Data/HubDbContext.cs`, `Endpoints/TicketEndpoints.cs` (`DeleteAsync`): a ticket's event history is deleted with the ticket.
- CWE-532, `Endpoints/AssetEndpoints.cs`: the full device token is written to the application log on return.
- CWE-1392 (with CWE-1327, CWE-250, CWE-260), root `docker-compose.yml` and `appsettings.Development.json`: the database is reachable from the network as a superuser with a committed fallback password. Your Compose file must not repeat this.

Threats to mitigate:
- T1 Tampering, allocation service (device return): a return trusts the device token alone, so a device can be marked returned while it is still out. Decide what a return must prove.
- T2 Repudiation, event log: asset changes and returns leave no event and no verified identity. Every change to tickets, stock and devices must be recorded with the caller's `sub`, the time and the outcome.
- T3 Denial of service, API endpoints: there is no rate limiting, so one caller can flood the API. Add rate limiting with limits you choose; the tests must still pass.
```


3.4.b Peer repository

```text
## 3.4 Service context

Service: Project Artifact & Repository Indexer. Projects belong to labs, department programs and courses, and hold three kinds of artifacts: manuscripts (with uploaded files), presentation videos and Git repositories.
Language stack: Python / FastAPI / PostgreSQL 17 / uv
Working folder: `PA2/functional-codebase/` (application in `app/`, tests in `tests/`). Harden the service in place. The Compose file is `../docker-compose.yml`; you may read and change it. Don't read `../security-analysis.md`, `../sast-triage-report.md`, `../.ai_logs/` or `../evidence/`.

Identity and roles:
- Token `sub` values are member ids. Course and lab association for faculty comes from the service's local static mock data.
- Token roles: `admin`, `faculty_instructor`, `student`.

Primary resource: Project (`/api/v1/projects/{project_id}`), including its artifacts.
- Any authenticated caller may read project profiles and artifact metadata and use the search endpoint.
- Only members listed on the project, or `admin`, may update or delete the project, and add, update or delete its artifacts.
- Only project members, `faculty_instructor` users of the project's course or lab, or `admin` may download manuscript files (single file or zip).
- The creator's `sub` is added as a member when the project is created.

Sensitive field: manuscript file content (the bytes uploaded to `/artifacts/manuscripts/{artifact_id}/file`), wherever it is stored. Encrypt the bytes before storing them and decrypt them on download, including in the zip download. Compute the stored file hash on the plaintext so integrity can still be checked.

Weaknesses to fix:
- CWE-306: no endpoint requires authentication.
- CWE-862: no authorization on projects, artifacts or manuscript downloads.
- CWE-312: manuscript files may be stored in plain text.

Threats to mitigate:
- PT1 Tampering, project and artifact updates: updates to a project or its artifacts can silently overwrite newer changes. Make a stale update fail instead.
- PT2 Repudiation, change and download record: there is no record of who changed, deleted or downloaded a project, artifact or manuscript. Record the caller's `sub`, the action, the time and the outcome, and keep the record when the project or artifact is deleted.
- PT3 Denial of service, manuscript upload and zip download: file size is unbounded, and the zip of all manuscripts multiplies it. Limit upload size and protect the zip download; choose the limits.
```

Choices I made that the prompt carries:
- the same environment variable names in both repositories, so the runs compare
- RS256 only, since the issuer's keys are RS256
- roles from the token, and course or lab association from local data
- in my service, the IT director and administrator both map to `admin`, and course instructor and lab advisor to `faculty_instructor`, as in the definitions document
- 404 for records the caller can't access, 403 for a role that's never allowed the action
- the 17 token cases from the issuer's public test set named as required tests
- the peer's mapped CWEs given to the agent as plain weaknesses, with the "interpretation" label kept in my own records
- one fixed reply if the agent asked a question mid-run ("Proceed with your best judgment and record the decision in your task note"), which neither agent needed


3.5 Iteration 2 Input (if used)

Iteration 2 may add only the scan output from iteration 1. State what was added.

| Repository | Scan output added (tool, file or excerpt location) |
|---|---|
| Own | None: no iteration 2 (iteration 1 scan output had no security finding) |
| Peer | None: no iteration 2 (the only scan finding was triaged as a false positive) |

================================================================================

SECTION 4: MEASURED RUN LOG

================================================================================

One row per iteration per repository.

| Date | Repository | Iteration | Agent stop reason | Scan tool(s) | Findings before / after | Notes |
|---|---|---|---|---|---|---|
| 2026-11-26 | Own | 1 | Agent finished: work done, 239 tests passing (about 34 minutes, Auto mode) | Semgrep, SonarQube, Trivy | Semgrep 0 → 1 (false positive, my triage; 2 partial parses); SonarQube: 22 new issues, all low-severity code smells, 0 bugs, 0 vulnerabilities, 0 hotspots; Trivy: 0 dependency vulnerabilities, image 0 high or critical (18 medium, 6 low; PA2 image 31 medium, 5 low) | No iteration 2: the scan output has no security finding to hand back. Scans: Semgrep and Trivy local with the pipeline's pinned images; SonarQube through the pipeline on a pull request, after a config-only commit (`29d1ddd`) |
| 2026-11-26 | Peer | 1 | Agent finished: work done, 173 tests in-process and 165 against Compose (8 skipped) (about 16 minutes, Auto mode) | Semgrep | 0 → 1 (`python-logger-credential-disclosure`, `app/security.py` line 132: logs the rejection reason, not the token; false positive, my triage) | No iteration 2. Supporting check at pa3-measured, not part of the measured scan: all five controls and PT1–PT3 hold (`peer-evidence/peer-control-check-measured-2026-11-26.txt`, `peer-threat-check-measured-2026-11-26.txt`) |

Peer "findings before" is my Semgrep baseline on the peer's PA2 tag (Semgrep 1.180.0 with a pinned digest, `p/python` + `p/security-audit` + `p/jwt`, scope `PA2/functional-codebase`); the rescan used the same configuration. Because Semgrep alone can't show whether the controls exist, I also ran a small check, fixed before the run, on the baseline (threats only) and at pa3-measured, outside the measured scan. It deviates from "Semgrep alone is enough" on purpose; the reasons and results are in cross_service_remediation_analysis.md (sections 3 and 5) and the conceptual journal.

================================================================================

SECTION 5: POST-MEASURED HARDENING (OWN REPOSITORY ONLY)

================================================================================

Summarize changes made after the pa3-measured tag and how they were made (AI, HUMAN, or HYBRID commits).

| Commit tag | Change | Reason |
|---|---|---|
| AI: (`69131cc`) | H1: separate database roles. A one-shot `migrate` container runs migrations as a new owner role; the API's role owns nothing, can only read and write data, and can only read and add rows in `ticket_events` and `audit_events` | The agent's own named gap: the app role owned its tables and could drop the history triggers. Checked on the running stack: every table owned by `resourcehub_owner`; the API's role refused disabling or dropping triggers, dropping tables, deleting audit rows and creating tables; no owner credentials in the API container |
| AI: (`69131cc`) | H2: API health check (`dotnet ResourceHub.Api.dll --healthcheck`, no extra packages) | Compose could only report the API as "up". Now reported `healthy` |
| AI: (`69131cc`) | H3: encryption key passed as a Docker secret file; containers get only its path in `FIELD_ENCRYPTION_KEY_FILE` (still located by an environment variable, as the minimum requirement asks) | `docker inspect` showed the key. Checked: the API's environment has the path only |
| AI: (`69131cc`) | H4: the 22 SonarQube code smells fixed (generated log methods, constant arrays, return type, `AddAuthorizationBuilder`); CA1861 turned off only for already-applied migrations in a new `.editorconfig` | Tidier scans without editing applied migrations again. SonarQube after the push: 0 issues and 0 security hotspots, down from 22 (`PA3/evidence/own/sonar-*-hardening.json`) |

Run: Claude Code 2.1.330, `claude-opus-5-5` Medium, Auto, prompt `prompts/PA3/2026-11-26_02_post-measured-hardening.txt`, about 19 minutes, 297 tests passing (9 live Compose tests), transcript in `PA3/.ai_logs/own/hardening/`. Remaining follow-ups the agent named: database passwords are still visible on the `migrate` and `db` containers, and on Linux the key file needs to be readable by the API's user.

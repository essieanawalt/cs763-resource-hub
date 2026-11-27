# MASTER CONCEPTUAL JOURNAL & AI TELEMETRY LOG

* **Student Name:** Essie Anawalt
* **Assigned Domain Module:** Domain 2: Resource Allocation & Help Desk Hub
* **Core Technology Stack:** C# / ASP.NET Core (.NET 10) minimal API / Entity Framework Core / PostgreSQL 18 / Docker Compose
* **Peer repository (PA3 track 2):** Domain 1: Project Artifact & Repository Indexer, Python / FastAPI / PostgreSQL / uv

Running journal of how I work with AI tools in PA3: what they got wrong, where I caught it, and what I changed as a result. Entries were drafted with the chat assistant during our sessions and edited by me.

================================================================================

## SECTION 1: SYSTEM ENVIRONMENT & TOOL SPECIFICATION

================================================================================

### 1.1 AI Tool & Engine Deployments

| Tool Context | Framework / Platform Name & Version | Underlying LLM Brain Model String | Operational Setting / Access Mode |
| :---- | :---- | :---- | :---- |
| **Code Generation** | Claude Code 2.1.330 in the Claude desktop app 6.28899.6 (macOS 27.2.1, arm64) | `claude-opus-5-5`, Medium effort | Local; started in each run folder with local deny rules and the model pin; Auto mode; no sandbox or MCP servers (details in `PA3/Agent_environment.md`) |
| **Document/Design** | Claude chat (claude.ai and the Claude desktop app) | Claude Opus 5.5 (`claude-opus-5-5`) | Cloud chat in a project, linked to my Project folder through the desktop app; it read and edited documents and logs, wrote the check scripts, and I ran every command, check and commit myself |
| **Test tooling** | Mock token issuer `schin8/cs763-mock-token-issuer` v1.0.0 (AI-generated with Claude Code, per its README) | n/a | Local only, in its own Python environment, outside both repositories; its own tests passed before I used it |

### 1.2 Verification Telemetry Access Checklist

- [x] Primary Auto-Extracted Session Logs / Appendices Saved to `/.ai_logs/` directory
  - Claude Code, own run: `PA3/.ai_logs/own/iteration-1/`
  - Claude Code, peer run: `PA3/.ai_logs/peer/iteration-1/`
  - Chat assistant (manual log): `PA3/.ai_logs/2026-11-26_prompt-architecture-log.md`
- [ ] External Chat Cloud Share Link 1 (If applicable): n/a
- [ ] External Chat Cloud Share Link 2 (If applicable): n/a

================================================================================

## SECTION 2: COGNITIVE REFLECTION — CODE GENERATION & COMPREHENSION

================================================================================

### 2.1 The Architectural Flow (How You Understand the Code)

> Paths are inside `PA3/secure-codebase/src/ResourceHub.Api/`.

The structure is still the same as it was in PA2. It's a minimal API with small handlers in `Endpoints/`, a few static service classes and EF Core as the data layer, with no repository layer. I won't go into more detail since it hasn't changed. The part that's new is everything in front of those handlers: a request is authenticated, then counted by a rate limiter, and anything without a valid token is refused upfront, except the health check (`Program.cs` 42–60).

The authentication checks the token against the public keys in a local JWKS file, so nothing is fetched over a network connection. The token's key id (`kid`) picks the key, only RS256 is accepted (so `none` and HS256 are refused), and issuer, audience and expiry are checked with 60 seconds of clock skew instead of .NET's default 5 minutes. The caller's id and roles come only from the token, and the seeded user row just supplies their course or lab (`Security/JwtAuthentication.cs`, `Services/Actors.cs`).

Each handler then asks two questions. Is this role ever allowed to do this? If not, it's 403 before any record is read. If the role is fine, can this caller access the record? If not, it's 404, the same as a record that doesn't exist. Faculty only see their own tickets and can only change them while they're pending, and admins can do everything. Listings are filtered before counting, so even the totals don't show other people's tickets.

License keys are encrypted with AES-256-GCM before they're stored and decrypted only when the response goes to an admin or the person who requested them, and each value is tied to its row so it can't be copied to another one (`Security/FieldEncryptor.cs`). The key comes from `FIELD_ENCRYPTION_KEY`, and the service won't start without it. 

The other fixes are smaller and listed with each CWE in the results table: ticket history is kept and the database refuses changes to it, the log no longer carries device tokens, the database is no longer reachable from outside or run as superuser, returns need a check-in code, and every change is recorded with who made it.

One thing I noticed is that the agent leaned on what the framework already provides (the built-in JWT handler and rate limiter) rather than writing its own. This was similar to PA2. What's different is that it also put protection into the database itself, with triggers that block changes to the history, and it added a separate `Security/` folder, so the code has one more layer than before. Signing keys can be rotated, since the JWKS file can hold more than one, but it's only read at startup, so a change needs a restart. The encryption key has no rotation yet.

### 2.2 Defensive Edge-Case Evaluation (Line-by-Line Code Review)

Edge cases I found while reviewing and checking the code at `pa3-measured`, none of them covered by the scanners:

- **Own: a token for an unknown user still authenticates.** The `sub` only has to be a GUID, so a valid token for a user who isn't seeded gets in with its token roles and no course or lab. It can't submit tickets, but an admin token like that would be a full admin. It rests on trusting the issuer, which is fine here, but it's worth knowing.
- **Peer: 404 for a project the caller can read.** A non-member gets 404 when they try to update a project they just read. 404 is meant to hide that a record exists, which doesn't apply here, so 403 would be the clearer answer.
- **Peer: early rejections leave no audit record.** Bad tokens (401), bad input (422) and oversized bodies are refused before a route runs, so the audit table never sees them. Probing the API leaves no trace there.
- **Peer: the encryption key can be read with `docker inspect`.** The prompt asked for an environment variable, so this is as much on my prompt as on the agent.

### 2.3 The AI Agent's "Security Sins" (Evaluating Code Deficiencies)

#### 2026-11-26 — The deny rules didn't load from the folder the agent starts in

I start Claude Code inside the code folder so my logs sit outside it, and I put the PA3 deny rules in the repository's `.claude/settings.json`. In the test session a denied file brought up a permission request instead of a refusal, and when I approved one, the agent read `PA2/Agent_environment.md` and told me nothing had blocked it. Settings at the repository root only load for sessions started at the root, so my PA2 subfolder runs may not have had deny rules either. The fix was a local settings file in each run folder with absolute paths, and the re-test from both folders refused the read without asking. This mattered more than planned, because I then switched the runs to Auto, and with no approvals the deny rules were the only thing keeping the agent out of my files.

#### 2026-11-26 — Own run: what the agent did beyond the prompt

- **It rewrote history instead of adding to it.** To keep plaintext keys out of a fresh database, it edited the already-applied `InitialCreate` migration rather than only adding a new one, so a database built from the PA2 version no longer matches the migration history.
- **It found and fixed a fault in its own tests.** One run failed 212 tests because parallel test classes could sign with a different throwaway key than the one in the test JWKS. It fixed it and reran the suite 10 times; stopping at the first run would have looked like a broken auth layer.
- **It named the gap it left.** The app's database role still owns its tables, so it could drop the history triggers: the same root cause as my V6, only narrower.

#### 2026-11-26 — Peer run: same prompt, different service

- **It finished faster with less code.** About 16 minutes and about half the code of my run, though it also started from a much smaller service.
- **It chose stricter designs.** No update without an `If-Match` header (428), and any role outside the Domain 1 set refused, which meant the issuer's own test tokens (role `user`) couldn't create anything.
- **It reported its own scope slip.** It listed `PA2/` and read `PA2/.gitignore`, but opened none of the denied files.
- **It protected the existing data.** It ran its Docker tests under a separate project name so it wouldn't touch the existing volumes.
- **It named the gaps it left.** Early rejections aren't audited and the key is visible through `docker inspect`. As in my own run, that made the results table easier to fill honestly.

================================================================================

## SECTION 3: COGNITIVE REFLECTION — DOCUMENT AUTHORING & THREAT DESIGN

================================================================================

### 3.1 AI Collaboration Matrix for Security Analysis

#### 2026-11-26 — The peer scan came back clean, so I had to decide what to measure

My fresh Semgrep baseline on the peer's PA2 tag came back with 0 findings and 0 errors. This meant I didn't have the needed CWEs for the peer table for the comparison. I asked for options, and the assistant came back with four: record the 0 as it is and use the threats and minimum controls as rows; widen the rulesets; review the peer code beyond what Semgrep flags; or map the missing controls to CWEs labeled as my interpretation. I chose the first plus the last. Widening the rulesets after a clean result looks like hunting for findings, and reviewing the code breaks my rule of not studying the peer service. A clean scan of code with no authentication, authorization or encryption at all says more about what pattern-based SAST can see than about the code, the same lesson as PA2's "scanners passed code I could take over with one header".

#### 2026-11-26 — Richer results or a cleaner experiment

The instructions say Semgrep alone is enough for the peer, but the results table asks whether each row was resolved, and Semgrep can't see whether authentication or encryption exist. Followed exactly, "resolved" would rest on the agent's own report and the two repositories would be measured differently. This was another instance where I didn't have data to work with to meet the final requirements. The assistant kept offering small, reasonable additions: a control check at `pa3-measured`, then the threat requests on the baseline too. This was the dilemma: stop at Semgrep and follow the instructions exactly, which is better for the experiment, or accept that the result didn't feel like "enough" for a graded assignment. Ultimately, I added the checks anyway, because this is also for my own learning and a real before and after teaches me more. I kept them small, fixed before the run and separate from the measured scan, and the limitations note says what I added. In my eyes, it's the same pattern as PA2, where scope grew one reasonable addition at a time. The difference this time is that I set the limit myself: two small checks, fixed before the run, and nothing after it. I've gotten better at deciding when it's enough.

### 3.2 Evaluation of Automated Conceptual "Blind Spots"

#### 2026-11-26 — Mistakes in the assistant's work that I or the results caught

- **It overstated its own share.** After drafting the prompt from my decisions, it said only my hand edits would count as my work and all else would be considered AI. The scope, threats, setup and review decisions were mine; it only wrote the wording. I ran into this in PA2, as well. Even if I wrote a paragraph and told it to add the paragraph directly, it would claim more authorship over the output. This is a gap in its ability to reconcile what authorship and ownership mean. 
- **Its first key choice would have failed a test.** It first suggested mounting `public.pem`. Reading the issuer's README showed that a service holding one key might ignore the `kid` and accept the `unknown_kid` token, so we switched to `jwks.json`.
- **Its control check used the wrong role.** The script used the issuer's test tokens, which all carry the role `user`, not a Domain 1 role, so the first run measured role refusals instead of row-level checks. It found the cause from the output and reran with `student` tokens; the first run is kept as evidence.

================================================================================

## SECTION 4: HUMAN VERIFICATION & AUDIT TRAIL LOG

================================================================================

Evidence paths are inside `PA3/evidence/`.

| Timestamp | Component Target | Testing/Auditing New Methodology | Verified Outcome / Discovered Defect |
| :---- | :---- | :---- | :---- |
| 2026-11-26 | Peer repo build, PA2 tag | Fresh build check, after a first run reused old containers | Both containers healthy, `/healthz` ok (`peer/build-check-pa2.txt`) |
| 2026-11-26 | Peer code, PA2 tag | Semgrep baseline | 0 findings, 0 errors, 9 files, ~100% parsed (`peer/semgrep-pa2-baseline.json`) |
| 2026-11-26 | Peer API, PA2 tag | Threat requests PT1–PT3 from the OpenAPI description | All three unprotected: stale update accepted, no audit record, 50 MB upload accepted (`peer/peer-threat-check-baseline-2026-11-26.txt`) |
| 2026-11-26 | Token issuer v1.0.0 | Its own tests and its independent check of all 17 test tokens | 23 tests passed; all 17 tokens correct for RS256 and ES256 |
| 2026-11-26 | Deny rules in both run folders | Throwaway sessions asking for a denied file | First test: read went through (rules didn't load); after the fix: refused without asking |
| 2026-11-26 | Own transcript, iteration 1 | Checked every read and command outside its folder | Only the allowed root build files; it saw the deny list in its own settings file; nothing denied opened |
| 2026-11-26 | Own stack at pa3-measured | Build check with a real `.env` and the issuer's `jwks.json` | Builds, database port not published, health ok; API container has no health check (`own/build-check-pa3-iter1.txt`) |
| 2026-11-26 | Own code at pa3-measured | Semgrep (local) and SonarQube (pipeline on a pull request), triaged | Semgrep `csharp-sqli` in `DatabaseStartup.cs` line 89: false positive; SonarQube 22 low-severity code smells, 0 security items (`own/`) |
| 2026-11-26 | Own image and dependencies | Trivy, pinned image | 0 dependency vulnerabilities; image 0 high or critical (`own/trivy-*-pa3-iter1.json`) |
| 2026-11-26 | Peer transcript, iteration 1 | Checked every read and command outside its folder | Listed `PA2/` and read `PA2/.gitignore` (self-reported); no denied file opened |
| 2026-11-26 | Peer code at pa3-measured | Semgrep rescan, triaged | 0 → 1: `python-logger-credential-disclosure` in `app/security.py` line 132: false positive (logs the rejection reason, not the token) (`peer/semgrep-pa3-measured.json`) |
| 2026-11-26 | Peer API at pa3-measured | Fixed control and threat check with issuer tokens; rerun with `student` tokens after the first run hit the role mismatch | 14 bad tokens → 401; other user refused (404); manuscript encrypted at rest; bad writes → 422; PT1 428, PT2 audit rows kept after deletes, PT3 413 (`peer/peer-control-check-measured-2026-11-26.txt`, `peer/peer-threat-check-measured-2026-11-26.txt`) |
| 2026-11-26 | Own code at pa3-measured | My review of authentication, authorization, encryption and the CWE fixes | Matches the agent's summary; the role now comes only from the token (the stored role is unused) (section 2.1) |

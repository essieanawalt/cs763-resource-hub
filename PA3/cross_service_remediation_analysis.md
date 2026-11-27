# PA3 CROSS-SERVICE REMEDIATION ANALYSIS (cross_service_remediation_analysis.md)

* Student Name: Essie Anawalt
* Assigned Domain Module: Domain 2: Resource Allocation & Help Desk Hub
* Core Technology Stack: C# / ASP.NET Core (.NET 10) / Entity Framework Core / PostgreSQL 18 / Docker Compose
* Keep it concise. Use one results table per repository, then the analysis and limitations note.

================================================================================

AI DELIVERABLE METADATA BLOCK

================================================================================

* Conceptual AI Contribution to this document: ~50% (the approach, scope, triage and edits are mine; the assistant drafted the tables and text from the run records and evidence)
* Summary of design-aid prompts: I decided how to measure the peer (record Semgrep's 0 as it was, add labeled interpretation rows and a small fixed check), triaged both Semgrep findings, decided against a second iteration in both repositories, and reviewed every section and directed the edits for accuracy and length. The Claude chat assistant (claude.ai, Claude Opus 5.5) set out the measurement options I chose from, wrote the check scripts, and drafted the tables and text from the run records.
* Chat share link(s) or appendix path in ./.ai_logs/: `PA3/.ai_logs/2026-11-26_prompt-architecture-log.md`; evidence in `PA3/evidence/`

================================================================================

SECTION 1: REPOSITORIES

================================================================================

| Field | Own repository | Peer repository |
|---|---|---|
| Repository URL (peer: your fork or copy) | https://github.com/essieanawalt/cs763-resource-hub | https://github.com/essieanawalt/artifact-indexer |
| Peer name (owner of original repo) | n/a | Atharva Nimbalkar ([Glitch18/artifact-indexer](https://github.com/Glitch18/artifact-indexer), release `assignment-2-v1.0.0`) |
| Domain module | Domain 2: Resource Allocation & Help Desk Hub | Domain 1: Project Artifact & Repository Indexer |
| Stack | C# / ASP.NET Core (.NET 10) / EF Core / PostgreSQL 18 | Python / FastAPI / PostgreSQL / uv |
| Starting tag | pa2 | assignment-2-v1.0.0 |
| pa3-measured tag | `bd9772c` | `fd093ca` |
| Iterations used (max 2) | 1 | 1 |
| Builds in Docker at pa3-measured | Yes | Yes |
| Scan tool(s) used | Semgrep, SonarQube, Trivy | Semgrep |

================================================================================

SECTION 2: RESULTS TABLE, OWN REPOSITORY

================================================================================

> "Resolved" rests on the agent's 239 tests (also passing in CI), the scans, my build check and my code review.

| CWE or threat | Resolved in | New issues introduced | Fix tag |
|---|---|---|---|
| CWE-290: identity from the `X-User-Id` header (V1) | Iteration 1 | None found | pa3-measured |
| CWE-862: tickets open to anyone (V2) | Iteration 1 | Faculty asking for another course's allocations get 200 with an empty list instead of 403 | pa3-measured |
| CWE-312: license keys in plain text (V3) | Iteration 1 | Edited the already-applied `InitialCreate` migration instead of only adding one | pa3-measured |
| CWE-221: ticket history deleted with the ticket (V4) | Iteration 1 | The app's database role still owns its tables, so it could drop the history triggers (agent-reported) | pa3-measured |
| CWE-532: device token in the log (V5) | Iteration 1 | None found | pa3-measured |
| CWE-1392 (+1327, 250, 260): database open as superuser with a committed password (V6) | Iteration 1 | API container has no Compose health check | pa3-measured |
| T1: Tampering, device return | Iteration 1 | None found | pa3-measured |
| T2: Repudiation, event log | Iteration 1 | None found | pa3-measured |
| T3: Denial of service, API | Iteration 1 | None found | pa3-measured |
| Scanners at pa3-measured | n/a | Semgrep: 1 (`csharp-sqli`, false positive by my triage) + 2 partial parses; SonarQube: 22 low-severity code smells, 0 bugs, vulnerabilities or hotspots; Trivy: 0 dependency vulnerabilities, image 0 high or critical | n/a |

Design consequence, not an agent error: the definitions map the IT director and the administrator to one `admin` role, so one person can now approve and allocate a ticket (my AC2 and AC7).

================================================================================

SECTION 3: RESULTS TABLE, PEER REPOSITORY

================================================================================

My Semgrep baseline found nothing to triage, so the rows are the 3 threats I targeted, the five minimum controls, and CWEs mapped from the missing controls (*interpretation*, see section 5). "Resolved" comes from a small request check outside the measured scan:

| | Semgrep alone | With the small check |
|---|---|---|
| Baseline (PA2 tag) | 0 findings | PT1–PT3 shown unprotected |
| At pa3-measured | 1 finding (false positive) | All five controls and PT1–PT3 shown to hold |
| What "resolved" could rest on | The agent's own report | One request per row, fixed before the run |

| CWE or threat | Resolved in | New issues introduced | Fix tag |
|---|---|---|---|
| PT1: Tampering, lost update | Iteration 1 (428 without `If-Match`, 412 when stale) | None found | pa3-measured |
| PT2: Repudiation, change and download record | Iteration 1 (`audit_events` rows with `sub`, kept after deletes) | Rejections before routing (401, 422, oversized bodies) aren't recorded | pa3-measured |
| PT3: Denial of service, upload and zip | Iteration 1 (50 MB upload → 413) | None found | pa3-measured |
| Control 1: RS256 allowlist | Iteration 1 (14 bad test tokens → 401) | None found | pa3-measured |
| Control 2: `exp`, `iss`, `aud` | Iteration 1 | None found | pa3-measured |
| Control 3: row-level authorization | Iteration 1 (another user's update, delete and downloads → 404) | A non-member who can read a project still gets 404 on update | pa3-measured |
| Control 4: manuscript encryption | Iteration 1 (plaintext absent from storage) | Key visible through `docker inspect` (env variable, as the prompt asked) | pa3-measured |
| Control 5: input validation | Iteration 1 (bad writes → 422) | None found | pa3-measured |
| CWE-306 (*interpretation*): no authentication | Iteration 1 | None found | pa3-measured |
| CWE-862 (*interpretation*): no authorization | Iteration 1 | None found | pa3-measured |
| CWE-312 (*interpretation, assumed*): plaintext manuscripts | Iteration 1 | None found | pa3-measured |
| Semgrep at pa3-measured | n/a | 0 → 1: `python-logger-credential-disclosure`, logs the rejection reason, not the token (false positive by my triage) | n/a |

================================================================================

SECTION 4: COMPARATIVE ANALYSIS

================================================================================

4.1 Effectiveness: Own vs. Peer

Both runs fixed everything I targeted in one iteration, and neither broke the build, so on paper the results are the same: 9 of 9 rows on my repository and 11 of 11 on the peer. What's different is how much I can back that up. On my own repository I have three scanners, 239 tests and my own review of the code. On the peer I have Semgrep, which can't tell whether authentication or encryption exist at all, and the small check I added. The peer also started from a much simpler place, one Python file with no protections, so the agent finishing in about 16 minutes with about half the code isn't really a fair comparison to the 34 minutes it spent on mine.

4.2 Agent Behavior

The agent stayed inside its folder in both runs, apart from small slips it reported itself, and it didn't open any of the files I'd denied. It didn't break either build or invent framework calls that didn't exist. On my repository it made some heavier choices, like editing a migration that had already been applied, and it found and fixed a fault in its own tests before stopping. On the peer it went stricter than I expected. It refused any update without an `If-Match` header and any role outside the Domain 1 set, which meant even the issuer's own test tokens couldn't create anything. It also followed "no access returns 404" so literally that someone who can read a project gets 404 for trying to update it. In both runs it listed the gaps it left, which made the results table much easier to fill in honestly.

4.3 Prompt Architecture Lessons

The service-context section had to carry a lot more for the peer. It held the domain rules, the roles, where the Compose file sits, which documents were off limits, and the threats I couldn't get from triage. A few things didn't transfer well, and next time I'd move them into the shared sections: the roles the test tokens actually carry, the fact that settings and deny rules only load from the folder the agent starts in, and fixed issuer and audience values. I also wrote my check before the run, which is what I wanted, but it meant I didn't plan for a stricter design like a required `If-Match` header changing what a refusal looks like. If the prompt is meant to work across services, how much should it assume about the service it's going into?

================================================================================

SECTION 5: LIMITATIONS NOTE ON PEER TRIAGE QUALITY

================================================================================

- **Semgrep-only coverage.** Baseline and rescan used the same configuration. All PA2 application code was in one file, test files are skipped by default, and missing controls are absent code that pattern-based SAST doesn't detect.
- **Interpretation rows.** The mapped CWEs come from the Domain 1 definitions and the peer's README, not triage; CWE-312 was assumed until the check.
- **Threat source.** The chat assistant proposed candidates from the Domain 1 spec and definitions; I chose and reworded them. I didn't use the peer's analysis documents or study the service beyond Semgrep's findings and the request shapes the checks needed.
- **Beyond "Semgrep alone".** Without a check, every peer row would rest on the agent's own report, and my two tables would be measured in different ways. I fixed the check before the run, ran it on the baseline (threats only) and at pa3-measured, and kept it out of the measured scan. I chose to go past the instructions here and kept it as small as I could.
- **How the check ran.** The issuer's test tokens carry the role `user`, which the hardened service refuses, so the rerun used issuer tokens with the role `student`; PT1 was confirmed by 428 because the threat script sends no `If-Match`.
- **Unfamiliar domain and stack; no line-by-line review** of the peer code.

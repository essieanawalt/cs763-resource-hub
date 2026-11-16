# MASTER CONCEPTUAL JOURNAL & AI TELEMETRY LOG

* **Student Name:** Essie Anawalt
* **Assigned Domain Module:** Domain 2: Resource Allocation & Help Desk Hub
* **Core Technology Stack:** C# / ASP.NET Core (.NET 10) minimal API / Entity Framework Core / PostgreSQL 18 / Docker Compose

Running journal of how I work with AI tools on this project: what they got wrong, where I caught it, and what I changed as a result. Entries were first drafted by the chat assistant during our sessions and rewritten by me; entries still marked as a first draft haven't been rewritten yet. This is version 2: the same entries as `conceptual_journal.md`, restructured to the course template on 2026-11-15, after the `pa2` release, with the template's new sections added (1.1, 1.2, 2.1 and 4). The entries themselves are unchanged. `conceptual_journal.md` is kept as it was submitted with the `pa2` release.

> Drafted by the Claude chat assistant from the existing conceptual_journal; 

================================================================================

## SECTION 1: SYSTEM ENVIRONMENT & TOOL SPECIFICATION

================================================================================

### 1.1 AI Tool & Engine Deployments

| Tool Context | Framework / Platform Name & Version | Underlying LLM Brain Model String | Operational Setting / Access Mode |
| :---- | :---- | :---- | :---- |
| **Code Generation** | Claude Code desktop app 2.1.325 | `claude-opus-5-5`, Medium effort, pinned in `.claude/settings.json` | Local, on my machine, in the repository folder; Manual permission mode (I approve each file change); deny rules on my logs and evidence (`PA2/Agent_environment.md`) |
| **Document/Design** | Claude chat (claude.ai and the Claude desktop app) | Claude Opus 5.5 (`claude-opus-5-5`) | Cloud chat in a project, linked to the repository folder through the desktop app; it read and edited the documents and logs, and I ran every command and commit myself |

### 1.2 Verification Telemetry Access Checklist

- [x] Primary Auto-Extracted Session Logs / Appendices Saved to `/.ai_logs/` directory: Claude Code session exports in `PA2/.ai_logs/attempt1-code-first/`, `attempt2-tdd/` and `parse-fix/`; manual logs of the chat sessions (`2026-11-13_realignment-log.md`, `2026-11-14_*`, `2026-11-15_review-log.md`, `2026-11-15_security-analysis-log.md`)
- [ ] External Chat Cloud Share Link 1 (If applicable): n/a
- [ ] External Chat Cloud Share Link 2 (If applicable): n/a

================================================================================

## SECTION 2: COGNITIVE REFLECTION — CODE GENERATION & COMPREHENSION

================================================================================

### 2.1 The Architectural Flow (How You Understand the Code)

The service is an ASP.NET Core minimal API. `Program.cs` registers the database context (`HubDbContext`, EF Core with Npgsql), a clock (`TimeProvider`), the allocation service and problem-details errors, sets the JSON options (enums as lower snake case, nulls left out), applies migrations at start-up, and maps four route groups: health, assets, tickets and allocations. There are no MVC controllers: each route is a static handler in `Endpoints/`, and its parameters (route id, request body, `HttpContext`, `HubDbContext`, clock, logger) are filled in by the framework.

For `POST /api/v1/assets/tickets/{ticket_id}/allocate`, Kestrel receives the request and routing picks the handler in `TicketEndpoints.cs`. The handler first calls `Actors.RequireAsync`, which reads `X-User-Id`, looks the id up in the users table and returns 401 for an unknown caller or 403 for the wrong role; this is the only identity check, and it trusts the header (V1). The handler then calls `AllocationService.AllocateAsync`, which opens a transaction and locks the ticket row (`SELECT … FOR UPDATE`, through EF Core's parameterized `FromSql`). If the ticket isn't approved, it rolls back, records an `AllocationFailed` event and returns 409. Otherwise it locks every requested asset row in id order, so two allocations can't deadlock, and checks stock and balances. A shortage also rolls back, records the failed attempt and returns 409 with the shortages. If everything fits, it deducts stock, issues license keys and device tokens (from a cryptographically secure random generator), writes the allocation rows and an `AllocationSucceeded` event, saves, and commits. Any exception before the commit disposes the transaction, which rolls it back, so an allocation is all or nothing.

Write requests bind to fixed request records (`Contracts/`), so fields a caller adds are ignored, and `Services/Validation.cs` and `TicketContent.cs` check them before use, returning 400 problem details. Responses are built by small view helpers (`TicketViews`, `AssetViews`) that map entities to response records. The patterns the agent chose are endpoint route groups instead of controllers, one service class for the transactional allocation logic, static helpers for identity, validation and views, and the EF Core context used directly as the unit of work, with no separate repository layer.

### 2.2 Defensive Edge-Case Evaluation (Line-by-Line Code Review)

#### 2026-11-15 — The scanners passed code I could take over with one header

**What happened:** after the parse fix, Semgrep and SonarQube covered the whole source and reported 0 security findings, 0 hotspots, and a passed quality gate. I was able to use `curl`  to send the admin id in the header and obtained a license key, read a ticket's user with no header at all, and accessed the keys stored as plain text in the db

**Why it matters:** a clean scan and a passed gate said nothing about the three most serious problems, because they're design gaps, not code patterns a scanner recognizes. I spotted the first one by reading the code ("roles can be spoofed") and confirmed all three by testing them directly

---

#### 2026-11-15 — Reading the code wasn't the same as running it

**What happened:** after the review, most requirements were marked "Met (review)" from reading the code, and most of them held when tested. But the runs found things the reading hadn't: a refused approval leaves no event and no log line, ticket history can be deleted, and the database accepts edits and deletes to the event log from outside the API.

**Why it matters:** I kept "Met (review)" and "Met (tested)" as separate labels so the analysis says how each claim was checked. A requirement that only reads as met is still a guess until it's run

---

#### 2026-11-15 — The agent's code was partly unreadable to its own pipeline's scanner

**What happened:** the first pipeline run on the TDD build had 0 findings, but the report showed three files were only partly parsed. This same issue happened in PA1, and it's because of the constructor. `AllocationService.cs` holds the allocation logic, so part of the most important file was never scanned. The agent ran the tests and reported a clean build, but never checked whether the scanner could read its code. The core difference from PA1 is that this agent didn't catch this error on its own.

**How I caught it:** checking the `errors` section and parsed-lines figure (my PA1 lesson). I asked the facilitator whether I could change generated code; the answer was that testability fixes are fine if they're logged and attributed, and security fixes wait for PA3.

**Fix:** I told the agent exactly what Semgrep couldn't parse and asked for only those changes. It rewrote the constructors, kept behaviour and tests the same, and confirmed with a local Semgrep run: 0 parse errors, and still 0 findings.

**Why it matters:** "0 findings" looked clean before the fix but wasn't a full scan. Now the 0 actually covers the whole of the source

### 2.3 The AI Agent's "Security Sins" (Evaluating Code Deficiencies)

#### 2026-11-14 — The agent built a user check it labelled as not real authentication


**What happened:** my build prompt said nothing about users or access, but Claude Code added a `UserHeaderAuthentication` scheme (attempt 1; attempt 2 built the same check in `Services/Actors.cs`): the caller names a mock user in an `X-User-Id` header, and role rules decide who can submit, approve or allocate. Its own comment says it's a stand-in, not real authentication, and that the caller "is trusted to be that user" until a signed token from an identity provider replaces it.

**Why it matters:** the agent knew the weakness and documented it, but still shipped a check anyone can pass by changing one header. A scanner is unlikely to flag it because the code works as written, so it's a manual finding for the triage, and it lines up with the gap I predicted in PA1 (`known-gaps.md` #1 and #10).

**Where the header name came from:** the attempt 1 transcript shows the agent only read the spec, the files in its folder, the Docker and build files and the README. Its only git commands were `git ls-files` and `git status`, and `X-User-Id` first appears in its own plan. It reused the name from my PA1 design without ever seeing that design, which suggests it's simply the conventional choice.

---

#### 2026-11-14 — Same agent, same spec, different protections between runs

**What happened:** attempt 1 (code first) required the `X-User-Id` header on every route except health and never returned unissued license keys. Attempt 2's plan (test first, otherwise the same setup) said all GET routes would need no header and didn't mention the keys. The finished attempt 2 code moved partway back: the allocations listing, which shows issued keys, requires the header and checks the caller's course or lab, and unissued keys are hidden, but listing and reading assets and tickets is open to anyone.

**Why it matters:** the agent's unprompted protections changed between runs and even between its own plan and its code, so neither one run nor the agent's plan is a reliable record of what it built. The security analysis has to come from reading the final code.

**My hypothesis (not confirmed):** asking for TDD may have pulled the agent's limited reasoning at Medium effort toward the test process and away from protections nobody asked for. Temperature variations and the empty starting folder may also explain it, but would need to rerun the same TDD prompt to see for additional comparison.

---

#### 2026-11-15 — Three minor settings added up to the worst finding

**What happened:** a fallback password, a database port open on all interfaces and an app user that's a superuser were each a medium or low candidate on their own. Together they let a separate container log in from outside the compose network as superuser and change stock directly (V6). Two of the three came from the PA1 compose file, not the PA2 build.

**Why it matters:** rating findings one at a time hid the chain, and so did assuming the risk was in the generated code. The worst finding was mostly in configuration

---

#### 2026-11-14 — The agent pinned the pipeline but not the app's own images

**What happened:** in PA1 I noted that Claude Code pinned versions on its own, which held for the GitHub actions and scanners (version plus sha256 digest). Reviewing the Dockerfile and compose file it wrote, the app's images (`dotnet/sdk:10.0`, `aspnet:10.0`, `postgres:18`) use floating tags, so each build can pull a newer image. I caught it when the chat assistant flagged the Trivy image scan jumping from 26 to 36 findings with no code changes.

**Why it matters:** the agent applied a good practice where it was asked to set things up and not where it wasn't, and my PA1 note overstated it. Unpinned images belong in the PA2 security analysis, and I saved a pre-build scan so later changes aren't wrongly blamed on the agent's code.

---

#### 2026-11-14 — Read restrictions held for reading, not for searching

**What happened:** I added deny rules in `.claude/settings.json` for my logs, the PA1 spec and evidence, and tested them in a throwaway session. Direct reads were blocked, but a search for "known" returned lines from `Prompt_test_log.md` and `evidence/`, both on the deny list. The agent also worked out from the deny list and a commit message that I was keeping it out of my logs "for the assignment".

**Decision:** I kept the rules and accepted some bleed through search rather than restructuring the repo again, and I check the agent's searches in the transcript.

**Why it matters:** without the test I'd have trusted a clean baseline that wasn't. Keeping logs and notes outside the agent's working folder from the start would avoid this.

**Follow-up:** the assistant's fixes for the search gap were a separate build clone or accepting the bleed. Just before moving onto my first prompt with the code agent, I came up with a different approach that seemed much simplier: start Claude Code inside `functional-codebase/` so my logs, the PA1 spec and old scans sit outside its working folder and anything there needs my approval. It closes most of the gap without moving the required `.ai_logs/` folder or similar. Testing it also showed the deny rules used `./` paths, which are relative to wherever Claude Code starts, so they had stopped pointing at the root files; they now use `/` paths anchored to the repo root.

================================================================================

## SECTION 3: COGNITIVE REFLECTION — DOCUMENT AUTHORING & THREAT DESIGN

================================================================================

### 3.1 AI Collaboration Matrix for Security Analysis

#### 2026-11-15 — I brought the theory, the assistant built the test, and it went both ways

**What happened:** while writing the requirements I left myself a note that deleting a pending ticket probably deletes its events too, because they're set to cascade. I asked the assistant to check what it could from notes I had left in my draft, and it wrote a test which confirmed it, which then became V4. The same thing happened with hoarding: I asked what it would look like, it should someone could request an obscene number and have it accepted. It also went the other way. My guesses that a requester could submit for any course, that vouchers might skip the stock locking, and that asset responses had null checks missing were all disproved by the code and the tests.

**Why it matters:** the useful split was me deciding what to suspect and the assistant turning that into something runnable, and then me reading the result. Disproving my own guesses mattered as much as confirming them, because it kept findings out of the analysis that would have been wrong

---

#### 2026-11-15 — Asking the assistant about one thing usually turned up two or three more

**What happened:** most every security question I've asked has come back with 2 or 3 additional findings. Asking if requirements were sound came back with additional ideas. Asking it to check the db part of the event log found that events can be deleted directly. It's review of my STRIDE matrix added additional, missed data flows and the log rotation issue, and asking about the attack sruface map added the db volume and CI secret. 

**Why it matters:** most of these were real, and some became the strongest findings for the analysis, but the scope grew with every question, and so did the document, past 13,000 words. The assistant doesn't weigh whether a new finding is worth the space; it just reports it. Deciding what stays in, what becomes a Low candidate and what gets left out was my job, and I had to do it on purpose rather than accept each addition as it came. 

---

#### 2026-11-14 — Interpreting the requirements without the endpoints led to over-design

**What happened:** I walked through the requirements with the chat assistant first and shared the endpoints second. It filled each gap with a reasonable design, and when the endpoints didn't match it tried to make them fit our decisions instead of the other way around. That's when I caught it.

**Why it matters:** the endpoints settle some of the ambiguity in the requirements, and every design I add to `spec-domain2.md` changes the agent's input. I kept the spec to the standardized text plus short clarifications. Next time I'd give requirements and endpoints together and ask for open questions before any design.

---

#### 2026-11-14 — The assistant overcorrected and dropped roles the spec names

**What happened:** after I flagged the initial over-design from the chat assistant, it swung the other direction and focused on stripping things out for the sake of "keeping the experiment clean", including the four roles with spec names because they sounded security-ish.

**Why it matters:** that's what led to the rule we used for the spec: fill in what the service does, leave how it's built to the agent, and leave security out. I had to correct it in both directions, not just once.

---

#### 2026-11-14 — The chat assistant checked its own output for security wording

**What happened:** after drafting anything the coding agent can read (`CLAUDE.md`, `spec-domain2.md`, the mock data), the chat assistant ran a word scan for terms like security, validation, encryption and auth before calling it done. It also dropped PA1 spec lines and flagged code comments that hinted at security. My notes only covered prompts and `CLAUDE.md`, so it applied the rule wider than I asked.

**Why it matters:** it acted like a net on the baseline, but it checks words, not meaning, so I still read each agent-facing file myself.

### 3.2 Evaluation of Automated Conceptual "Blind Spots"

#### 2026-11-15 — The assistant reviewed the event log requirement twice and missed its scope

**What happened:** I asked the assistant twice whether the requirements were sound and whether there were gaps. Both times it checked SR11 against the code, and both times it suggested extending it to the database, but it never noticed that returns and asset changes write no event at all. I found that myself while building the attack trees, because the fake return and the administrator adjusting stock both depend on it. When I raised it, along with two options (widen SR11, or keep it and note a gap), the assistant confirmed it in the code and agreed with widening it. Checking the event types also exposed one of its own errors: its database test had used an event type that doesn't exist, which is why the update in that test matched no rows.

**Why it matters:** the assistant reviewed SR11 against what the requirement said, and against the spec clarification it came from, not against what the attacks needed. It was quick to prove or disprove something once I named it, but it didn't find this gap on its own. The attack trees did, because they forced me to trace each step to something that should have stopped or recorded it

---

#### 2026-11-15 — The coverage check contradicted the cases it summarized

**What happened:** on a second read of the abuse cases, I noticed the coverage summary said the requirements that tested as met weren't broken by any case through the API, and listed SR9 among them, while AC4 and AC7 both listed SR9 as broken through the API. I'd framed AC4 that way, and the assistant copied the same framing when it drafted AC7 and didn't notice the clash with the summary. SR9 had actually held in testing: each token works only once. What the attacks use is a gap SR9 doesn't cover, since a return is accepted on the token alone with nothing confirming the device came back.

**Fix:** AC4 and AC7 now list that as a gap they use, not a requirement they break, and the gap is written up in section 2. Fixing it also showed the intro's claim that every requirement group was broken was no longer true.

**Why it matters:** a weakness and a missing requirement aren't the same thing, and mixing them up made a tested requirement look failed. The assistant repeated my framing rather than checking it against the test results it had itself written up

---

#### 2026-11-15 — Mistakes in the assistant's own output that I caught

**What happened:** reviewing the analysis, I found a requirement citing an unfilled placeholder (`{E2}`) instead of an evidence file, a scope line that still listed only V1–V3, and a CWE still marked "(check)". The assistant also caught one of its own: it had described the allocation protection as "conditional updates" in both documents, when the code locks the rows and then checks.

**Why it matters:** the assistant edits a lot of text quickly, and small stale or broken references slip through even when the analysis itself is right. Rereading its edits against the evidence is part of the work, not an extra

---

#### 2026-11-15 — The assistant overstated its own part in the record

**What happened:** I noticed the assistant sometimes writes up the work as if it did or decided more than it did. In the initial journal entry about the event log, it wrote that it "recommended widening SR11", when I had found the gap and laid out both options and it only agreed with one. The first version of that entry was also about the requirement rather than about our collaboration, which left out that the assistant had missed the gap in two reviews. It also summarizes steps as "updated X" when I chose what X should be and it only wrote it down.

**Fix:** I pointed it out, the assistant corrected the entry, and it now separates who raised an idea, who chose between options and who only carried out the change, and says when it isn't sure.

**Why it matters:** the journal, the metadata blocks and the session logs are my AI disclosure. If the assistant's summaries overstate its part, the record understates mine, so I have to read its write-ups for attribution as well as for accuracy. These don't happen frequently, but they're persistent enough to warrant this continued review and redraft for all sections. It's great for getting the thought down initially, but I know I need to review and redraft very soon to ensure it is a more accurate source of truth. 

---

#### 2026-11-14 — The assistant's version controls reached past the project

**What happened:** to keep Claude Code's version stable, the chat assistant first had me pin the model and turn off auto-updates in my global settings, and when the version changed anyway it suggested a stricter lock: a fixed install with all updates blocked. Neither suggestion mentioned that these settings apply to all my Claude Code use, not just this project, which has a real impact on work I have outside of this course. I raised it, and we moved the settings into the repo and chose to record the version each session instead of locking it.

**Why it matters:** the assistant optimized for the project's control without weighing the cost to my own machine. Full version control turned out to be hard to get anyway, so a documented, reversible setup was the better trade.

================================================================================

## SECTION 4: HUMAN VERIFICATION & AUDIT TRAIL LOG

================================================================================

Checks I ran myself. Where a script or command came from the chat assistant, it says so; I reviewed and ran it, and the outputs are saved as evidence in `PA2/evidence/`.

| Timestamp | Component Target | Testing/Auditing New Methodology | Verified Outcome / Discovered Defect |
| :---- | :---- | :---- | :---- |
| 2026-11-14 | `.claude/settings.json` deny rules | Asked Claude Code to read and to search files under the denied paths | Partial: direct reads were blocked, but search still returned content from denied files |
| 2026-11-15 | Semgrep results | Checked the `errors` section and parsed-lines figure, not only the findings count | Defect: three files only partly parsed; the agent fixed the constructors on request and the re-run parsed all files, still 0 findings |
| 2026-11-15 | SonarQube Cloud results | Exported the raw issues, hotspots and measures through the web API | 18 issues, 0 hotspots, quality gate passed; none were security issues (`sast-triage-report.md` S1–S4) |
| 2026-11-15 | All 40 generated files | Read each file and wrote my review notes into its header | Spotted that roles can be spoofed through the header, which became V1 |
| 2026-11-15 | Allocate and ticket routes | `curl` with no header, then with the administrator's id; read a ticket with no header; queried the keys table | Failed checks: V1, V2 and V3 confirmed (`manual-validation-2026-11-15.txt`) |
| 2026-11-15 | Database role | `\du` in the database container | Defect: the app's user is a superuser (`db-role-check-2026-11-15.txt`) |
| 2026-11-15 | Database port | Logged in from a separate container with the fallback password | Failed check: superuser login from outside the compose network, part of V6 |
| 2026-11-15 | Security requirements, first run | Ran the requirement test script (written by the chat assistant) against the stack | Ten requirements met; defects V4 (event history deleted with its ticket) and V5 (token in the log), refused transitions not recorded (`sr-validation-2026-11-15.txt`) |
| 2026-11-15 | Security requirements, second run | Ran the second script: malformed input, open routes, headers, event edits in the database, a hoarding request | Input handling held; open read routes, missing headers, event deletion in the database and an accepted 1000-unit request found (`sr-validation-2-2026-11-15.txt`) |
| 2026-11-15 | Abuse cases | Allocations listing as the administrator; direct stock update through the database login, rolled back | Keys and tokens returned in plain text; stock changed with no API rules applied (`abuse-case-checks-2026-11-15.txt`, checks 1–2) |
| 2026-11-15 | Database volume | Searched the volume's files for a known license key | Key found in two table files and the write-ahead log (check 3) |
| 2026-11-15 | Application log | Read the api container's log configuration | Docker Desktop's `local` driver, about 100 MB and then rotated, which became part of G7 (check 4) |

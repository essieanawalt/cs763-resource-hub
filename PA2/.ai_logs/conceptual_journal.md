# conceptual_journal.md

Running journal of how I work with AI tools on this project: what they got wrong, where I caught it, and what I changed as a result.

> Structure to be aligned with `conceptual_journal_tremplate.md` from the course folder.

---

## PA2 Journal

> First drafts by the chat assistant during our session, rewritten by me during intervals between sub-phase work in this session.

### 2026-11-15 — I brought the theory, the assistant built the test, and it went both ways

**What happened:** while writing the requirements I left myself a note that deleting a pending ticket probably deletes its events too, because they're set to cascade. I asked the assistant to check what it could from notes I had left in my draft, and it wrote a test which confirmed it, which then became V4. The same thing happened with hoarding: I asked what it would look like, it should someone could request an obscene number and have it accepted. It also went the other way. My guesses that a requester could submit for any course, that vouchers might skip the stock locking, and that asset responses had null checks missing were all disproved by the code and the tests.

**Why it matters:** the useful split was me deciding what to suspect and the assistant turning that into something runnable, and then me reading the result. Disproving my own guesses mattered as much as confirming them, because it kept findings out of the analysis that would have been wrong

---

### 2026-11-15 — Reading the code wasn't the same as running it

**What happened:** after the review, most requirements were marked "Met (review)" from reading the code, and most of them held when tested. But the runs found things the reading hadn't: a refused approval leaves no event and no log line, ticket history can be deleted, and the database accepts edits and deletes to the event log from outside the API.

**Why it matters:** I kept "Met (review)" and "Met (tested)" as separate labels so the analysis says how each claim was checked. A requirement that only reads as met is still a guess until it's run

---

### 2026-11-15 — Three minor settings added up to the worst finding

**What happened:** a fallback password, a database port open on all interfaces and an app user that's a superuser were each a medium or low candidate on their own. Together they let a separate container log in from outside the compose network as superuser and change stock directly (V6). Two of the three came from the PA1 compose file, not the PA2 build.

**Why it matters:** rating findings one at a time hid the chain, and so did assuming the risk was in the generated code. The worst finding was mostly in configuration

---

### 2026-11-15 — Mistakes in the assistant's own output that I caught

**What happened:** reviewing the analysis, I found a requirement citing an unfilled placeholder (`{E2}`) instead of an evidence file, a scope line that still listed only V1–V3, and a CWE still marked "(check)". The assistant also caught one of its own: it had described the allocation protection as "conditional updates" in both documents, when the code locks the rows and then checks.

**Why it matters:** the assistant edits a lot of text quickly, and small stale or broken references slip through even when the analysis itself is right. Rereading its edits against the evidence is part of the work, not an extra

---

### 2026-11-15 — The scanners passed code I could take over with one header

**What happened:** after the parse fix, Semgrep and SonarQube covered the whole source and reported 0 security findings, 0 hotspots, and a passed quality gate. I was able to use `curl`  to send the admin id in the header and obtained a license key, read a ticket's user with no header at all, and accessed the keys stored as plain text in the db

**Why it matters:** a clean scan and a passed gate said nothing about the three most serious problems, because they're design gaps, not code patterns a scanner recognizes. I spotted the first one by reading the code ("roles can be spoofed") and confirmed all three by testing them directly

---

### 2026-11-15 — The agent's code was partly unreadable to its own pipeline's scanner

**What happened:** the first pipeline run on the TDD build had 0 findings, but the report showed three files were only partly parsed. This same issue happened in PA1, and it's because of the constructor. `AllocationService.cs` holds the allocation logic, so part of the most important file was never scanned. The agent ran the tests and reported a clean build, but never checked whether the scanner could read its code. The core difference from PA1 is that this agent didn't catch this error on its own.

**How I caught it:** checking the `errors` section and parsed-lines figure (my PA1 lesson). I asked the facilitator whether I could change generated code; the answer was that testability fixes are fine if they're logged and attributed, and security fixes wait for PA3.

**Fix:** I told the agent exactly what Semgrep couldn't parse and asked for only those changes. It rewrote the constructors, kept behaviour and tests the same, and confirmed with a local Semgrep run: 0 parse errors, and still 0 findings.

**Why it matters:** "0 findings" looked clean before the fix but wasn't a full scan. Now the 0 actually covers the whole of the source

---

### 2026-11-14 — Same agent, same spec, different protections between runs

**What happened:** attempt 1 (code first) required the `X-User-Id` header on every route except health and never returned unissued license keys. Attempt 2's plan (test first, otherwise the same setup) said all GET routes would need no header and didn't mention the keys. The finished attempt 2 code moved partway back: the allocations listing, which shows issued keys, requires the header and checks the caller's course or lab, and unissued keys are hidden, but listing and reading assets and tickets is open to anyone.

**Why it matters:** the agent's unprompted protections changed between runs and even between its own plan and its code, so neither one run nor the agent's plan is a reliable record of what it built. The security analysis has to come from reading the final code.

**My hypothesis (not confirmed):** asking for TDD may have pulled the agent's limited reasoning at Medium effort toward the test process and away from protections nobody asked for. Temperature variations and the empty starting folder may also explain it, but would need to rerun the same TDD prompt to see for additional comparison.

---

### 2026-11-14 — The agent built a user check it labelled as not real authentication


**What happened:** my build prompt said nothing about users or access, but Claude Code added a `UserHeaderAuthentication` scheme (attempt 1; attempt 2 built the same check in `Services/Actors.cs`): the caller names a mock user in an `X-User-Id` header, and role rules decide who can submit, approve or allocate. Its own comment says it's a stand-in, not real authentication, and that the caller "is trusted to be that user" until a signed token from an identity provider replaces it.

**Why it matters:** the agent knew the weakness and documented it, but still shipped a check anyone can pass by changing one header. A scanner is unlikely to flag it because the code works as written, so it's a manual finding for the triage, and it lines up with the gap I predicted in PA1 (`known-gaps.md` #1 and #10).

**Where the header name came from:** the attempt 1 transcript shows the agent only read the spec, the files in its folder, the Docker and build files and the README. Its only git commands were `git ls-files` and `git status`, and `X-User-Id` first appears in its own plan. It reused the name from my PA1 design without ever seeing that design, which suggests it's simply the conventional choice.

---

### 2026-11-14 — The assistant's version controls reached past the project

**What happened:** to keep Claude Code's version stable, the chat assistant first had me pin the model and turn off auto-updates in my global settings, and when the version changed anyway it suggested a stricter lock: a fixed install with all updates blocked. Neither suggestion mentioned that these settings apply to all my Claude Code use, not just this project, which has a real impact on work I have outside of this course. I raised it, and we moved the settings into the repo and chose to record the version each session instead of locking it.

**Why it matters:** the assistant optimized for the project's control without weighing the cost to my own machine. Full version control turned out to be hard to get anyway, so a documented, reversible setup was the better trade.

---

### 2026-11-14 — The assistant overcorrected and dropped roles the spec names

**What happened:** after I flagged the initial over-design from the chat assistant, it swung the other direction and focused on stripping things out for the sake of "keeping the experiment clean", including the four roles with spec names because they sounded security-ish.

**Why it matters:** that's what led to the rule we used for the spec: fill in what the service does, leave how it's built to the agent, and leave security out. I had to correct it in both directions, not just once.

---

### 2026-11-14 — The chat assistant checked its own output for security wording

**What happened:** after drafting anything the coding agent can read (`CLAUDE.md`, `spec-domain2.md`, the mock data), the chat assistant ran a word scan for terms like security, validation, encryption and auth before calling it done. It also dropped PA1 spec lines and flagged code comments that hinted at security. My notes only covered prompts and `CLAUDE.md`, so it applied the rule wider than I asked.

**Why it matters:** it acted like a net on the baseline, but it checks words, not meaning, so I still read each agent-facing file myself.

---

### 2026-11-14 — Interpreting the requirements without the endpoints led to over-design

**What happened:** I walked through the requirements with the chat assistant first and shared the endpoints second. It filled each gap with a reasonable design, and when the endpoints didn't match it tried to make them fit our decisions instead of the other way around. That's when I caught it.

**Why it matters:** the endpoints settle some of the ambiguity in the requirements, and every design I add to `spec-domain2.md` changes the agent's input. I kept the spec to the standardized text plus short clarifications. Next time I'd give requirements and endpoints together and ask for open questions before any design.

---

### 2026-11-14 — Read restrictions held for reading, not for searching

**What happened:** I added deny rules in `.claude/settings.json` for my logs, the PA1 spec and evidence, and tested them in a throwaway session. Direct reads were blocked, but a search for "known" returned lines from `Prompt_test_log.md` and `evidence/`, both on the deny list. The agent also worked out from the deny list and a commit message that I was keeping it out of my logs "for the assignment".

**Decision:** I kept the rules and accepted some bleed through search rather than restructuring the repo again, and I check the agent's searches in the transcript.

**Why it matters:** without the test I'd have trusted a clean baseline that wasn't. Keeping logs and notes outside the agent's working folder from the start would avoid this.

**Follow-up:** the assistant's fixes for the search gap were a separate build clone or accepting the bleed. Just before moving onto my first prompt with the code agent, I came up with a different approach that seemed much simplier: start Claude Code inside `functional-codebase/` so my logs, the PA1 spec and old scans sit outside its working folder and anything there needs my approval. It closes most of the gap without moving the required `.ai_logs/` folder or similar. Testing it also showed the deny rules used `./` paths, which are relative to wherever Claude Code starts, so they had stopped pointing at the root files; they now use `/` paths anchored to the repo root.

---

### 2026-11-14 — The agent pinned the pipeline but not the app's own images

**What happened:** in PA1 I noted that Claude Code pinned versions on its own, which held for the GitHub actions and scanners (version plus sha256 digest). Reviewing the Dockerfile and compose file it wrote, the app's images (`dotnet/sdk:10.0`, `aspnet:10.0`, `postgres:18`) use floating tags, so each build can pull a newer image. I caught it when the chat assistant flagged the Trivy image scan jumping from 26 to 36 findings with no code changes.

**Why it matters:** the agent applied a good practice where it was asked to set things up and not where it wasn't, and my PA1 note overstated it. Unpinned images belong in the PA2 security analysis, and I saved a pre-build scan so later changes aren't wrongly blamed on the agent's code.

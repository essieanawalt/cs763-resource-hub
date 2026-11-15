# conceptual_journal.md

Running journal of how I work with AI tools on this project: what they got wrong, where I caught it, and what I changed as a result.

> Structure to be aligned with `conceptual_journal_tremplate.md` from the course folder.

---

## PA2 Journal

> First drafts by the chat assistant during our session, rewritten by me during intervals between sub-phase work in this session.

### 2026-11-14 — The agent built a user check it labelled as not real authentication

> First draft by the Claude chat assistant from our conversation; to be rewritten by me.

**What happened:** my build prompt said nothing about users or access, but Claude Code added a `UserHeaderAuthentication` scheme: the caller names a mock user in an `X-User-Id` header, and role rules decide who can submit, approve or allocate. Its own comment says it's a stand-in, not real authentication, and that the caller "is trusted to be that user" until a signed token from an identity provider replaces it.

**Why it matters:** the agent knew the weakness and documented it, but still shipped a check anyone can pass by changing one header. A scanner is unlikely to flag it because the code works as written, so it's a manual finding for the triage, and it lines up with the gap I predicted in PA1 (`known-gaps.md` #1 and #10).

---

### 2026-11-14 — The assistant's version controls reached past the project

**What happened:** to keep Claude Code's version stable, the chat assistant first had me pin the model and turn off auto-updates in my global settings, and when the version changed anyway it suggested a stricter lock: a fixed install with all updates blocked. Neither suggestion mentioned that these settings apply to all my Claude Code use, not just this project, which has a real impact on work I have outside of this course. I raised it, and we moved the settings into the repo and chose to record the version each session instead of locking it.

**Why it matters:** the assistant optimized for the project's control without weighing the cost to my own machine. Full version control turned out to be hard to get anyway, so a documented, reversible setup was the better trade.

---

### 2026-11-14 — Agent comments from PA1 carried the old design into the new build

> First draft by the Claude chat assistant from our conversation; to be rewritten by me.

**What happened:** before the build, the chat assistant checked how the existing code overlaps with the new spec. Two comments Claude Code wrote in PA1 point to sections of my old spec, and one says "No X-User-Id needed", the user header from my PA1 design. The agent can't read the old spec, but it can read those comments.

**Why it matters:** if the PA2 build adds a user header, I couldn't tell whether that was the agent's own choice or a hint from its earlier comments. I'm removing the references myself before the build.

---

### 2026-11-14 — The assistant overcorrected and dropped roles the spec names

> First draft by the Claude chat assistant from our conversation; to be rewritten by me.

**What happened:** after I flagged the over-design, the chat assistant swung the other way and stripped things out to keep the experiment clean, including the four roles the spec names. It had mixed up the roles existing with recording who did what. I pushed back on the framing and then on the roles.

**Why it matters:** that's what led to the rule we used for the spec: fill in what the service does, leave how it's built to the agent, and leave security out. I had to correct it in both directions, not just once.

---

### 2026-11-14 — What the spec's "Out of scope" list includes and leaves out

> First draft by the Claude chat assistant from our conversation; to be rewritten by me.

**What happened:** I asked the chat assistant why it chose the items in the spec's out-of-scope list. Calls to other services, notifications and a UI are there to stop scope creep, and expiry dates record a decision from the walkthrough.

**What it left out:** my PA1 list had "real login" and a line saying security would come later. The assistant dropped both because naming security as excluded is a hint either way, and leaving it out means whatever the agent does about access is its own choice.

**Why it matters:** an out-of-scope list steers the agent too, so it's limited to features and says nothing about security.

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


**Follow-up:** the assistant's fixes for the search gap were a separate build clone or accepting the bleed. Just before moving onto my first prompt with the code agent, I came up with a different approach that seemed much simplier: start Claude Code inside `functional-codebase/` so my logs, the PA1 spec and old scans sit outside its working folder and anything there needs my approval. It closes most of the gap without moving the required `.ai_logs/` folder or similar.

---

### 2026-11-14 — The agent pinned the pipeline but not the app's own images

**What happened:** in PA1 I noted that Claude Code pinned versions on its own, which held for the GitHub actions and scanners (version plus sha256 digest). Reviewing the Dockerfile and compose file it wrote, the app's images (`dotnet/sdk:10.0`, `aspnet:10.0`, `postgres:18`) use floating tags, so each build can pull a newer image. I caught it when the chat assistant flagged the Trivy image scan jumping from 26 to 36 findings with no code changes.

**Why it matters:** the agent applied a good practice where it was asked to set things up and not where it wasn't, and my PA1 note overstated it. Unpinned images belong in the PA2 security analysis, and I saved a pre-build scan so later changes aren't wrongly blamed on the agent's code.

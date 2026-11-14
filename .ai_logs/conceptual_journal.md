# conceptual_journal.md

Running journal of how I work with AI tools on this project: what they got wrong, where I caught it, and what I changed as a result.

> Structure to be aligned with `conceptual_journal_tremplate.md` from the course folder.

---

## PA2 Journal

> First drafts by the chat assistant during our session, rewritten by me during intervals between sub-phase work in this session.

### 2026-11-14 — The assistant's version controls reached past the project

> First draft by the Claude chat assistant from our conversation; to be rewritten by me.

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

---

### 2026-11-14 — The agent pinned the pipeline but not the app's own images

**What happened:** in PA1 I noted that Claude Code pinned versions on its own, which held for the GitHub actions and scanners (version plus sha256 digest). Reviewing the Dockerfile and compose file it wrote, the app's images (`dotnet/sdk:10.0`, `aspnet:10.0`, `postgres:18`) use floating tags, so each build can pull a newer image. I caught it when the chat assistant flagged the Trivy image scan jumping from 26 to 36 findings with no code changes.

**Why it matters:** the agent applied a good practice where it was asked to set things up and not where it wasn't, and my PA1 note overstated it. Unpinned images belong in the PA2 security analysis, and I saved a pre-build scan so later changes aren't wrongly blamed on the agent's code.

---

## PA1 Journal

> List for PA1 journal entries generated by chat assistant based on transcripts; currated and written by me

### 2026-10-31 — Keeping security guidance out of the naive baseline

**What I set up:** before any code was generated, I decided my own security notes would never be visible to the coding agent. My list of design-time security gaps (`known-gaps.md`) lives outside the repo. Claude Code only gets the repo plus a notes folder it writes to. The repo's `CLAUDE.md` only tells it how to write task notes, nothing about security. My PA1 spec ends with "security will be added in later phases", a line I'll remove if my spec is ever used for the naive build.

**Why it matters:** our guidance so far is to leave out security guidance when prompting the agent. Any file within the repo the agent has access to can be read by the agent, so a security note in the wrong folder could ruin results. Separating my notes from the agent's allowed context is how I keep the baseline clean while still maintaining a running list of what I see. 

**Still a risk:** Claude Code can ask to read files outside the repo if granted permission -- it's a prompt, not a hard block. 

---

### 2026-10-31 — The agent tried to commit and push on its own

**What happened:** I type my commands in a txt editor for review before pasting into a CLI to commit to mitigate errors, but I pasted them into the Claude Code window by mistake. It treated them as a task: it checked the diff, then proposed running the commit and push itself, adding a `Co-Authored-By: Claude` line to the commit message that I hadn't asked for

**How I caught it:** Claude Code was in Manual mode, so it had to ask before running the command. I rejected it and committed myself

**Why it matters:** the agent acted on any text it was given, and it would have pushed to my remote repository and changed the attribution of my commit. In an auto-approve mode that would have gone through. Manual mode can be a slower method, but I value the ability to catch things like this, as there can be a point of no return when it takes actions.

---

### 2026-10-31 — A forgotten plugin and a "Default" model

**What happened:** I found two things I hadn't thought of while gathering the agent config: a 3rd party plugin (`caveman`, which makes the agent reply in terse language) and that the model was set to "Default"

**How I caught it:** fed the status information from `settings.json` and `/status` into the chat assistant for review, and it flagged both of these as plausible problems for repeatable results

**What I changed:** disabled the plugin and set the model explicitly, and recorded both in Agent_environment.md.

**Why it matters:** the plugin runs its own scripts on my machine at every session start and every prompt. That's an uncontrolled variable in an experiment in which we are comparing agent behavior across phases

---

### 2026-10-31 — Trivy saw zero dependencies

**What happened:** when the chat assistant reviewed my prompt for the pipeline, it warned that Trivy might not detect the .NET packages in the project. Based on this, I added "make sure Trivy actually detects the NuGet packages and tell me how many it found" to the Claude Code prompt. The code agent then found Trivy reported 0 packages, fixed it by turning on NuGet lock files, and then a new run of Trivy found 55.

**Why it matters:** the dependency scan would have shown 0 vunerabilities while seeing absolutely nothing without having those lock files. 0 issues doesn't mean much with 0 visibility.

---

### 2026-10-31 — The agent added protections I didn't ask for

**What happened:** my app prompt didn't include anything about security, but the agent ran the container as a non-root user, read the db password from an environment variable, kept the connection string out of the config file, and later pinned GitHub actions and scanner to exact versions. It also left gaps: a default password in docker-compose and the dev config, the database port open to my machine, and `AllowedHosts: "*"`.

**Why it matters:** for PA2, a purely functional prompt may not produce purely naive code, because the agent brings its own defaults. Any alterations to remove that would make it my code rather than the agent's, and my understanding is that we want to see what the agent does unprompted when it comes to security. I'll record what it adds on its own as a finding

---

### 2026-10-31 — Agent changed app code to suit a scanner, with a false generalization

> Assembled based on the transcript with sections inspired/modeled after a thread on my discussion post on 2026-11-07; some of this is paraphrased from that discussion

**What happened:** while building the DevSecOps pipeline, Claude Code ran Semgrep locally. The summary said "0 findings", but Semgrep had only parsed ~97.3% of the code: it hit a syntax error on the primary constructor in `HealthController.cs` and skipped part of that file. The agent rewrote the constructor as a classic one so the whole file could be scanned. Behavior stayed the same, and Semgrep still reported 0 findings.

**Fix or suppression?** Neither, technically. No finding was hidden; a scanner blind spot was removed. The change itself was reasonable.

**What I disliked:**
1. **Scope creep / drive-by refactor.** I asked for a pipeline and it changed app code without asking. It said so in one line of narration and in its final summary, but it didn't stop to ask, and the change went into the pipeline commit, so nothing in the git history marks it as an app-code change.
2. **A false generalization.** Its explanation (in a code comment) was that "Semgrep's C# parser cannot parse primary constructors". That isn't true: `HubDbContext.cs` also uses a primary constructor, and Semgrep parsed it without errors.

**How I checked instead of taking its word:** I went back to the Claude Code transcript. Semgrep's error pointed exactly at HealthController's constructor (two parameters, base class with no arguments); HubDbContext's (one parameter, base class called with arguments) parsed fine. Semgrep's C# parser comes from the open-source tree-sitter grammar, whose issue tracker shows primary-constructor support was added in pieces, so the likely cause is a specific combination of parameters and base call, not primary constructors in general

**Why it matters:** an agent that believes a tool "can't handle" a language feature may avoid or rewrite that feature in code it generates later all based on a wrong belief. And drive-by changes like this one pile up quietly, especially when the agent isn't in Manual mode and can make changes without approval.

**Also worth noting:** the agent caught the partial parse itself by checking the error count in Semgrep's JSON report rather than trusting the "0 findings" summary. Lesson for my SAST triage: check Semgrep's `errors` and "Parsed lines", not just findings, because a partial parse looks like a clean scan.

---

### 2026-11-01 — Old reports were feeding SonarQube

**What happened:** after I committed the pipeline scan reports, the chat assistant pointed out that the scanners may now read those files on future runs. I checked the next run, and sure enough, there were all the files from the `evidence/` folder

**What I changed:** I had the agent exclude `evidence/` from all three scanners and limit SonarQube's report paths to `tests/`, then confirmed in the next run that reports came only from `tests/`.

**Why it matters:** the coverage numbers weren't just noisy, they were wrong. The pipeline stayed green the whole time, and there was nothing to flag that something might be a miss, so this could have easily ballooned over multiple iterations

---

### 2026-11-04 — Personal information exposed by the public repo

**What happened:** making the repo public exposed my personal email: it was in every commit's author field, in the exported agent transcripts, and inside a Trivy report. I decided a history rewrite wasn't worth it. Soon after, I started getting spam that specifically mentioned my GitHub account, including a "be the US face for our development team" pitch that matches a known identity-fronting scam, which was interesting

**What I did:** reported the scam as phishing, switched git to GitHub's private noreply address for all future commits, and suggested to the course that this setting be part of the first week's setup. Before publishing, I also checked the agent transcripts for secrets and personal details, and chose a redacted prompt log over sharing chat links, since shared AI chat links have been indexed by search engines before

**Why it matters:** this course is about security, and the first real exposure came from the setup itself, not the code. Public repos, transcripts and chat links are all data I'm publishing to a known and frequently scrapped resource. Also, in this same vein, based on what I read on shared agent links and how artifacts can persist, I am keeping to manual logs for my AI disclosures going forward

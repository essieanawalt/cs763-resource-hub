# AI_usage_appendix.md — Design-aid prompts (PA1)

Design-aid prompts I sent to the Claude chat assistant (claude.ai, model Claude Opus 5.5) while producing the PA1 deliverables. Referenced from the AI Usage Log in spec.md, Agent_environment.md, Prompt_test_log.md and code_AI_history.md.

**About this appendix:** each prompt is copied verbatim (including typos and pasted output). Personal details are masked with `[REDACTED: …]`, and nothing else in a prompt is removed. Answers I gave to the assistant's multiple-choice questions are shown as *Answered:*. The assistant's replies are summarized in one line each, not quoted. Assembled by the chat assistant from the conversation; reviewed by me. Approach confirmed with the facilitator on 2026-11-02 (appendix instead of a chat link; personal details masked).

**How I used the assistant:** I instructed it to prioritize accuracy over agreeableness, flag uncertainty, and not present guesses as facts. Its guesses are marked in the documents (e.g. the "tracking parameters" interpretation). It still made errors; I caught them in review (wrong file names and an inaccurate claim in Prompt_test_log.md) and they were corrected before submission.

**What I left out, and why:**
- **Tool setup and troubleshooting** (installing .NET, Docker, `gh`; git and `gh` command help; downloading artifacts; finding the Claude Code transcript folder): no design content. The resulting configuration is recorded in full in Agent_environment.md
- **Questions about my own logging files** (e.g. how to word the assistant log and this appendix): about record-keeping, not about a deliverable's design
- **Personal details inside included prompts:** email address, session IDs, local paths, shell prompt and one personal topic, masked where they appear

---

## 1. Workflow setup

**1.1**
```text
I received this advice for the project, 
"Hi everyone,

A tip from my own workflow: I keep all my prompts and notes on what I've done in Obsidian, a free note-taking app that stores everything as plain markdown files. Obsidian is completely optional. The folders below work just as well in any editor (VS Code, Notepad, whatever you like), because they're just folders of markdown files.

Here's a simple setup:

* prompts folder: Save your prompts here as you go, so you can find and reuse them later. That will come in handy across the project phases.
* MyAI folder: Have your AI save its own notes here, such as summaries of what it did, decisions it made, or reports you ask it for. This gives you a running record of the AI's work alongside your own.


Not using Obsidian? If you aren't using Obsidian, just create the two folders and tell your AI agent where they are. Most coding agents can read and write files in a local folder directly.

Using Obsidian? If you do, most AI tools can connect to it through an MCP server (Model Context Protocol, a standard way to give an AI access to other tools), so your AI can read your notes, write to MyAI, and generate things like a summary report for you.

Either way, everything is markdown, so it's easy to pull the relevant prompts into your Prompt_test_log.md for Assignment 1."
```
Response: assessed the tip against the deliverables; suggested keeping notes outside the repo so the PA2 agent can't see security notes, recording model/mode per prompt, and a fixed note format.

**1.2**
```text
[REDACTED: local path]. I use AnyType instead of Obsidian, so I would need to modify our handling. Ask me questions as needed and help me understand our process
```
*Answered:* agent "Claude Code"; notes "Plain folders (Recommended)"; location "In this course folder (Recommended)".
Response: explained AnyType keeps notes in a database rather than files; recommended plain folders for the agent; set up `prompts/` and `MyAI/` with templates.

**1.3**
```text
I like your suggestions for 1, and your assumption for tracking parameters mirrors mine. I think the approach sounds valid. Before we formally begin, make sure I understand what we're doing. Also, is there anything I need to set up beforehand? github? access for you? How do we need to set up how we're going to work
```
Response: explained the two roles (chat assistant plans and reviews; Claude Code writes code), the PA1 order, the setup list, and a working agreement including a log of who did what.

**1.4**
```text
Check in: do you think PA2 is in scope right now? Or are we still working within scope for Assignment 1
```
Response: confirmed the work was PA1; flagged that the private known-gaps list was PA2 preparation, not a PA1 requirement; listed what was left for PA1.

---

## 2. spec.md

**2.1**
```text
Domain #2, The Resource Allocation & Help Desk Hub
C# / ASP.NET Core, PostgreSQL, Docker
```
*Answered:* users "If there isn't enough guidance in the summary, then your recommended approach based on reasable assumptions"; help desk "Simple tickets (Recommended)"; tracking parameters "This was also vague, provide suggestions based on most used defaults"; drafting approach "I'm unsure here".
Response: noted tool fit for .NET and the domain's sensitive assets; agreed spec is to be defined; suggested 3 roles; offered a reading of "tracking parameters" as an asset audit log (flagged as a guess); recommended: I answer questions, the assistant formats, I review.

**2.2**
```text
I know as much as you do based on the summary I've provided. We will have to work out additional details as we go and start with assumptions
```
Response: proposed defaults with reasons: 2 asset types, request/approve flow, team ownership, 4 statuses, voucher value.

**2.3**
```text
That looks good for a first draft
```
Response: drafted spec.md.

**2.4**
```text
How should we keep a separate note of deliberate holes to circle back on later? Like the login gap. I like the note that "security will be added in later phases" note.
```
Response: recommended a private gap list outside the repo (PA2 prep); added the "security in later phases" line.

**2.5**
```text
are we confident in the current specs ?
```
Response: listed 9 gaps (missing schemas, response shapes, approval matching rule, audit log scope, duplicate codes, paging, etc.).

**2.6**
```text
let's start on filling in those gaps
```
*Answered:* approval matching "Same type + same name"; audit log "Assets + requests".
Response: added business rules, request/response schemas, paging, unique codes.

**2.7**
```text
Do a new, quick review
```
Response: found 7 inconsistencies (audit-entry count, voucher value, seed names, missing-header behavior, etc.).

**2.8**
```text
5, 400 if missing or unknown. Fix the others
```
Response: added R11 (400 for missing/unknown `X-User-Id`), a seed asset table and the other fixes.

**2.9**
```text
claude.md already done. Is what we created valid against project requirements for Assignment 1 / Phase 1 only
```
Response: mapped the PA1 deliverables to status; flagged the missing architecture section.

**2.10**
```text
Let's do the architecture section first
```
Response: added §2a Architecture (containers, layers, example flow, repo layout).

**2.11**
```text
I think we should check the connection -- that seems normal / within scope for a barebones health check
```
Response: updated `/health` to check the database (200/503) and exempted it from R11.

**2.12**
```text
add the gaps to our document, it's hard for me with the .md format. Update the OpenAPI/Swagger note you highlighted. Should I remove the security claude code generated since it's technically out of scope?
```
Response: updated the OpenAPI line to match the build; advised keeping the agent's unrequested protections and recording them as findings.

---

## 3. Agent_environment.md

**3.1**
```text
Version:           2.1.283
  Session name:      ResourceHub ASP.NET Core Web API
  Session ID:        [REDACTED: session ID]
  Session kind:      interactive
  Peer address:      [REDACTED: local socket path]
  cwd:               [REDACTED: local path]/cs763-resource-hub
  Login method:      Claude Pro account
  Organization:      [REDACTED: email]'s Organization
  Email:             [REDACTED: email]
  Cloud sessions:    GitHub connected
   
  Model:             Default (Opus 5.5 · Best for everyday, complex tasks)
  MCP servers:       1 connected, 3 need auth, 1 disabled · /mcp
  Setting sources:   User settings
  Auto mode server:  Disabled
```
Response: listed what matters for the file: model set to "Default", 1 MCP server connected, user settings only.

**3.2**
```text
Version:           2.1.283
  Session name:      ResourceHub ASP.NET Core Web API
  Session ID:        [REDACTED: session ID]
  Session kind:      interactive
  Peer address:      [REDACTED: local socket path]
  cwd:               [REDACTED: local path]/cs763-resource-hub
  Login method:      Claude Pro account
  Organization:      [REDACTED: email]'s Organization
  Email:             [REDACTED: email]
  Cloud sessions:    GitHub connected
   
  Model:             opus (claude-opus-5-5)
  MCP servers:       1 connected, 3 need auth, 1 disabled · /mcp
  Setting sources:   User settings
  Auto mode server:  Disabled

.... 

  ❯ Default permission mode                    Manual
    Thinking mode                              true
      Thinking can't be turned off for Opus 5.5
Effort: MEDIUM

....

  Manage MCP servers
  5 servers

    claude.ai
  ❯ ✔ claude.ai Claude Docs   8 tools
    ⚠ claude.ai Gmail   needs authentication
    ⚠ claude.ai Google Calendar   needs authentication
    ⚠ claude.ai Google Drive   needs authentication

    Built-in MCPs (always available)
    ◯ computer-use
```
Response: drafted Agent_environment.md; asked about settings.json and approvals.

**3.3**
```text
Nothing claude is connected to -- the MCP server -- is relevant for this project. And manual is ask before acting. "[REDACTED: shell prompt] % cat ~/.claude/settings.json
{
  "permissions": {
    "defaultMode": "default"
  },
  "enabledPlugins": {
    "caveman@caveman": true
  },
  "extraKnownMarketplaces": {
    "caveman": {
      "source": {
        "source": "github",
        "repo": "JuliusBrussee/caveman"
      }
    }
  },
  "theme": "auto",
  "model": "opus"
}"
```
Response: flagged the enabled `caveman` plugin as part of the agent's configuration and a repeatability issue; filled in the remaining fields.

**3.4**
```text
how do I disable it
```
Response: gave the disable command and how to verify it.

**3.5**
```text
[REDACTED: shell prompt] % cat ~/.claude/settings.json          
{
  "permissions": {
    "defaultMode": "default"
  },
  "model": "opus",
  "enabledPlugins": {
    "caveman@caveman": false
  },
  "extraKnownMarketplaces": {
    "caveman": {
      "source": {
        "source": "github",
        "repo": "JuliusBrussee/caveman"
      }
    }
  },
  "theme": "auto"
},,, you can note that it was disabled now. I had forgotten about it. I trust it. But I'm disabling it going forward to help create repeatable things
```
Response: recorded the plugin as enabled during the PA1 prompts and disabled on 2026-10-31 for repeatability.

---

## 4. Prompts to Claude Code, code review and Prompt_test_log.md

Prompts 1–2 to Claude Code were drafted by me and revised after the assistant's review; prompts 3–4 were drafted by the assistant and approved and run by me. Final versions are verbatim in Prompt_test_log.md.

**4.1** (my first draft of Claude Code prompt 1)
```text
Build an ASP.NET core web API on .NET 10. Use spec.md as the basis. This is a barebones app: a `api/v1/health` endpoint, the project structure, and a db connection to PostgreSQL only. 
Create: `global.json` for SDK 10, `docker-compose.yml` with an api container and a db container, a multi-stage dockerfile (build with the sdk image, run on the runtime image). 
Ensure that the app builds and runs. Create instructions on how it should run. 
Write your notes to the MyAI folder
```
Response: suggested 5 changes: ASP.NET Core runtime image, an explicit scope limit, the layout from spec §2a, a run-and-verify step, README location.

**4.2**
```text
No I like the known-gaps for later. I committed, Let's start the pipeline
```
Response: recommended the SonarQube Cloud free plan; gave setup steps and what the prompt should cover.

**4.3**
```text
"Create or update your `.github/workflows/build.yml`
Here is a base configuration to run a SonarQube Cloud analysis on your master branch and Pull Requests. If you already have some GitHub Actions, you might want to just add some of these new steps to an existing one."
```
Response: advised folding SonarQube's sample into devsecops.yml instead of creating build.yml, and fixing master → main.

**4.4** (my first draft of Claude Code prompt 2)
````text
Prompt
"

```markdown
Create `.github/workflows/devsecops.yml` that runs on every push to main. It should:
- build and test the .net app
- build the docker image from the dockerfile
- trivy: scans dependencies and docker image for known vulnerabilities
- semgrep: static analysis of the c# code
- sonarqube cloud: analysis using the SONAR_TOKEN secret, plus project and organization keys
- non-blocking: scan findings are reported but don't fail the pipeline -- only a broken build or failing test fails it
- saves each scanner's report as a downloadable artifact

Once built, explain how to check the run and write all notes to MyAI. 

SonarQube Cloud's suggested configuration (use these steps inside devsecops.yml, don't create build.yml):
[SonarQube sample: identical to the sample quoted verbatim in Prompt_test_log.md, Prompt 2, so not repeated here]
```
````
Response: flagged the Windows/PowerShell sample, Trivy needing lock files, Semgrep without an account, building only once, and not letting the agent commit.

**4.5**
```text
is there anything I should change by hand ?
```
Response: nothing required; leaving it as generated keeps the record clean.

**4.6**
```text
I completed the google doc sign up prior, the email is fine and I'll make the repo private after the course is over, I've made the repo public. We now have to do: headers + code_AI_history.md, Prompt_test_log.md, AI USage Logs, and tage the release. I've commited agent_environment
```
Response: drafted Claude Code prompt 3 (headers) and the order of the remaining work.

**4.7**
```text
Is what it said it skipped valid? like .md
```
Response: confirmed the skipped files were reasonable, with JSON config files as a grey area to document.

**4.8**
```text
before we do /export and do the ai-history folder, are there any areas in the code that could benefit from a refactor, an edit, or a challenge? Gaps or things done incorrectly
```
Response: listed: scanners may read `evidence/`, undisposed test factories, a test that can't fail, unused `UseAuthorization()`, base-image vulnerabilities.

**4.9**
```text
how do I check the output of the pipeline for evidence/ ? and how do I decide if it's noise
```
Response: explained comparing the new run with the PA1 baseline; after my log check confirmed SonarQube was reading old reports, drafted Claude Code prompt 4.

**4.10**
```text
how to do this? 3. The tests never dispose `WebApplicationFactory`. It's a small resource leak in the tests, and SonarQube may flag it later.
```
Response: explained the fix; I made it by hand (HUMAN MOD).

**4.11**
```text
Are the updates done correctly?
```
Response: confirmed both hand edits were correct; noted cosmetic formatting.

**4.12**
```text
I ran the dotnet test earlier and they passed / skipped as expected. I fixed the two file names. I've made some other updates, as well. Review the documents uncommitted, as well as what was outstanding, to see what's still outstanding. 
Also, is there anything potentially problematic in our current session? I am worried about exposing personal information, such as [REDACTED: personal topic].
```
Response: found wrong prompt file names, an inaccurate commit claim and the Semgrep primary-constructor inconsistency; checked the repo and transcripts for personal information.

**4.13**
```text
apply the fixes. Also, include the challenge.
```
Response: applied the fixes and added the challenge to Prompt_test_log.md.

**4.14**
```text
do we have all items including the updated pipeline artifacts if tthat's what's created? Anything stale?
```
Response: found the scan reports and screenshot were from the first run and the commit table in code_AI_history.md was out of date; updated the table.

# AI_usage_appendix.md — Design-aid prompts (PA1)

Design-aid prompts I sent to the Claude chat assistant (claude.ai, model Claude Opus 5.5) while producing the PA1 deliverables. Referenced from the AI Usage Log in spec.md, Agent_environment.md and Prompt_test_log.md.

**About this appendix:** my prompts are copied verbatim (including typos); `…` marks where I trimmed parts of a message that weren't design-related. Answers to the assistant's multiple-choice questions are shown as *Answered:*. The assistant's replies are summarized in one line each, not quoted. Assembled by the chat assistant from the conversation; reviewed by me. 

**How I used the assistant:** I instructed it to prioritize accuracy over agreeableness, flag uncertainty, and not present guesses as facts. Its guesses are marked in the documents (e.g. the "tracking parameters" interpretation). It still made errors; I caught them in review (wrong file names and an inaccurate claim in Prompt_test_log.md) and they were corrected before submission. 

**Scope:** includes every prompt that shaped a deliverable. Left out: tool installation and troubleshooting (installing .NET, `gh` and git commands, downloading artifacts), terminal output I pasted, and messages containing personal or account details (marked *[omitted]* where they sit inside a relevant prompt). The full conversation is available to the instructor on request.

## 1. Workflow setup

| # | My prompt | Assistant's response (summary) |
|---|---|---|
| 1.1 | I received this advice for the project, "Hi everyone, A tip from my own workflow: I keep all my prompts and notes on what I've done in Obsidian … [facilitator's tip on prompts/ and MyAI/ folders]" | Assessed the tip against the deliverables; suggested keeping notes outside the repo so the PA2 agent can't see security notes, recording model/mode per prompt, a fixed note format |
| 1.2 | I use AnyType instead of Obsidian, so I dont' know how that changes anything. Ask me questions as needed and help me understand our process | Explained AnyType stores notes in a database, not files; recommended plain folders for the agent. *Answered:* Claude Code; plain folders; in the course folder |
| 1.3 | I like your suggestions for 1, and your guess for tracking parameters sound correct. I think the approach sounds valid. Before we formally begin, make sure I understand what we're doing. Also, is there anything I need to set up beforehand? github? access for you? Do we need to set up how we're going to work? | Explained the two roles (chat assistant plans/reviews, Claude Code writes code), PA1 order, setup list, and a working agreement including a log of who did what |

## 2. spec.md

| # | My prompt | Assistant's response (summary) |
|---|---|---|
| 2.1 | Domain #2, The Resource Allocation & Help Desk Hub / C# / ASP.NET Core, PostgreSQL, Docker | Noted tool fit for .NET and the domain's sensitive assets; asked design questions. *Answered:* users "I'm not sure. Do I decide this? It didn't come from the project?"; help desk "Simple tickets"; tracking parameters "I have no idea. What do you think?"; drafting approach "I don't know" |
| 2.2 | (the assistant's reply to my answers in 2.1) | Explained the spec is mine to define; suggested 3 roles; offered a reading of "tracking parameters" as an asset audit log (flagged as a guess); recommended Q&A → assistant formats → I review |
| 2.3 | I don't know. I know as much as you do based on the assignment doc | Proposed defaults with reasons: 2 asset types, request/approve flow, team ownership, 4 statuses, voucher value |
| 2.4 | That looks good for now | Drafted spec.md |
| 2.5 | should we keep a separate note of deliberate holes to circle back on later or keep them unmarked to "rediscover" later? Like the login gap. I like the note that "security will be added in later phases" note. | Recommended a private gap list outside the repo (PA2 prep); added the "security in later phases" line |
| 2.6 | are we confident in the current specs ? | Listed 9 gaps: missing schemas, response shapes, matching rule, audit log scope, etc. |
| 2.7 | let's start on filling in those gaps | Asked me 2 design questions. *Answered:* approval matching "Same type + same name"; audit log "Assets + requests". Added business rules, schemas, paging |
| 2.8 | Do a new, quick review | Found 7 inconsistencies (audit-entry count, voucher value, seed names, missing-header behavior) |
| 2.9 | 5, 400 if missing or unknown. Fix the others | Added R11 (400 for missing/unknown `X-User-Id`), seed asset table, other fixes |
| 2.10 | claude.md already done. Is what we created valid against project requirements for Assignment 1 / Phase 1 only | Mapped PA1 deliverables to status; flagged the missing architecture section |
| 2.11 | it sounds like you think the architecture section should be done first, so lets do that | Added §2a Architecture (containers, layers, flow, repo layout) |
| 2.12 | I think we should check the connection -- that feels normal for a health check | Updated `/health` to check the database (200/503) and exempted it from R11 |
| 2.13 | add the gaps, it's hard for me with the format. Update the OpenAPI/Swagger note you highlighted. Should I remove the security? | Updated the OpenAPI line to match the build; advised keeping the agent's unrequested protections and recording them as findings |

## 3. Agent_environment.md

| # | My prompt | Assistant's response (summary) |
|---|---|---|
| 3.1 | *[omitted: pasted `/status` output with account details]* | Listed what matters for the file: model set to "Default", 1 MCP server connected, user settings only |
| 3.2 | *[omitted: pasted `/status`]* … Default permission mode Manual · Thinking mode true · Effort: MEDIUM · [MCP list: Claude Docs connected; Gmail, Google Calendar, Google Drive need authentication; computer-use built-in] | Drafted Agent_environment.md; asked about settings.json and approvals |
| 3.3 | Nothing claude is connected to -- the MCP server -- is relevant for this project. And manual is ask before acting. [pasted `~/.claude/settings.json`] | Flagged the enabled `caveman` plugin as part of the agent's configuration and a repeatability issue; filled the remaining fields |
| 3.4 | how do I disable it | Gave the disable command and how to verify |
| 3.5 | you can note that it was disabled now. I had forgotten about it. I trust it. But I'm disabling it going forward to help create repeatable things | Recorded: enabled during PA1 prompts, disabled 2026-11-01 |

## 4. Prompts to Claude Code and Prompt_test_log.md

Prompts 1–2 to Claude Code were drafted by me and revised after the assistant's review; prompts 3–4 were drafted by the assistant and approved and run by me. Final versions are verbatim in Prompt_test_log.md.

| # | My prompt | Assistant's response (summary) |
|---|---|---|
| 4.1 | [my draft of Claude Code prompt 1, barebones app] | Suggested 5 changes: ASP.NET runtime image, explicit scope limit, layout from spec §2a, run-and-verify step, README location |
| 4.2 | … I committed, Let's start the pipeline | Recommended SonarQube Cloud free plan; setup steps; what the prompt should cover |
| 4.3 | [my draft of Claude Code prompt 2, pipeline, with the SonarQube sample] | Flagged the Windows/PowerShell sample, Trivy needing lock files, Semgrep without an account, single build, no commit |
| 4.4 | is there anything I should change by hand ? | Nothing required; keeping it as generated keeps the record clean |
| 4.5 | … We now have to do: headers + code_AI_history.md, Prompt_test_log.md, AI USage Logs, and tage the release. … | Drafted Claude Code prompt 3 (headers) |
| 4.6 | before we do /export and do the ai-history folder, are there any areas in the code that could benefit from a refactor, an edit, or a challenge? Gaps or things done incorrectly | Listed: scanners may read `evidence/`, `app.Run()`, undisposed test factories, a test that can't fail, unused `UseAuthorization()`, base-image vulnerabilities |
| 4.7 | how do I check the output of the pipeline for evidence/ ? and how do I decide if it's noise | Explained comparing the new run with the PA1 baseline; after my log check confirmed SonarQube was reading old reports, drafted Claude Code prompt 4 |
| 4.8 | how to do this? 3. The tests never dispose `WebApplicationFactory`. … | Explained the fix; I made it by hand (HUMAN MOD) |
| 4.9 | Did I do the updates correctly? | Confirmed both hand edits correct; noted cosmetic formatting |
| 4.10 | … I've made some other updates, as well. Review the documents uncommitted, as well as what was outstanding, to see what's outstanding. *[omitted: privacy question]* | Found wrong prompt file names, an inaccurate commit claim, and the Semgrep primary-constructor inconsistency |
| 4.11 | apply the fixes. Also, include the challenge. | Applied the fixes and added the challenge to Prompt_test_log.md |

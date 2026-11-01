# code_AI_history.md

Log of every AI interaction that generated or modified code in this repo, and how each result was modified and verified.

> **AI Usage Log for this file:** drafted by the Claude chat assistant (claude.ai, not Claude Code) from my saved prompts, Claude Code's task notes and the exported transcripts; I reviewed and edited it. Conceptual AI contribution: ~80%.

## Setup

| Item | Value |
|---|---|
| Code agent | Claude Code 2.1.283, model Claude Opus 5.5 (`claude-opus-5-5`), effort Medium, thinking on |
| Permission mode | Manual (asks before every command and file change) |
| Plugin active during PA1 | `caveman` (terse replies), disabled after PA1. See Agent_environment.md |
| Chat assistant | Claude (claude.ai) for planning, spec and docs. It did not write source code. Its contributions are disclosed in each .md file's AI Usage Log |
| Full prompts | `Prompt_test_log.md` |

## Exported agent history

`/export` was unavailable, so the raw Claude Code session transcripts were copied from `~/.claude/projects/`.

| File | Session | Contains |
|---|---|---|
| `ai-history/claude-code-session-a44980b8-setup.jsonl` | a44980b8 | `/model` and `/add-dir` setup; the rejected commit (#1); the interrupted first run of prompt 01 (#2); reading settings for Agent_environment.md |
| `ai-history/claude-code-session-58ced76a-main-build.jsonl` | 58ced76a | Prompts 01–04 (#3–#6) with all tool calls and results |

Checked before publishing: no tokens, keys or passwords other than the local dev/CI placeholder passwords already in the repo.

## My review process

Before every commit, I read the changed code and files myself, then asked the chat assistant to check them against my understanding, for example:
- "Did I do the updates correctly?" (my two hand edits)
- "Is what it said it skipped valid? like .md" (Claude Code's header choices)
- "Is there anything I should change by hand?" (pipeline)
- "Are there any areas in the code that could benefit from a refactor, an edit, or a challenge?" (whole codebase, led to the two HUMAN MOD fixes and the evidence fix)
- "Are we confident in the current specs?" and "Do a new, quick review" (spec.md)

I also verified each change myself: running the app and tests, watching each GitHub Actions run, and checking scanner logs.

## Interactions

Times are local (Toronto).

| # | When | Prompt | What the agent did | How I modified / verified | Commit |
|---|---|---|---|---|---|
| 1 | 10-31 13:48 | (accidental) I pasted my own git commands into Claude Code | Proposed `git add spec.md && git commit … && git push`, adding a `Co-Authored-By: Claude` line | **Rejected** the action in Manual mode. Nothing changed. I committed myself | — |
| 2 | 10-31 14:13 | 01 barebones app (first try) | Listed files, read spec.md | I interrupted it and restarted in a fresh session (#3). No files changed | — |
| 3 | 10-31 14:14 | 01 barebones app | Created solution, API project (health endpoint, empty DbContext), test project (3 tests), global.json, multi-stage Dockerfile, .dockerignore, docker-compose.yml, README | Agent: build 0 errors; tests 2 passed / 1 skipped (3/3 with db); compose up, health 200; db stopped → 503; db started → 200. Me: reviewed the files (with chat-assistant review); no hand edits | `1ed1d10` |
| 4 | 10-31 14:52 | 02 DevSecOps pipeline | Wrote devsecops.yml (build, test, Docker image, Trivy, Semgrep, SonarQube Cloud, non-blocking scans, artifacts); enabled NuGet lock files (Directory.Build.props, 2 × packages.lock.json); Dockerfile restores in locked mode; rewrote HealthController constructor so Semgrep could parse it; README update | Agent: local scanner runs, actionlint. Me: reviewed the workflow (with chat-assistant review); GitHub run `41232766096` succeeded; saved log and scan reports in `evidence/` | `f9adda2` |
| 5 | 11-01 12:23 | 03 AI contribution headers | Added the header to 13 source files; skipped JSON, lock files, .md, .gitignore, evidence/ | Agent: build, tests 3/3 with db, docker build, actionlint. Me: checked diff (header lines only); GitHub run `41331450855` succeeded | `29c340a` |
| 6 | 11-01 12:32 | 04 exclude evidence from scans | Excluded `evidence/` from SonarQube, Trivy and Semgrep; limited SonarQube report paths to `tests/` | The chat assistant flagged that committed reports might be scanned; I checked the run log and confirmed SonarQube was reading old coverage/test reports from evidence/. Agent: tested exclusions with planted files. Me: GitHub run `41332159805`, confirmed reports now only come from `tests/` | `6608d21` |

## Human modifications

| When | File | Change | Why | Verified | Commit |
|---|---|---|---|---|---|
| 11-01 12:47 | `src/ResourceHub.Api/Program.cs` | `app.Run()` → `await app.RunAsync()`; header 95/5 | SonarQube finding | `dotnet test`: 2 passed, 1 skipped (no local db), as before | `5bfa2d2` |
| 11-01 12:47 | `tests/ResourceHub.Tests/HealthEndpointTests.cs` | Dispose `WebApplicationFactory` instances with `using`; header 85/15 | Resource leak in tests | `dotnet test`: 2 passed, 1 skipped (no local db), as before | `0c091eb` |

## All commits

Commit labels: `AI AGENT BUILD:` (agent code), `HUMAN MOD:` (my hand edits), `DOCS:` (documentation, my own convention; the course labels cover code only). The `AI AGENT BUILD:` / `HUMAN MOD:` labels were introduced on 2026-11-01 (commit `29c340a` onward). Earlier commits are not rewritten; this table records what every commit contains. Checked against `git log --name-status`.

| Commit | Date | Message | Files | Category |
|---|---|---|---|---|
| `20b4bd5` | 10-31 13:32 | Add spec draft and .gitignore | spec.md, .gitignore | Docs: spec.md drafted by chat assistant. `.gitignore` from `dotnet new gitignore` template + 1 line by me |
| `e8cb3b1` | 10-31 13:51 | Spec: business rules, schemas, audit log covers requests | spec.md | Docs (chat-assistant drafted). Committed by me after rejecting Claude Code's attempt (#1) |
| `08fc025` | 10-31 13:53 | claude instructions | CLAUDE.md | Docs: text drafted by chat assistant, added by me |
| `9361e90` | 10-31 13:56 | Spec: R11 user header, seed asset names, audit rules | spec.md | Docs (chat-assistant drafted) |
| `19f9684` | 10-31 14:13 | architecture | spec.md | Docs: architecture section (chat-assistant drafted) |
| `1ed1d10` | 10-31 14:35 | Barebones API: health check, Docker, compose, tests | 16 new code/config files, spec.md | **Mixed:** AI AGENT BUILD (#3) for all code/config files, plus a spec.md edit (health check verifies the database, R11 exempts /health) drafted by the chat assistant and committed together |
| `f9adda2` | 10-31 15:08 | devsecops pipeline | devsecops.yml, Directory.Build.props, 2 × packages.lock.json, Dockerfile, README.md, HealthController.cs | AI AGENT BUILD (#4) |
| `cebfa01` | 10-31 15:15 | PA1 pipeline run evidence | evidence/pipeline-run-pa1.txt | Evidence (CI log) |
| `e81a92a` | 10-31 15:19 | PA1 pipeline run evidence | evidence/pipeline-run-pa1.png | Evidence (my screenshot) |
| `6531dfd` | 10-31 15:20 | Add pipeline scan reports | evidence/scan-reports/** (13 files) | Evidence (CI artifacts) |
| `3e8fd97` | 11-01 12:19 | AI environment | Agent_environment.md | Docs (chat-assistant drafted from my settings) |
| `29c340a` | 11-01 12:26 | AI AGENT BUILD: AI contribution headers | 13 source files | AI AGENT BUILD (#5) |
| `6608d21` | 11-01 12:34 | AI AGENT BUILD: exclude evidence from scans | devsecops.yml | AI AGENT BUILD (#6) |
| `5bfa2d2` | 11-01 12:47 | HUMAN MOD: use RunAsync (SonarQube finding) | Program.cs | HUMAN MOD |
| `0c091eb` | 11-01 12:47 | HUMAN MOD: dispose test factories | HealthEndpointTests.cs | HUMAN MOD |
| `05589ec` | 11-01 12:52 | PA1 final pipeline run log | evidence/pipeline-run-pa1-final.txt | Evidence (CI log) |
| `08357e9` | 11-01 13:21 | DOCS: code_AI_history.md and Claude Code transcripts | code_AI_history.md, ai-history/ (2 files) | Docs (chat-assistant drafted, reviewed by me) + exported agent history |

## Files without a header

| File | Reason |
|---|---|
| `global.json`, `appsettings.json`, `appsettings.Development.json`, `launchSettings.json` | JSON: no comment syntax. Generated by Claude Code (#3) |
| `src/…/packages.lock.json`, `tests/…/packages.lock.json` | Generated by `dotnet restore`; editing breaks locked restore |
| `.gitignore` | Microsoft template + 1 line by me, not AI generated |
| `*.md` | Documents: AI Usage Log on the first page instead |
| `evidence/**`, `ai-history/**` | Tool output, not source code |

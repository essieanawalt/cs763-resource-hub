# Agent_environment.md


**Contents**

- [AI Usage Log - PA2 updates](#ai-usage-log---pa2-updates)
- [9. Update for PA2 (2026-11-13)](#9-update-for-pa2-2026-11-13)
- [AI Usage Log - PA1 specific / general](#ai-usage-log---pa1-specific--general)
- [1. Agent](#1-agent)
- [2. Model](#2-model)
- [3. Hyperparameters and configuration](#3-hyperparameters-and-configuration)
- [4. Operating mode](#4-operating-mode)
- [5. Context and instructions given to the agent](#5-context-and-instructions-given-to-the-agent)
- [6. Tools the agent used](#6-tools-the-agent-used)
- [7. Host environment](#7-host-environment)
- [8. Reproducibility notes](#8-reproducibility-notes)

## AI Usage Log - PA2 updates

| Item | Detail |
|---|---|
| Conceptual AI contribution (section 9) | ~60% AI / ~40% me |
| My part | Decided to pin the model and turn off auto-updates; edited `~/.claude/settings.json` myself; restarted Claude Code and verified the session info; decided to record PA2 changes in a separate update section instead of changing the PA1 sections; rewrote the section intro, the table and the notes; reorganized the file and added the table of contents structure |
| AI part (Claude chat assistant) | Pointed out the model alias and auto-update gaps and how to close them; drafted the first version of section 9 (tables and notes); generated the table-of-contents links |
| Design-aid prompts | Setup only (agent configuration); no design-aid prompts for this section. PA2 design-aid prompts are logged in `.ai_logs/PA2/` |

## 9. Update for PA2 (2026-11-13)

This section records what changed before the PA2 build, so the agent stays the same across PA2 sessions and provides continuity going forward based on the updated requirements. These updates were made based on threads in the M2 discussion as well as the updated project outline.

| Setting | PA1 | PA2 | Why |
|---|---|---|---|
| `model` in `~/.claude/settings.json` | `opus` (alias) | `claude-opus-5-5` (full model ID) | An alias can point to a newer model |
| Auto-updates | On (default) | Off: `"env": {"DISABLE_AUTOUPDATER": "1"}` | Keeps the Claude Code version consistent |
| Design-aid prompts | `AI_usage_appendix.md` | `.ai_logs/PA1/AI_usage_appendix.md` | To align with updated requirements |

Verified after restart (Session info):

| Item | Value |
|---|---|
| Claude Code | 2.1.321 |
| Interface | Claude desktop app 5.11492.2 |
| Host OS | macOS 27.2.1 (arm64) |
| Model / effort | Opus 5.5 · Medium |
| Permissions | Manual |
| MCP servers | None |
| Sandbox | Off |

Notes:
- PA1 and PA2 run on different Claude Code versions (2.1.283 vs 2.1.321), because the update happened before auto-updates were turned off
- Some users report the auto-update setting being ignored, so the version is checked at the start of each session and recorded with each prompt
- **Not controllable:** sampling settings such as temperature are fixed by Claude Code and not visible 


## AI Usage Log - PA1 specific / general

| Item | Detail |
|---|---|
| Conceptual AI contribution | ~70% AI / ~30% me |
| My part | Gathered and verified the full configuration myself (versions, `/status`, `/config`, `/mcp`, `settings.json`); set the model explicitly instead of "Default"; decided to disable the caveman plugin so later phases are repeatable; confirmed which settings and MCP servers were relevant; reviewed and corrected the draft |
| AI part (Claude chat assistant) | Organized the facts into sections and wrote the text; flagged the caveman plugin and the "Default" model setting as reproducibility issues |
| Design-aid prompts | See `AI_usage_appendix.md` [** see PA2 updates, section 9]|

Configuration of the autonomous AI developer agent used to build this service (PA1).
Recorded 2026-10-31.

## 1. Agent

| Item | Value |
|---|---|
| Agent | Claude Code (Anthropic), command-line coding agent |
| Version | 2.1.283 |
| Interface | Interactive terminal session, started in the repo root |
| Account / plan | Claude Pro subscription (no API key) |
| Session kind | Interactive (one prompt at a time, human present) |

## 2. Model

| Item | Value |
|---|---|
| Model | Claude Opus 5.5 (`claude-opus-5-5`) |
| How selected | Set explicitly with `/model` → `opus` (earlier the same model ran as the "Default" choice) |
| Thinking mode | On (can't be turned off for Opus 5.5) |
| Effort | Medium |

## 3. Hyperparameters and configuration

Claude Code does not expose sampling hyperparameters (temperature, top-p, max tokens) to the user. They are set by the tool and not visible. The settings that can be controlled are:

| Setting | Value | Effect |
|---|---|---|
| Model | claude-opus-5-5 | Which LLM runs |
| Effort | Medium | How much reasoning the model does per step |
| Thinking | On | Model reasons before answering |
| Permission mode | Manual (default) | Agent asks before running commands or editing files |
| Auto mode | Disabled | No unattended multi-step runs |
| Settings source | User settings only (`~/.claude/settings.json`); no project `.claude/` settings | |
| `defaultMode` | `default` | Shown as "Manual" in `/config`: ask before acting |
| `model` | `opus` | Pins the Opus model alias |
| Plugins | `caveman@caveman` (third-party, github.com/JuliusBrussee/caveman). **Enabled during PA1 prompts 1–2 (2026-10-31); disabled 2026-10-31, before prompts 3–4** | Makes the agent reply in compressed, terse language to cut output tokens. Per the plugin's description it affects how the agent writes explanations, not the code. Disabled so later phases run on standard Claude Code and results are repeatable |
| Theme | auto | Display only |

## 4. Operating mode

- **Permission mode: Manual** (`defaultMode: default`). Claude Code asked for approval before running shell commands and changing files; I approved actions during the session.
- **Human in the loop:** I write each prompt, approve actions, review the output, and do all git commits and pushes myself. Prompts told the agent not to commit or push.
- **Scope per prompt:** one task per prompt (PA1: barebones app; DevSecOps pipeline).

## 5. Context and instructions given to the agent

| Source | What it contains |
|---|---|
| `CLAUDE.md` (repo root) | Instruction to write a short task note after each task, with fixed headings |
| `spec.md` | Service specification the prompts point to |
| Added directory | `MyAI/PA1` notes folder outside the repo, added with `/add-dir`, write-only use for task notes |
| Prompts | Saved verbatim before running; see Prompt_test_log.md |

The agent had no access to my other project notes outside the repo.

## 6. Tools the agent used

**Built-in Claude Code tools:** reading, searching, creating and editing files; running shell commands in the repo.

**Shell commands it ran during PA1** (from its task notes): `dotnet new`, `dotnet add package`, `dotnet restore / build / test`, `docker build`, `docker compose up / stop / start / down`, `curl` (health check), Trivy and Semgrep in Docker containers, `actionlint` (workflow lint), `gh secret list`.

**MCP servers (Claude Code `/mcp`):** none are relevant to this project; none were used.

| Server | Status | Used in PA1 |
|---|---|---|
| claude.ai Claude Docs | Connected (8 tools) | No |
| claude.ai Gmail | Needs authentication (not available) | No |
| claude.ai Google Calendar | Needs authentication (not available) | No |
| claude.ai Google Drive | Needs authentication (not available) | No |
| computer-use (built-in) | Disabled | No |

**Plugins:** `caveman` (see Section 3). Third-party plugin that runs its own hook scripts when a session starts and on each prompt. It had been installed earlier and was still enabled during PA1 prompts 1–2; disabled on 2026-10-31, before prompts 3–4. From then on, no plugins are enabled.

## 7. Host environment

| Item | Value |
|---|---|
| Machine | MacBook Pro, Apple M5 |
| OS | macOS Golden Gate 27.0.1 |
| Shell | zsh |
| IDE | VS Code with .NET extensions |
| Git | 2.54.0 (Apple Git-157) |
| .NET SDK | 10.0.403 (Homebrew cask `dotnet-sdk`); repo pins SDK 10 via `global.json` |
| Docker | Docker Desktop 29.9.2 (build a82e528), Compose v5.6.1 |
| GitHub CLI | `gh` (Homebrew) |
| CI | GitHub Actions, `ubuntu-latest` runners |

Note: Docker was not on the agent's shell PATH; it used Docker Desktop's `~/.docker/bin` directly.

## 8. Reproducibility notes

- No plugins enabled from 2026-10-31 on (caveman disabled), so the agent runs as standard Claude Code.
- Model is now set explicitly rather than "Default", so updates to Claude Code's default model don't change results.
- Effort and thinking settings are recorded above; changing them may change agent behavior.
- Every prompt is saved verbatim, with the agent's own task note, so runs can be compared across phases.

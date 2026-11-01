# Agent_environment.md

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
| Plugins | `caveman@caveman` (third-party, github.com/JuliusBrussee/caveman). **Enabled during both PA1 prompts; disabled 2026-10-31 after PA1 prompts** | Makes the agent reply in compressed, terse language to cut output tokens. Per the plugin's description it affects how the agent writes explanations, not the code. Disabled so later phases run on standard Claude Code and results are repeatable |
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

**Plugins:** `caveman` (see Section 3). Third-party plugin that runs its own hook scripts when a session starts and on each prompt. It had been installed earlier and was still enabled during PA1; disabled on 2026-10-31. From then on, no plugins are enabled.

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

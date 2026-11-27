# Agent_environment.md (PA3)

Configuration of the autonomous AI developer agent for PA3. Earlier configuration is in the earlier phase folders, and the section numbers continue from there: sections 1–8 (PA1) in `PA1/Agent_environment.md`, sections 9–10 (PA2) in `PA2/Agent_environment.md`.

**Contents**

- [AI Usage Log - section 11](#ai-usage-log---section-11)
- [11. Update for PA3 (2026-11-26)](#11-update-for-pa3-2026-11-26)

## AI Usage Log - section 11

| Item | Detail |
|---|---|
| Conceptual AI contribution (section 11) | ~55% AI / ~45% me |
| My part | Decided the starting points, working folders, deny rules, key handling and the switch to Auto; made the setup commits; set up and tested the token issuer; ran the restriction tests, including the one that showed the deny rules weren't loading; gathered the session info for both runs |
| AI part (Claude chat assistant) | Proposed the setup options, checked the copy against the tag, read the issuer README, wrote the local settings files after the deny-rule finding, and drafted this section |
| Design-aid prompts | Logged in `PA3/.ai_logs/2026-11-26_prompt-architecture-log.md` |

## 11. Update for PA3 (2026-11-26)

Setup for the PA3 measured runs, recorded before the first PA3 prompt and completed after the runs. One section covers both runs: my own repository and the peer repository. The peer run's session info matched the own run's (Claude Code 2.1.330, `claude-opus-5-5` · Medium, no MCP servers, sandbox off), started in `PA2/functional-codebase/`, and was switched to Auto before the prompt in the same way.

Session info (own working folder, 2026-11-26, from the Claude Code session info):

| Item | Value |
|---|---|
| Claude Code | **2.1.330** (2.1.325 in PA2) |
| Interface | Claude desktop app 6.28899.6 (5.11492.5 in PA2) |
| Host OS | macOS 27.2.1 (arm64) |
| Model / effort | `claude-opus-5-5` · Medium |
| Thinking | Not shown in the session info (PA1 recorded thinking on) |
| Permissions | Manual at session start; switched to **Auto** for the own measured run, iteration 1, after the session info was taken and before the prompt was sent |
| MCP servers | None |
| Sandbox | Off |

Changes since PA2:

| Setting | PA2 | PA3 | Why |
|---|---|---|---|
| Claude Code version | 2.1.325 | 2.1.330 | Changed with auto-updates off in the repo settings. Cause not confirmed; the desktop app also changed version, so the update likely came with the app (same pattern as PA2) |
| Permission mode | Manual (I approve each file change and command) | Auto for the measured runs (setup and restriction tests stayed Manual) | One iteration is one agent session that ends when the agent stops; Auto lets it work through without stopping at each approval. With no approvals, the local deny rules are what keeps the agent out of my logs, documents and the other repository |
| Starting point (own) | `main` | Branch `pa3-own` from the `pa2` tag (`5211272`), plus two setup commits with no code changes: `2b82089` (unchanged copy of the PA2 code) and `b2b6c34` (deny rules) | The measured run starts from the PA2 release tag |
| Code location (own) | `PA2/functional-codebase/` | `PA3/secure-codebase/`, an unchanged copy of `PA2/functional-codebase/` from the `pa2` tag (46 files, checked identical) | PA3 deliverable folder. `PA2/` stays as the baseline, and the root build files still build it |
| Build files (own) | Root `Dockerfile` and `docker-compose.yml` | The agent writes its own Dockerfile and compose file inside `PA3/secure-codebase/` | Keeps PA2 building as tagged; the database fix (V6) lands in PA3's compose file |
| Working folder (own) | `PA2/functional-codebase/`, plus `/add-dir ../../../MyAI/PA2` | `PA3/secure-codebase/`, plus `/add-dir ../../../MyAI/PA3` for the agent's task notes. Nothing else is added; in particular not the token issuer folder, which holds the private key | Same reason as PA2: logs and documents stay outside the agent's folder |
| Read restrictions (own) | `/PA1/**`, `/PA2/.ai_logs/**`, `/PA2/evidence/**`, `/PA2/Agent_environment.md` | Adds `/PA3/.ai_logs/**` and `/PA3/*.md` | Keeps my PA3 logs, the prompt architecture, the analysis and this file out of the run. `/PA3/*.md` covers only files directly in `PA3/`, so the agent can still write `secure-codebase/README.md` |
| Peer repository | n/a | Fork, branch `pa3-peer` at the partner's PA2 tag `assignment-2-v1.0.0` (`1a094d6`), no commits. Working folder `PA2/functional-codebase/`, plus `/add-dir ../../../MyAI/PA3-peer` for the agent's task notes (a separate folder, so the peer run can't see my own run's notes). Local, uncommitted deny rules (`.claude/settings.local.json`, excluded through `.git/info/exclude`) on the partner's `security-analysis.md`, `sast-triage-report.md`, `.ai_logs/`, `evidence/`. No `CLAUDE.md` in the peer repo, so the header and task-note rules come from the prompt (section 3.3) | Same agent setup on a different service and stack, without adding files to the partner's history |
| Model pin and auto-update setting (peer) | n/a | Added to the peer's local settings file: `"model": "claude-opus-5-5"`, `"env": {"DISABLE_AUTOUPDATER": "1"}`. Confirmed with the peer session info at the start of the peer run | The own repo pins these in `.claude/settings.json`; the peer run needs the same |
| Test tooling | None | Mock token issuer `schin8/cs763-mock-token-issuer` v1.0.0, cloned outside both repositories in its own Python environment. Its 23 tests passed and its check with an independent JWT library passed all 17 test tokens for RS256 and ES256. Keys generated with RS256 in its `keys/` folder (not committed). Both services read `jwks.json` from that folder as a local read-only file, with `iss` `http://localhost:9000` and `aud` `cs763-api` | Provides signing keys and valid and invalid tokens without an identity provider. **The tool was generated with AI assistance (Claude Code), as its README and the facilitator state** |

Restriction test (throwaway sessions, not used for the runs):

| Working folder | Request | Result |
|---|---|---|
| Own: `PA3/secure-codebase/` | Read a file on the deny list | Claude Code asked for permission instead of reading it |
| Peer: `PA2/functional-codebase/` | Read a file on the deny list | Claude Code asked for permission instead of reading it |
| Own: `PA3/secure-codebase/` | Read `/PA2/Agent_environment.md` (on the deny list), approved | **Read succeeded.** The agent reported that nothing blocked it and that my earlier refusals were my own, not a deny rule |

**Finding:** the repository's `.claude/settings.json` deny rules do not apply when Claude Code starts in a subfolder. The requests for permission came from the working-folder limit and Manual mode, not from the deny list.

**Fix:** a local, uncommitted `.claude/settings.local.json` in each working folder (`PA3/secure-codebase/`, excluded through `.git/info/exclude`; the peer's `PA2/functional-codebase/`, covered by the existing `.claude/` exclude). Each pins the model, turns off auto-updates and denies reads by absolute path. Own: `PA1/`, the PA2 logs, evidence and `Agent_environment.md`, my PA3 logs and documents, the peer repository, the peer's task notes folder, the token issuer, the peer evidence, my prompts and private notes. Peer: the partner's analysis documents, logs and evidence, my repository, my own task notes folder, and the same shared items. The committed root rules stay; they apply only to sessions started at the root.

Re-test after the fix (new throwaway sessions):

| Working folder | Request | Result |
|---|---|---|
| Own: `PA3/secure-codebase/` | Read `PA2/Agent_environment.md` | Refused without asking: "File is in a directory that is denied by your permission settings." |
| Peer: `PA2/functional-codebase/` | Read the partner's `security-analysis.md` | Refused without asking, same message |

Notes:
- **Not controllable:** sampling settings such as temperature are fixed by Claude Code and not visible (unchanged since PA1)
- The version is read from the session info at the start of each session; the peer run's session info is recorded with its iteration

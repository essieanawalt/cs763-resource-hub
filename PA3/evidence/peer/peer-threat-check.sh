#!/usr/bin/env bash
# Tool: Claude chat assistant (claude.ai, Claude Opus 5.5)
# Language stack: Bash / curl / Python 3 (standard library) / Docker Compose
# Prompt Summary: "Fixed requests for the peer threats PT1-PT3, written from the peer's OpenAPI description before the run"
# AI Contribution: Whole script: project, stale-update, change/download/delete and oversized-upload requests (~100%)
# Human Modifications: None; I read it before running it
# Verification: Run on the PA2 baseline and at pa3-measured; outputs in this folder
# Confidence: Medium
# Peer threat check (PT1-PT3), fixed before the measured run.
# Run once on the peer's PA2 baseline (no token) and again at pa3-measured (TOKEN=... set).
# Written by the Claude chat assistant from the peer API's OpenAPI description
# (peer-openapi-pa2.json) and the member/unit format in the peer's fixture data; not from the peer's code.
# Usage: from anywhere, with the peer stack running:
#   bash peer-threat-check.sh baseline
#   TOKEN=<jwt> bash peer-threat-check.sh measured
set -u
LABEL="${1:-baseline}"
BASE="${BASE:-http://127.0.0.1:8002}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PEER_PA2="$HERE/../peer-artifact-indexer/PA2"
OUT="$HERE/peer-threat-check-$LABEL-$(date +%Y-%m-%d).txt"
SIZE_MB="${SIZE_MB:-50}"
TMP="$(mktemp -d)"
AUTH=()
[ -n "${TOKEN:-}" ] && AUTH=(-H "Authorization: Bearer $TOKEN")

exec > >(tee "$OUT") 2>&1

say() { printf '\n%s\n' "$*"; }
# req METHOD PATH [JSON] -> prints status and body; body saved in $TMP/body
req() {
  local m="$1" p="$2" data="${3:-}" code
  if [ -n "$data" ]; then
    code=$(curl -sS -o "$TMP/body" -w '%{http_code}' -X "$m" ${AUTH[@]+"${AUTH[@]}"} -H 'Content-Type: application/json' -d "$data" "$BASE$p")
  else
    code=$(curl -sS -o "$TMP/body" -w '%{http_code}' -X "$m" ${AUTH[@]+"${AUTH[@]}"} "$BASE$p")
  fi
  echo "$m $p -> $code"
  head -c 600 "$TMP/body"; echo
}
# id from the last JSON body: tries id, project_id, artifact_id
lastid() { python3 -c "import json,sys
try: d=json.load(open('$TMP/body'))
except Exception: print(''); sys.exit()
print(next((d[k] for k in ('id','project_id','artifact_id') if isinstance(d,dict) and k in d),''))"; }

PROJECT='{"title":"PT check original","description":"original","members":[{"id":"alice","role":"student"}],"units":[{"type":"lab","id":"lab-robotics"}]}'
proj_with_title() { printf '{"title":"%s","description":"%s","members":[{"id":"alice","role":"student"}],"units":[{"type":"lab","id":"lab-robotics"}]}' "$1" "$2"; }

echo "Peer threat check: $LABEL"
date
echo "Base URL: $BASE   Token sent: $([ -n "${TOKEN:-}" ] && echo yes || echo no)"
( cd "$PEER_PA2" && echo "Peer repo: $(git rev-parse --short HEAD) on $(git branch --show-current)"; git status --short | head -5 )
START="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

say "=== PT1 Tampering: two clients update the same project from the same original read ==="
req POST /api/v1/projects "$PROJECT"; P1="$(lastid)"; echo "project: $P1"
req GET "/api/v1/projects/$P1"
echo "-- client A updates (based on the original read)"
req PUT "/api/v1/projects/$P1" "$(proj_with_title 'PT1 update A' 'change from client A')"
echo "-- client B updates, also based on the original read (stale)"
req PUT "/api/v1/projects/$P1" "$(proj_with_title 'PT1 update B (stale)' 'change from client B')"
echo "-- final state"
req GET "/api/v1/projects/$P1"
echo "Expected if protected: client B's update refused (409 or 412). Not protected: 200 and A's change is gone."

say "=== PT2 Repudiation: change, download and delete, then look for a record ==="
req POST /api/v1/projects "$(proj_with_title 'PT2 project' 'for the record check')"; P2="$(lastid)"; echo "project: $P2"
req POST "/api/v1/projects/$P2/artifacts/manuscripts/" '{"title":"PT2 manuscript","abstract":"check","authors":["alice"],"keywords":["check"],"year":2026}'; A2="$(lastid)"; echo "artifact: $A2"
printf 'PT2 small manuscript\n' > "$TMP/small.txt"
code=$(curl -sS -o "$TMP/body" -w '%{http_code}' ${AUTH[@]+"${AUTH[@]}"} -F "file=@$TMP/small.txt" "$BASE/api/v1/projects/$P2/artifacts/manuscripts/$A2/file"); echo "POST upload -> $code"; head -c 400 "$TMP/body"; echo
code=$(curl -sS -o /dev/null -w '%{http_code}' ${AUTH[@]+"${AUTH[@]}"} "$BASE/api/v1/projects/$P2/artifacts/manuscripts/$A2/file"); echo "GET download file -> $code"
code=$(curl -sS -o /dev/null -w '%{http_code}' ${AUTH[@]+"${AUTH[@]}"} "$BASE/api/v1/projects/$P2/artifacts/manuscripts/download"); echo "GET download zip -> $code"
req PUT "/api/v1/projects/$P2/artifacts/manuscripts/$A2" '{"title":"PT2 manuscript (edited)","abstract":"check","authors":["alice"],"keywords":["check"],"year":2026}'
req DELETE "/api/v1/projects/$P2/artifacts/manuscripts/$A2"
req DELETE "/api/v1/projects/$P2"
echo "-- API log since start of this check"
( cd "$PEER_PA2" && docker compose logs --no-color --since "$START" api 2>&1 | tail -40 )
echo "-- database tables (looking for an audit or event table)"
( cd "$PEER_PA2" && docker compose exec -T db sh -c 'psql -U "$POSTGRES_USER" -d "${POSTGRES_DB:-$POSTGRES_USER}" -c "\dt"' 2>&1 )
echo "Expected if protected: a record of who changed, downloaded and deleted, still present after the delete."

say "=== PT3 Denial of service: one oversized manuscript upload (${SIZE_MB} MB) ==="
req POST /api/v1/projects "$(proj_with_title 'PT3 project' 'for the size check')"; P3="$(lastid)"; echo "project: $P3"
req POST "/api/v1/projects/$P3/artifacts/manuscripts/" '{"title":"PT3 manuscript","abstract":"check","authors":["alice"],"keywords":["check"],"year":2026}'; A3="$(lastid)"; echo "artifact: $A3"
python3 -c "open('$TMP/big.bin','wb').write(b'\0'*($SIZE_MB*1024*1024))"
code=$(curl -sS -o "$TMP/body" -w '%{http_code} in %{time_total}s' ${AUTH[@]+"${AUTH[@]}"} -F "file=@$TMP/big.bin" "$BASE/api/v1/projects/$P3/artifacts/manuscripts/$A3/file"); echo "POST ${SIZE_MB} MB upload -> $code"; head -c 400 "$TMP/body"; echo
echo "Expected if protected: 413 (or another refusal). Not protected: accepted."

rm -rf "$TMP"
say "Done. Output saved to $OUT"
echo "The stack is still running; stop it with: (cd \"$PEER_PA2\" && docker compose down -v)"

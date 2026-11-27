#!/usr/bin/env bash
# Tool: Claude chat assistant (claude.ai, Claude Opus 5.5)
# Language stack: Bash / curl / Python 3 (standard library) / Docker Compose
# Prompt Summary: "Fixed check of the five minimum controls at pa3-measured, from the list set before the run"
# AI Contribution: Whole script; changed after the first run to use student-role tokens and to fix a bash 3.2 error (~100%)
# Human Modifications: None; I read it before running it
# Verification: Run at pa3-measured (first run kept as _run1-role-user); outputs in this folder
# Confidence: Medium
# Peer control check (minimum controls 1-5), run once at pa3-measured as supporting evidence.
# The list of checks was fixed on 2026-11-26 before the peer run (working notes); this script
# implements it after the run. Written by the Claude chat assistant from the issuer's README,
# the peer's request schema and its If-Match handling; not from the peer's security analysis.
# Needs: the peer stack running (docker compose --env-file functional-codebase/.env up),
# and the token issuer's venv. Then also runs peer-threat-check.sh with a valid token.
set -u
BASE="${BASE:-http://127.0.0.1:8002}"
HERE="$(cd "$(dirname "$0")" && pwd)"
PEER_PA2="$HERE/../peer-artifact-indexer/PA2"
ISSUER="$HERE/../cs763-mock-token-issuer"
OUT="$HERE/peer-control-check-measured-$(date +%Y-%m-%d).txt"
TMP="$(mktemp -d)"
exec > >(tee "$OUT") 2>&1

PY="$ISSUER/.venv/bin/python"
( cd "$ISSUER" && "$PY" mock_issuer.py negative --sub alice --other-sub bob --out "$TMP/tokens.json" ) >/dev/null
tok() { python3 -c "import json;print(json.load(open('$TMP/tokens.json'))['$1']['token'])"; }
# The issuer's test set gives every token the role "user", which is not a Domain 1 role
# (admin, faculty_instructor, student). For the row-level, encryption, validation and threat
# checks, alice and bob get valid tokens with the role "student" instead.
ALICE="$(cd "$ISSUER" && "$PY" mock_issuer.py issue --sub alice --roles student)"
BOB="$(cd "$ISSUER" && "$PY" mock_issuer.py issue --sub bob --roles student)"

# call TOKEN METHOD PATH [JSON] [IF_MATCH] -> sets CODE, ETAG; body in $TMP/body
call() {
  local t="$1" m="$2" p="$3" data="${4:-}" im="${5:-}"
  local args
  args=(-sS -o "$TMP/body" -D "$TMP/hdr" -w '%{http_code}' -X "$m" -H "Authorization: Bearer $t")
  [ -n "$im" ] && args+=(-H "If-Match: $im")
  [ -n "$data" ] && args+=(-H 'Content-Type: application/json' -d "$data")
  CODE=$(curl "${args[@]}" "$BASE$p"); ETAG=$(grep -i '^etag:' "$TMP/hdr" | tr -d '\r' | cut -d' ' -f2-)
}
jid() { python3 -c "import json;print(json.load(open('$TMP/body')).get('id',''))" 2>/dev/null; }
show() { printf '%-60s -> %s   (%s)\n' "$1" "$CODE" "$2"; }

echo "Peer control check at pa3-measured"; date
( cd "$PEER_PA2" && echo "Peer repo: $(git rev-parse --short HEAD) on $(git branch --show-current)"; git status --short | head -5 )

echo; echo "=== (1)+(2) The issuer's 17 test tokens against GET /api/v1/projects (accept = 200, reject = 401) ==="
echo "    (all carry the role \"user\"; 403 on an accept token means authentication passed and the unknown role was refused)"
for label in $(python3 -c "import json;print(' '.join(json.load(open('$TMP/tokens.json'))))"); do
  expect=$(python3 -c "import json;print(json.load(open('$TMP/tokens.json'))['$label']['expect'])")
  call "$(tok "$label")" GET /api/v1/projects
  ok=FAIL; { [ "$expect" = accept ] && [ "$CODE" = 200 ]; } && ok=OK; { [ "$expect" = reject ] && [ "$CODE" = 401 ]; } && ok=OK
  { [ "$expect" = accept ] && [ "$CODE" = 403 ]; } && ok="AUTH-OK, role refused"
  printf '  %-4s %-22s expect=%-6s got=%s\n' "$ok" "$label" "$expect" "$CODE"
done

call "$ALICE" GET /api/v1/projects; show "valid token for alice, role student" "expect 200"
call "$BOB" GET /api/v1/projects; show "valid token for bob, role student" "expect 200"

echo; echo "=== (3) Row-level authorization: bob against alice's project ==="
P='{"title":"Control check","description":"row-level","members":[{"id":"alice","role":"student"}],"units":[{"type":"lab","id":"lab-robotics"}]}'
call "$ALICE" POST /api/v1/projects "$P"; PID=$(jid); PETAG="$ETAG"; show "alice creates project" "expect 201"
call "$BOB" GET "/api/v1/projects/$PID"; show "bob reads it" "expect 200: any authenticated caller may read"
call "$BOB" PUT "/api/v1/projects/$PID" "$P" "$PETAG"; show "bob updates it (with the current If-Match)" "expect 403 or 404"
call "$BOB" DELETE "/api/v1/projects/$PID" "" "$PETAG"; show "bob deletes it (with the current If-Match)" "expect 403 or 404"
call "$ALICE" POST "/api/v1/projects/$PID/artifacts/manuscripts/" '{"title":"Control manuscript","abstract":"check","authors":["alice"],"keywords":["check"],"year":2026}'
AID=$(jid); AETAG="$ETAG"; show "alice adds a manuscript" "expect 201"
MARK="CONTROLCHECK-PLAINTEXT-$(date +%s)"; printf '%s\n' "$MARK" > "$TMP/m.txt"
CODE=$(curl -sS -o "$TMP/body" -D "$TMP/hdr" -w '%{http_code}' -H "Authorization: Bearer $ALICE" -H "If-Match: $AETAG" -F "file=@$TMP/m.txt" "$BASE/api/v1/projects/$PID/artifacts/manuscripts/$AID/file")
show "alice uploads the manuscript file (with If-Match)" "expect 200"
call "$BOB" GET "/api/v1/projects/$PID/artifacts/manuscripts/$AID/file"; show "bob downloads it" "expect 403 or 404"
call "$BOB" GET "/api/v1/projects/$PID/artifacts/manuscripts/download"; show "bob downloads the zip" "expect 403 or 404"
call "$ALICE" GET "/api/v1/projects/$PID/artifacts/manuscripts/$AID/file"
grep -q "$MARK" "$TMP/body" && D="plaintext returned" || D="plaintext NOT returned"
show "alice downloads it" "expect 200 with the plaintext: $D"

echo; echo "=== (4) Encryption at rest: look for the plaintext marker in the stored files ==="
( cd "$PEER_PA2" && docker compose --env-file functional-codebase/.env exec -T api sh -c \
  'd="${STORAGE_DIR:-/data/manuscripts}"; echo "storage dir: $d, files: $(find "$d" -type f | wc -l)"; if grep -rqF "'"$MARK"'" "$d"; then echo "FOUND plaintext in storage"; else echo "marker not found in storage (stored encrypted)"; fi' )

echo; echo "=== (5) Input validation on a write ==="
call "$ALICE" POST /api/v1/projects '{"title":123,"members":[],"units":[]}'; show "project with a numeric title and empty lists" "expect 400 or 422"
call "$ALICE" POST /api/v1/projects '{"title":"x","members":[{"id":"alice","role":"student"}],"units":[{"type":"lab","id":"lab-robotics"}],"owner_sub":"bob"}'; show "project with an extra owner field" "expect 400 or 422"

echo; echo "=== Clean up the control project ==="
call "$ALICE" GET "/api/v1/projects/$PID"; call "$ALICE" DELETE "/api/v1/projects/$PID" "" "$ETAG"; show "alice deletes her project" "expect 204"

rm -rf "$TMP"
echo; echo "Saved to $OUT"
echo; echo "=== Threat check PT1-PT3 (same script as the baseline), with alice's valid token ==="
# The threat script calls docker compose without --env-file; export the .env values in a subshell so
# Compose can fill in its required variables. The requests themselves are unchanged.
( set -a; . "$PEER_PA2/functional-codebase/.env"; set +a; TOKEN="$ALICE" bash "$HERE/peer-threat-check.sh" measured )

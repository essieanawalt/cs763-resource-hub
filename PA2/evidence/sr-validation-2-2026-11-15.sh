#!/bin/bash
# Second round of checks for security-analysis.md section 2: malformed input (SR15, SR17),
# search wildcards, deny by default (SR24), data minimization (SR23), response headers (SR25),
# the event log inside the database (SR10, through V6), and a hoarding probe.
# Run from the repository root:
#   bash PA2/evidence/sr-validation-2-2026-11-15.sh 2>&1 | tee PA2/evidence/sr-validation-2-2026-11-15.txt

B=http://localhost:8080/api/v1
CHEN=40000000-0000-4000-8000-000000000001     # course instructor, CS501 A1
ADMIN=40000000-0000-4000-8000-000000000006    # administrator
COURSE=10000000-0000-4000-8000-000000000001   # CS501 A1
PI=50000000-0000-4000-8000-000000000001       # Raspberry Pi 5 kit, 10 units
LICENSE=50000000-0000-4000-8000-000000000003  # JetBrains IDE license
J='Content-Type: application/json'

step() { echo; echo "========================================================================"; echo "$1"; echo "========================================================================"; }
# status code, then the first 300 characters of the body, then whether it looks like a stack trace
probe() { local label="$1"; shift; local out; out=$(curl -s -w '\n%{http_code}' "$@"); local code=${out##*$'\n'}; local body=${out%$'\n'*}
  echo; echo "[$label] -> $code"; echo "${body:0:300}"; echo "stack-trace lines: $(echo "$body" | grep -c -e '   at ' -e 'StackTrace' -e 'Exception')"; }
code() { curl -s -o /dev/null -w "%{http_code}" "$@"; }
dbnet() { docker run --rm -e PGPASSWORD=resourcehub_dev postgres:18 psql -h host.docker.internal -U resourcehub -d resourcehub "$@"; }

echo "Run on $(date -u '+%Y-%m-%d %H:%M UTC'), repo commit $(git rev-parse --short HEAD)"

step "SR15 / SR17: malformed input (expect 400 or 415, never 500, no stack trace)"
probe "broken JSON" -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d '{"courseId": "'$COURSE'", "items": [ {'
probe "wrong type (quantity as text)" -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$COURSE\",\"items\":[{\"assetId\":\"$PI\",\"quantity\":\"abc\"}]}"
probe "negative quantity" -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$COURSE\",\"items\":[{\"assetId\":\"$PI\",\"quantity\":-5}]}"
LONG=$(printf 'x%.0s' $(seq 1 3000))
probe "notes of 3000 characters" -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$COURSE\",\"notes\":\"$LONG\",\"items\":[{\"assetId\":\"$PI\",\"quantity\":1}]}"
probe "unknown status value" -X PUT "$B/assets/tickets/00000000-0000-0000-0000-000000000000" -H "X-User-Id: $ADMIN" -H "$J" -d '{"status":"hacked"}'
probe "empty body" -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J"
probe "text/plain body" -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H 'Content-Type: text/plain' -d 'hello'
probe "route id not a GUID" "$B/assets/tickets/not-a-guid"
probe "X-User-Id not a GUID" -X POST "$B/assets/tickets" -H "X-User-Id: ' OR 1=1--" -H "$J" -d "{\"courseId\":\"$COURSE\",\"items\":[{\"assetId\":\"$PI\",\"quantity\":1}]}"

step "Search wildcards and SQL-like text (expect the same count as a plain search, or 0; never every asset)"
for q in 'Laptop' '%25' '_' "%27%20OR%201%3D1--"; do
  echo "search=$q -> $(curl -s "$B/assets?search=$q" | grep -o '"total":[0-9]*')"
done

step "M8: the 401 message (no header)"
probe "allocate with no header" -X POST "$B/assets/tickets/00000000-0000-0000-0000-000000000000/allocate"

step "SR24: every read route with no header (which ones answer?)"
T=$(curl -s "$B/assets/tickets?pageSize=1" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4)
echo "GET health                 $(code "$B/health")"
echo "GET assets                 $(code "$B/assets")"
echo "GET assets/{id}            $(code "$B/assets/$PI")"
echo "GET tickets                $(code "$B/assets/tickets")"
echo "GET tickets/{id}           $(code "$B/assets/tickets/$T")"
echo "GET allocations?courseId   $(code "$B/assets/allocations?courseId=$COURSE")"

step "SR23: user ids handed out with no header (ticket list and one ticket's detail)"
echo "distinct requester ids in the ticket list:"; curl -s "$B/assets/tickets?pageSize=100" | grep -o '"requesterId":"[^"]*"' | sort -u
echo "distinct actor ids in one ticket's events:"; curl -s "$B/assets/tickets/$T" | grep -o '"actorId":"[^"]*"' | sort -u

step "SR25: response headers on the allocations listing (returns license keys and device tokens)"
curl -s -D - -o /dev/null "$B/assets/allocations?courseId=$COURSE" -H "X-User-Id: $ADMIN"

step "SR10 in the database: through V6, change and delete events, then roll back (expect UPDATE/DELETE to succeed)"
dbnet -c 'BEGIN' \
  -c 'SELECT count(*) AS events_before FROM ticket_events' \
  -c "UPDATE ticket_events SET \"ActorId\" = '$CHEN' WHERE \"Type\" = 'Allocated'" \
  -c "DELETE FROM ticket_events WHERE \"Type\" = 'Submitted'" \
  -c 'SELECT count(*) AS events_inside_transaction FROM ticket_events' \
  -c 'ROLLBACK' \
  -c 'SELECT count(*) AS events_after_rollback FROM ticket_events'

step "Hoarding probe: Dr. Chen asks for 1000 Pi kits (stock 10) on a 365-day loan plus every license key; is it accepted?"
R=$(curl -s -w '\n%{http_code}' -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$COURSE\",\"items\":[{\"assetId\":\"$PI\",\"quantity\":1000,\"loanDays\":365},{\"assetId\":\"$LICENSE\",\"quantity\":3}]}")
echo "status: ${R##*$'\n'}"; H=$(echo "${R%$'\n'*}" | grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4); echo "ticket: $H"
echo "Dr. Chen's pending tickets now: $(curl -s "$B/assets/tickets?status=pending&pageSize=100" | grep -o "\"requesterId\":\"$CHEN\"" | wc -l | tr -d ' ')"
[ -n "$H" ] && echo "cleanup, delete the probe ticket: $(code -X DELETE "$B/assets/tickets/$H" -H "X-User-Id: $CHEN")"

echo; echo "Done."

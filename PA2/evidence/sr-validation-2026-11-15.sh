#!/bin/bash
# Checks the security requirements marked "Met (review)" in security-analysis.md section 2,
# plus SR2 (refused transitions), SR10 (event deletion), SR18 and SR22, against the local compose stack.
# Run from the repository root:
#   bash PA2/evidence/sr-validation-2026-11-15.sh 2>&1 | tee PA2/evidence/sr-validation-2026-11-15.txt

B=http://localhost:8080/api/v1
CHEN=40000000-0000-4000-8000-000000000001     # course instructor, CS501 A1
ITDIR=40000000-0000-4000-8000-000000000005    # IT director
ADMIN=40000000-0000-4000-8000-000000000006    # administrator
COURSE=10000000-0000-4000-8000-000000000001   # CS501 A1
EE580=10000000-0000-4000-8000-000000000002    # EE580 B1 (not Dr. Chen's)
LAPTOP=50000000-0000-4000-8000-000000000002   # Dell Latitude 7450, physical
VOUCHER=50000000-0000-4000-8000-000000000004  # Cloud credit pool
J='Content-Type: application/json'

sql() { echo "\$ psql: $1"; docker compose exec -T db psql -U resourcehub -d resourcehub -c "$1"; }
show() { echo; echo "\$ curl $*"; curl -s -i "$@" | grep -v -i '^\(date\|server\|transfer-encoding\):'; echo; }
idof() { grep -o '"id":"[^"]*"' | head -1 | cut -d'"' -f4; }
submit() { curl -s -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$COURSE\",\"items\":[$1]}" | idof; }
approve() { curl -s -o /dev/null -w "approve $1: %{http_code}\n" -X PUT "$B/assets/tickets/$1" -H "X-User-Id: $ITDIR" -H "$J" -d '{"status":"approved"}'; }
step() { echo; echo "========================================================================"; echo "$1"; echo "========================================================================"; }

echo "Run on $(date -u '+%Y-%m-%d %H:%M UTC'), repo commit $(git rev-parse --short HEAD)"
show "$B/health"

step "SR1: Dr. Chen submits a ticket for EE580, a course she doesn't teach (expect 403)"
show -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$EE580\",\"items\":[{\"assetId\":\"$LAPTOP\",\"quantity\":1}]}"

step "SR2 setup: T1 = 2 laptops, approved"
T1=$(submit "{\"assetId\":\"$LAPTOP\",\"quantity\":2}"); echo "T1=$T1"; approve "$T1"

step "SR2: Dr. Chen edits the contents of approved T1 (expect 409)"
show -X PUT "$B/assets/tickets/$T1" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$COURSE\",\"notes\":\"changed after approval\",\"items\":[{\"assetId\":\"$LAPTOP\",\"quantity\":5}]}"

step "SR2: refused transition. T2 pending; Dr. Chen (wrong role) tries to approve it (expect 403), then is the refusal recorded?"
T2=$(submit "{\"assetId\":\"$LAPTOP\",\"quantity\":1}"); echo "T2=$T2"
show -X PUT "$B/assets/tickets/$T2" -H "X-User-Id: $CHEN" -H "$J" -d '{"status":"approved"}'
sql "SELECT \"Type\", \"ActorId\", \"Detail\" FROM ticket_events WHERE \"TicketId\" = '$T2' ORDER BY 1;"

step "SR3: allocate T1 (expect 200), then allocate T1 again (expect 409)"
R=$(curl -s -X POST "$B/assets/tickets/$T1/allocate" -H "X-User-Id: $ADMIN"); echo "$R"
TOKEN=$(echo "$R" | grep -o '"token":"[^"]*"' | head -1 | cut -d'"' -f4); echo "TOKEN=$TOKEN"
show -X POST "$B/assets/tickets/$T1/allocate" -H "X-User-Id: $ADMIN"

step "SR5: lower laptop total stock to 1 while devices are out (expect 409)"
show -X PUT "$B/assets/$LAPTOP" -H "X-User-Id: $ADMIN" -H "$J" -d '{"name":"Dell Latitude 7450 laptop","type":"physical","category":"Laptop","departmentProgram":"Computer Science","units":1,"defaultLoanDays":30}'

step "SR7 / SR16: update the laptop with extra fields remainingStock=999 and status=overdue (expect remaining stock unchanged)"
show "$B/assets/$LAPTOP"
show -X PUT "$B/assets/$LAPTOP" -H "X-User-Id: $ADMIN" -H "$J" -d '{"name":"Dell Latitude 7450 laptop","type":"physical","category":"Laptop","departmentProgram":"Computer Science","units":5,"defaultLoanDays":30,"remainingStock":999,"remainingUnits":999,"status":"overdue"}'

step "SR16: Dr. Chen submits a ticket with requesterId set to the administrator (expect requesterId = Dr. Chen)"
show -X POST "$B/assets/tickets" -H "X-User-Id: $CHEN" -H "$J" -d "{\"courseId\":\"$COURSE\",\"requesterId\":\"$ADMIN\",\"items\":[{\"assetId\":\"$LAPTOP\",\"quantity\":1}]}"

step "SR8: return TOKEN (expect 200), return it again (expect 409), return a made-up token (expect 404)"
show -X POST "$B/assets/$LAPTOP/return" -H "X-User-Id: $ADMIN" -H "$J" -d "{\"token\":\"$TOKEN\"}"
show -X POST "$B/assets/$LAPTOP/return" -H "X-User-Id: $ADMIN" -H "$J" -d "{\"token\":\"$TOKEN\"}"
show -X POST "$B/assets/$LAPTOP/return" -H "X-User-Id: $ADMIN" -H "$J" -d '{"token":"MADE-UP-TOKEN-0000"}'

step "SR4: two voucher allocations at once that together exceed the balance (expect one 200, one 409, balance never below 0)"
BAL=$(docker compose exec -T db psql -U resourcehub -d resourcehub -tAc "SELECT \"Balance\" FROM assets WHERE \"Id\" = '$VOUCHER';")
AMT=$(awk "BEGIN { printf \"%.2f\", $BAL * 0.6 }"); echo "balance before=$BAL, each ticket asks for $AMT"
V1=$(submit "{\"assetId\":\"$VOUCHER\",\"amount\":$AMT}"); V2=$(submit "{\"assetId\":\"$VOUCHER\",\"amount\":$AMT}")
echo "V1=$V1 V2=$V2"; approve "$V1"; approve "$V2"
curl -s -o /tmp/sr4a.txt -w "allocate V1: %{http_code}\n" -X POST "$B/assets/tickets/$V1/allocate" -H "X-User-Id: $ADMIN" &
curl -s -o /tmp/sr4b.txt -w "allocate V2: %{http_code}\n" -X POST "$B/assets/tickets/$V2/allocate" -H "X-User-Id: $ADMIN" &
wait; cat /tmp/sr4a.txt; echo; cat /tmp/sr4b.txt; echo
sql "SELECT \"Balance\", \"TotalBalance\" FROM assets WHERE \"Id\" = '$VOUCHER';"

step "SR3 / SR6: each license key issued at most once, with ticket and time; devices recorded with ticket and check-out time"
sql "SELECT \"Key\", \"IssuedTicketId\", \"AllocationId\", \"IssuedAt\" FROM license_keys ORDER BY 1;"
sql "SELECT \"Token\", \"TicketId\", \"CheckedOutAt\", \"ReturnedAt\" FROM device_assignments ORDER BY \"CheckedOutAt\";"

step "SR9: delete the laptop, which is in use (expect 409); create an unused asset and delete it (expect 204)"
show -X DELETE "$B/assets/$LAPTOP" -H "X-User-Id: $ADMIN"
A=$(curl -s -X POST "$B/assets" -H "X-User-Id: $ADMIN" -H "$J" -d '{"name":"SR9 test asset","type":"physical","category":"Test","departmentProgram":"Computer Science","units":1,"defaultLoanDays":7}' | idof); echo "A=$A"
show -X DELETE "$B/assets/$A" -H "X-User-Id: $ADMIN"

step "SR10: submit T3, read its events, delete it as the requester, then check the events table directly"
T3=$(submit "{\"assetId\":\"$LAPTOP\",\"quantity\":1}"); echo "T3=$T3"
sql "SELECT \"Type\", \"ActorId\" FROM ticket_events WHERE \"TicketId\" = '$T3';"
show -X DELETE "$B/assets/tickets/$T3" -H "X-User-Id: $CHEN"
sql "SELECT count(*) AS events_left FROM ticket_events WHERE \"TicketId\" = '$T3';"

step "SR18: is the returned token in the application log? Are the refused requests above logged?"
echo "lines containing TOKEN:"; docker compose logs api | grep -c "$TOKEN"
docker compose logs api | grep "$TOKEN"
echo "lines mentioning refusals (401/403/forbidden/unknown caller):"; docker compose logs api | grep -i -c 'forbidden\|unknown caller\| 401\| 403'

step "SR22: page size above the cap, then 60 fast requests (any 429?)"
curl -s "$B/assets?pageSize=500" | grep -o '"page":[0-9]*,"pageSize":[0-9]*'; echo
for i in $(seq 1 60); do curl -s -o /dev/null -w "%{http_code}\n" "$B/assets"; done | sort | uniq -c

echo; echo "Done."

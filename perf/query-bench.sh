#!/usr/bin/env bash
# Deterministic per-endpoint database cost: logical reads, CPU and statements per HTTP request.
# Unlike latency, logical reads do not depend on how busy the machine is, so before/after comparisons of a
# query change are exact even on a laptop. Requires the performance stack (perf/README.md) to be running.
#
#   perf/query-bench.sh [calls-per-endpoint]      (default 10; run from the repository root)
set -euo pipefail

CALLS=${1:-10}
BASE=${BASE_URL:-http://localhost:8080}
COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.perf.yml)
set -a; . ./.env; set +a

sql() {
  "${COMPOSE[@]}" exec -T sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -h -1 -W -s '|' -Q "SET NOCOUNT ON; $1"
}

token() {
  curl -sf -X POST "$BASE/api/v1/auth/token" -H 'Content-Type: application/json' \
    -d "{\"email\":\"$1@perf.local\",\"password\":\"$SEED_DEMO_PASSWORD\"}" | sed -E 's/.*"accessToken":"([^"]+)".*/\1/'
}

EMPLOYEE=$(token employee1); MANAGER=$(token manager1); FINANCE=$(token finance1); DIRECTOR=$(token director1)
SAMPLE_ID=$(curl -sf "$BASE/api/v1/requests?pageSize=1&page=500" -H "Authorization: Bearer $FINANCE" | sed -E 's/.*"items":\[\{"id":([0-9]+).*/\1/')

bench() { # name, token, path
  local name=$1 auth=$2 path=$3
  sql "DBCC FREEPROCCACHE WITH NO_INFOMSGS;" >/dev/null
  curl -sf -o /dev/null "$BASE$path" -H "Authorization: Bearer $auth"   # warm-up: compile plans
  sql "DBCC FREEPROCCACHE WITH NO_INFOMSGS;" >/dev/null                 # measure execution, not compilation
  for _ in $(seq 1 "$CALLS"); do
    curl -sf -o /dev/null "$BASE$path" -H "Authorization: Bearer $auth"
  done
  local row
  row=$(sql "SELECT SUM(qs.execution_count), SUM(qs.total_logical_reads), SUM(qs.total_worker_time)
             FROM sys.dm_exec_query_stats qs
             CROSS APPLY (SELECT CONVERT(int, value) AS dbid FROM sys.dm_exec_plan_attributes(qs.plan_handle) WHERE attribute = 'dbid') pa
             CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st
             WHERE pa.dbid = DB_ID('OpsFlowPerf') AND st.text NOT LIKE '%plan_persist%';" | tr -d ' ')
  IFS='|' read -r execs reads cpu <<< "$(printf '%s' "$row" | tr -d '\r' | head -1)"
  local cpu_ms
  cpu_ms=$(awk -v t="$cpu" -v n="$CALLS" 'BEGIN { printf "%.1f", t / 1000 / n }')
  # Per HTTP request. Statements include the visibility/permission checks the endpoint performs.
  printf "| %-28s | %10s | %8s | %10s |\n" "$name" "$(( reads / CALLS ))" "$cpu_ms" "$(( execs / CALLS ))"
}

printf "| %-28s | %10s | %8s | %10s |\n" "Endpoint (per request)" "Reads" "CPU ms" "Statements"
printf "|%s|%s|%s|%s|\n" "------------------------------" "------------" "----------" "------------"
bench "list_mine (employee)"        "$EMPLOYEE" "/api/v1/requests?pageSize=20"
bench "list_mine status=InApproval" "$EMPLOYEE" "/api/v1/requests?pageSize=20&status=InApproval"
bench "list_mine (manager, team)"   "$MANAGER"  "/api/v1/requests?pageSize=20"
bench "list_all default sort"       "$FINANCE"  "/api/v1/requests?pageSize=20"
bench "list_all sort=-amount"       "$FINANCE"  "/api/v1/requests?pageSize=20&sort=-amount"
bench "list_all status=Approved"    "$FINANCE"  "/api/v1/requests?pageSize=20&status=Approved"
bench "list_all minAmount=5000"     "$FINANCE"  "/api/v1/requests?pageSize=20&minAmount=5000"
bench "list_all createdFrom"        "$FINANCE"  "/api/v1/requests?pageSize=20&createdFrom=2026-06-01"
bench "list_all page=50"            "$FINANCE"  "/api/v1/requests?pageSize=20&page=50"
bench "get (finance)"               "$FINANCE"  "/api/v1/requests/$SAMPLE_ID"
bench "inbox (manager)"             "$MANAGER"  "/api/v1/approvals/inbox?pageSize=10"
bench "inbox (finance)"             "$FINANCE"  "/api/v1/approvals/inbox?pageSize=10"
bench "inbox (director)"            "$DIRECTOR" "/api/v1/approvals/inbox?pageSize=10"

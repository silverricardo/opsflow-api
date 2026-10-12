# Performance: load-test plan and baseline

## What is measured

A **baseline**: how the API behaves under a realistic, read-heavy mix at moderate concurrency, against a database
of **100,000 requests**. The goal is a reference point for spotting regressions and checking design assumptions
(indexes, query shapes). It is not a capacity claim.

**Targets (NFR-10, hypotheses until measured):** p95 < 200 ms per operation and < 1% errors, at 20 concurrent
virtual users.

## Method

| Aspect | Choice | Why |
|---|---|---|
| Tool | [k6](https://k6.io) 2.3.0 in a container (`grafana/k6:2.3.0`) | Scriptable, reproducible, no local install |
| Data | `Seed:Mode=Performance`: 228 users (20 managers × 10 employees, 5 Finance, 2 Directors, 1 Admin) and 100k requests across every status over 12 months. Deterministic (fixed random seed), built through the domain | Realistic volume and distribution. Comparable between runs |
| Workload | 35% employee lists own requests · 20% Finance lists all · 20% open a request (list + get) · 15% manager inbox → get → approve · 10% create + submit (with `Idempotency-Key`) | Approval tools are read-heavy. The mix includes the most expensive query (Finance, 100k visible rows) and every write path |
| Think time | 0.25–0.75 s per iteration | Load from interactive users, not a raw-throughput test |
| Phases | 30 s warm-up (excluded from results), then the measured phase (default 20 VUs × 2 min) | JIT, connection pool, SQL Server plan cache and buffer pool warm up first |
| App config | `ASPNETCORE_ENVIRONMENT=Performance`: production logging (JSON, Information, no SQL command logging); rate limits lifted, because the test drives many users from one IP | Measures the app, not the protective limits |
| Database evidence | `perf/sql/top-queries.sql` after the run: top statements by CPU and logical reads, and index seeks vs scans | Ties latency to specific queries and checks the indexes are used |

## How to run

**Locally** (Docker; leave plenty of free RAM, since SQL Server alone needs 2 GB):

```bash
cp .env.example .env    # if not done already
docker compose -f docker-compose.yml -f docker-compose.perf.yml up -d --build
# wait for "Performance seed finished" in: docker compose -f docker-compose.yml -f docker-compose.perf.yml logs -f api
docker compose -f docker-compose.yml -f docker-compose.perf.yml --profile perf run --rm k6 run /scripts/baseline.js
docker compose -f docker-compose.yml -f docker-compose.perf.yml cp perf/sql/top-queries.sql sqlserver:/tmp/q.sql
docker compose -f docker-compose.yml -f docker-compose.perf.yml exec sqlserver \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -W -i /tmp/q.sql
```

Tune with `PERF_VUS`, `PERF_DURATION` and `PERF_REQUESTS`. The raw k6 summary is written to `perf/results/`
(git-ignored).

**On GitHub Actions** (standardised hardware): run the **Performance baseline** workflow manually
(Actions → Performance baseline → Run workflow). The k6 summary appears in the job summary, and the full results,
environment and database statistics are uploaded as the `perf-results` artifact.

## Results

Results are published here **only after a run**, with the environment they were measured on. Numbers from
different environments are not comparable.

### Local baseline, 2026-10-10: before and after the visibility fix

**Environment (measured):** MacBook Pro, Intel Core i5-7267U (2 cores / 4 threads), 8 GB RAM. Docker Desktop
28.1.1, VM with 4 CPUs and 3.8 GB. API and SQL Server 2022 (CU27) in containers on the same machine, k6 in a
third container. Commit range: `56f640c` (before) → the commit adding `VisibilityIndexes` (after).
Data: 100,000 requests (seed finished in 538 s). 20 VUs measured for 2 min after a 30 s warm-up. The plan cache
was cleared before each run.

> ⚠️ **This machine was heavily overloaded:** load average 9–17 during the runs and swap 3.4–3.5 GB of 4 GB used.
> The IDE and other apps were running alongside, and Docker took half of the RAM. Absolute latencies are
> therefore pessimistic and noisy. The **relative** before/after comparison is meaningful: same data, same
> script, same machine, minutes apart. For numbers on standardised hardware, use the GitHub Actions workflow.

| Operation (p95) | Before | After | Target |
|---|---|---|---|
| `get` (request details) | 54 ms | **40 ms** | < 200 ms ✓ |
| `list_mine` (employee list) | 18.1 s | **4.0 s** (median 9.8 s → 0.48 s) | < 200 ms ✗ |
| `inbox` | 6.0 s | **2.1 s** | < 200 ms ✗ |
| `list_all` (Finance, 100k visible) | 1.9 s | 1.2 s ¹ | < 200 ms ✗ |
| `create` | 641 ms | 415 ms ¹ | < 200 ms ✗ |
| `submit` | 274 ms | 269 ms ¹ | < 200 ms ✗ |
| `decide` | 370 ms | 251 ms ¹ | < 200 ms ✗ |
| **Throughput** | 5.7 req/s | **17.8 req/s** | — |
| Errors | 0.20% | 0.45% ² | < 1% ✓ |

¹ No code change affected these. The difference shows how much machine noise there is, and part of it is that
the fixed queries stopped competing for CPU.
² Every "failure" was a `409`/`412` on concurrent approvals of the same request: the expected,
correct outcome under contention, accepted by the script's checks (100% of checks passed).

**What the database showed** (`perf/sql/top-queries.sql`):

| Evidence | Before | After |
|---|---|---|
| Visibility `COUNT` for employees/managers, average logical reads | **742,835** | **2,068** |
| Seeks on `IX_Requests_RequesterId_Status_CreatedAt` | 0 (never used) | 2,748 |
| Seeks on `ApprovalSteps(AssigneeId)` / `(DecidedById)` | n/a / 80 | 2,672 / 2,673 |

**Diagnosis and fix.** The visibility rule (BR-40) was one `WHERE` with an `OR` of three conditions:
`RequesterId = me`, `EXISTS` on steps where I am the assignee or decider, and `EXISTS` on my direct reports. That
shape stops SQL Server from seeking any index. On top of that, the only `AssigneeId` index was filtered to
pending steps, so each request probed the steps with a scan. The fix rewrites it as
`Id IN (… UNION … UNION … UNION …)`, one index seek per branch, and adds unfiltered `ApprovalSteps(AssigneeId)`
and `(DecidedById)` indexes that include `RequestId`. The inbox's `OR` was split the same way into a
`UNION ALL` of "assigned to me" and "my role's pool". The behaviour is unchanged: the full test suite, including
the visibility and authorization matrices, passes.

**Still above target, next steps (hypotheses to verify with the same method):**
1. **Inbox** (2.1 s, 15.6k reads per `COUNT`). The pool branch does not use
   `IX_ApprovalSteps_Kind_PendingUnassigned` (0 seeks), and the two `NOT EXISTS` checks run per candidate.
   Next: check the actual plan, and consider keeping the "current step" on the request so the
   `NOT EXISTS (earlier pending step)` disappears.
2. **Lists** (CPU-bound: ~2.3k reads but 340 ms CPU per page query here). Correlated subqueries for the
   requester's display name, plus `COUNT(*)` on every page. Next: join users once, and cache or skip `totalCount`
   for deep pages (ADR-008).
3. **Writes** (create/submit/decide 250–415 ms): two or three round trips (load, save, re-read for the
   response). Next: build the response from the saved aggregate instead of re-reading. Re-measure on
   standardised hardware first, because this machine inflates CPU-bound work.
4. **Title search** (`IX_Requests_Reference` is scanned 867 times, from the `q` filter's `LIKE '%term%'`): as
   expected (operations.md §6). Full-text search if search becomes important.

Raw outputs (k6 summaries, SQL statistics) are written to `perf/results/` and are not committed.

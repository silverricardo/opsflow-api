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
docker compose -f docker-compose.yml -f docker-compose.perf.yml --profile perf run --rm --no-deps k6 run /scripts/baseline.js
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

### Query optimisation, 2026-10-12: database cost per request (deterministic)

Latency on a busy laptop is noisy, but **logical reads are not**: they count the pages SQL Server touches, whatever
the machine is doing. `perf/query-bench.sh` clears the plan cache, sends 10 real HTTP requests per endpoint
against the 100k-request database, and reports reads, CPU and SQL statements **per request**. CPU is still
machine-dependent, so read it as an order of magnitude.

| Endpoint | Reads before | Reads after | CPU ms before → after |
|---|---|---|---|
| `list_all` default sort (newest first) | 2,835 | **516** | 375 → **22** |
| `list_all` `sort=-amount` | 2,835 | **516** | 305 → **32** |
| `list_all` `minAmount=5000` | 4,248 | **392** | 97 → **11** |
| `list_all` `createdFrom=…` | 4,248 | **335** | 121 → **17** |
| `list_all` `status=Approved` | 2,603 | 1,618 | 62 → 11 |
| `list_all` `page=50` | 2,835 | 3,643 | 329 → **48** |
| `inbox` (manager, 1,589 pending items) | 20,995 | **9,803** | 162 → 151 |
| `inbox` (director) | 16,154 | **9,042** | 166 → 96 |
| `inbox` (finance) | 10,458 | 9,786 | 265 → 142 |
| `list_mine` (employee) | 2,779 | 2,539 | 66 → 74 |
| `get` | 33 | 33 | 0.6 → 0.8 |

**What changed and why:**
- **Lists:** Finance, Director and Admin see every request. Sorting by date or amount used to sort all 100k rows to
  return 20 (the CPU column). Two indexes, `(CreatedAt DESC, Id DESC)` and `(Amount DESC, Id DESC)`, turn
  "top 20" into an index seek, in both directions, since SQL Server scans an index backwards for ascending sorts.
  They also serve the date-range filter. Deep pages (`page=50`) read more pages, because the offset rows are
  looked up too, but cost 7× less CPU.
- **Inbox:** finding the actionable step used to mean checking, for **every** pending step (43k), that no earlier
  step was still pending. Each step now has an `IsCurrent` flag, maintained by the domain: exactly one step per
  request in approval, covered by invariant tests. The migration backfills it with a single set-based statement,
  verified on the 100k database (33,072 requests in approval, 33,072 current steps, 0 inconsistencies). Two
  indexes filtered on `IsCurrent = 1` serve the two inbox branches. The `COUNT` dropped from 16,292 to 5,139
  reads. The cost now grows with the **size of the caller's inbox** (about 3 reads per item), not with the size of
  the database. The seed gives each manager an unrealistic ~1,600 pending approvals, so this is a worst case.
- **Not changed:** the employee list is already a `UNION` of index seeks (see below). Its remaining cost is the
  employee's own ~500 requests.

Reproduce with `perf/query-bench.sh` against the performance stack.

### GitHub-hosted runner, 2026-10-12: after the query optimisation

[Workflow run](https://github.com/silverricardo/opsflow-api/actions/runs/38307436362) on commit `ebefdd6`, with the
same data, script and parameters as the baseline below. **The runner had a different CPU** (Intel Xeon Platinum
8573C instead of AMD EPYC 7763): GitHub does not pin runner hardware. Part of the latency change is therefore
hardware. Operations whose code did not change (the writes, `get`) also got faster. The **logical reads** in
the previous section are the hardware-independent evidence for the optimisation itself.

| Operation (p95) | Baseline (EPYC) | After (Xeon) | Target (< 200 ms) |
|---|---|---|---|
| `list_all` (Finance, 100k visible) | 745 ms | **80 ms** | ✓, now met (~9×; matches 5–10× fewer reads and CPU) |
| `get` | 26 ms | 9 ms | ✓ |
| `decide` | 147 ms | 64 ms | ✓ |
| `submit` | 196 ms | 73 ms | ✓ |
| `create` | 281 ms | 103 ms | ✓, now met (code unchanged: hardware) |
| `inbox` | 1.33 s | **440 ms** | ✗ |
| `list_mine` (employee) | 1.92 s (median 351 ms) | **767 ms** (median 123 ms) | ✗ |
| Throughput | 25.8 req/s | **41.0 req/s** | — |
| Errors (all `409`/`412` contention) | 0.88% | 0.40% | < 1% ✓ |

All 5,081 checks passed. Database statistics from the run confirm the new indexes are used:
`IX_ApprovalSteps_Current_Assignee` 1,242 seeks, `IX_Requests_CreatedAt` 348 seeks, `IX_Requests_Amount` 161.

**What is still above target, and why:**
- **Inbox (440 ms):** the inbox `COUNT` reads ~5.2k pages per call. That cost is proportional to the caller's inbox,
  and the seed gives each manager ~1,600 pending approvals. The pool branch's filtered index was not chosen
  (0 seeks), which is worth a look at the actual plan. Options if real inboxes grow this large: denormalise
  `SubmittedAt`/`RequesterId` onto the step, or cap the count.
- **Employee list (767 ms p95, 123 ms median):** the slow tail comes from the filter variants in the script.
  `q=Laptop` is a `LIKE '%term%'` scan by design. The visibility `UNION` plus `COUNT` reads ~2.1k pages for an
  employee with ~500 requests. Next: full-text search for `q`, and an approximate or cached count.

### GitHub-hosted runner, 2026-10-12 (reference baseline)

**Environment (recorded by the workflow):** GitHub Actions `ubuntu-24.04` runner, AMD EPYC 7763 with 4 vCPUs, 15 GB
RAM, Docker 28.0.4. API, SQL Server 2022 and k6 share the runner. Commit `1a7bebe`, which includes the visibility
fix. Data: 100,000 requests (seed finished in 373 s). 20 VUs measured for 2 min after a 30 s warm-up.
[Workflow run](https://github.com/silverricardo/opsflow-api/actions/runs/38303182577).

| Operation | p95 | Median | Target (p95 < 200 ms) |
|---|---|---|---|
| `get` (request details) | **26 ms** | 11 ms | ✓ |
| `decide` | **147 ms** | 66 ms | ✓ |
| `submit` | **196 ms** | 78 ms | ✓ (just) |
| `create` | 281 ms | 133 ms | ✗ |
| `list_all` (Finance, 100k visible) | 745 ms | 413 ms | ✗ |
| `inbox` | 1.33 s | 826 ms | ✗ |
| `list_mine` (employee list) | 1.92 s | 351 ms | ✗ |
| **Throughput** | 25.8 req/s (15.8 iterations/s, bounded by the 0.25–0.75 s think time) | | |
| Errors | 0.88% in the measured phase | | < 1% ✓ |

All checks passed (3,219 of 3,219). As in the local runs, every "error" was a `409`/`412` from concurrent
approvals of the same request, which is the correct outcome.

**Reading it.** On adequate hardware, key lookups and every write except `create` meet the target. What remains above
it are the read-heavy queries: inbox, then the lists. They rank in the same order as on the overloaded laptop, which
confirms the remaining work is in those queries, not in the environment. The next steps below target exactly those.
`create` is slower than the other writes because it re-reads the full request to build the response (next step 3).

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
1. ~~**Inbox**~~ and ~~**lists**~~: done, see "Query optimisation" above. The remaining inbox cost scales with
   inbox size. If inboxes grow to thousands of items, denormalise the sort key (`SubmittedAt`) onto the step.
3. **Writes** (create/submit/decide 250–415 ms): two or three round trips (load, save, re-read for the
   response). Next: build the response from the saved aggregate instead of re-reading. Re-measure on
   standardised hardware first, because this machine inflates CPU-bound work.
4. **Title search** (`IX_Requests_Reference` is scanned 867 times, from the `q` filter's `LIKE '%term%'`): as
   expected (operations.md §6). Full-text search if search becomes important.

Raw outputs (k6 summaries, SQL statistics) are written to `perf/results/` and are not committed.

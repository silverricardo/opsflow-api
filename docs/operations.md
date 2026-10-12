# OpsFlow: Operations Guide

How to run, observe and troubleshoot OpsFlow in a deployed environment. For performance measurement see
[perf/README.md](../perf/README.md). For the Azure deployment path see [deployment-azure.md](deployment-azure.md).

## 1. Configuration

All settings come from `appsettings.json`. Override them with environment variables, where `__` separates
sections (e.g. `Jwt__SigningKey`). Secrets must come from the environment or a secret store, never from files
in the repository.

| Setting | Secret | Default | Notes |
|---|---|---|---|
| `ConnectionStrings__OpsFlow` | **yes** | — | Required. The app refuses to start without it. |
| `Jwt__SigningKey` | **yes** | — | ≥ 32 bytes. Validated at startup. |
| `Jwt__Issuer`, `Jwt__Audience` | no | `opsflow`, `opsflow-api` | Must match the tokens' issuer and audience. |
| `Jwt__AccessTokenLifetimeMinutes` | no | `15` | Also bounds how long a deactivated user keeps access. |
| `Database__MigrateOnStartup` | no | `false` | Convenient locally. Production applies the migration bundle from the pipeline instead. |
| `Seed__Enabled`, `Seed__Mode`, `Seed__DemoPassword` | password: **yes** | `false` | Refused in `Production`. |
| `OpenApi__Enabled` | no | `false` | Exposes `/openapi/v1.json` and `/scalar`. |
| `RateLimiting__TokenPermitLimit` | no | `5` | Sign-in attempts per minute per client IP. |
| `RateLimiting__GlobalTokenLimit` / `GlobalTokensPerSecond` | no | `100` / `50` | Token bucket per user (or per IP when anonymous). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | no | — | Turns on OTLP export of traces and metrics. |
| `ForwardedHeaders__Enabled` | no | `false` | Trust `X-Forwarded-For` / `-Proto` from the nearest proxy. Enable it only behind an ingress. |
| `Serilog__MinimumLevel__Default` | no | `Information` | |

## 2. Health checks

| Endpoint | Meaning | Use it for |
|---|---|---|
| `GET /health/live` | The process is running. It checks no dependencies. | **Liveness** probe. A failure means restart the container. |
| `GET /health/ready` | The database is reachable (bounded by a 3-second timeout). | **Readiness** probe and load-balancer health. A failure means stop sending traffic, but do not restart. |

Both endpoints are anonymous, are not rate limited, and return a compact JSON report with no exception details.
Keep liveness free of dependencies. Otherwise a database outage would make the orchestrator restart every
instance, which turns a partial outage into a full one.

## 3. Logs

- Outside Development, logs are **structured JSON on stdout** (Serilog, compact rendered format), one event per
  line, ready for any log collector.
- Every event of a request carries:
  - **`CorrelationId`**: taken from the caller's `X-Correlation-Id` header when it is well formed (1–64
    characters `[A-Za-z0-9._-]`), otherwise the W3C trace ID. The same value is returned in the
    `X-Correlation-Id` response header, included in every error body (`correlationId`), and stored on every
    audit entry.
  - **`@tr` / `@sp`**: the trace and span IDs, for joining logs with traces.
  - `UserId` on the request-completion event.
- What is never logged: passwords, tokens, the `Authorization` header or e-mail addresses. Failed sign-ins log
  the user ID only when the account exists.

**Finding everything about one failed call:** the client reports the `correlationId` from the error body.
Search the logs for it. The same ID is on the audit entries if the call changed data.

## 4. Traces and metrics (OpenTelemetry)

Set `OTEL_EXPORTER_OTLP_ENDPOINT` to send OTLP to any collector (Azure Monitor via the OpenTelemetry distro or
collector, Grafana, Jaeger, Honeycomb, …).

- **Traces:** incoming HTTP (health checks excluded), outgoing HTTP, and SQL Server commands. The root span
  carries `opsflow.correlation_id`.
- **Metrics:** ASP.NET Core (request duration, active requests, rate-limiter and error counts), HttpClient,
  .NET runtime (GC, thread pool, exceptions), and business metrics:

| Metric | Tags | Question it answers |
|---|---|---|
| `opsflow.requests.created` | `category` | How much work comes in? |
| `opsflow.approval.decisions` | `decision` | Are approvals flowing, or are rejections spiking? |
| `opsflow.idempotency.replays` | — | Are clients retrying a lot? This usually points to network or timeout problems on the client side. |

**Locally:** start the Aspire dashboard (traces, metrics and structured logs in one UI) with:

```bash
OTEL_EXPORTER_OTLP_ENDPOINT=http://dashboard:18889 docker compose --profile observability up -d
# UI: http://localhost:18888
```

## 5. Troubleshooting

| Symptom | Likely cause | What to check |
|---|---|---|
| App exits at startup with `Connection string 'OpsFlow' is not configured` or an options validation error for `Jwt` | Missing or short secret | Environment variables / Key Vault references. The key must be ≥ 32 bytes. |
| `/health/ready` returns 503, `/health/live` returns 200 | Database unreachable | Firewall rules, credentials, SQL Server availability. The app keeps running and recovers on its own. |
| Clients get many `412 concurrency.etag_mismatch` | Several users editing or approving the same request, or a client sending stale ETags | Expected under contention. If it is constant, check the client re-reads (`GET`) before retrying. |
| `409 idempotency.in_progress` that never clears | The process crashed between reserving a key and finishing the command | The key expires after 24 hours (ADR-005). The client should retry with a new key. |
| `429 rate_limit.exceeded` | A brute-force attempt, or a legitimate burst through a proxy | Behind a reverse proxy, set `ForwardedHeaders__Enabled=true`, or every client shares the proxy's IP. |
| `401` right after deactivating a user is *not* immediate | Tokens are valid until they expire | By design: at most `AccessTokenLifetimeMinutes`. |
| `500 server.error` | Unhandled exception | Search the logs for the `correlationId`: the exception is logged at Error level with its stack trace. The response never contains it. |
| Slow responses | See §6 | Find the slow endpoint in the request logs (`Elapsed`) or traces, then the slow SQL span. |

## 6. Performance: likely bottlenecks and how to measure them

These are **hypotheses ranked by expected impact**, not measurements. The measured baseline is in
[perf/README.md](../perf/README.md).

1. **`COUNT(*)` on wide lists.** Every list response includes `totalCount`. For Finance, Director and Admin
   that counts every matching request. Measure with `SET STATISTICS IO, TIME ON` on the generated query, or the
   `avg_logical_reads` of the count statement in `perf/sql/top-queries.sql`. If needed: cache counts, return an
   approximate count, or switch to keyset pagination (ADR-008).
2. **Visibility filter for employees and managers.** The `OR` across `RequesterId`, an `EXISTS` on steps and an
   `EXISTS` on users can prevent an index seek. Measure: compare the execution plan for an employee and for
   Finance. Mitigation: split it into a `UNION` of three seeks, or store the requester's manager on the request.
3. **Title search `LIKE '%term%'`.** This is a scan by definition. Mitigation: SQL Server full-text search, or a
   search service if search becomes a key feature.
4. **Audit and idempotency growth.** Both are append-only. Plan for retention (idempotency is cleaned up after
   24 hours) and, for audit, archiving or partitioning by `OccurredAt`.
5. **Password hashing on sign-in.** PBKDF2 is CPU-heavy on purpose. A sign-in storm costs CPU. That is the
   reason for the strict sign-in rate limit, and in production for an external identity provider.

**Tools:** EF Core command logging (Development logs every SQL statement with its duration), Query Store on Azure
SQL, `sys.dm_exec_query_stats` (see `perf/sql/top-queries.sql`), actual execution plans, and the k6 script in
`perf/k6`.

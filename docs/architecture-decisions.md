# Architecture Decision Records

Each record is short: context, decision, consequences. Superseded records stay here and are marked as such.

---

## ADR-001 — Modular monolith: pure domain project + feature-folder API

**Context.** The MVP is one bounded context (request approvals) with about 15 endpoints. Full Clean Architecture
(Domain / Application / Infrastructure / API) would add projects and interfaces with a
single implementation each, without isolating anything meaningful.

**Decision.** Two production projects:
- `OpsFlow.Domain`: the aggregate, state machine, policy and errors. It has no dependencies.
- `OpsFlow.Api`: one folder per feature (endpoint, request/response types, validator, handler),
  plus `Persistence/` and `Infrastructure/`.

**Consequences.** The business rules stay isolated and fast to unit-test, which is where the risk is.
Infrastructure is not swappable without code changes, and that is acceptable. If a second bounded context
appears (e.g. budgets), it becomes its own module, and splitting into services stays possible because
the aggregate boundaries are explicit.

## ADR-002 — Use `DbContext` directly; no repository or unit-of-work layer

**Context.** EF Core's `DbContext` already implements unit of work, and `DbSet` already implements
repository. Generic repositories hide query shape (projections, includes, tracking) and often lead
to over-fetching.

**Decision.** Handlers use `OpsFlowDbContext` directly. Query logic that is reused (visibility filter,
sorting whitelist) lives in small extension methods on `IQueryable<Request>`.

**Consequences.** Queries are explicit and easy to tune. Handlers are tested through integration tests
against a real database (ADR-007) rather than with mocked repositories.

## ADR-003 — Hard-coded lifecycle; approval chain from data, snapshotted at submission

**Context.** "Configurable workflows" can mean anything up to a BPMN engine. The real need is
different approval chains depending on the amount.

**Decision.** The request lifecycle (states and transitions) is code in the domain. The approval chain
is generated from `ApprovalPolicyRules` rows and copied onto the request at submission.

**Consequences.** Transitions are exhaustively testable and can't be misconfigured. Thresholds can change
without a deployment. In-flight requests aren't affected by policy changes, which is what auditors expect.
New *states* still require code. That is intentional.

## ADR-004 — Optimistic concurrency on the aggregate root via `rowversion` + `ETag`/`If-Match`

**Context.** Two approvers (or an approver and an admin reassigning) may act on the same request
at the same time. Last-write-wins would silently lose decisions.

**Decision.** `Requests.RowVersion` is the concurrency token for the whole aggregate. Every command
touches the root. Clients must send `If-Match`: a missing header → `428`, a mismatch → `412`.

**Consequences.** No locks held across requests, and conflicts are explicit and recoverable (re-read, retry).
Clients have to handle ETags. That's standard HTTP, and the `.http` samples show how.

## ADR-005 — Idempotency keys stored in SQL Server

**Context.** Mobile and flaky-network clients retry POSTs. A duplicate "create request" or "approve" must
not produce duplicate effects.

**Decision.** An optional `Idempotency-Key` header, stored in an `IdempotencyRecords` table keyed
by (`UserId`, `Key`) with a request hash and the stored response. The key is reserved before the handler
runs, in its own transaction.

**Consequences.** No extra infrastructure (Redis) is needed. Because the reservation and the command are
separate transactions, a crash between them leaves a key "in progress" until it expires (24 hours). The client
gets `409` and can use a new key, which is an accepted trade-off. For approve/reject, `If-Match` already prevents
double application, so idempotency there mainly gives retried calls a friendly replayed response.

## ADR-006 — Self-issued JWT for the demo; external identity provider for production

**Context.** The repository must run from a clean checkout with no external accounts, but a real
deployment should not manage passwords itself.

**Decision.** A minimal token endpoint backed by the `Users` table and `PasswordHasher<T>`.
ASP.NET Core Identity's full stack is not used. The API validates standard JWT bearer tokens, so replacing the
issuer with Microsoft Entra ID or another OIDC provider is a configuration change plus role-claim mapping.
Refresh tokens are out of scope for v1.0.

**Consequences.** The repository is easy to run and the security surface is small. There is no
password reset, MFA or lockout, and that is documented. Short-lived access tokens limit the cost of having
no revocation.

## ADR-007 — Integration tests against real SQL Server (Testcontainers), not the in-memory provider

**Context.** Key behaviours rely on SQL Server: `rowversion`, check constraints, filtered
indexes, sequences, the audit trigger, and translation of the queries.

**Decision.** `OpsFlow.Api.Tests` starts a SQL Server container once per test run, applies migrations,
and resets data between tests (Respawn, or scripted deletes in dependency order). The API is hosted in-process
with `WebApplicationFactory`.

**Consequences.** Tests are slower (container start is about 15–30 s once per run) and need Docker locally and in CI.
In return they catch real problems. Domain tests need no Docker and stay sub-second.

## ADR-008 — Offset pagination with total count

**Context.** Consumers (an admin UI, an inbox) usually need page numbers and totals. Expected
volumes are tens of thousands of requests, not hundreds of millions.

**Decision.** `page`/`pageSize` with `totalCount`, a capped page size, and a stable sort (`Id` tiebreaker).

**Consequences.** Simple and predictable. Deep pages and `COUNT(*)` get more expensive as data grows.
Phase 6 measures this, and keyset pagination is the documented next step for the audit log if needed.

## ADR-009 — Avoid commercially licensed libraries in the core

**Context.** In 2025, MediatR, AutoMapper and FluentAssertions (v8+) moved to commercial licences. This
is an MIT-licensed portfolio project that clients may reuse.

**Decision.** Handlers are plain classes resolved from DI. Mapping is done with explicit projections and
assertions use NUnit's constraint model. Every package version is pinned in `Directory.Packages.props`, and
licences are reviewed before release.

**Consequences.** Slightly more explicit code, no licence ambiguity, and fewer abstractions to explain.

## ADR-010 — Audit trail written in the same transaction, enforced append-only in the database

**Context.** The audit trail must be trustworthy. An entry that exists without the change it describes
(or the reverse) undermines it, and an editable audit log is worthless.

**Decision.** The aggregate raises audit events. The `DbContext` turns them into `AuditEntries` in the same
`SaveChanges`. An `INSTEAD OF UPDATE, DELETE` trigger blocks modification, whatever the login.

**Consequences.** Consistency is guaranteed by the database. Audit volume grows linearly with activity, and
archiving or partitioning is future work. Writing to an external audit or SIEM system would need an outbox,
which is out of scope.

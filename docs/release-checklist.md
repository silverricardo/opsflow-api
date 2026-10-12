# Release checklist: v1.0.0

## Verified (2026-10-10)

- [x] **Clean checkout runs by following the README.** Fresh `git clone`, `cp .env.example .env`,
      `docker compose up -d --build`, then: readiness healthy, Scalar UI served, the README example reproduced
      (`201` + `ETag` + `Location`, `428` without `If-Match`), all six demo users sign in, and the Finance inbox
      shows the seeded request. Verified on macOS (Intel) with Docker Desktop 28.1.1.
- [x] **Build:** Release, warnings as errors, `dotnet format` clean, no pending model changes.
- [x] **Tests:** 120 domain and 226 API tests (unit + integration against SQL Server 2022) pass locally and in
      GitHub Actions.
- [x] **CI** green on the release branch: format, build, migration check, tests, migration bundle, image build.
- [x] **No secrets** in the working tree or the git history (only example and test values). `.env` is ignored.
- [x] **Dependencies:** no known vulnerable packages (`dotnet list package --vulnerable --include-transitive`).
      All licences are MIT, Apache-2.0 or BSD-3-Clause.
- [x] **Independent security and code reviews** done. All findings fixed or documented (see the commit
      "fix: address findings from independent security and code reviews").
- [x] **Performance baseline** measured and documented with its environment (perf/README.md).
- [x] **Docs:** requirements, design, ADRs, operations, deployment, performance, security policy,
      contributing guide, licence.

## Not verified

- [ ] **Windows.** The PowerShell commands in the README have not been run on Windows. The Docker path is
      platform-independent.
- [ ] **Azure deployment.** The guide documents a path. It has not been executed.
- [x] **Performance on standardised hardware:** measured on a GitHub-hosted runner on 2026-10-12. Results are in
      perf/README.md.

## Steps to publish (repository owner)

1. **Merge** `feature/release-prep` into `main` through a pull request. It contains every phase.
2. ~~**Rename** the repository to `opsflow-api`~~ ✅ Done; links updated.
3. **About** (repository home page → ⚙️):
   - *Description* (one line; keep it unwrapped when pasting):

     ```text
     Approval workflows API for purchase and expense requests: .NET 10, EF Core, SQL Server. Policy-driven approvals, optimistic concurrency, idempotent retries, audit trail, k6-tested.
     ```
   - *Topics* (type each one and press Enter): `dotnet`, `aspnetcore`, `csharp`, `entity-framework-core`,
     `sql-server`, `rest-api`, `domain-driven-design`, `testcontainers`, `docker`, `github-actions`,
     `opentelemetry`, `k6`, `approval-workflow`.
4. **Security:** enable *Private vulnerability reporting* (Settings → Code security), as referenced by
   SECURITY.md. Enable Dependabot alerts.
5. **Branch protection** on `main`: require pull requests and the `CI` checks (*Build, checks and tests*,
   *Container image*).
6. **Delete stale branches:** `feature/new_funcions` (empty, from before the project) and the merged feature
   branches.
7. **Tag and release:** `git tag -a v1.0.0 -m "OpsFlow 1.0.0" && git push origin v1.0.0`, then create a GitHub
   release from the tag. Summarise the highlights and link the README sections.
8. **Run the Performance baseline workflow** on `main` (Actions → Performance baseline → Run workflow). Add the
   measured results, with that run's environment, to perf/README.md.
9. **Screenshots** (optional, makes the README more visual). Capture:
   - the Scalar API reference at `/scalar`;
   - one trace in the Aspire dashboard (`--profile observability`), showing HTTP → SQL spans;
   - the green CI run.

   Store them in `docs/images/` and reference them from the README.
10. **Pin** the repository on your GitHub profile and publish the LinkedIn post.

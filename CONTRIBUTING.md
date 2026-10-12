# Contributing

Thanks for your interest. Issues and pull requests are welcome. For security problems, follow
[SECURITY.md](SECURITY.md) instead of opening an issue.

## Before you start

- For anything beyond a small fix, open an issue first to agree on the approach.
- Business rules live in [docs/requirements.md](docs/requirements.md) (`BR-xx`). A change to behaviour starts
  there, and the tests reference the rule IDs.
- Significant technical decisions are recorded as ADRs in
  [docs/architecture-decisions.md](docs/architecture-decisions.md). Add a new one rather than editing an accepted
  one; mark the old one as superseded.

## Development workflow

```bash
dotnet tool restore
dotnet format OpsFlow.slnx --verify-no-changes   # style (CI fails otherwise)
dotnet build OpsFlow.slnx -c Release             # warnings are errors
dotnet test OpsFlow.slnx                         # integration tests need Docker running
```

Schema changes: change the entity configuration, then
`dotnet ef migrations add <Name> --project src/OpsFlow.Api --output-dir Persistence/Migrations`. Review the
generated migration (indexes on large tables: see docs/deployment-azure.md §3). CI fails when the model and the
migrations disagree.

## Conventions

- **Branches:** `feature/<topic>` or `fix/<topic>`, from `main`.
- **Commits:** imperative, with a type prefix (`feat:`, `fix:`, `docs:`, `test:`, `refactor:`, `chore:`). The
  body explains *why*.
- **Code:** follow the existing patterns:
  - business rules go in `OpsFlow.Domain` and return `Result`/`DomainError`, never throw for rule violations;
  - endpoints live in feature folders and use `RequestCommands.ExecuteAsync` for commands on existing requests;
  - queries project straight into response types.
- **Tests:**
  - every business rule gets a domain test;
  - every endpoint behaviour gets an integration test against SQL Server;
  - race conditions get a repeated (`[Repeat]`) concurrent test.

## Pull request checklist

- [ ] Format, build and tests pass locally (CI runs the same steps).
- [ ] New or changed behaviour is covered by tests and reflected in the docs (requirements, design, README).
- [ ] No secrets, personal data or generated files (`bin/`, `obj/`, `.env`, `perf/results/`).
- [ ] Breaking API or schema changes are called out in the description.

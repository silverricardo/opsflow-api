# OpsFlow — Approval Workflows API

Approval workflows for purchase and expense requests. Approval chains are driven by policy, segregation of
duties is enforced, and every action leaves a full audit trail.
Built with ASP.NET Core 10, EF Core 10 and SQL Server.

> 🚧 Work in progress. The first vertical slice is in place (sign-in, create, submit, view).
> Approval decisions, assignment, lists and CI come next. See [docs/requirements.md](docs/requirements.md).

## Run it locally (Docker)

Requirements: Docker Desktop (or Docker Engine with Compose v2).

```bash
cp .env.example .env          # local-only values; change them if you like
docker compose up -d --build
```

- API reference (Scalar): http://localhost:8080/scalar
- Health: http://localhost:8080/health/ready
- Demo users: `employee1@opsflow.local`, `manager1@opsflow.local`, `finance@opsflow.local`, `director@opsflow.local`,
  `admin@opsflow.local`, … with the password from `SEED_DEMO_PASSWORD` in your `.env`.

```bash
# Get a token, then create and submit a request
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/auth/token -H 'Content-Type: application/json' \
  -d '{"email":"employee1@opsflow.local","password":"<SEED_DEMO_PASSWORD>"}' | jq -r .accessToken)

curl -i -X POST http://localhost:8080/api/v1/requests -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"title":"New laptop","category":"Purchase","amount":1200}'
# → 201 Created, with Location and ETag headers

curl -X POST http://localhost:8080/api/v1/requests/<id>/submit -H "Authorization: Bearer $TOKEN" -H 'If-Match: <ETag>'
```

## Run from source

Requirements: .NET SDK 10.0.100 or later, and Docker (for SQL Server and the integration tests).

```bash
docker compose up -d sqlserver
dotnet tool restore
cd src/OpsFlow.Api
dotnet user-secrets set "ConnectionStrings:OpsFlow" "Server=localhost,1433;Database=OpsFlow;User Id=sa;Password=<MSSQL_SA_PASSWORD>;TrustServerCertificate=True"
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
dotnet user-secrets set "Seed:DemoPassword" "<at least 12 characters>"
dotnet run          # http://localhost:5054/scalar — migrations and demo data are applied on start
```

On Windows (PowerShell), the same commands work. Generate the key with
`[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }))`.

## Tests

```bash
dotnet test
```

- `OpsFlow.Domain.Tests`: business rules. No dependencies, runs in about a second.
- `OpsFlow.Api.Tests`: API unit tests, plus integration tests that start a real SQL Server 2022 container through
  Testcontainers (Docker required). The API is hosted in-process, migrations run as they would in production, and
  data is reset between tests.

## Documentation

- [Requirements and business rules](docs/requirements.md)
- [Design: architecture, schema, API contract, security](docs/design.md)
- [Architecture decision records](docs/architecture-decisions.md)

## License

MIT (to be added before the first release).

# Security Policy

## Reporting a vulnerability

Please **do not open a public issue** for security problems. Report them privately through GitHub instead:
**Security → Report a vulnerability** on this repository. You should get a response within a few days. Once a fix
is available, the advisory will credit you (unless you prefer otherwise).

## Supported versions

This is a portfolio and reference project. Only the latest commit on `main` receives fixes.

## Security model

How the API authenticates, authorizes and protects data is documented in
[docs/design.md §5](docs/design.md#5-security-model). The production hardening checklist is in
[docs/deployment-azure.md](docs/deployment-azure.md#5-production-checklist). In short:

- JWT bearer authentication (HS256 for the demo issuer; an external OIDC provider for production, see ADR-006)
  and short-lived tokens.
- Three authorization layers: role policies on endpoints, visibility filtered in SQL (hidden requests return
  `404`), and business rules in the domain (segregation of duties).
- Sign-in protected per IP and per account. Responses do not reveal which accounts exist.
- An append-only audit trail enforced by a database trigger. Run the application with a least-privilege login.
- Secrets only from the environment or a secret store. Nothing sensitive is committed. Demo seeding is refused
  outside `Development`, `Testing` and `Performance`.

## Known limitations

These are documented trade-offs, not vulnerabilities:

- The demo token endpoint has no MFA, password reset or account recovery. Production should use an identity
  provider.
- There is no token revocation: a deactivated user keeps access until the token expires (15 minutes by default).
- The per-account sign-in throttle is in memory, per instance, and can be used to temporarily block sign-in for
  a known e-mail address.
- The local `docker-compose.yml` uses the SQL Server `sa` login with values from `.env`, and is bound to
  `127.0.0.1`. It is for local development only.

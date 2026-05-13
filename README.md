# app-foundation

An opinionated application foundation for .NET 10 / Blazor projects — built to be forked,
not just read. Implements the boilerplate that every serious web app needs before any
business logic can begin.

---

## What this is

Starting a new .NET web application means solving the same problems every time:
authentication, user management, roles, admin UI — before a single line of actual product
code can be written. This foundation solves that once, cleanly, and in a way that can be
extended without fighting the architecture.

It's the base used for client projects and internal tools. Made public because the
patterns might be useful to others.

For a deeper look at technical decisions and architecture, see [TECHNICAL.md](TECHNICAL.md).

---

## Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor (.NET 10) |
| Backend | ASP.NET Core (.NET 10) |
| Persistence | [Marten](https://martendb.io/) (PostgreSQL document store + event store) |
| Messaging / CQRS | [Wolverine](https://wolverine.netlify.app/) |
| Orchestration | [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) |
| Authentication | ASP.NET Core Identity — extended |
| Infrastructure | Docker / docker-compose |

---

## Features

### Authentication — three paths, one system

- **Username / Password** — standard credential flow with hashed storage
- **Passkey (WebAuthn / FIDO2)** — passwordless login via device biometrics or hardware key
- **OTP (Time-based One-Time Password)** — TOTP-based two-factor authentication

All three live in the same Identity pipeline — no separate auth services, no third-party
auth provider required.

### User & Role Management

- Full user administration UI (create, edit, deactivate users)
- Role assignment per user
- Admin-protected area, separated from the public app surface

### Admin Frontend

A functional Blazor admin interface — not a UI demo, but a working management layer
wired to real data operations via Wolverine handlers and Marten persistence.

### Infrastructure

- **.NET Aspire** orchestration for local development and deployment
- **Docker** support via `docker-compose.example.yml`
- **CI/CD** pipeline via GitHub Actions

---

## Architecture notes

**Why Marten?**
PostgreSQL as a document store removes the ORM mapping layer for most use cases while
keeping the option to use relational queries when needed. Event sourcing support is
built in for when that pattern fits. No separate NoSQL infrastructure required.

**Why Wolverine?**
Clean handler dispatch without the ceremony of MediatR — with built-in support for
message persistence, retries and outbox patterns. The foundation is wired for async
messaging from day one, not bolted on later.

**Why .NET Aspire?**
Local orchestration and service discovery without Docker Compose complexity. Aspire
handles the coordination between app, database and any future services — and maps
cleanly to cloud deployment targets.

**Why not microservices from the start?**
Modular monolith by design. Modules are separated by namespace and handler boundary,
not by network boundary. Splitting later is possible — splitting prematurely creates
operational overhead before there's a scaling problem that justifies it.

---

## Getting started

### Prerequisites

- .NET 10 SDK
- Docker (for the local PostgreSQL container via Aspire)
- (Optional) A FIDO2-capable device or browser for Passkey testing

### Run with Aspire (recommended)

```bash
git clone https://github.com/andregoepel/app-foundation.git
cd app-foundation
```

Set required secrets (see [Development secrets](#development-secrets) below), then:

```bash
dotnet run --project AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.AppHost
```

Aspire starts the app and spins up a local PostgreSQL container automatically.
The Aspire dashboard is available at `https://localhost:15888`.

### Run with Docker Compose

```bash
cp docker-compose.example.yml docker-compose.yml
# Edit docker-compose.yml — adjust credentials if needed
docker compose up
```

---

## Development secrets

Secrets are stored via `dotnet user-secrets` and are never committed to source control.

### AppHost (`AndreGoepel.AppFoundation.AppHost`)

Run from `AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.AppHost/`:

```
dotnet user-secrets set "Parameters:database-password" "<your-password>"
```

| Key | Description |
| --- | --- |
| `Parameters:database-password` | Password for the local Postgres container |

### App (`AndreGoepel.AppFoundation`)

Run from `AndreGoepel.AppFoundation/AndreGoepel.AppFoundation/`:

```
dotnet user-secrets set "ConnectionStrings:appfoundation-database" "Host=localhost;Port=59746;Username=db-user;Password=<your-password>;Database=appfoundation-database"
```

| Key | Description |
| --- | --- |
| `ConnectionStrings:appfoundation-database` | Full connection string for the local Postgres database |

---

## Status

Active development. Core authentication flows and admin UI are functional.
Not yet: production hardening, full test coverage, deployment guides.

Feedback and issues welcome.

---

## License

MIT — use freely, attribution appreciated but not required.

---

*Built by [André Göpel](https://andregoepel.dev) — Senior Web Engineer · .NET & Blazor*

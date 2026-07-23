# app-foundation

A reusable **backend + management-frontend foundation** for .NET 10 / Blazor apps,
published as NuGet packages. A consuming app references the packages and gets
authentication, user/role administration, a Blazor management shell, durable email,
and .NET Aspire service defaults out of the box — then adds its own pages and branding
on top.

- **Reference consumer:** [andregoepel.dev](https://github.com/andregoepel/andregoepel-dev)
- **Identity** is provided by the companion [AndreGoepel.Marten.Identity](https://github.com/andregoepel/marten-identity) packages.

> This repository used to host both the foundation *and* a website. The website has been
> extracted to its own repo; this repository is now purely the published packages.

---

## Packages

| Package | Purpose |
|---|---|
| `AndreGoepel.AppFoundation` | Razor Class Library — the management frontend: layout, navigation, setup, dashboard, error pages. Brand/extend via `AppFoundationLayoutOptions`. |
| `AndreGoepel.AppFoundation.Hosting` | **Umbrella backend seam** — `AddAppFoundation` / `UseAppFoundation`. Transitively pulls in the other packages + `AndreGoepel.Marten.Identity.Blazor`. |
| `AndreGoepel.AppFoundation.MailService` | Email via a Wolverine handler + MailKit SMTP, backed by a durable Marten outbox. |
| `AndreGoepel.AppFoundation.ServiceDefaults` | .NET Aspire service defaults: OpenTelemetry, health checks, HTTP resilience, service discovery. |
| `AndreGoepel.AppFoundation.Core` | Shared abstract base types and interfaces (e.g. `SettingsDocument`) with no framework or infrastructure dependencies of their own. |

All five are published to NuGet with lockstep versioning. A host typically references
`AndreGoepel.AppFoundation.Hosting` (for the wiring) and `AndreGoepel.AppFoundation` (for
the UI components it renders directly).

---

## Stack

| Layer | Technology |
|---|---|
| Frontend | Blazor (.NET 10), [Radzen](https://blazor.radzen.com/) components |
| Backend | ASP.NET Core (.NET 10) |
| Persistence | [Marten](https://martendb.io/) (PostgreSQL document + event store) |
| Messaging / CQRS | [Wolverine](https://wolverine.netlify.app/) (durable outbox) |
| Email | [MailKit](https://github.com/jstedfast/MailKit) |
| Orchestration | [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) |
| Authentication | ASP.NET Core Identity, event-sourced ([marten-identity](https://github.com/andregoepel/marten-identity)) |

---

## Using it in a host app

Reference the packages (versions are centrally managed in your repo):

```xml
<PackageReference Include="AndreGoepel.AppFoundation.Hosting" />
<PackageReference Include="AndreGoepel.AppFoundation" />
<PackageReference Include="AndreGoepel.Marten.Identity.Blazor" />
```

Wire the foundation in `Program.cs`:

```csharp
builder.AddAppFoundation(options =>
{
    options.DatabaseConnectionName = "appfoundation-database"; // default
    // options.WolverineServiceName, options.SecretsDirectory ...
});

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Brand the management shell and contribute your own admin menu entries.
builder.Services.Configure<AppFoundationLayoutOptions>(o =>
{
    o.BrandName = "your.app";
    o.Copyright = "your.app © 2026";
    o.AdminMenu = typeof(YourAdminMenu); // optional Razor component
});

var app = builder.Build();

app.UseAppFoundation();

app.MapStaticAssets();
app.MapRazorComponents<App>() // your root App.razor
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(AppFoundationLayoutOptions).Assembly,                         // AppFoundation UI
        typeof(AndreGoepel.Marten.Identity.Blazor.Initialization).Assembly   // identity pages
    );

app.MapAdditionalIdentityEndpoints();
```

The host owns its root `App.razor` and `Routes.razor`; the router includes the AppFoundation
and Marten.Identity.Blazor assemblies and uses AppFoundation's `MainLayout` for the
authenticated management area. See [andregoepel.dev](https://github.com/andregoepel/andregoepel-dev)
for a complete, working example.

---

## Try it locally (sample host)

The repo ships a runnable example under `samples/`: a .NET Aspire AppHost that starts
PostgreSQL in a container plus a minimal Blazor Server host that wires the packages exactly
as [Using it in a host app](#using-it-in-a-host-app) describes. It doubles as a manual smoke
test for the foundation.

**Prerequisites:** the .NET 10 SDK and a container runtime (Docker / Podman) for the database.

```bash
dotnet run --project samples/AndreGoepel.AppFoundation.AppHost
```

Open the Aspire dashboard URL printed to the console, start the **web** resource, and open it.
The first visit funnels you to **/Setup** to create the administrator; after that you can sign
in and explore the dashboard and the Administration area.

| Project | Role |
|---|---|
| `samples/AndreGoepel.AppFoundation.AppHost` | Aspire orchestrator — starts Postgres and the web app, wiring the `appfoundation-database` connection string |
| `samples/AndreGoepel.AppFoundation.Sample` | Minimal Blazor Server host consuming the packages — the reference `Program.cs`, `App.razor`, and `Routes.razor` |

---

## Configuration

A PostgreSQL connection string is required, by default under
`ConnectionStrings:appfoundation-database`.

**`AppFoundationOptions`** (backend seam):

| Option | Default | Purpose |
|---|---|---|
| `DatabaseConnectionName` | `appfoundation-database` | Connection-string name to read |
| `WolverineServiceName` | `AppFoundation` | Service name for the durable inbox/outbox |
| `SecretsDirectory` | `/run/secrets` | Key-per-file secrets directory (see below) — *from 1.1.0* |
| `SchemaCreation` | env-based | Marten schema mode; `null` ⇒ `All` in Development, `CreateOrUpdate` otherwise. Set `None` for least-privilege — see [Database schema](#database-schema) |
| `KnownProxyNetworks` / `KnownProxies` | *(empty)* | Trusted reverse-proxy CIDRs / IPs for `X-Forwarded-*` — see [Running behind a reverse proxy](#running-behind-a-reverse-proxy) |
| `ConfigureForwardedHeaders` | — | Callback for full `ForwardedHeadersOptions` control |
| `DataProtectionApplicationDiscriminator` | `WolverineServiceName` | Isolates protected payloads from other apps sharing infrastructure |
| `ConfigureDataProtection` | — | Callback on the DataProtection builder (Key Vault, cert rotation, …) |
| `AllowUnprotectedKeyRing` | `false` | Accept an unencrypted key ring outside Development — see [Data protection keys](#data-protection-keys) |

**`AppFoundationLayoutOptions`** (management shell branding):
`BrandName`, `LogoPath`, `Copyright`, and `AdminMenu` (a Razor component type rendered as
extra administrator nav entries).

### Docker secrets (from 1.1.0)

Configuration — notably the connection string — can be supplied as files instead of
plaintext environment variables. `AddAppFoundation` loads `SecretsDirectory` (default
`/run/secrets`) key-per-file with `optional: true` (a no-op when absent, so local
development is unaffected). A secret named `ConnectionStrings__appfoundation-database`
(content = the full connection string; `__` maps to the `:` section separator) is read
straight into configuration and never enters the process environment.

```yaml
services:
  app:
    image: ghcr.io/you/your-app:latest
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    secrets:
      - ConnectionStrings__appfoundation-database

secrets:
  ConnectionStrings__appfoundation-database:
    file: ./secrets/connectionstring   # chmod 600; or `external: true` under Swarm
```

---

## Production configuration

Outside `Development`, a few things must be configured — the app is secure by default and
**fails fast** rather than running in an unsafe state. Checklist for a non-Development host
(`ASPNETCORE_ENVIRONMENT=Production` or `Staging`):

1. **Connection string** — required (see [Configuration](#configuration)).
2. **Data protection keys** — required, or the app won't start (see below).
3. **Reverse proxy** — set the trusted proxy networks so client IP/scheme are honored (see below).
4. **Database schema** — defaults to non-destructive; tighten for least privilege (see below).

### Data protection keys

The DataProtection key ring is persisted in the same database as the data it protects, so
outside Development the foundation **refuses to start unless the keys are encrypted at rest**
— otherwise a database dump would also expose the keys guarding the SMTP password, login
tokens, and auth cookies. Provide one of:

- **A certificate** via `DataProtection:CertificatePath` (+ `DataProtection:CertificatePassword`),
  ideally supplied as Docker secrets:

  ```yaml
  environment:
    - DataProtection__CertificatePath=/run/secrets/dataprotection_cert
  secrets:
    - dataprotection_cert
    - DataProtection__CertificatePassword
  ```

- **Key Vault / KMS** via `options.ConfigureDataProtection = b => b.ProtectKeysWithAzureKeyVault(...)`.
- Or, only when the database storage is already encrypted at rest by other means, opt out with
  `options.AllowUnprotectedKeyRing = true`.

### Running behind a reverse proxy

The app trusts the TCP peer of each request — which behind a proxy is the **proxy's** address,
not the client's. Declare the proxy so `X-Forwarded-For` / `X-Forwarded-Proto` are honored only
from it (and never spoofable by arbitrary clients). Configure via code or, since the production
CIDR is usually only known at deploy time, via configuration:

```yaml
# docker-compose.yml — app service
environment:
  - AppFoundation__KnownProxyNetworks=172.28.0.0/16   # your proxy/ingress subnet
expose: ["8080"]        # not published to the host — only the proxy can reach the app
networks: [edge]
networks:
  edge:
    ipam:
      config: [{ subnet: 172.28.0.0/16 }]
```

`AppFoundation__KnownProxyNetworks` (and `AppFoundation__KnownProxies` for single IPs) accept a
comma/semicolon/space-delimited list or a config array; IPv6 CIDRs are supported. With none set,
forwarded headers are trusted from any origin in Development but only from loopback otherwise
(the app logs a warning). The proxy must forward the headers — for nginx, and to keep Blazor
Server circuits alive over WebSockets:

```nginx
location / {
    proxy_pass http://app:8080;
    proxy_set_header Host              $host;
    proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;   # https → app sees IsHttps=true
    proxy_http_version 1.1;                        # WebSocket upgrade for SignalR
    proxy_set_header Upgrade    $http_upgrade;
    proxy_set_header Connection $connection_upgrade;
}
```

For multiple proxies (e.g. Cloudflare → nginx), also raise `ForwardLimit` via
`ConfigureForwardedHeaders` and trust each hop.

### Database schema

`SchemaCreation` defaults to `AutoCreate.All` in Development (permits destructive rebuilds) and
`AutoCreate.CreateOrUpdate` (additive only — never drops) elsewhere, so a code/database mismatch
can't destroy data at runtime. For a least-privilege deployment, set `SchemaCreation = AutoCreate.None`
and provision the schema out-of-band (a migration job / `db-apply`) with a privileged role, then
run the app with a role that has no DDL rights.

---

## Repository layout

```
src/
  AndreGoepel.AppFoundation/             # management-frontend RCL
  AndreGoepel.AppFoundation.Core/        # shared abstract base types/interfaces, no dependencies
  AndreGoepel.AppFoundation.Hosting/     # umbrella backend seam
  AndreGoepel.AppFoundation.MailService/ # Wolverine + MailKit email
  AndreGoepel.AppFoundation.ServiceDefaults/
tests/
  AndreGoepel.AppFoundation.Tests/
  AndreGoepel.AppFoundation.MailService.Tests/
```

---

## Build & release

```bash
dotnet restore     # --locked-mode in CI
dotnet build -c Release
dotnet test -c Release
```

- **Central Package Management** with committed `packages.lock.json`; CI restores in
  `--locked-mode`, runs a vulnerability gate, and pins actions to commit SHAs.
- **Publishing:** push a `vX.Y.Z` tag → CI packs all four packages and publishes them to
  NuGet via OIDC trusted publishing (no stored API key).

---

## Architecture notes

**Why Marten?** PostgreSQL as a document store removes the ORM mapping layer for most use
cases while keeping relational queries available, with event sourcing built in. No separate
NoSQL infrastructure.

**Why Wolverine?** Clean handler dispatch with built-in message persistence, retries and
outbox support — async messaging from day one, not bolted on later.

**Why .NET Aspire?** Local orchestration and service discovery that map cleanly to cloud
deployment targets.

**Why a modular monolith?** Modules are separated by namespace and handler boundary, not by
network boundary. Splitting later is possible; splitting prematurely adds operational
overhead before there's a scaling problem that justifies it.

**Why a shared settings table?** Small, singleton, admin-configured records — SMTP settings
today, whatever a host app adds tomorrow — would otherwise each get their own one-row Marten
table. `SettingsDocument` (in `AndreGoepel.AppFoundation.Core`, which has no dependencies of its
own) is an abstract base type; subclasses register via Marten's document-hierarchy support
instead:

```csharp
marten.Schema.For<SettingsDocument>()
    .AddSubClass<EmailSettingsDocument>()
    .AddSubClass<YourOwnSettingsDocument>(); // host apps add their own the same way
```

All of them then share one physical table (a `mt_doc_type` discriminator column tells them
apart), rather than the table count growing with every settings type a host app adds. This is
purely a storage detail — `IEmailSettingsStore`/`IMailSettingsProvider` and the Email settings
page are unaffected either way.

---

## License

MIT — use freely, attribution appreciated but not required.

---

*Built by [André Göpel](https://andregoepel.dev) — Senior Web Engineer · .NET & Blazor*

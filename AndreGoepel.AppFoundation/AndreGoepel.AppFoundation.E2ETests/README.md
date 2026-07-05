# AndreGoepel.AppFoundation.E2ETests

End-to-end tests that drive the **real** application — the Blazor UI, the cookie-login
middleware, PostgreSQL, and email delivery — through a Chromium browser.

## How it works

- **Aspire.Hosting.Testing** boots the actual `AppHost` graph (Postgres + MailHog + the web
  app) once per test run. The AppHost is started with `AppHost:TestRun=true`, which makes the
  Postgres and MailHog containers **ephemeral** (no persistent data volume, dynamic host ports)
  so runs are isolated and never collide with a developer's local `dotnet run` Aspire session.
- **Microsoft.Playwright** (Chromium) drives the browser. Each test gets a fresh
  `IBrowserContext` so cookies never leak between tests.
- **MailHog** captures every outgoing email. `MailHogClient` reads the inbox over MailHog's HTTP
  API so confirmation / password-reset links are followed for real.
- **Otp.NET** generates TOTP codes for the authenticator (2FA) flows.
- A Chromium **CDP virtual authenticator** satisfies WebAuthn ceremonies, so passkey
  register/login run headlessly with no physical device.

The whole suite runs **serially** inside one xUnit collection because it shares a single app
instance and database. The first test that needs it provisions the root admin exactly once via
the `/Setup` flow (`E2EAppFixture.ProvisionAdminAsync`, idempotent).

## Prerequisites

1. **A container runtime** must be running — Docker **or** Podman. The tests start real
   containers; if none is reachable the fixture fails fast.
2. **.NET 10 SDK**.
3. **Playwright browsers** installed once:

   ```bash
   # after a build, from the repo:
   pwsh AndreGoepel.AppFoundation/AndreGoepel.AppFoundation.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

### Using Podman instead of Docker

Aspire's orchestrator auto-detects the runtime, but if Docker Desktop's `docker.exe` is on
your PATH (even with its daemon stopped) it may be picked first. Force Podman:

1. Start a Podman machine (one-time init on Windows/macOS):

   ```powershell
   podman machine init      # only if `podman machine list` shows none
   podman machine start
   podman version           # should show a Server section
   ```

2. Select the Podman runtime, either via a user env var:

   ```powershell
   setx DOTNET_ASPIRE_CONTAINER_RUNTIME podman   # new terminals pick it up
   ```

   …or per-run with the provided settings file:

   ```bash
   dotnet test AndreGoepel.AppFoundation.E2ETests --settings AndreGoepel.AppFoundation.E2ETests/podman.runsettings
   ```

> First run pulls `postgres` and `mailhog/mailhog`. If Podman prompts to choose a registry,
> add `unqualified-search-registries = ["docker.io"]` to your `containers.conf`, or pre-pull:
> `podman pull docker.io/mailhog/mailhog` and `podman pull docker.io/library/postgres`.

## Running

```bash
# from the solution folder (AndreGoepel.AppFoundation)
dotnet test AndreGoepel.AppFoundation.E2ETests
```

Watch the browser (debugging locally):

```bash
E2E_HEADED=true dotnet test AndreGoepel.AppFoundation.E2ETests
```

The main `CI` workflow skips these (`--filter "FullyQualifiedName!~E2ETests"`); they run in the
dedicated `E2E` GitHub Actions workflow, which has Docker available.

## Coverage

| Area | Tests |
| --- | --- |
| Smoke | app boots, `/Setup` runs once, admin login → dashboard, public landing |
| Registration | register → email confirmation → login; login blocked before confirmation; password-mismatch validation |
| Login | wrong password, lockout after repeated failures, logout → protected page redirects |
| Password reset | forgot → emailed link → reset → login; invalid link; resend confirmation |
| Two-factor (TOTP) | enable via authenticator, login with generated code, login with recovery code, disable |
| Passkeys (WebAuthn) | register + rename + list, login with passkey (virtual authenticator) |
| Account management | update profile, change password, delete account |
| Administration | list users, create role, non-admin authorization boundary |
| Website / navigation | public landing, content admin (admin-only), not-found page |

## Tuning notes

The UI is built with **Radzen**, whose markup can shift between versions. Selectors are
centralized in `Infrastructure/PageExtensions.cs` and the page flows in `E2ETestBase`, so if a
selector drifts you fix it in one place. The 2FA and passkey tests are the most timing-sensitive
(JS-driven ceremonies); if they flake, check `WaitForBlazorAsync` and the module-load delay in
`PasskeyTests.RegisterPasskeyAsync` first.

# AndreGoepel.AppFoundation.E2ETests

End-to-end tests that drive the **real** application — the Blazor UI, the cookie-login
middleware, PostgreSQL, and email delivery — through a Chromium browser.

## How it works

- **Aspire.Hosting.Testing** boots the sample `AppHost`
  (`samples/AndreGoepel.AppFoundation.AppHost`) once per test run: PostgreSQL, MailHog, and the
  sample web app (Aspire resource `web`). The fixture waits for `web` to become healthy, then
  reads its `https` endpoint and MailHog's `http` endpoint.
- **Microsoft.Playwright** (Chromium) drives the browser. Each test gets a fresh
  `IBrowserContext` so cookies never leak between tests.
- **MailHog** captures every outgoing email. `MailHogClient` reads the inbox over MailHog's HTTP
  API so confirmation / password-reset links are followed for real.
- **Otp.NET** generates TOTP codes for the authenticator (2FA) flows.
- A Chromium **CDP virtual authenticator** satisfies WebAuthn ceremonies, so passkey
  register/login run headlessly with no physical device.

The suite runs **serially** inside one xUnit collection because it shares a single app instance
and database. The first test that needs it provisions the root admin exactly once via the
`/Setup` flow (`E2EAppFixture.ProvisionAdminAsync`, idempotent). On CI's fresh runners the
Postgres volume starts empty; because the flows are idempotent (admin provisioned once, unique
emails per test) they also tolerate reused local state.

The account pages under test (`/Account/*`, `/Administration/Users`, `/Administration/Roles`)
ship in the **`AndreGoepel.Marten.Identity.Blazor`** NuGet package the app consumes; the
foundation pages (`/Setup`, `/dashboard`, `/Administration/EmailSettings`,
`/Administration/LoginFeatures`) live in `src/AndreGoepel.AppFoundation`.

## Prerequisites

1. **A container runtime** must be running — Docker **or** Podman. The tests start real
   containers; if none is reachable the fixture fails fast.
2. **.NET 10 SDK**.
3. **Playwright browsers** installed once:

   ```bash
   # after a build, from the repo root:
   pwsh tests/AndreGoepel.AppFoundation.E2ETests/bin/Debug/net10.0/playwright.ps1 install chromium
   ```

### Using Podman instead of Docker

Aspire's orchestrator auto-detects the runtime, but if Docker Desktop's `docker.exe` is on your
PATH (even with its daemon stopped) it may be picked first. Force Podman:

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
   dotnet test tests/AndreGoepel.AppFoundation.E2ETests --settings tests/AndreGoepel.AppFoundation.E2ETests/podman.runsettings
   ```

> First run pulls the `postgres` and `mailhog/mailhog` images. If Podman prompts to choose a
> registry, add `unqualified-search-registries = ["docker.io"]` to your `containers.conf`, or
> pre-pull: `podman pull docker.io/mailhog/mailhog:v1.0.1` and `podman pull docker.io/library/postgres`.

## Running

```bash
# from the repo root
dotnet test tests/AndreGoepel.AppFoundation.E2ETests
```

Watch the browser (debugging locally):

```bash
E2E_HEADED=true dotnet test tests/AndreGoepel.AppFoundation.E2ETests
```

The main `CI` workflow skips these (`--filter "FullyQualifiedName!~E2ETests"`); they run in the
dedicated `E2E` GitHub Actions workflow, which has Docker available.

## Coverage

| Area | Tests |
| --- | --- |
| Smoke | app boots, `/Setup` runs once, admin login → dashboard |
| Registration | register → email confirmation → login; login blocked before confirmation; password-mismatch validation |
| Login | wrong password, lockout after repeated failures, logout → protected page redirects |
| Password reset | forgot → emailed link → reset → login; invalid link; resend confirmation |
| Two-factor (TOTP) | enable via authenticator, login with generated code, login with recovery code, disable |
| Passkeys (WebAuthn) | register + rename + list, login with passkey (virtual authenticator) |
| Account management | update profile, change password, delete account |
| Administration | list users, create role, non-admin authorization boundary |
| App pages | sample home, Email Settings, Login Features (admin-only) |

## Tuning notes

The UI is built with **Radzen**, whose markup can shift between versions. Selectors are
centralized in `Infrastructure/PageExtensions.cs` and the page flows in `E2ETestBase`, so if a
selector drifts you fix it in one place. The account-flow selectors target the current
`AndreGoepel.Marten.Identity.Blazor` package markup; verify them on the first live run after a
package bump. The 2FA and passkey tests are the most timing-sensitive (JS-driven ceremonies); if
they flake, check `WaitForBlazorAsync` and the module-load delay in
`PasskeyTests.RegisterPasskeyAsync` first.

# app-foundation

Blazor InteractiveServer application foundation with a custom event-sourced
ASP.NET Core Identity library (`AndreGoepel.Marten.Identity`) backed by
Marten/PostgreSQL. Orchestrated via .NET Aspire.

## Solution Projects
- `AndreGoepel.AppFoundation` — main Blazor app
- `AndreGoepel.AppFoundation.AppHost` — .NET Aspire host
- `AndreGoepel.AppFoundation.MailService` — email sending
- `AndreGoepel.AppFoundation.ServiceDefaults` — shared ASP.NET Core defaults
- `AndreGoepel.Marten.Identity` — packable NuGet: Identity stores (event-sourced)
- `AndreGoepel.Marten.Identity.Blazor` — Blazor UI components for identity flows

## Repo Specifics
- Quartz.NET for scheduled jobs
- Testing scope: domain logic and handlers

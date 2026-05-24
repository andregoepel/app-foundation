# Project Instructions

## Project Overview

Blazor InteractiveServer application foundation with a custom event-sourced ASP.NET Core Identity library (`AndreGoepel.Marten.Identity`) backed by Marten/PostgreSQL. Orchestrated via .NET Aspire.

**Solution projects:**
- `AndreGoepel.AppFoundation` — main Blazor app
- `AndreGoepel.AppFoundation.AppHost` — .NET Aspire host
- `AndreGoepel.AppFoundation.MailService` — email sending
- `AndreGoepel.AppFoundation.ServiceDefaults` — shared ASP.NET Core defaults
- `AndreGoepel.Marten.Identity` — packable NuGet: Identity stores (event-sourced)
- `AndreGoepel.Marten.Identity.Blazor` — Blazor UI components for identity flows

## Tech Stack
- .NET 10, Blazor InteractiveServer, .NET Aspire
- Marten + PostgreSQL, Wolverine (durable messaging)
- Radzen (UI components)
- xUnit, bUnit

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Format: `csharpier format .` (run after every change)

## Git Workflow
- Branches: `feature/`, `bugfix/`, `hotfix/`
- Commits: `type: description` (feat, fix, refactor, test, docs)
- Always branch before changes; run tests before committing

## Code Conventions

### Naming
- Commands: `Create[Entity]Command`, `Update[Entity]Command`
- Queries: `Get[Entity]Query`, `List[Entities]Query`
- Handlers: `[Command/Query]Handler`
- DTOs: `[Entity]Dto`, `Create[Entity]Request`

### Quality
- Use async/await for all I/O; always pass `CancellationToken`
- Classes are `sealed internal` by default
- Use bare `default` instead of `default(T)` when type is inferrable
- Use `#region` / `#endregion` to group sections, not decorative dash comments

### Patterns
- Primary constructors for DI
- Records for DTOs and commands
- `Result<T>` for error handling — no exceptions for flow control
- File-scoped namespaces

## Testing
- Scope: domain logic and handlers
- Naming: `[Method]_[Scenario]_[ExpectedResult]`
- Files: `[Subject].Tests.cs`; class name inside stays `[Subject]Tests`
- `InternalsVisibleTo`: use `<InternalsVisibleTo Include="AssemblyName" />` shorthand in csproj
- Every test needs `// Arrange`, `// Act`, `// Assert` comments
  - Combine as `// Arrange / Act` when inseparable (e.g. a single `Render<>()`)
  - Omit `// Arrange` when there is no setup

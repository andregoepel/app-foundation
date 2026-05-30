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
- Quartz.NET (scheduled jobs)
- Radzen (UI components)
- xUnit, bUnit

## Commands
- Build: `dotnet build`
- Test: `dotnet test`
- Format: `csharpier format .` (run after every change)

## Git Workflow
- Branches: `feature/`, `bugfix/`, `hotfix/`
- Commits: `type: description` (feat, fix, refactor, test, docs)
- **Always create a branch before making any file edits.** Never edit files on `main`.
- **Never commit without explicit user confirmation.** Ask before every commit, no exceptions.
- **Never push to `main` or `master`.** All pushes go to a feature/bugfix/hotfix branch only.
- **Never add a `Co-Authored-By` trailer to commits.** Commit messages contain only the description.
- Run tests before committing

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

## Blazor

### Folder Structure
- `Components/Pages/` — routed page components
- `Components/Layout/` — layout components
- `Components/Shared/` — reusable components without a route
- `Components/[Feature]/Pages/` — feature-scoped pages
- `Components/[Feature]/Dialogs/` — Radzen dialog components

### UI Components
- Use Radzen components for all UI by default (`RadzenStack`, `RadzenButton`, `RadzenTextBox`, `RadzenDataGrid`, etc.)
- Use plain HTML / CSS for pages that are highly designed: homepages, landing pages, content pages. Radzen components are not needed there and get in the way of custom styling.

### Component Rules
- Prefer `@rendermode InteractiveServer` on page-level components; only set it on child components when you need a different render mode for a specific interactive island
- Shared `@using` directives belong in `_Imports.razor`; use per-file `@using` only for non-global namespaces
- Every routed page must have `<PageTitle>`
- Use `@attribute [Authorize(Roles = "...")]` on pages, not conditionals in code
- Form models: private `sealed class InputModel` inside `@code` (not a record — needs mutable properties for `@bind-Value`)

### Lifecycle & Events
- Implement `IDisposable` / `IAsyncDisposable` on any component that subscribes to events or services; unsubscribe in `Dispose()`
- Use `EventCallback<T>` for component output events — not `Action` or `Func`
- Avoid calling `StateHasChanged()` explicitly; let the framework re-render naturally. Only use it when triggering a render from an external thread or non-Blazor event

### `@code` Block Order
1. `[Parameter]` / `[SupplyParameterFromQuery]` properties
2. Private state fields
3. Lifecycle methods (`OnInitializedAsync`, `OnParametersSetAsync`)
4. Event handlers
5. Private helper methods
6. Nested types (e.g. `InputModel`)

### Code-Behind
Extract to a `.razor.cs` partial class when logic is independently testable or the file becomes hard to navigate. Keep UI-bound state fields in the `.razor` file.

## Testing
- Scope: domain logic and handlers
- Naming: `[Method]_[Scenario]_[ExpectedResult]`
- Files: `[Subject].Tests.cs`; class name inside stays `[Subject]Tests`
- `InternalsVisibleTo`: use `<InternalsVisibleTo Include="AssemblyName" />` shorthand in csproj
- Every test needs `// Arrange`, `// Act`, `// Assert` comments
  - Combine as `// Arrange / Act` when inseparable (e.g. a single `Render<>()`)
  - Omit `// Arrange` when there is no setup

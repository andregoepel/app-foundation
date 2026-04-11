# Project Instructions

## Formatting

Use **CSharpier** for all C# code formatting. It is installed globally.

```bash
csharpier format .
```

Run from `AndreGoepel.MembersArea/`. Do not use `dotnet format`.

## Test file naming

Name test files `Subject.Tests.cs` (dot-separated), not `SubjectTests.cs`.
Class names inside the file stay as generated (e.g. `UserIdTests`).

```
UserId.Tests.cs         ✓
UserProjection.Tests.cs ✓
UserIdTests.cs          ✗
```

## InternalsVisibleTo in .csproj

Use the MSBuild shorthand:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="AssemblyName" />
</ItemGroup>
```

Not the verbose `AssemblyAttribute` form.

## Default expressions (IDE0034)

Use bare `default` instead of `default(T)` when the type is inferrable from context.

```csharp
Assert.Equal(default, result);   // ✓
Assert.Equal(default(UserId), result);  // ✗
```

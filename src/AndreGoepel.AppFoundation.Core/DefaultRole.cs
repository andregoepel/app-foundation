namespace AndreGoepel.AppFoundation;

/// <summary>
/// A role seeded at first-run setup. See <c>AppFoundationOptions.DefaultRoles</c>
/// (<c>AndreGoepel.AppFoundation.Hosting</c>). Declared here, rather than alongside
/// <c>AppFoundationOptions</c>, so <c>Setup.razor</c> (in <c>AndreGoepel.AppFoundation</c>,
/// which the hosting project depends on — not the other way around) can consume the
/// resolved role list without a circular project reference.
/// </summary>
/// <param name="Name">The role name, e.g. <c>"Editor"</c>.</param>
/// <param name="Deletable">
/// Whether an administrator can later remove this role from Administration → Roles.
/// Defaults to <c>true</c> — only <c>Administrator</c> needs the non-deletable guarantee
/// (#103).
/// </param>
public sealed record DefaultRole(string Name, bool Deletable = true);

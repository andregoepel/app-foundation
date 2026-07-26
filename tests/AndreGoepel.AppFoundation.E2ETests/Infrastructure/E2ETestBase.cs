using AndreGoepel.Testing.E2E;

namespace AndreGoepel.AppFoundation.E2ETests.Infrastructure;

/// <summary>
/// Thin non-generic wrapper over <see cref="AndreGoepel.Testing.E2E.E2ETestBase{TFixture}"/>
/// closed over <see cref="AppFoundationE2EAppFixture"/>, so <c>: E2ETestBase(fixture)</c> doesn't
/// need the closed generic name spelled out on every test class. Also ensures MailHog is
/// configured on the real Email Settings admin page before any test runs — email settings are
/// database-only, and every E2E run starts from an empty database, so nothing could send mail
/// otherwise.
/// </summary>
public abstract class E2ETestBase(AppFoundationE2EAppFixture fixture)
    : AndreGoepel.Testing.E2E.E2ETestBase<AppFoundationE2EAppFixture>(fixture)
{
    public override async ValueTask InitializeAsync()
    {
        await Fixture.EnsureEmailConfiguredAsync();
        await base.InitializeAsync();
    }
}

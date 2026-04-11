using AndreGoepel.MembersArea.Components.Account.Shared;
using Bunit;

namespace AndreGoepel.MembersArea.Tests.Account.Shared;

public class ShowRecoveryCodesTests : BunitContext
{
    #region Rendering

    [Fact]
    public void RendersEachRecoveryCode()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<ShowRecoveryCodes>(p =>
            p.Add(c => c.RecoveryCodes, ["CODE-ONE", "CODE-TWO", "CODE-THREE"])
        );

        Assert.Contains("CODE-ONE", cut.Markup);
        Assert.Contains("CODE-TWO", cut.Markup);
        Assert.Contains("CODE-THREE", cut.Markup);
    }

    [Fact]
    public void RendersWarningAlert()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<ShowRecoveryCodes>(p => p.Add(c => c.RecoveryCodes, ["CODE-ONE"]));

        Assert.Contains("Put these codes in a safe place", cut.Markup);
    }

    [Fact]
    public void EmptyCodes_RendersNoCodes()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = Render<ShowRecoveryCodes>(p => p.Add(c => c.RecoveryCodes, []));

        Assert.Contains("Put these codes in a safe place", cut.Markup);
    }

    #endregion
}

using System.ComponentModel.DataAnnotations;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Radzen.Blazor;

namespace AndreGoepel.AppFoundation.Tests.Components.Pages;

/// <summary>
/// Setup.razor's InputModel can't be rendered directly from outside the assembly (it's a
/// private nested class), and mocking its full DI graph (IQuerySession, UserManager,
/// RoleManager, SignInManager, ...) just to prove client-side validation blocks a bad
/// submit is disproportionate to what's being tested. This pins down the underlying
/// mechanism instead: RadzenTemplateForm only invokes Submit once validation passes,
/// on an equivalent throwaway model — the same EditContext-driven validation pipeline
/// that Setup.razor's Radzen validator components (RadzenRequiredValidator /
/// RadzenEmailValidator / RadzenLengthValidator / RadzenCompareValidator) participate
/// in. Setup.razor's actual page path is covered end-to-end by the E2E suite's
/// ProvisionAdminAsync, which submits real (valid) data through the live, compiled form.
/// </summary>
public class SetupValidatorConversionTests : BunitContext
{
    public SetupValidatorConversionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void InvalidModel_DoesNotInvokeSubmit()
    {
        var submitted = false;
        var model = new ProbeModel { Password = "abc", ConfirmPassword = "xyz" };

        var cut = Render<RadzenTemplateForm<ProbeModel>>(p =>
            p.Add(f => f.Data, model)
                .Add(
                    f => f.Submit,
                    EventCallback.Factory.Create<ProbeModel>(this, _ => submitted = true)
                )
                .Add(f => f.ChildContent, ValidatorOnly)
        );

        cut.Find("form").Submit();

        Assert.False(submitted);
    }

    [Fact]
    public void ValidModel_InvokesSubmit()
    {
        var submitted = false;
        var model = new ProbeModel { Password = "abcdefghijkl", ConfirmPassword = "abcdefghijkl" };

        var cut = Render<RadzenTemplateForm<ProbeModel>>(p =>
            p.Add(f => f.Data, model)
                .Add(
                    f => f.Submit,
                    EventCallback.Factory.Create<ProbeModel>(this, _ => submitted = true)
                )
                .Add(f => f.ChildContent, ValidatorOnly)
        );

        cut.Find("form").Submit();

        Assert.True(submitted);
    }

    private static readonly RenderFragment<EditContext> ValidatorOnly = _ =>
        builder =>
        {
            builder.OpenComponent<DataAnnotationsValidator>(0);
            builder.CloseComponent();
        };

    private sealed class ProbeModel
    {
        [Required, StringLength(100, MinimumLength = 12)]
        public string Password { get; set; } = "";

        [Required, Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = "";
    }
}

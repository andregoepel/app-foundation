using Marten;

namespace AndreGoepel.Marten.Identity.Blazor.Components.Administration.Dialogs;

public partial class UserRoleDialog
{
    protected override async Task OnInitializedAsync()
    {
        user =
            await UserManager.FindByIdAsync(UserId)
            ?? throw new InvalidOperationException("User not found");
        roles = await RoleManager
            .Roles.Where(role => !role.Deleted)
            .ToListAsync(CancellationToken.None);

        Values = [.. user.Roles.Select(roleId => roleId)];

        await base.OnInitializedAsync();
    }
}

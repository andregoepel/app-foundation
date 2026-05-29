using AndreGoepel.Marten.Identity.Users;
using Marten;

namespace AndreGoepel.Marten.Identity.Blazor.Components.Administration.Pages;

public partial class Users
{
    private async Task LoadUsersAsync()
    {
        var query = ShowDeleted ? UserManager.Users : UserManager.Users.Where(u => !u.Deleted);

        users = await query.ToListAsync(CancellationToken.None);
    }
}

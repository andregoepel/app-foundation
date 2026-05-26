using AndreGoepel.Marten.Identity.Users;

namespace AndreGoepel.Marten.Identity.Services;

public interface ICurrentUserService
{
    Task<UserId> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);
}

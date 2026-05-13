using AndreGoepel.Marten.Identity.Blazor.Components.Account;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.Marten.Identity.Blazor;

public static class Initialization
{
    public static IServiceCollection AddMartenIdentityBlazor(this IServiceCollection services)
    {
        services.AddCascadingAuthenticationState();
        services.AddScoped<
            AuthenticationStateProvider,
            IdentityRevalidatingAuthenticationStateProvider
        >();
        return services;
    }
}

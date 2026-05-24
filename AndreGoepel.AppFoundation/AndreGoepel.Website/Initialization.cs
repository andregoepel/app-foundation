using AndreGoepel.Website.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AndreGoepel.Website;

public static class Initialization
{
    public static IServiceCollection AddWebsite(this IServiceCollection services)
    {
        services.AddScoped<SiteStateService>();
        services.AddScoped<ContentService>();
        return services;
    }
}

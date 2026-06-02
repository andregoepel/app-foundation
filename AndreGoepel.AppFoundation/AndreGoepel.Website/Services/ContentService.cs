using AndreGoepel.Website.Models;
using Marten;
using Microsoft.Extensions.Caching.Memory;

namespace AndreGoepel.Website.Services;

public class ContentService(IDocumentStore store, IMemoryCache cache)
{
    private const string _defaultSiteId = "andregoepel.dev";
    private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(10);

    public async Task<HomeContent> GetAsync(string lang, string siteId = _defaultSiteId)
    {
        var id = GetContentId(lang, siteId);

        if (cache.TryGetValue(id, out HomeContent? cached) && cached is not null)
            return cached;

        var content = await LoadAsync(id, lang, siteId);
        cache.Set(id, content, _cacheDuration);
        return content;
    }

    private async Task<HomeContent> LoadAsync(string id, string lang, string siteId)
    {
        try
        {
            using var session = store.LightweightSession();
            var document = await session.LoadAsync<SiteContent>(id);
            if (document is null)
            {
                document = new SiteContent
                {
                    Id = id,
                    SiteId = siteId,
                    Lang = lang,
                    Content = HomeContentDefaults.For(lang),
                };
                session.Store(document);
                await session.SaveChangesAsync();
            }
            return document.Content;
        }
        catch
        {
            return HomeContentDefaults.For(lang);
        }
    }

    private static string GetContentId(string lang, string siteId) => $"{siteId}:{lang}";

    public async Task SaveAsync(string lang, HomeContent content, string siteId = _defaultSiteId)
    {
        var id = GetContentId(lang, siteId);
        var document = new SiteContent
        {
            Id = id,
            SiteId = siteId,
            Lang = lang,
            Content = content,
        };
        using var session = store.LightweightSession();
        session.Store(document);
        await session.SaveChangesAsync();

        cache.Set(id, content, _cacheDuration);
    }
}

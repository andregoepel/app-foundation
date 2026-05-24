using AndreGoepel.Website.Models;
using Marten;

namespace AndreGoepel.Website.Services;

public class ContentService(IDocumentStore store)
{
    private const string _defaultSiteId = "andregoepel.dev";

    public async Task<HomeContent> GetAsync(string lang, string siteId = _defaultSiteId)
    {
        var id = GetContentId(lang, siteId);
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
        var document = new SiteContent
        {
            Id = GetContentId(lang, siteId),
            SiteId = siteId,
            Lang = lang,
            Content = content,
        };
        using var session = store.LightweightSession();
        session.Store(document);
        await session.SaveChangesAsync();
    }
}

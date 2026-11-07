namespace Reelshelf.Uploader;

/// <summary>
/// Where the app talks to and links to. Release builds always use https://reelshelf.app; debug builds read
/// <c>REELSHELF_API_BASE_URL</c> (a local or dev API), falling back to the same.
/// </summary>
internal static class AppLinks
{
    public const string ProductionSite = "https://reelshelf.app";

    /// <summary>
    /// The Store listing, opened by T8 and N4: the app's Store ID from Partner Center.
    /// </summary>
    public const string StoreProductId = "9PC7WVJ4H5MD";

    public static Uri Site { get; } = ReadSite();

    /// <summary>The API is served from the site's own origin.</summary>
    public static Uri Api => Site;

    public static Uri Library => Site;

    public static Uri Plans => new(Site, "/plan");

    public static Uri AddGame => Site;

    public static Uri Clip(string bookSlug, Guid clipId) =>
        new(Site, $"/games/{Uri.EscapeDataString(bookSlug)}/{clipId}");

    public static Uri Store => new($"ms-windows-store://pdp/?productid={StoreProductId}");

    /// <summary>"reelshelf.app/device", as S1 names the page.</summary>
    public static string DevicePageName => $"{Site.Host}{(Site.IsDefaultPort ? "" : $":{Site.Port}")}/device";

    private static Uri ReadSite()
    {
#if DEBUG
        string? configured = Environment.GetEnvironmentVariable("REELSHELF_API_BASE_URL");
        if (!string.IsNullOrWhiteSpace(configured) && Uri.TryCreate(configured.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return new Uri(uri.GetLeftPart(UriPartial.Authority));
        }
#endif
        return new Uri(ProductionSite);
    }
}

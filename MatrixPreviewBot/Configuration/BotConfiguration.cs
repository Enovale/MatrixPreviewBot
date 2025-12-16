namespace MatrixPreviewBot.Configuration;

public class BotConfiguration
{
    public BotConfiguration(IConfiguration config) => config.GetRequiredSection("UrlPreviewBot").Bind(this);

    public string UserAgent { get; init; } =
        "Mozilla/5.0 (compatible; MatrixPreviewBot; +https://github.com/Enovale/MatrixPreviewBot; embed bot; like Discordbot)";

    public string? DecryptedHomeserverUrl { get; init; }
    public bool DeleteOriginalIfEmpty { get; set; } = true;

    public RegexEntry[] SiteReplacements { get; set; } = [];
}
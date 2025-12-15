using System.Text.RegularExpressions;

namespace MatrixPreviewBot.Configuration;

public class BotConfiguration
{
    public BotConfiguration(IConfiguration config) => config.GetRequiredSection("UrlPreviewBot").Bind(this);

    public string? DecryptedHomeserverUrl { get; init; }
    public bool DeleteOriginalIfEmpty { get; set; } = true;

    public RegexEntry[] SiteReplacements { get; set; } = [];
}
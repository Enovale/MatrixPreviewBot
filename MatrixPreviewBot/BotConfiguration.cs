namespace MatrixPreviewBot;

public class BotConfiguration
{
    public BotConfiguration(IConfiguration config) => config.GetRequiredSection("UrlPreviewBot").Bind(this);

    public bool DeleteOriginalIfEmpty { get; set; } = true;
    public string? DecryptedHomeserverUrl { get; init; }
}
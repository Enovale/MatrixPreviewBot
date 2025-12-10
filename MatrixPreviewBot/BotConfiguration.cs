using Microsoft.Extensions.Configuration;

namespace MatrixPreviewBot;

public class BotConfiguration
{
    public BotConfiguration(IConfiguration config) => config.GetRequiredSection("UrlPreviewBot").Bind(this);
}
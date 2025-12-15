using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace MatrixPreviewBot.Configuration;

[UsedImplicitly]
[Serializable]
public class RegexEntry
{
    [JsonIgnore]
    public Regex MatchRegex
    {
        get
        {
            _cachedMatch ??= new Regex(Match, RegexOptions.Compiled);

            return _cachedMatch;
        }
    }

    private Regex? _cachedMatch;
    
    public required string Match { get; set; }
    public required string Replace { get; set; }
}
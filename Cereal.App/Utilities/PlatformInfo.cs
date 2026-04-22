namespace Cereal.App.Utilities;

public static class PlatformInfo
{
    private static readonly Dictionary<string, (string Label, string Letter, string Color)> Data = new()
    {
        ["steam"]     = ("Steam",        "S",  "#66c0f4"),
        ["epic"]      = ("Epic Games",   "E",  "#a0a0a0"),
        ["gog"]       = ("GOG",          "G",  "#b44aff"),
        ["psn"]       = ("PlayStation",  "P",  "#0070d1"),
        ["xbox"]      = ("Xbox",         "X",  "#107c10"),
        ["custom"]    = ("Custom",       "C",  "#d4a853"),
        ["battlenet"] = ("Battle.net",   "B",  "#009ae5"),
        ["ea"]        = ("EA App",       "EA", "#f44040"),
        ["ubisoft"]   = ("Ubisoft",      "U",  "#0070ff"),
        ["itchio"]    = ("itch.io",      "i",  "#fa5c5c"),
    };

    public static string GetLabel(string? platform) =>
        platform is not null && Data.TryGetValue(platform, out var d) ? d.Label : platform ?? "Custom";

    public static string GetLetter(string? platform) =>
        platform is not null && Data.TryGetValue(platform, out var d) ? d.Letter
        : platform is { Length: > 0 } p ? p[0].ToString().ToUpperInvariant() : "?";

    public static string GetColor(string? platform) =>
        platform is not null && Data.TryGetValue(platform, out var d) ? d.Color : "#888888";

    public static IEnumerable<string> KnownPlatforms => Data.Keys;
}

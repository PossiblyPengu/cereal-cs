namespace Cereal.App.Utilities;

public static class PlatformInfo
{
    private static readonly Dictionary<string, (string Label, string Color)> Data = new()
    {
        ["steam"]     = ("Steam",        "#66c0f4"),
        ["epic"]      = ("Epic Games",   "#a0a0a0"),
        ["gog"]       = ("GOG",          "#b44aff"),
        ["psn"]       = ("PlayStation",  "#0070d1"),
        ["xbox"]      = ("Xbox",         "#107c10"),
        ["custom"]    = ("Custom",       "#d4a853"),
        ["battlenet"] = ("Battle.net",   "#009ae5"),
        ["ea"]        = ("EA App",       "#f44040"),
        ["ubisoft"]   = ("Ubisoft",      "#0070ff"),
        ["itchio"]    = ("itch.io",      "#fa5c5c"),
    };

    public static string GetLabel(string? platform) =>
        platform is not null && Data.TryGetValue(platform, out var d) ? d.Label : platform ?? "Custom";

    public static string GetColor(string? platform) =>
        platform is not null && Data.TryGetValue(platform, out var d) ? d.Color : "#888888";

    public static IEnumerable<string> KnownPlatforms => Data.Keys;
}

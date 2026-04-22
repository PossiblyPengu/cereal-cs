using System.Text.RegularExpressions;
using Cereal.App.Models;

namespace Cereal.App.Services.Providers;

public static partial class ProviderUtils
{
    public static string Canonicalize(string name) =>
        NonAlphanumeric().Replace(name.ToLowerInvariant(), "").Trim();

    public static bool IsDlcTitle(string title) =>
        DlcKeywords().IsMatch(title);

    public static Game? FindExisting(DatabaseService db, string platform, string platformId, string name)
    {
        var byId = db.Db.Games.FirstOrDefault(g =>
            g.Platform == platform && g.PlatformId == platformId);
        if (byId is not null) return byId;

        var canonical = Canonicalize(name);
        return db.Db.Games.FirstOrDefault(g =>
            g.Platform == platform && Canonicalize(g.Name) == canonical);
    }

    public static Game MakeGameEntry(string platform, string launcher, string name, string? platformId,
        string? coverUrl = null, string? headerUrl = null, int playtimeMinutes = 0,
        bool installed = false, Dictionary<string, object?>? extra = null)
    {
        var game = new Game
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = name,
            Platform = platform,
            PlatformId = platformId,
            CoverUrl = coverUrl,
            HeaderUrl = headerUrl,
            PlaytimeMinutes = playtimeMinutes > 0 ? playtimeMinutes : null,
            Installed = installed,
            AddedAt = DateTime.UtcNow.ToString("o"),
        };

        if (extra is not null)
        {
            if (extra.TryGetValue("executablePath", out var ep)) game.ExecutablePath = ep as string;
            if (extra.TryGetValue("streamUrl", out var su)) game.StreamUrl = su as string;
        }

        return game;
    }

    [GeneratedRegex(@"[^a-z0-9]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"\b(dlc|pack|bundle|edition|soundtrack|season pass|expansion)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DlcKeywords();
}

using System.Text.RegularExpressions;
using Cereal.App.Models;

namespace Cereal.App.Services.Providers;

public static partial class ProviderUtils
{
    /// <summary>Trim and treat blank as null so imports merge reliably.</summary>
    public static string? NormalizePlatformId(string? platformId)
    {
        if (string.IsNullOrWhiteSpace(platformId)) return null;
        return platformId.Trim();
    }

    public static string Canonicalize(string name)
    {
        var s = (name ?? string.Empty).ToLowerInvariant();
        // Match source behavior more closely: strip edition-style suffix noise
        // before alnum canonicalization so merges remain stable.
        s = EditionNoise().Replace(s, " ");
        return NonAlphanumeric().Replace(s, "").Trim();
    }

    public static bool IsDlcTitle(string title) =>
        DlcKeywords().IsMatch(title);

    public static Game? FindExisting(DatabaseService db, string platform, string platformId, string name)
    {
        var pid = NormalizePlatformId(platformId);
        if (pid is not null)
        {
            var byId = db.Db.Games.FirstOrDefault(g =>
                g.Platform == platform && NormalizePlatformId(g.PlatformId) == pid);
            if (byId is not null) return byId;
        }

        var canonical = Canonicalize(name);
        return db.Db.Games.FirstOrDefault(g =>
            g.Platform == platform && Canonicalize(g.Name) == canonical);
    }

    /// <summary>Fast merge lookups during bulk imports (Steam/Epic/GOG).</summary>
    public sealed class GameImportIndex
    {
        private readonly Dictionary<(string Plat, string Pid), Game> _byId = new();
        private readonly Dictionary<(string Plat, string Canon), Game> _byName = new();

        public static GameImportIndex FromGames(IEnumerable<Game> games)
        {
            var idx = new GameImportIndex();
            foreach (var g in games)
                idx.Track(g);
            return idx;
        }

        /// <summary>Register a game already in <see cref="Database.Games"/> or about to be added.</summary>
        public void Track(Game g)
        {
            var plat = g.Platform;
            if (NormalizePlatformId(g.PlatformId) is { } pid)
                _byId[(plat, pid)] = g;
            var canon = Canonicalize(g.Name);
            if (!_byName.ContainsKey((plat, canon)))
                _byName[(plat, canon)] = g;
        }

        public Game? Find(string platform, string? platformId, string name)
        {
            if (NormalizePlatformId(platformId) is { } pid &&
                _byId.TryGetValue((platform, pid), out var byId))
                return byId;
            var canon = Canonicalize(name);
            return _byName.TryGetValue((platform, canon), out var byName) ? byName : null;
        }
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
            PlatformId = NormalizePlatformId(platformId),
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

    // Keep "edition" out to avoid false-positive DLC tagging for base games.
    [GeneratedRegex(@"\b(dlc|pack|bundle|soundtrack|season pass|expansion)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DlcKeywords();

    [GeneratedRegex(@"\b(game of the year|goty|deluxe|ultimate|complete|collector'?s|definitive|premium|standard|enhanced|anniversary|remaster(ed)?|director'?s cut|edition)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EditionNoise();
}

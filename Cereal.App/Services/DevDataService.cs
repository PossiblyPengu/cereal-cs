using Cereal.App.Models;

namespace Cereal.App.Services;

/// <summary>
/// Seeds placeholder games for local development/testing when explicitly enabled.
/// </summary>
public sealed class DevDataService(DatabaseService db)
{
    private const string DevIdPrefix = "dev_";

    private sealed record SeedGame(
        string Name,
        string Platform,
        string? PlatformId,
        string? StoreUrl,
        string? Website,
        string[] Categories);

    // Real game titles so metadata/art lookup behaves like production data.
    private static readonly SeedGame[] Catalog =
    [
        new("Cyberpunk 2077", "steam", "1091500", "https://store.steampowered.com/app/1091500", "https://www.cyberpunk.net", ["RPG", "Open World"]),
        new("The Witcher 3: Wild Hunt", "steam", "292030", "https://store.steampowered.com/app/292030", "https://www.thewitcher.com", ["RPG", "Adventure"]),
        new("DOOM Eternal", "steam", "782330", "https://store.steampowered.com/app/782330", "https://slayersclub.bethesda.net", ["Shooter", "Action"]),
        new("Hades", "steam", "1145360", "https://store.steampowered.com/app/1145360", "https://www.supergiantgames.com/games/hades", ["Roguelike", "Action"]),
        new("Baldur's Gate 3", "steam", "1086940", "https://store.steampowered.com/app/1086940", "https://baldursgate3.game", ["RPG", "Strategy"]),
        new("Red Dead Redemption 2", "epic", "Heather", "https://store.epicgames.com/p/red-dead-redemption-2", "https://www.rockstargames.com/reddeadredemption2", ["Action", "Open World"]),
        new("Fortnite", "epic", "Fortnite", "https://store.epicgames.com/p/fortnite", "https://www.fortnite.com", ["Shooter", "Multiplayer"]),
        new("Alan Wake 2", "epic", "Sand", "https://store.epicgames.com/p/alan-wake-2", "https://www.alanwake.com", ["Horror", "Adventure"]),
        new("Control Ultimate Edition", "gog", "1207658680", "https://www.gog.com/en/game/control_ultimate_edition", "https://www.remedygames.com/games/control", ["Action", "Sci-Fi"]),
        new("Disco Elysium - The Final Cut", "gog", "1435827233", "https://www.gog.com/en/game/disco_elysium", "https://discoelysium.com", ["RPG", "Narrative"]),
        new("Stardew Valley", "gog", "1453375253", "https://www.gog.com/en/game/stardew_valley", "https://www.stardewvalley.net", ["Indie", "Simulation"]),
        new("Dead Space", "ea", "dead-space", null, "https://www.ea.com/games/dead-space", ["Horror", "Shooter"]),
        new("EA SPORTS FC 24", "ea", "ea-sports-fc-24", null, "https://www.ea.com/games/ea-sports-fc/fc-24", ["Sports", "Multiplayer"]),
        new("Overwatch 2", "battlenet", "pro", null, "https://overwatch.blizzard.com", ["Shooter", "Multiplayer"]),
        new("Diablo IV", "battlenet", "fen", null, "https://diablo4.blizzard.com", ["RPG", "Action"]),
        new("Tom Clancy's Rainbow Six Siege", "ubisoft", "635", null, "https://www.ubisoft.com/game/rainbow-six/siege", ["Shooter", "Tactical"]),
        new("Assassin's Creed Valhalla", "ubisoft", "13504", null, "https://www.ubisoft.com/game/assassins-creed/valhalla", ["Action", "RPG"]),
        new("Celeste", "itchio", "celeste", "https://mattmakesgames.itch.io/celeste", "https://www.celestegame.com", ["Platformer", "Indie"]),
        new("Ultrakill", "itchio", "ultrakill", "https://hakita.itch.io/ultrakill", "https://store.steampowered.com/app/1229490", ["Shooter", "Indie"]),
        new("Halo Infinite", "xbox", "2043073184", "https://www.xbox.com/games/store/halo-infinite/9PP5G1F0C2B6", "https://www.halowaypoint.com", ["Shooter", "Multiplayer"]),
        new("Forza Horizon 5", "xbox", "1551193120", "https://www.xbox.com/games/store/forza-horizon-5/9NKX70BBCDRN", "https://forza.net", ["Racing", "Open World"]),
    ];

    public int SeedPlaceholders(int count, bool force)
    {
        if (count <= 0) return 0;

        if (!force && db.Db.Games.Any(g => g.Id.StartsWith(DevIdPrefix, StringComparison.Ordinal)))
            return 0;

        if (force)
        {
            db.Db.Games.RemoveAll(g => g.Id.StartsWith(DevIdPrefix, StringComparison.Ordinal));
        }

        var rng = new Random(1337);
        var now = DateTimeOffset.UtcNow;
        var inserted = 0;

        for (var i = 0; i < count; i++)
        {
            var baseGame = Catalog[i % Catalog.Length];
            var cycle = i / Catalog.Length;
            var name = cycle == 0 ? baseGame.Name : $"{baseGame.Name} ({cycle + 1})";
            var platformId = cycle == 0
                ? baseGame.PlatformId
                : string.IsNullOrWhiteSpace(baseGame.PlatformId)
                    ? $"dev-{baseGame.Platform}-{i + 1:000}"
                    : $"{baseGame.PlatformId}-{cycle + 1}";

            var game = new Game
            {
                Id = $"{DevIdPrefix}{baseGame.Platform}_{i + 1:000}",
                Name = name,
                Platform = baseGame.Platform,
                PlatformId = platformId,
                AddedAt = now.AddMinutes(-i * 47).ToString("o"),
                LastPlayed = i % 3 == 0 ? now.AddDays(-(i % 21)).ToString("o") : null,
                PlaytimeMinutes = rng.Next(15, 4200),
                Favorite = i % 7 == 0,
                Installed = i % 5 != 0,
                Hidden = false,
                IsCustom = false,
                Categories = baseGame.Categories.ToList(),
                StoreUrl = baseGame.StoreUrl,
                Website = baseGame.Website,
                Notes = "DEV_PLACEHOLDER",
                Description = "Development placeholder game seeded from a real title for metadata/artwork testing.",
            };

            db.Db.Games.Add(game);
            inserted++;
        }

        if (inserted > 0) db.Save();
        return inserted;
    }

    public int ClearPlaceholders()
    {
        var removed = db.Db.Games.RemoveAll(g => g.Id.StartsWith(DevIdPrefix, StringComparison.Ordinal));
        if (removed > 0) db.Save();
        return removed;
    }
}


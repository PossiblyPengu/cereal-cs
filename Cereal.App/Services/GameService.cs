using Cereal.App.Models;
using Cereal.App.Services.Providers;

namespace Cereal.App.Services;

public class GameService
{
    private readonly DatabaseService _db;

    public GameService(DatabaseService db) => _db = db;

    /// <summary>Fired after any mutation that changes the game list or rows (UI should refresh).</summary>
    public event EventHandler? LibraryChanged;

    private void NotifyLibraryChanged() => LibraryChanged?.Invoke(this, EventArgs.Empty);

    public List<Game> GetAll() => _db.Db.Games;

    /// <summary>Remove every game and playtime entry in one save (e.g. settings “clear library”).</summary>
    public void ClearLibrary()
    {
        _db.Db.Games.Clear();
        _db.Db.Playtime.Clear();
        _db.Save();
        NotifyLibraryChanged();
    }

    public Game Add(Game game)
    {
        var survivor = Upsert(game);
        _db.Save();
        NotifyLibraryChanged();
        return survivor;
    }

    /// <summary>Add or merge many games with a single disk flush (debounced save still applies once).</summary>
    public (int Processed, int NewRows) AddRange(IEnumerable<Game> games)
    {
        var list = games as IList<Game> ?? games.ToList();
        var before = _db.Db.Games.Count;
        foreach (var g in list)
            Upsert(g);
        _db.Save();
        NotifyLibraryChanged();
        return (list.Count, _db.Db.Games.Count - before);
    }

    /// <returns>The row that holds the merged data (existing or the newly added <paramref name="game"/>).</returns>
    private Game Upsert(Game game)
    {
        if (string.IsNullOrEmpty(game.Id))
            game.Id = Guid.NewGuid().ToString("N")[..12];

        game.PlatformId = ProviderUtils.NormalizePlatformId(game.PlatformId);
        game.AddedAt ??= DateTime.UtcNow.ToString("o");

        if (!string.IsNullOrEmpty(game.PlatformId))
        {
            var existing = _db.Db.Games.FirstOrDefault(g =>
                g.Platform == game.Platform &&
                ProviderUtils.NormalizePlatformId(g.PlatformId) == game.PlatformId);
            if (existing is not null)
            {
                MergeInto(existing, game);
                return existing;
            }
        }

        if (!string.IsNullOrWhiteSpace(game.Name))
        {
            var incomingCanon = ProviderUtils.Canonicalize(game.Name);
            var byName = _db.Db.Games.FirstOrDefault(g =>
                g.Platform == game.Platform &&
                !string.IsNullOrWhiteSpace(g.Name) &&
                ProviderUtils.Canonicalize(g.Name) == incomingCanon);
            if (byName is not null)
            {
                MergeInto(byName, game);
                return byName;
            }
        }

        _db.Db.Games.Add(game);
        return game;
    }

    private static void MergeInto(Game target, Game incoming)
    {
        target.Name = incoming.Name;
        target.PlatformId ??= incoming.PlatformId;
        target.CoverUrl ??= incoming.CoverUrl;
        target.HeaderUrl ??= incoming.HeaderUrl;
        target.LocalCoverPath ??= incoming.LocalCoverPath;
        target.LocalHeaderPath ??= incoming.LocalHeaderPath;
        target.StoreUrl ??= incoming.StoreUrl;
        target.StreamUrl ??= incoming.StreamUrl;
        target.ExecutablePath ??= incoming.ExecutablePath;
        target.Installed = incoming.Installed ?? target.Installed;
        if (incoming.PlaytimeMinutes is int inc && inc > (target.PlaytimeMinutes ?? 0))
            target.PlaytimeMinutes = inc;
        if (incoming.Categories is { Count: > 0 })
        {
            target.Categories ??= [];
            foreach (var c in incoming.Categories)
            {
                if (!target.Categories.Contains(c, StringComparer.OrdinalIgnoreCase))
                    target.Categories.Add(c);
            }
        }
    }

    public Game Update(Game updated)
    {
        updated.PlatformId = ProviderUtils.NormalizePlatformId(updated.PlatformId);
        var idx = _db.Db.Games.FindIndex(g => g.Id == updated.Id);
        if (idx < 0) throw new KeyNotFoundException($"Game {updated.Id} not found");
        _db.Db.Games[idx] = updated;
        _db.Save();
        NotifyLibraryChanged();
        return updated;
    }

    public void Delete(string id)
    {
        _db.Db.Games.RemoveAll(g => g.Id == id);
        _db.Db.Playtime.Remove(id);
        _db.Save();
        NotifyLibraryChanged();
    }

    public Game ToggleFavorite(string id)
    {
        var game = _db.Db.Games.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException($"Game {id} not found");
        game.Favorite = !(game.Favorite ?? false);
        _db.Save();
        NotifyLibraryChanged();
        return game;
    }

    public Game SetFavorite(string id, bool value)
    {
        var game = _db.Db.Games.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException($"Game {id} not found");
        game.Favorite = value;
        _db.Save();
        NotifyLibraryChanged();
        return game;
    }

    public Game SetHidden(string id, bool value)
    {
        var game = _db.Db.Games.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException($"Game {id} not found");
        game.Hidden = value;
        _db.Save();
        NotifyLibraryChanged();
        return game;
    }

    public void RecordPlaySession(string id, int minutes)
    {
        _db.Db.Playtime.TryGetValue(id, out var prev);
        _db.Db.Playtime[id] = prev + minutes;

        var game = _db.Db.Games.FirstOrDefault(g => g.Id == id);
        if (game is not null)
        {
            game.PlaytimeMinutes = (game.PlaytimeMinutes ?? 0) + minutes;
            game.LastPlayed = DateTime.UtcNow.ToString("o");
        }
        _db.Save();
        NotifyLibraryChanged();
    }

    public List<string> GetCategories() => _db.Db.Categories;

    public List<string> AddCategory(string name)
    {
        if (!_db.Db.Categories.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _db.Db.Categories.Add(name);
            _db.Save();
            NotifyLibraryChanged();
        }
        return _db.Db.Categories;
    }

    public List<string> RemoveCategory(string name)
    {
        _db.Db.Categories.RemoveAll(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        // Remove from all games too
        foreach (var g in _db.Db.Games)
            g.Categories?.RemoveAll(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        _db.Save();
        NotifyLibraryChanged();
        return _db.Db.Categories;
    }
}

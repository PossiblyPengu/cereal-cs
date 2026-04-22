using Cereal.App.Models;

namespace Cereal.App.Services;

public class GameService
{
    private readonly DatabaseService _db;

    public GameService(DatabaseService db) => _db = db;

    public List<Game> GetAll() => _db.Db.Games;

    public Game Add(Game game)
    {
        if (string.IsNullOrEmpty(game.Id))
            game.Id = Guid.NewGuid().ToString("N")[..12];

        game.AddedAt ??= DateTime.UtcNow.ToString("o");

        // Deduplicate: same platform + platformId
        if (!string.IsNullOrEmpty(game.PlatformId))
        {
            var existing = _db.Db.Games.FirstOrDefault(
                g => g.Platform == game.Platform && g.PlatformId == game.PlatformId);
            if (existing is not null)
            {
                // Merge — preserve user customizations
                existing.Name = game.Name;
                existing.CoverUrl ??= game.CoverUrl;
                existing.HeaderUrl ??= game.HeaderUrl;
                existing.Installed = game.Installed;
                _db.Save();
                return existing;
            }
        }

        _db.Db.Games.Add(game);
        _db.Save();
        return game;
    }

    public Game Update(Game updated)
    {
        var idx = _db.Db.Games.FindIndex(g => g.Id == updated.Id);
        if (idx < 0) throw new KeyNotFoundException($"Game {updated.Id} not found");
        _db.Db.Games[idx] = updated;
        _db.Save();
        return updated;
    }

    public void Delete(string id)
    {
        _db.Db.Games.RemoveAll(g => g.Id == id);
        _db.Db.Playtime.Remove(id);
        _db.Save();
    }

    public Game ToggleFavorite(string id)
    {
        var game = _db.Db.Games.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException($"Game {id} not found");
        game.Favorite = !(game.Favorite ?? false);
        _db.Save();
        return game;
    }

    public Game SetFavorite(string id, bool value)
    {
        var game = _db.Db.Games.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException($"Game {id} not found");
        game.Favorite = value;
        _db.Save();
        return game;
    }

    public Game SetHidden(string id, bool value)
    {
        var game = _db.Db.Games.FirstOrDefault(g => g.Id == id)
            ?? throw new KeyNotFoundException($"Game {id} not found");
        game.Hidden = value;
        _db.Save();
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
    }

    public List<string> GetCategories() => _db.Db.Categories;

    public List<string> AddCategory(string name)
    {
        if (!_db.Db.Categories.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _db.Db.Categories.Add(name);
            _db.Save();
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
        return _db.Db.Categories;
    }
}

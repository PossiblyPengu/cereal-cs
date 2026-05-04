using Cereal.Core.Models;

namespace Cereal.Core.Services;

public interface ISettingsService
{
    Settings Current { get; }

    Task<Settings> LoadAsync(CancellationToken ct = default);

    /// <summary>Persist settings and fire <see cref="Messaging.SettingsMessages.SettingsChangedMessage"/>.</summary>
    Task SaveAsync(Settings settings, CancellationToken ct = default);
}

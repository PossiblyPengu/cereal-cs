namespace Cereal.Infrastructure.Services;

public sealed class SettingsService(
    ISettingsRepository repo,
    IMessenger messenger) : ISettingsService
{
    private Settings _current = new();

    public Settings Current => _current;

    public async Task<Settings> LoadAsync(CancellationToken ct = default)
    {
        _current = await repo.LoadAsync(ct);
        return _current;
    }

    public async Task SaveAsync(Settings settings, CancellationToken ct = default)
    {
        _current = settings;
        await repo.SaveAsync(settings, ct);
        messenger.Send(new SettingsChangedMessage(settings));
    }
}

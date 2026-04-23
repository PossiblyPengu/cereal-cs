using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cereal.App.Services.Integrations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace Cereal.App.ViewModels;

public partial class MediaViewModel : ObservableObject, IDisposable
{
    private readonly SmtcService _smtc;
    private readonly CancellationTokenSource _cts = new();

    // Poll cadence matches the Electron version (3 s) so now-playing updates feel
    // close to real-time without hammering the WinRT bridge.
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    [ObservableProperty] private string? _title;
    [ObservableProperty] private string? _artist;
    [ObservableProperty] private string? _album;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private double _position;
    [ObservableProperty] private double _duration;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private Bitmap? _albumArt;

    // User toggle — the media widget starts expanded and can be collapsed to a
    // compact play-button-only pill.
    [ObservableProperty] private bool _isCollapsed;

    public MediaViewModel(SmtcService smtc)
    {
        _smtc = smtc;
        _ = PollAsync(_cts.Token);
    }

    private string? _lastArtKey;

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var info = await _smtc.GetMediaInfoAsync(ct);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HasMedia       = info is not null && !string.IsNullOrEmpty(info.Title);
                    Title          = info?.Title;
                    Artist         = info?.Artist;
                    Album          = info?.Album;
                    IsPlaying      = info?.IsPlaying ?? false;
                    Position       = info?.Position ?? 0.0;
                    Duration       = info?.Duration ?? 0.0;
                    ProgressPercent = Duration > 0 ? (Position / Duration) * 100.0 : 0.0;
                    UpdateAlbumArt(info?.AlbumArtUrl);
                });
            }
            catch (OperationCanceledException) { break; }
            catch { /* non-fatal */ }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    // Decodes the MediaInfoTool's albumArt payload, which may be a file path, an
    // absolute URL, or a base64 "data:image/…" data URL. We avoid re-decoding on
    // every poll by hashing the payload.
    private void UpdateAlbumArt(string? payload)
    {
        var key = payload ?? "";
        if (key == _lastArtKey) return;
        _lastArtKey = key;

        AlbumArt?.Dispose();
        AlbumArt = null;

        if (string.IsNullOrWhiteSpace(payload)) return;

        try
        {
            if (payload.StartsWith("data:", StringComparison.Ordinal))
            {
                var comma = payload.IndexOf(',');
                if (comma < 0) return;
                var bytes = Convert.FromBase64String(payload[(comma + 1)..]);
                using var ms = new MemoryStream(bytes);
                AlbumArt = new Bitmap(ms);
            }
            else if (File.Exists(payload))
            {
                AlbumArt = new Bitmap(payload);
            }
            // http(s):// URLs left for the host's Image source binding to fetch.
        }
        catch (Exception ex) { Log.Debug(ex, "[media] AlbumArt decode failed"); }
    }

    [RelayCommand] private void PlayPause() => _smtc.SendMediaKey("playpause");
    [RelayCommand] private void Next()      => _smtc.SendMediaKey("next");
    [RelayCommand] private void Prev()      => _smtc.SendMediaKey("prev");
    [RelayCommand] private void ToggleCollapse() => IsCollapsed = !IsCollapsed;

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}

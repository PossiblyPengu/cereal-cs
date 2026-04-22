using Avalonia.Threading;
using Cereal.App.Services.Integrations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cereal.App.ViewModels;

public partial class MediaViewModel : ObservableObject, IDisposable
{
    private readonly SmtcService _smtc;
    private readonly CancellationTokenSource _cts = new();

    [ObservableProperty] private string? _title;
    [ObservableProperty] private string? _artist;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private bool _hasMedia;
    [ObservableProperty] private double _progressPercent;

    public MediaViewModel(SmtcService smtc)
    {
        _smtc = smtc;
        _ = PollAsync(_cts.Token);
    }

    private async Task PollAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var info = await _smtc.GetMediaInfoAsync(ct);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HasMedia  = info is not null && !string.IsNullOrEmpty(info.Title);
                    Title     = info?.Title;
                    Artist    = info?.Artist;
                    IsPlaying = info?.IsPlaying ?? false;
                });
            }
            catch (OperationCanceledException) { break; }
            catch { /* non-fatal */ }

            try { await Task.Delay(5_000, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    [RelayCommand] private void PlayPause() => _smtc.SendMediaKey("playpause");
    [RelayCommand] private void Next()      => _smtc.SendMediaKey("next");
    [RelayCommand] private void Prev()      => _smtc.SendMediaKey("prev");

    public void Dispose() { _cts.Cancel(); _cts.Dispose(); }
}

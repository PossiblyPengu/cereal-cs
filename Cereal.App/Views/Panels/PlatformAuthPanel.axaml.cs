using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Cereal.App.Controls;
using Cereal.App.ViewModels;
using Serilog;

namespace Cereal.App.Views.Panels;

public partial class PlatformAuthPanel : UserControl
{
    private WebView2Host? _web;
    private MainViewModel? _vm;

    public PlatformAuthPanel()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Detach();

    private void Attach(MainViewModel vm)
    {
        Detach();
        _vm = vm;
        _vm.PropertyChanged += OnMainVmPropertyChanged;
        PostRefreshWeb();
    }

    private void Detach()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnMainVmPropertyChanged;
        _vm = null;
        ClearWeb();
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PlatformAuthUrl) or
            nameof(MainViewModel.ShowPlatformAuth))
        {
            PostRefreshWeb();
        }
    }

    private void PostRefreshWeb() =>
        Dispatcher.UIThread.Post(RefreshWeb, DispatcherPriority.Loaded);

    private void ClearWeb()
    {
        var host = this.FindControl<Border>("WebHost");
        if (host is null) return;
        if (_web is not null)
        {
            _web = null;
        }
        host.Child = null;
    }

    private void RefreshWeb()
    {
        var host = this.FindControl<Border>("WebHost");
        if (host is null) return;
        if (_vm is null || !_vm.ShowPlatformAuth || string.IsNullOrEmpty(_vm.PlatformAuthUrl))
        {
            ClearWeb();
            return;
        }

        Uri uri;
        try { uri = new Uri(_vm.PlatformAuthUrl!); }
        catch (Exception ex)
        {
            Log.Debug(ex, "[auth] Invalid platform auth URL: {Url}", _vm.PlatformAuthUrl);
            ClearWeb();
            return;
        }

        if (_web is null)
        {
            _web = new WebView2Host
            {
                Source = uri,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            host.Child = _web;
        }
        else
        {
            _web.Source = uri;
        }
    }

    private void OpenBrowser_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var url = _vm?.PlatformAuthUrl;
        if (string.IsNullOrEmpty(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[auth] Open in browser failed");
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
        {
            Detach();
            if (DataContext is MainViewModel m)
                Attach(m);
        }
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
}

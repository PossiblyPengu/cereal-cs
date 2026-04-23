using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Cereal.App.Services.Providers;
using Cereal.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Cereal.App.Views.Panels;

public partial class SettingsPanel : UserControl
{
    public SettingsPanel()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(
            App.Services.GetRequiredService<SettingsService>(),
            App.Services.GetRequiredService<DiscordService>(),
            App.Services.GetRequiredService<CoverService>(),
            App.Services.GetRequiredService<CredentialService>(),
            App.Services.GetRequiredService<ThemeService>(),
            App.Services.GetRequiredService<GameService>(),
            App.Services.GetRequiredService<UpdateService>(),
            App.Services.GetRequiredService<IEnumerable<IProvider>>());
    }

    // Called when the user clicks a theme swatch button. The Button.Tag holds the theme Id.
    private void ThemeSwatch_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string themeId
            && DataContext is SettingsViewModel vm)
        {
            vm.Theme = themeId;
        }
    }

    // Shows the accent preview square color from the current AccentColor text.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel vm)
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(SettingsViewModel.AccentColor))
                    UpdateAccentPreview(vm.AccentColor);
            };
    }

    private void UpdateAccentPreview(string? hex)
    {
        if (AccentPreview is null) return;
        if (!string.IsNullOrWhiteSpace(hex) && Color.TryParse(hex, out var c))
            AccentPreview.Background = new SolidColorBrush(c);
        else
            AccentPreview.Background = Brushes.Transparent;
    }

    private void AccentPreview_Click(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // No native color picker in Avalonia yet — user types the hex in the TextBox
    }

    private void ResetAccent_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
            vm.AccentColor = string.Empty;
    }

    private async void PasteSgdbKey_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var txt = (await App.ReadClipboardTextAsync()).Trim();
        if (!string.IsNullOrEmpty(txt)) vm.SteamGridDbKey = txt;
    }

    private async void ExportLibrary_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Library",
            SuggestedFileName = "cereal-library.json",
            FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (file is null) return;
        await vm.ExportLibraryAsync(file.Path.LocalPath);
    }

    // Opens the Platforms side panel from the Library action grid.
    private void OpenPlatforms_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            && d.MainWindow?.DataContext is MainViewModel mvm)
        {
            mvm.OpenPlatformsCommand.Execute(null);
        }
    }

    private async void ImportLibrary_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import Library",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0) return;
        await vm.ImportLibraryAsync(files[0].Path.LocalPath);
    }
}

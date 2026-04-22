using Avalonia.Controls;
using Avalonia.Platform.Storage;
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

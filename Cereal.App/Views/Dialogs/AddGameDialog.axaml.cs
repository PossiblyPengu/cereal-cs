using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Cereal.App.Models;
using Cereal.App.Services;
using Cereal.App.Services.Integrations;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views.Dialogs;

/// <summary>Result returned from ShowDialog — null means cancelled.</summary>
public sealed class AddGameResult
{
    public required Game Game { get; init; }
}

public partial class AddGameDialog : Window
{
    private readonly CoverService _covers;
    private readonly GameService _games;

    public AddGameDialog()
    {
        InitializeComponent();
        _covers = App.Services.GetRequiredService<CoverService>();
        _games = App.Services.GetRequiredService<GameService>();
    }

    // Pre-fill from an existing game (edit mode)
    public void LoadGame(Game g)
    {
        this.FindControl<TextBox>("NameBox")!.Text = g.Name;
        this.FindControl<TextBox>("ExeBox")!.Text = g.ExecutablePath ?? "";
        this.FindControl<TextBox>("CoverBox")!.Text = g.CoverUrl ?? "";
        this.FindControl<TextBox>("SteamIdBox")!.Text =
            g.Platform == "steam" ? g.PlatformId ?? "" : "";

        var platformBox = this.FindControl<ComboBox>("PlatformBox")!;
        var match = platformBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == g.Platform);
        if (match is not null) platformBox.SelectedItem = match;

        if (!string.IsNullOrEmpty(g.CoverUrl))
            _ = LoadPreviewAsync(g.CoverUrl);
    }

    private async void BrowseExe_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select executable",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Executables") { Patterns = new[] { "*.exe", "*.sh", "*.AppImage", "*" } },
                }
            });
            if (files.Count > 0)
                this.FindControl<TextBox>("ExeBox")!.Text = files[0].Path.LocalPath;
        }
        catch (Exception ex) { Log.Warning(ex, "[AddGame] BrowseExe failed"); }
    }

    private async void PickArt_Click(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("NameBox")!.Text ?? "";
        var dlg = new ArtPickerDialog(name);
        var result = await dlg.ShowDialog<string?>(this);
        if (result is not null)
        {
            this.FindControl<TextBox>("CoverBox")!.Text = result;
            _ = LoadPreviewAsync(result);
        }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        var nameBox = this.FindControl<TextBox>("NameBox")!;
        var name = nameBox.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(name))
        {
            ShowError("Game name is required.");
            return;
        }

        var platform = (this.FindControl<ComboBox>("PlatformBox")!.SelectedItem as ComboBoxItem)
            ?.Content?.ToString() ?? "custom";
        var exe = this.FindControl<TextBox>("ExeBox")!.Text?.Trim();
        var cover = this.FindControl<TextBox>("CoverBox")!.Text?.Trim();
        var steamIdText = this.FindControl<TextBox>("SteamIdBox")!.Text?.Trim();

        var game = new Game
        {
            Id = Guid.NewGuid().ToString("N")[..12],
            Name = name,
            Platform = platform,
            ExecutablePath = string.IsNullOrEmpty(exe) ? null : exe,
            CoverUrl = string.IsNullOrEmpty(cover) ? null : cover,
            PlatformId = string.IsNullOrEmpty(steamIdText) ? null : steamIdText,
            IsCustom = platform == "custom",
            AddedAt = DateTime.UtcNow.ToString("o"),
            Installed = true,
        };

        // Queue cover download if a URL is set (will be stored after caller adds to GameService)
        // Caller is responsible for calling _covers.EnqueueGame(game.Id) after GameService.Add

        Close(new AddGameResult { Game = game });
    }

    private void ShowError(string msg)
    {
        var tb = this.FindControl<TextBlock>("ErrorText")!;
        tb.Text = msg;
        tb.IsVisible = true;
    }

    private async Task LoadPreviewAsync(string url)
    {
        try
        {
            using var http = new System.Net.Http.HttpClient();
            var bytes = await http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            this.FindControl<Image>("CoverPreview")!.Source = bmp;
        }
        catch { /* preview is best-effort */ }
    }
}

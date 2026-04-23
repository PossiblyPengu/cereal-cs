using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Cereal.App.Models;
using Cereal.App.Services.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views.Dialogs;

public sealed class AddGameResult
{
    public required Game Game { get; init; }
}

public partial class AddGameDialog : Window
{
    private Game? _editGame;
    private bool _isEditMode;

    public AddGameDialog()
    {
        InitializeComponent();
    }

    public void LoadGame(Game g)
    {
        _editGame = g;
        _isEditMode = true;

        this.FindControl<TextBlock>("DialogTitle")!.Text = "Edit Game";
        this.FindControl<Button>("SaveBtn")!.Content = "Save Changes";

        this.FindControl<TextBox>("NameBox")!.Text = g.Name;
        this.FindControl<TextBox>("NotesBox")!.Text = g.Notes ?? "";
        this.FindControl<TextBox>("CoverBox")!.Text = g.CoverUrl ?? g.LocalCoverPath ?? "";
        this.FindControl<TextBox>("HeaderBox")!.Text = g.HeaderUrl ?? g.LocalHeaderPath ?? "";
        this.FindControl<TextBox>("PlatformIdBox")!.Text = g.PlatformId ?? "";
        this.FindControl<TextBox>("ExeBox")!.Text = g.ExecutablePath ?? "";

        // Metadata
        this.FindControl<TextBox>("DescBox")!.Text = g.Description ?? "";
        this.FindControl<TextBox>("DevBox")!.Text = g.Developer ?? "";
        this.FindControl<TextBox>("PubBox")!.Text = g.Publisher ?? "";
        this.FindControl<TextBox>("ReleaseDateBox")!.Text = g.ReleaseDate ?? "";
        this.FindControl<TextBox>("MetacriticBox")!.Text = g.Metacritic?.ToString() ?? "";
        this.FindControl<TextBox>("WebsiteBox")!.Text = g.Website ?? "";

        // Auto-expand metadata if any field set
        if (g.Description != null || g.Developer != null || g.Publisher != null ||
            g.ReleaseDate != null || g.Metacritic != null || g.Website != null)
            this.FindControl<Expander>("MetaExpander")!.IsExpanded = true;

        // Platform
        var platformBox = this.FindControl<ComboBox>("PlatformBox")!;
        var match = platformBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == g.Platform);
        if (match is not null) platformBox.SelectedItem = match;

        UpdatePlatformSections(g.Platform);

        if (!string.IsNullOrEmpty(g.CoverUrl) || !string.IsNullOrEmpty(g.LocalCoverPath))
            _ = LoadPreviewAsync(g.CoverUrl ?? g.LocalCoverPath!);
    }

    private void PlatformBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var platform = (this.FindControl<ComboBox>("PlatformBox")!.SelectedItem as ComboBoxItem)
            ?.Content?.ToString() ?? "custom";
        UpdatePlatformSections(platform);
    }

    private void UpdatePlatformSections(string? platform)
    {
        var showId = platform is "steam" or "epic" or "gog" or "battlenet" or "ea" or "ubisoft" or "itchio" or "xbox";
        var showExe = platform == "custom";
        this.FindControl<StackPanel>("PlatformIdSection")!.IsVisible = showId;
        this.FindControl<StackPanel>("ExeSection")!.IsVisible = showExe;
    }

    private async void BrowseExe_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select executable",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Executables") { Patterns = ["*.exe", "*.sh", "*.AppImage", "*"] }],
            });
            if (files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                this.FindControl<TextBox>("ExeBox")!.Text = path;
                // Auto-fill name if empty
                var nameBox = this.FindControl<TextBox>("NameBox")!;
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    var filename = System.IO.Path.GetFileNameWithoutExtension(path)
                        .Replace('-', ' ').Replace('_', ' ').Trim();
                    nameBox.Text = filename;
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[AddGame] BrowseExe failed"); }
    }

    private async void BrowseCover_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select cover image",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"] }],
            });
            if (files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                this.FindControl<TextBox>("CoverBox")!.Text = path;
                _ = LoadPreviewAsync(path);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[AddGame] BrowseCover failed"); }
    }

    private async void BrowseHeader_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select header image",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"] }],
            });
            if (files.Count > 0)
                this.FindControl<TextBox>("HeaderBox")!.Text = files[0].Path.LocalPath;
        }
        catch (Exception ex) { Log.Warning(ex, "[AddGame] BrowseHeader failed"); }
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

    private async void PickHeaderArt_Click(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("NameBox")!.Text ?? "";
        var dlg = new ArtPickerDialog(name);
        var result = await dlg.ShowDialog<string?>(this);
        if (result is not null)
            this.FindControl<TextBox>("HeaderBox")!.Text = result;
    }

    private void CoverBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var text = this.FindControl<TextBox>("CoverBox")!.Text;
        if (!string.IsNullOrWhiteSpace(text))
            _ = LoadPreviewAsync(text);
    }

    private async void FetchMeta_Click(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("NameBox")!.Text?.Trim() ?? "";
        var statusTb = this.FindControl<TextBlock>("FetchMetaStatus")!;
        var btn = this.FindControl<Button>("FetchMetaBtn")!;
        if (string.IsNullOrEmpty(name))
        {
            statusTb.Text = "Enter a name first";
            return;
        }

        try
        {
            btn.IsEnabled = false;
            statusTb.Text = "Fetching…";
            var platform = (this.FindControl<ComboBox>("PlatformBox")!.SelectedItem as ComboBoxItem)
                ?.Content?.ToString();

            var svc = App.Services.GetRequiredService<MetadataService>();
            var meta = await svc.FetchForNameAsync(name, platform);
            if (meta is null)
            {
                statusTb.Text = "No match found";
                return;
            }

            // Only overwrite fields the user hasn't already filled in.
            void Fill(string id, string? value)
            {
                if (string.IsNullOrEmpty(value)) return;
                var tb = this.FindControl<TextBox>(id)!;
                if (string.IsNullOrWhiteSpace(tb.Text)) tb.Text = value;
            }

            Fill("DescBox", meta.Description);
            Fill("DevBox", meta.Developer);
            Fill("PubBox", meta.Publisher);
            Fill("ReleaseDateBox", meta.ReleaseDate);
            Fill("WebsiteBox", meta.Website);
            if (meta.Metacritic is int mc) Fill("MetacriticBox", mc.ToString());

            var coverBox = this.FindControl<TextBox>("CoverBox")!;
            var cover = meta.SgdbCoverUrl ?? meta.CoverUrl;
            if (!string.IsNullOrEmpty(cover) && string.IsNullOrWhiteSpace(coverBox.Text))
            {
                coverBox.Text = cover;
                _ = LoadPreviewAsync(cover);
            }

            var headerBox = this.FindControl<TextBox>("HeaderBox")!;
            if (!string.IsNullOrEmpty(meta.HeaderUrl) && string.IsNullOrWhiteSpace(headerBox.Text))
                headerBox.Text = meta.HeaderUrl;

            this.FindControl<Expander>("MetaExpander")!.IsExpanded = true;
            statusTb.Text = $"Filled from {meta.Source}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AddGame] FetchMeta failed");
            statusTb.Text = "Fetch failed";
        }
        finally { btn.IsEnabled = true; }
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        var name = this.FindControl<TextBox>("NameBox")!.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            ShowError("Game name is required.");
            return;
        }

        var platform = (this.FindControl<ComboBox>("PlatformBox")!.SelectedItem as ComboBoxItem)
            ?.Content?.ToString() ?? "custom";
        var exe = this.FindControl<TextBox>("ExeBox")!.Text?.Trim();
        var cover = this.FindControl<TextBox>("CoverBox")!.Text?.Trim();
        var header = this.FindControl<TextBox>("HeaderBox")!.Text?.Trim();
        var platformId = this.FindControl<TextBox>("PlatformIdBox")!.Text?.Trim();
        var notes = this.FindControl<TextBox>("NotesBox")!.Text?.Trim();
        var desc = this.FindControl<TextBox>("DescBox")!.Text?.Trim();
        var dev = this.FindControl<TextBox>("DevBox")!.Text?.Trim();
        var pub = this.FindControl<TextBox>("PubBox")!.Text?.Trim();
        var releaseDate = this.FindControl<TextBox>("ReleaseDateBox")!.Text?.Trim();
        var mcText = this.FindControl<TextBox>("MetacriticBox")!.Text?.Trim();
        var website = this.FindControl<TextBox>("WebsiteBox")!.Text?.Trim();

        int? metacritic = null;
        if (!string.IsNullOrEmpty(mcText) && int.TryParse(mcText, out var mc))
            metacritic = Math.Clamp(mc, 0, 100);

        var game = _isEditMode && _editGame is not null
            ? _editGame
            : new Game
            {
                Id = Guid.NewGuid().ToString("N")[..12],
                AddedAt = DateTime.UtcNow.ToString("o"),
                Installed = true,
            };

        game.Name = name;
        game.Platform = platform;
        game.IsCustom = platform == "custom";
        game.ExecutablePath = string.IsNullOrEmpty(exe) ? null : exe;
        game.CoverUrl = string.IsNullOrEmpty(cover) ? null : cover;
        game.HeaderUrl = string.IsNullOrEmpty(header) ? null : header;
        game.PlatformId = string.IsNullOrEmpty(platformId) ? null : platformId;
        game.Notes = string.IsNullOrEmpty(notes) ? null : notes;
        game.Description = string.IsNullOrEmpty(desc) ? null : desc;
        game.Developer = string.IsNullOrEmpty(dev) ? null : dev;
        game.Publisher = string.IsNullOrEmpty(pub) ? null : pub;
        game.ReleaseDate = string.IsNullOrEmpty(releaseDate) ? null : releaseDate;
        game.Metacritic = metacritic;
        game.Website = string.IsNullOrEmpty(website) ? null : website;

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
            Bitmap? bmp;
            if (System.IO.File.Exists(url))
            {
                bmp = new Bitmap(url);
            }
            else
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromSeconds(10);
                var bytes = await http.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);
                bmp = new Bitmap(ms);
            }

            var preview = this.FindControl<Image>("CoverPreview")!;
            preview.Source = bmp;
            preview.IsVisible = true;
        }
        catch { /* preview is best-effort */ }
    }
}

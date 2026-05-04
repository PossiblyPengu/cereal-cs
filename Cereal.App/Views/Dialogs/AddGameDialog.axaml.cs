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

    private void NameBox_AttachedToVisualTree(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        NameBox.Focus();
        NameBox.SelectionStart = 0;
        NameBox.SelectionEnd = NameBox.Text?.Length ?? 0;
    }

    public void LoadGame(Game g)
    {
        _editGame   = g;
        _isEditMode = true;

        DialogTitle.Text    = "Edit Game";
        SaveBtn.Content     = "Save Changes";

        NameBox.Text        = g.Name;
        NotesBox.Text       = g.Notes ?? "";
        CoverBox.Text       = g.CoverUrl ?? g.LocalCoverPath ?? "";
        HeaderBox.Text      = g.HeaderUrl ?? g.LocalHeaderPath ?? "";
        PlatformIdBox.Text  = g.PlatformId ?? "";
        ExeBox.Text         = g.ExecutablePath ?? "";
        CategoriesBox.Text  = g.Categories is { Count: > 0 } ? string.Join(", ", g.Categories) : "";
        DescBox.Text        = g.Description ?? "";
        DevBox.Text         = g.Developer ?? "";
        PubBox.Text         = g.Publisher ?? "";
        ReleaseDateBox.Text = g.ReleaseDate ?? "";
        MetacriticBox.Text  = g.Metacritic?.ToString() ?? "";
        WebsiteBox.Text     = g.Website ?? "";

        if (g.Description != null || g.Developer != null || g.Publisher != null ||
            g.ReleaseDate != null || g.Metacritic != null || g.Website != null)
            MetaExpander.IsExpanded = true;

        var match = PlatformBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => i.Content?.ToString() == g.Platform);
        if (match is not null) PlatformBox.SelectedItem = match;

        UpdatePlatformSections(g.Platform);

        if (!string.IsNullOrEmpty(g.CoverUrl) || !string.IsNullOrEmpty(g.LocalCoverPath))
            _ = LoadPreviewAsync(g.CoverUrl ?? g.LocalCoverPath!, CoverPreview);
        if (!string.IsNullOrEmpty(g.HeaderUrl) || !string.IsNullOrEmpty(g.LocalHeaderPath))
            _ = LoadPreviewAsync(g.HeaderUrl ?? g.LocalHeaderPath!, HeaderPreview);
    }

    private void PlatformBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var platform = (PlatformBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "custom";
        UpdatePlatformSections(platform);
    }

    private void UpdatePlatformSections(string? platform)
    {
        PlatformIdSection.IsVisible = platform is "steam" or "epic" or "gog" or "battlenet" or "ea" or "ubisoft" or "itchio" or "xbox";
        ExeSection.IsVisible        = platform == "custom";
    }

    // ─── Browse file ──────────────────────────────────────────────────────────

    private async void BrowseCover_Click(object? sender, RoutedEventArgs e)  => await BrowseImageAsync(CoverBox, CoverPreview);
    private async void BrowseHeader_Click(object? sender, RoutedEventArgs e) => await BrowseImageAsync(HeaderBox, HeaderPreview);

    private async Task BrowseImageAsync(TextBox target, Image preview)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select image",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.webp"] }],
            });
            if (files.Count > 0)
            {
                target.Text = files[0].Path.LocalPath;
                _ = LoadPreviewAsync(target.Text, preview);
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[AddGame] BrowseImage failed"); }
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
                ExeBox.Text = files[0].Path.LocalPath;
                if (string.IsNullOrWhiteSpace(NameBox.Text))
                    NameBox.Text = System.IO.Path.GetFileNameWithoutExtension(ExeBox.Text)
                        .Replace('-', ' ').Replace('_', ' ').Trim();
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[AddGame] BrowseExe failed"); }
    }

    // ─── Art picker ───────────────────────────────────────────────────────────

    private async void PickArt_Click(object? sender, RoutedEventArgs e)       => await PickArtAsync(CoverBox, CoverPreview);
    private async void PickHeaderArt_Click(object? sender, RoutedEventArgs e) => await PickArtAsync(HeaderBox, HeaderPreview);

    private async Task PickArtAsync(TextBox target, Image preview)
    {
        var result = await new ArtPickerDialog(NameBox.Text ?? "").ShowDialog<string?>(this);
        if (result is not null)
        {
            target.Text = result;
            _ = LoadPreviewAsync(result, preview);
        }
    }

    // ─── Live preview on URL change ───────────────────────────────────────────

    private void CoverBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(CoverBox.Text)) _ = LoadPreviewAsync(CoverBox.Text, CoverPreview);
    }

    private void HeaderBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(HeaderBox.Text)) _ = LoadPreviewAsync(HeaderBox.Text, HeaderPreview);
    }

    // ─── Metadata auto-fill ───────────────────────────────────────────────────

    private async void FetchMeta_Click(object? sender, RoutedEventArgs e)
    {
        var name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) { FetchMetaStatus.Text = "Enter a name first"; return; }

        try
        {
            FetchMetaBtn.IsEnabled = false;
            FetchMetaStatus.Text   = "Fetching…";

            var platform = (PlatformBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var meta = await App.Services.GetRequiredService<MetadataService>().FetchForNameAsync(name, platform);
            if (meta is null) { FetchMetaStatus.Text = "No match found"; return; }

            void Fill(TextBox tb, string? value) { if (!string.IsNullOrEmpty(value) && string.IsNullOrWhiteSpace(tb.Text)) tb.Text = value; }

            Fill(DescBox,        meta.Description);
            Fill(DevBox,         meta.Developer);
            Fill(PubBox,         meta.Publisher);
            Fill(ReleaseDateBox, meta.ReleaseDate);
            Fill(WebsiteBox,     meta.Website);
            if (meta.Metacritic is int mc) Fill(MetacriticBox, mc.ToString());

            if (!string.IsNullOrEmpty(meta.CoverUrl) && string.IsNullOrWhiteSpace(CoverBox.Text))
            {
                CoverBox.Text = meta.CoverUrl;
                _ = LoadPreviewAsync(meta.CoverUrl, CoverPreview);
            }
            if (!string.IsNullOrEmpty(meta.HeaderUrl) && string.IsNullOrWhiteSpace(HeaderBox.Text))
            {
                HeaderBox.Text = meta.HeaderUrl;
                _ = LoadPreviewAsync(meta.HeaderUrl, HeaderPreview);
            }

            MetaExpander.IsExpanded = true;
            FetchMetaStatus.Text    = $"Filled from {meta.Source}";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[AddGame] FetchMeta failed");
            FetchMetaStatus.Text = "Fetch failed";
        }
        finally { FetchMetaBtn.IsEnabled = true; }
    }

    // ─── Save / Cancel ────────────────────────────────────────────────────────

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.IsVisible = false;
        var name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name)) { ShowError("Game name is required."); return; }

        var platform = (PlatformBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "custom";
        int? metacritic = null;
        if (int.TryParse(MetacriticBox.Text?.Trim(), out var mc))
            metacritic = Math.Clamp(mc, 0, 100);

        var game = _isEditMode && _editGame is not null
            ? _editGame
            : new Game { Id = Guid.NewGuid().ToString("N")[..12], AddedAt = DateTime.UtcNow.ToString("o"), Installed = true };

        game.Name           = name;
        game.Platform       = platform;
        game.IsCustom       = platform == "custom";
        game.ExecutablePath = ExeBox.Text?.Trim().NullIfEmpty();
        game.CoverUrl       = CoverBox.Text?.Trim().NullIfEmpty();
        game.HeaderUrl      = HeaderBox.Text?.Trim().NullIfEmpty();
        game.PlatformId     = PlatformIdBox.Text?.Trim().NullIfEmpty();
        game.Notes          = NotesBox.Text?.Trim().NullIfEmpty();
        game.Description    = DescBox.Text?.Trim().NullIfEmpty();
        game.Developer      = DevBox.Text?.Trim().NullIfEmpty();
        game.Publisher      = PubBox.Text?.Trim().NullIfEmpty();
        game.ReleaseDate    = ReleaseDateBox.Text?.Trim().NullIfEmpty();
        game.Website        = WebsiteBox.Text?.Trim().NullIfEmpty();
        game.Metacritic     = metacritic;
        game.Categories     = CategoriesBox.Text?.Trim() is { Length: > 0 } cats
            ? cats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Where(c => !string.IsNullOrWhiteSpace(c))
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .ToList()
            : [];

        Close(new AddGameResult { Game = game });
    }

    private void ShowError(string msg) { ErrorText.Text = msg; ErrorText.IsVisible = true; }

    // ─── Image preview loader ─────────────────────────────────────────────────

    private async Task LoadPreviewAsync(string url, Image preview)
    {
        try
        {
            Bitmap bmp;
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
            preview.Source    = bmp;
            preview.IsVisible = true;
        }
        catch (Exception ex) { Log.Debug(ex, "[AddGame] Preview load failed for {Url}", url); }
    }
}

file static class StringExt
{
    public static string? NullIfEmpty(this string? s) => string.IsNullOrEmpty(s) ? null : s;
}

using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Cereal.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cereal.App.Views.Dialogs;

public sealed class ArtResult
{
    public required string FullUrl { get; init; }
    public required string ThumbUrl { get; init; }
    public Bitmap? Thumb { get; set; }
}

public partial class ArtPickerDialog : Window
{
    private readonly CredentialService _creds;
    private readonly HttpClient _http;
    private readonly ObservableCollection<ArtResult> _results = [];
    private ArtResult? _selected;
    private string _initialQuery;

    // Parameterless ctor is required for Avalonia's runtime XAML loader.
    public ArtPickerDialog() : this("") { }

    public ArtPickerDialog(string initialQuery)
    {
        InitializeComponent();
        _initialQuery = initialQuery;
        _creds = App.Services.GetRequiredService<CredentialService>();
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", "cereal-launcher/1.0");
        _http.Timeout = TimeSpan.FromSeconds(20);

        var list = this.FindControl<ItemsControl>("ResultsList")!;
        list.ItemsSource = _results;

        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_initialQuery))
        {
            this.FindControl<TextBox>("SearchBox")!.Text = _initialQuery;
            await RunSearchAsync(_initialQuery);
        }
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return || e.Key == Key.Enter)
            _ = RunSearchAsync(this.FindControl<TextBox>("SearchBox")!.Text ?? "");
    }

    private void Search_Click(object? sender, RoutedEventArgs e)
        => _ = RunSearchAsync(this.FindControl<TextBox>("SearchBox")!.Text ?? "");

    private async Task RunSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;

        var apiKey = _creds.GetPassword("cereal", "steamgriddb_key");
        if (string.IsNullOrEmpty(apiKey))
        {
            SetStatus("No SteamGridDB API key set. Add one in Settings → SteamGridDB.");
            return;
        }

        SetLoading(true);
        _results.Clear();
        _selected = null;
        this.FindControl<Button>("UseButton")!.IsEnabled = false;

        try
        {
            // Step 1: search for game
            var searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(query)}";
            var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                SetStatus($"Search failed ({(int)resp.StatusCode}). Check your API key.");
                return;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var data = doc.RootElement.TryGetProperty("data", out var d) ? d : default;
            if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
            {
                SetStatus("No results found.");
                return;
            }

            var gameId = data[0].GetProperty("id").GetInt64();

            // Step 2: fetch grids (portrait covers)
            var gridsUrl = $"https://www.steamgriddb.com/api/v2/grids/game/{gameId}?dimensions=600x900&limit=20";
            var gridsReq = new HttpRequestMessage(HttpMethod.Get, gridsUrl);
            gridsReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var gridsResp = await _http.SendAsync(gridsReq);

            if (gridsResp.IsSuccessStatusCode)
            {
                using var gridsDoc = JsonDocument.Parse(await gridsResp.Content.ReadAsStringAsync());
                if (gridsDoc.RootElement.TryGetProperty("data", out var gdata))
                {
                    foreach (var item in gdata.EnumerateArray())
                    {
                        var fullUrl = item.TryGetProperty("url", out var u) ? u.GetString() : null;
                        var thumbUrl = item.TryGetProperty("thumb", out var t) ? t.GetString() : fullUrl;
                        if (fullUrl is null) continue;
                        _results.Add(new ArtResult { FullUrl = fullUrl, ThumbUrl = thumbUrl ?? fullUrl });
                    }
                }
            }

            if (_results.Count == 0)
            {
                SetStatus("No covers found for this game.");
                return;
            }

            SetStatus("");
            // Load thumbnails in background
            _ = LoadThumbnailsAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ArtPicker] Search failed");
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            SetLoading(false);
        }
    }

    private async Task LoadThumbnailsAsync()
    {
        // Load all thumbs concurrently (max 6 at a time)
        var semaphore = new SemaphoreSlim(6, 6);
        await Task.WhenAll(_results.Select(async result =>
        {
            await semaphore.WaitAsync();
            try
            {
                var bytes = await _http.GetByteArrayAsync(result.ThumbUrl);
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    result.Thumb = bmp;
                    // Force ItemsControl to refresh the binding for this item
                    var list = this.FindControl<ItemsControl>("ResultsList")!;
                    var idx = _results.IndexOf(result);
                    if (idx >= 0)
                    {
                        // Trick to refresh: remove and re-add (only if still present)
                        if (idx < _results.Count && _results[idx] == result)
                        {
                            _results.RemoveAt(idx);
                            _results.Insert(idx, result);
                        }
                    }
                });
            }
            catch { /* thumbnail failed — item stays with null Thumb */ }
            finally { semaphore.Release(); }
        }));
    }

    private void Thumb_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border b || b.DataContext is not ArtResult result) return;

        _selected = result;
        this.FindControl<Button>("UseButton")!.IsEnabled = true;

        // Visual feedback: update selected class on all borders
        var list = this.FindControl<ItemsControl>("ResultsList")!;
        foreach (var child in GetAllBordersInPanel(list))
        {
            if (child.DataContext == result)
                child.Classes.Add("selected");
            else
                child.Classes.Remove("selected");
        }
    }

    private static IEnumerable<Border> GetAllBordersInPanel(Control root)
    {
        if (root is Border b) yield return b;
        if (root is Panel panel)
            foreach (var child in panel.Children)
                foreach (var c in GetAllBordersInPanel(child))
                    yield return c;
        if (root is ContentControl cc && cc.Content is Control content)
            foreach (var c in GetAllBordersInPanel(content))
                yield return c;
        if (root is ItemsControl ic && ic.ItemsPanelRoot is Panel ip)
            foreach (var child in ip.Children)
                foreach (var c in GetAllBordersInPanel(child))
                    yield return c;
    }

    private void Use_Click(object? sender, RoutedEventArgs e)
    {
        if (_selected is not null)
            Close(_selected.FullUrl);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void SetStatus(string msg)
    {
        var t = this.FindControl<TextBlock>("StatusText")!;
        t.Text = msg;
        t.IsVisible = !string.IsNullOrEmpty(msg);
    }

    private void SetLoading(bool loading)
    {
        this.FindControl<TextBlock>("LoadingText")!.IsVisible = loading;
        this.FindControl<TextBlock>("StatusText")!.IsVisible = !loading;
        this.FindControl<ScrollViewer>("ResultsScroll")!.IsVisible = !loading;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _http.Dispose();
    }
}

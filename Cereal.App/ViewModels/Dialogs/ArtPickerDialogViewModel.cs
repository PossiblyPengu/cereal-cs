using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.App.ViewModels.Dialogs;

/// <summary>
/// A single SGDB / manual art candidate.
/// </summary>
public sealed record ArtCandidate(string Url, string? Thumb, string Label);

/// <summary>
/// Art-picker dialog — searches SteamGridDB for cover/header art
/// and lets the user select one.
/// </summary>
public sealed partial class ArtPickerDialogViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly HttpClient _http;

    public ObservableCollection<ArtCandidate> Results { get; } = [];

    [ObservableProperty] private string _searchTerm = string.Empty;
    [ObservableProperty] private ArtCandidate? _selected;
    [ObservableProperty] private bool    _isSearching;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>
    /// The type of art to search for: "cover" (portrait) or "hero" (header/banner).
    /// </summary>
    public string ArtType { get; set; } = "cover";

    public ArtPickerDialogViewModel(ISettingsService settings, IHttpClientFactory httpFactory)
    {
        _settings = settings;
        _http     = httpFactory.CreateClient("sgdb");
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm)) return;

        IsSearching  = true;
        ErrorMessage = null;
        Results.Clear();

        try
        {
            var settings = await _settings.LoadAsync();
            var apiKey   = settings.SteamGridDbKey;
            if (string.IsNullOrEmpty(apiKey))
            {
                ErrorMessage = "No SteamGridDB key configured.";
                return;
            }

            // 1. Resolve game id
            var searchUrl = $"https://www.steamgriddb.com/api/v2/search/autocomplete/{Uri.EscapeDataString(SearchTerm)}";
            using var req1 = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req1.Headers.Authorization = new("Bearer", apiKey);
            using var resp1 = await _http.SendAsync(req1);
            if (!resp1.IsSuccessStatusCode) { ErrorMessage = "SGDB search failed."; return; }

            using var doc1 = JsonDocument.Parse(await resp1.Content.ReadAsStringAsync());
            if (!doc1.RootElement.TryGetProperty("data", out var dataArr) ||
                dataArr.GetArrayLength() == 0)
            {
                ErrorMessage = "No results found.";
                return;
            }

            var gameId = dataArr[0].GetProperty("id").GetInt64();

            // 2. Fetch art
            var artType  = ArtType == "hero" ? "heroes" : "grids";
            var artUrl   = $"https://www.steamgriddb.com/api/v2/{artType}/game/{gameId}";
            using var req2 = new HttpRequestMessage(HttpMethod.Get, artUrl);
            req2.Headers.Authorization = new("Bearer", apiKey);
            using var resp2 = await _http.SendAsync(req2);
            if (!resp2.IsSuccessStatusCode) { ErrorMessage = "SGDB art fetch failed."; return; }

            using var doc2 = JsonDocument.Parse(await resp2.Content.ReadAsStringAsync());
            if (!doc2.RootElement.TryGetProperty("data", out var artArr)) return;

            foreach (var item in artArr.EnumerateArray().Take(20))
            {
                var url   = item.TryGetProperty("url",   out var u) ? u.GetString() : null;
                var thumb = item.TryGetProperty("thumb", out var t) ? t.GetString() : null;
                if (url is not null)
                    Results.Add(new ArtCandidate(url, thumb ?? url, $"{Results.Count + 1}"));
            }

            if (Results.Count == 0)
                ErrorMessage = "No art found for this game.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Log.Warning(ex, "[ArtPicker] Search failed");
        }
        finally { IsSearching = false; }
    }

    [RelayCommand]
    private void Pick(ArtCandidate candidate)
    {
        Selected = candidate;
        Close?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => Close?.Invoke();

    public Action? Close { get; set; }
}

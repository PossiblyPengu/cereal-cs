using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cereal.Core.Models;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.App.ViewModels.Dialogs;

/// <summary>
/// View-model for the "Add Custom Game" dialog.
/// Collects game metadata and delegates to <see cref="IGameService"/>.
/// </summary>
public sealed partial class AddGameDialogViewModel : ObservableObject
{
    private readonly IGameService _games;

    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _name = string.Empty;

    [ObservableProperty] private string _platform = "pc";
    [ObservableProperty] private string? _exePath;
    [ObservableProperty] private string? _coverPath;
    [ObservableProperty] private string? _notes;
    [ObservableProperty] private bool    _isSaving;
    [ObservableProperty] private string? _errorMessage;

    public static IReadOnlyList<string> Platforms { get; } =
    [
        "pc", "steam", "gog", "epic", "ea", "ubisoft", "battlenet", "itchio",
        "xbox", "ps5", "ps4", "ps3", "switch", "other"
    ];

    public AddGameDialogViewModel(IGameService games) => _games = games;

    private bool CanSave => !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (!CanSave) return;
        IsSaving = true;
        ErrorMessage = null;
        try
        {
            var game = new Game
            {
                Id        = Guid.NewGuid().ToString("N"),
                Name      = Name.Trim(),
                Platform  = Platform,
                ExePath   = ExePath,
                CoverUrl  = CoverPath,
                IsCustom  = true,
                IsInstalled = !string.IsNullOrEmpty(ExePath),
                AddedAt   = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await _games.UpsertAsync(game);
            Result = game;
            Close?.Invoke();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            Log.Warning(ex, "[AddGame] Save failed");
        }
        finally { IsSaving = false; }
    }

    [RelayCommand]
    private void Cancel() => Close?.Invoke();

    /// <summary>
    /// Raised when the dialog should be dismissed (either saved or cancelled).
    /// The host view binds to this action.
    /// </summary>
    public Action? Close { get; set; }

    /// <summary>The newly created game, set on successful save.</summary>
    public Game? Result { get; private set; }
}

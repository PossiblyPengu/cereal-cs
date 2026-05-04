using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Cereal.Core.Messaging;
using Cereal.Core.Models;
using Cereal.Core.Services;
using Serilog;

namespace Cereal.App.ViewModels.Settings;

/// <summary>
/// Per-platform account connection row.
/// </summary>
public sealed partial class AccountRowViewModel : ObservableObject
{
    public string Id    { get; }
    public string Label { get; }

    [ObservableProperty] private bool    _isConnected;
    [ObservableProperty] private string? _displayName;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool    _isBusy;

    public AccountRowViewModel(string id, string label)
    {
        Id    = id;
        Label = label;
    }
}

/// <summary>
/// Accounts &amp; integrations settings section.
/// </summary>
public sealed partial class AccountsSettingsViewModel : ObservableObject,
    IRecipient<AuthStateChangedMessage>
{
    private readonly IAuthService _auth;
    private readonly IMessenger _messenger;

    public ObservableCollection<AccountRowViewModel> Accounts { get; } = new(new[]
    {
        new AccountRowViewModel("gog",   "GOG"),
        new AccountRowViewModel("epic",  "Epic Games"),
        new AccountRowViewModel("xbox",  "Xbox / Microsoft"),
    });

    public AccountsSettingsViewModel(IAuthService auth, IMessenger messenger)
    {
        _auth      = auth;
        _messenger = messenger;
        messenger.Register(this);
        RefreshConnectedState();
    }

    public void Receive(AuthStateChangedMessage msg)
    {
        var row = Accounts.FirstOrDefault(a => a.Id == msg.Platform);
        if (row is null) return;
        row.IsConnected  = msg.IsConnected;
        row.DisplayName  = msg.IsConnected ? row.Label : null;
        row.StatusMessage = null;
    }

    [RelayCommand]
    private async Task ConnectAsync(AccountRowViewModel row)
    {
        row.IsBusy = true;
        row.StatusMessage = "Connecting…";
        try
        {
            var session = await _auth.AuthenticateAsync(row.Id);
            row.IsConnected  = true;
            row.DisplayName  = row.Id.ToUpperInvariant();
            row.StatusMessage = null;
        }
        catch (NotImplementedException)
        {
            row.StatusMessage = "Not yet supported.";
        }
        catch (Exception ex)
        {
            row.StatusMessage = ex.Message;
            Log.Warning(ex, "[AccountsSettings] Connect failed for {Platform}", row.Id);
        }
        finally { row.IsBusy = false; }
    }

    [RelayCommand]
    private async Task DisconnectAsync(AccountRowViewModel row)
    {
        row.IsBusy = true;
        try
        {
            await _auth.SignOutAsync(row.Id);
            row.IsConnected  = false;
            row.DisplayName  = null;
            row.StatusMessage = null;
        }
        catch (Exception ex)
        {
            row.StatusMessage = ex.Message;
            Log.Warning(ex, "[AccountsSettings] Disconnect failed for {Platform}", row.Id);
        }
        finally { row.IsBusy = false; }
    }

    private void RefreshConnectedState()
    {
        foreach (var row in Accounts)
        {
            row.IsConnected = _auth.IsAuthenticated(row.Id);
            var session = _auth.GetSession(row.Id);
            row.DisplayName = session is not null ? row.Label : null;
        }
    }
}

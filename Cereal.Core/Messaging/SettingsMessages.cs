using Cereal.Core.Models;

namespace Cereal.Core.Messaging;

/// <summary>Fired by <c>SettingsService.SaveAsync</c> after settings are persisted.</summary>
public sealed record SettingsChangedMessage(Settings Settings);

/// <summary>Fired when the active theme changes (allows immediate UI update without full reload).</summary>
public sealed record ThemeChangedMessage(AppTheme Theme);

/// <summary>Fired by <c>AuthService</c> when a platform session is added or removed.</summary>
public sealed record AuthStateChangedMessage(string Platform, bool IsConnected);

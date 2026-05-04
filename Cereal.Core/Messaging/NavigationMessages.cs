namespace Cereal.Core.Messaging;

/// <summary>Ask the panel router to open (or activate) a named panel.</summary>
public sealed record NavigateToPanelMessage(string PanelId, object? Parameter = null);

/// <summary>Ask the panel router to close a panel tab.</summary>
public sealed record ClosePanelMessage(string PanelId);

/// <summary>Ask the focus panel to display a specific game.</summary>
public sealed record FocusGameMessage(string GameId);

/// <summary>Ask the search overlay to open.</summary>
public sealed record OpenSearchMessage;

/// <summary>Ask the search overlay to close.</summary>
public sealed record CloseSearchMessage;

/// <summary>Ask the shell window to minimize itself (e.g. after a game launch).</summary>
public sealed record MinimizeWindowMessage;

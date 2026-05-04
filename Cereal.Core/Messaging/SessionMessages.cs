using Cereal.Core.Models;

namespace Cereal.Core.Messaging;

/// <summary>Fired when a Chiaki or xCloud session starts.</summary>
public sealed record StreamStartedMessage(string SessionId, string Platform, string GameName);

/// <summary>Fired when a stream session ends.</summary>
public sealed record StreamEndedMessage(string SessionId);

/// <summary>Fired when streaming quality stats are updated (Chiaki only).</summary>
public sealed record StreamStatsUpdatedMessage(string SessionId, StreamStats Stats);

/// <summary>Fired when chiaki-ng reports a title change (e.g. a PS game launched).</summary>
public sealed record StreamTitleChangedMessage(string SessionId, string Title);

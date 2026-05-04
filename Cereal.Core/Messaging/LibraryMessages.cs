using Cereal.Core.Models;

namespace Cereal.Core.Messaging;

/// <summary>Sent by <c>GameRepository.SaveAsync</c> when a new game is inserted.</summary>
public sealed record GameAddedMessage(Game Game);

/// <summary>Sent by <c>GameRepository.SaveAsync</c> when an existing game is updated.</summary>
public sealed record GameUpdatedMessage(Game Game);

/// <summary>Sent by <c>GameRepository.DeleteAsync</c>.</summary>
public sealed record GameRemovedMessage(string GameId);

/// <summary>Sent after a batch upsert completes (e.g. import / detect).</summary>
public sealed record LibraryRefreshedMessage(int Count);

/// <summary>Sent when a game's cover art is updated (triggers image cache invalidation).</summary>
public sealed record GameCoverUpdatedMessage(string GameId, string? LocalCoverPath, string? LocalHeaderPath);

namespace Cereal.Core.Models;

/// <summary>Global Chiaki configuration (executable path, default display options).</summary>
public sealed record ChiakiConfig
{
    public string? ExecutablePath { get; set; }
    public string? DisplayMode { get; set; }
    public bool Dualsense { get; set; }
    public List<ChiakiConsole> Consoles { get; set; } = [];
}

/// <summary>A registered PlayStation console for Chiaki remote play.</summary>
public sealed record ChiakiConsole
{
    public string Id { get; set; } = string.Empty;
    public string? Nickname { get; set; }
    public string? Host { get; set; }
    public string? Profile { get; set; }
    public string? RegistKey { get; set; }
    public string? Morning { get; set; }
}

/// <summary>Live streaming quality statistics emitted by chiaki-ng.</summary>
public sealed record StreamStats(
    string? Resolution,
    string? Codec,
    int? Fps,
    double? Bitrate,
    double? PacketLoss);

/// <summary>SMTC media widget data.</summary>
public sealed record MediaInfo(
    string? Title,
    string? Artist,
    string? Album,
    string? AlbumArtUrl,
    bool IsPlaying,
    double? Position,
    double? Duration);

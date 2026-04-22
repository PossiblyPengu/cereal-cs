namespace Cereal.App.Models;

public record AppTheme(
    string Id,
    string Label,
    string Accent,
    string Void,
    string Surface,
    string Card,
    string CardUp,
    string Text,
    string Text2,
    string BodyBg
);

public static class AppThemes
{
    public static readonly AppTheme[] All =
    [
        new("midnight",  "Midnight",   "#d4a853", "#07070d", "#0d0d16", "#101018", "#16161f", "#e8e4de", "#b0aaa0", "#07070d"),
        new("obsidian",  "Obsidian",   "#8b5cf6", "#09090b", "#0f0f14", "#13131a", "#1a1a22", "#e4e2ef", "#a8a4b8", "#09090b"),
        new("aurora",    "Aurora",     "#34d399", "#060d0b", "#0a1410", "#0e1a15", "#13211c", "#dceee6", "#9abfad", "#060d0b"),
        new("ember",     "Ember",      "#f97316", "#0d0806", "#14100c", "#1a1410", "#221b15", "#f0e6dc", "#bfad9a", "#0d0806"),
        new("arctic",    "Arctic",     "#38bdf8", "#060a0f", "#0a1018", "#0e1520", "#141c28", "#dce8f0", "#96b0c4", "#060a0f"),
        new("rose",      "Rosé",       "#f472b6", "#0d060a", "#140c12", "#1a1018", "#22161f", "#f0dce6", "#c49aaf", "#0d060a"),
        new("carbon",    "Carbon",     "#a1a1aa", "#09090b", "#111113", "#18181b", "#1f1f23", "#e4e4e7", "#a1a1aa", "#09090b"),
        new("sakura",    "Sakura",     "#e879a0", "#0c070a", "#120e11", "#1a1318", "#201920", "#eedde4", "#c4a0af", "#0c070a"),
        new("contrast",  "Contrast",   "#ffff00", "#000000", "#0a0a0a", "#111111", "#1c1c1c", "#ffffff", "#ffffff", "#000000"),
    ];

    public static AppTheme? Find(string id) =>
        Array.Find(All, t => t.Id == id);
}

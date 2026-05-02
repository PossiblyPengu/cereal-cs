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
    string Text3,
    string Text4,
    string Glass,
    string GlassBorder,
    string Glow,
    string BodyBg
);

public static class AppThemes
{
    public static readonly AppTheme[] All =
    [
        //                 id          label       accent      void        surface     card        cardUp      text        text2       text3       text4       glass                        glassBorder                  glow                         bodyBg
        new("midnight", "Midnight",  "#d4a853", "#02020c", "#06071e", "#0b0d26", "#101428", "#dce4f8", "#8898be", "#465074", "#252840", "rgba(212,168,83,0.04)",  "rgba(212,168,83,0.08)",  "rgba(212,168,83,0.10)",  "#02020c"),
        new("cosmos",   "Cosmos",    "#7c6de0", "#01010d", "#05061a", "#090b20", "#0e102a", "#e0e6f8", "#8890b8", "#444c6e", "#222438", "rgba(100,80,220,0.04)",  "rgba(120,100,240,0.08)", "rgba(100,80,220,0.12)",  "#01010d"),
        new("obsidian", "Obsidian",  "#8b5cf6", "#09090b", "#0f0f14", "#13131a", "#1a1a22", "#e4e2ef", "#a8a4b8", "#5f5b72", "#36333f", "rgba(139,92,246,0.04)",  "rgba(139,92,246,0.08)",  "rgba(139,92,246,0.10)",  "#09090b"),
        new("aurora",   "Aurora",    "#34d399", "#060d0b", "#0a1410", "#0e1a15", "#13211c", "#dceee6", "#9abfad", "#4e7363", "#2d4238", "rgba(52,211,153,0.04)",  "rgba(52,211,153,0.08)",  "rgba(52,211,153,0.10)",  "#060d0b"),
        new("ember",    "Ember",     "#f97316", "#0d0806", "#14100c", "#1a1410", "#221b15", "#f0e6dc", "#bfad9a", "#73644f", "#403729", "rgba(249,115,22,0.04)",  "rgba(249,115,22,0.08)",  "rgba(249,115,22,0.10)",  "#0d0806"),
        new("arctic",   "Arctic",    "#38bdf8", "#060a0f", "#0a1018", "#0e1520", "#141c28", "#dce8f0", "#96b0c4", "#4a6578", "#283848", "rgba(56,189,248,0.04)",  "rgba(56,189,248,0.08)",  "rgba(56,189,248,0.10)",  "#060a0f"),
        new("rose",     "Rosé",      "#f472b6", "#0d060a", "#140c12", "#1a1018", "#22161f", "#f0dce6", "#c49aaf", "#7a5068", "#42293a", "rgba(244,114,182,0.04)", "rgba(244,114,182,0.08)", "rgba(244,114,182,0.10)", "#0d060a"),
        new("carbon",   "Carbon",    "#a1a1aa", "#09090b", "#111113", "#18181b", "#1f1f23", "#e4e4e7", "#a1a1aa", "#63636b", "#3a3a40", "rgba(255,255,255,0.04)", "rgba(255,255,255,0.07)", "rgba(255,255,255,0.09)", "#09090b"),
        new("sakura",   "Sakura",    "#e879a0", "#0c070a", "#120e11", "#1a1318", "#201920", "#eedde4", "#c4a0af", "#7a5a6a", "#402838", "rgba(232,121,160,0.04)", "rgba(232,121,160,0.08)", "rgba(232,121,160,0.10)", "#0c070a"),
        new("contrast", "Contrast",  "#ffff00", "#000000", "#0a0a0a", "#111111", "#1c1c1c", "#ffffff", "#ffffff", "#cccccc", "#999999", "rgba(255,255,255,0.06)", "rgba(255,255,255,0.45)", "rgba(255,255,0,0.20)",   "#000000"),
    ];

    public static AppTheme? Find(string id) =>
        Array.Find(All, t => t.Id == id);
}

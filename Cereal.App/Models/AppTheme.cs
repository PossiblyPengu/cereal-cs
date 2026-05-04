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
        //                 id          label       accent      void        surface     card        cardUp      text        text2       text3       text4       glass                         glassBorder                   glow                          bodyBg
        new("midnight", "Midnight",  "#93b4f0", "#02020c", "#06071e", "#0b0d26", "#101428", "#dce4f8", "#8898be", "#465074", "#252840", "rgba(147,180,240,0.04)", "rgba(147,180,240,0.08)", "rgba(147,180,240,0.12)", "#02020c"),
        new("cosmos",   "Cosmos",    "#a78bfa", "#03010f", "#08061c", "#0f0b28", "#160f34", "#e4deff", "#9880cc", "#5a4888", "#302460", "rgba(167,139,250,0.04)", "rgba(167,139,250,0.08)", "rgba(167,139,250,0.14)", "#03010f"),
        new("obsidian", "Obsidian",  "#8b5cf6", "#09090b", "#0f0f14", "#13131a", "#1a1a22", "#e4e2ef", "#a8a4b8", "#5f5b72", "#36333f", "rgba(139,92,246,0.04)",  "rgba(139,92,246,0.08)",  "rgba(139,92,246,0.10)",  "#09090b"),
        new("aurora",   "Aurora",    "#34d399", "#060d0b", "#0a1410", "#0e1a15", "#13211c", "#dceee6", "#9abfad", "#4e7363", "#2d4238", "rgba(52,211,153,0.04)",  "rgba(52,211,153,0.08)",  "rgba(52,211,153,0.10)",  "#060d0b"),
        new("ember",    "Ember",     "#f97316", "#0d0806", "#14100c", "#1a1410", "#221b15", "#f0e6dc", "#bfad9a", "#73644f", "#403729", "rgba(249,115,22,0.04)",  "rgba(249,115,22,0.08)",  "rgba(249,115,22,0.10)",  "#0d0806"),
        new("solar",    "Solar",     "#f59e0b", "#0c0902", "#16120a", "#1e180e", "#261f14", "#f4ead8", "#c8b888", "#806840", "#423218", "rgba(245,158,11,0.04)",  "rgba(245,158,11,0.08)",  "rgba(245,158,11,0.14)",  "#0c0902"),
        new("arctic",   "Arctic",    "#38bdf8", "#060a0f", "#0a1018", "#0e1520", "#141c28", "#dce8f0", "#96b0c4", "#4a6578", "#283848", "rgba(56,189,248,0.04)",  "rgba(56,189,248,0.08)",  "rgba(56,189,248,0.10)",  "#060a0f"),
        new("crimson",  "Crimson",   "#f43f5e", "#0e0209", "#160510", "#1f0918", "#280e21", "#f0dce4", "#c49aac", "#7a5062", "#422838", "rgba(244,63,94,0.04)",   "rgba(244,63,94,0.08)",   "rgba(244,63,94,0.14)",   "#0e0209"),
        new("carbon",   "Carbon",    "#a1a1aa", "#09090b", "#111113", "#18181b", "#1f1f23", "#e4e4e7", "#a1a1aa", "#63636b", "#3a3a40", "rgba(255,255,255,0.04)", "rgba(255,255,255,0.07)", "rgba(255,255,255,0.09)", "#09090b"),
        new("pulsar",   "Pulsar",    "#2dd4bf", "#010e0c", "#051714", "#091e1b", "#0d2826", "#d8f0ee", "#88b8b4", "#447470", "#223c3a", "rgba(45,212,191,0.04)",  "rgba(45,212,191,0.08)",  "rgba(45,212,191,0.12)",  "#010e0c"),
        new("contrast", "Contrast",  "#ffff00", "#000000", "#0a0a0a", "#111111", "#1c1c1c", "#ffffff", "#ffffff", "#cccccc", "#999999", "rgba(255,255,255,0.06)", "rgba(255,255,255,0.45)", "rgba(255,255,0,0.20)",   "#000000"),
    ];

    public static AppTheme? Find(string id) =>
        Array.Find(All, t => t.Id == id);
}

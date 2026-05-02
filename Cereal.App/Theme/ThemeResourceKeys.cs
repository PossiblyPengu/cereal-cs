namespace Cereal.App.Theme;

/// <summary>Central registry of application theme resource keys (Application.Resources).</summary>
public static class ThemeResourceKeys
{
    // ── Base palette (from <see cref="Models.AppTheme"/>) ─────────────────
    public const string Void        = "ThemeVoidColor";
    public const string Surface     = "ThemeSurfaceColor";
    public const string Card        = "ThemeCardColor";
    public const string CardUp      = "ThemeCardUpColor";
    public const string Text        = "ThemeTextColor";
    public const string Text2       = "ThemeText2Color";
    public const string Text3       = "ThemeText3Color";
    public const string Text4       = "ThemeText4Color";
    public const string BodyBg      = "ThemeBodyBgColor";
    public const string Accent      = "ThemeAccentColor";

    // ── Glass / glow (string tokens, rgba) ────────────────────────────────
    public const string Glass       = "ThemeGlass";
    public const string GlassBorder = "ThemeGlassBorder";
    public const string Glow        = "ThemeGlow";

    // ── Derived at runtime from accent (hover/press/focus/on-accent) ─────
    public const string AccentHover  = "ThemeAccentHoverColor";
    public const string AccentPressed= "ThemeAccentPressedColor";
    public const string OnAccent     = "ThemeOnAccentColor";
    public const string FocusRing    = "ThemeFocusRingColor";
    /// <summary>Preformatted <see cref="Avalonia.Media.BoxShadows"/> string (1px, accent @ ~40% α).</summary>
    public const string FocusRingBoxShadow = "ThemeFocusRingBoxShadow";
    /// <summary>Preformatted inset <see cref="Avalonia.Media.BoxShadows"/> string for inner focus outlines.</summary>
    public const string FocusRingBoxShadowInset = "ThemeFocusRingBoxShadowInset";
    /// <summary>~12% opacity accent tint — used for active/selected background fills.</summary>
    public const string AccentSoft   = "ThemeAccentSoftColor";
    public const string ScrollThumb  = "ThemeScrollThumbColor";
    public const string ScrollThumbHover = "ThemeScrollThumbHoverColor";
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace BcuHolo.Themes
{
    /// <summary>One dark palette. Glass whites/blacks stay neutral in every theme, so light themes aren't supported.</summary>
    public sealed record Theme(string Name, string Blurb,
        string Bg, string Rail, string Tile, string Text, string Muted, string Other,
        string Accent, string Line, string Good, string Danger, string Violet, string Blue);

    /// <summary>
    /// Swaps palette resources on Application.Resources. Consumers must use DynamicResource / SetResourceReference to follow
    /// a live switch (brushes are replaced, never mutated: brushes inside sealed styles are frozen).
    ///
    /// Keys:  Bg Rail Tile Text Muted Other Accent Line Good Danger Violet Blue    solid brushes
    ///        Wsl SysFiles AccentSoft DangerSoft PanelLine RowLine Track Panel     legacy aliases
    ///        GlassLine GlassFill AccentGlassFill                                   gradients
    ///        AccentText (accent-tinted text on accent glass)  OnDanger (text on a Danger-filled button)
    ///        <Hue>.<AA>   e.g. Accent.24, Good.1A  brush of that hue at hex alpha AA (Alphas below)
    ///        <Hue>.<AA>.C the same as a Color (for GradientStop.Color etc.)
    ///        <Hue>.C      the opaque Color
    /// </summary>
    public static class ThemeManager
    {
        public static readonly string[] Hues = { "Bg", "Rail", "Tile", "Text", "Muted", "Other", "Accent", "Line", "Good", "Danger", "Violet", "Blue" };
        public static readonly string[] Alphas =
            { "00", "08", "0A", "0F", "12", "14", "1A", "1F", "24", "29", "2E", "33", "38", "40", "4D", "52", "55", "59", "5A", "66", "80", "99", "B3", "BF", "CC", "E6", "EB" };

        public static readonly IReadOnlyList<Theme> All = new[]
        {
            new Theme("Holo", "Cyan glass, the original",
                "#070B14", "#0A1020", "#121B2E", "#E6F1FF", "#9FB2CC", "#3A4A66", "#3DDCFF", "#78C8FF", "#5BE7A9", "#FF8A3D", "#A78BFA", "#5B8DEF"),
            new Theme("Ember", "Amber on charcoal",
                "#0E0907", "#150D0A", "#22140F", "#FFF1E6", "#CDB4A3", "#5A4034", "#FFB13D", "#FF9A6B", "#8BE38B", "#FF4D6A", "#F472B6", "#FFCF7A"),
            new Theme("Phosphor", "Green terminal glow",
                "#040C08", "#07130D", "#0E1F16", "#E6FFF0", "#9CC4AE", "#2F4A3B", "#39FF88", "#5CE6A0", "#B6FF5C", "#FFB13D", "#5CC8FF", "#3DDCB0"),
            new Theme("Synthwave", "Magenta and violet neon",
                "#0D0616", "#140A20", "#1E1030", "#F7E8FF", "#BCA3D1", "#4A3466", "#FF4FD8", "#B07CFF", "#3DF5E0", "#FFB13D", "#7C8CFF", "#FF7AB6"),
            new Theme("Stealth", "Graphite with a red edge",
                "#0A0B0D", "#0F1113", "#181A1E", "#EEF1F5", "#A3A9B3", "#3D424B", "#FF4D4D", "#A0AEC0", "#5BE7A9", "#FFB13D", "#9AA5B8", "#C7CED9"),
            new Theme("Aurum", "Gold on midnight blue",
                "#070A12", "#0B1020", "#131A2C", "#F5F0E1", "#B8B09A", "#4A4636", "#FFD24D", "#E0C27A", "#7CE3A0", "#FF7A4D", "#C9A6FF", "#8FB4FF"),
        };

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BCU-Holo", "settings.json");

        public static Theme Current { get; private set; } = All[0];
        public static event Action Changed;

        private sealed class Settings { public string Theme { get; set; } }

        public static void LoadSaved()
        {
            string name = null;
            try { if (File.Exists(SettingsPath)) name = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))?.Theme; }
            catch (Exception) { /* bad settings file: fall back to the default theme */ }
            Apply(All.FirstOrDefault(t => t.Name == name) ?? All[0], save: false);
        }

        public static void Apply(Theme t, bool save = true)
        {
            var r = Application.Current.Resources;
            var baseColors = new Dictionary<string, Color>
            {
                ["Bg"] = C(t.Bg), ["Rail"] = C(t.Rail), ["Tile"] = C(t.Tile), ["Text"] = C(t.Text), ["Muted"] = C(t.Muted), ["Other"] = C(t.Other),
                ["Accent"] = C(t.Accent), ["Line"] = C(t.Line), ["Good"] = C(t.Good), ["Danger"] = C(t.Danger), ["Violet"] = C(t.Violet), ["Blue"] = C(t.Blue),
            };
            foreach (var (hue, c) in baseColors)
            {
                r[hue] = Brush(c);
                r[hue + ".C"] = c;
                foreach (var a in Alphas)
                {
                    var ac = WithAlpha(c, Convert.ToByte(a, 16));
                    r[$"{hue}.{a}"] = Brush(ac);
                    r[$"{hue}.{a}.C"] = ac;
                }
            }
            // Names older views use.
            r["Wsl"] = Brush(baseColors["Violet"]);
            r["SysFiles"] = Brush(baseColors["Blue"]);
            r["AccentSoft"] = r["Accent.1A"];
            r["DangerSoft"] = r["Danger.12"];
            r["PanelLine"] = r["Line.24"];
            r["RowLine"] = r["Line.12"];
            r["Track"] = r["Line.14"];
            r["Panel"] = Brush(Color.FromArgb(0x09, 0xFF, 0xFF, 0xFF));

            // Glass: light catches the top-left edge, fades through the middle, picks up the accent bottom-right.
            var line = baseColors["Line"];
            r["GlassLine"] = Gradient(new Point(0, 0), new Point(1, 1),
                (WithAlpha(Lerp(line, Colors.White, 0.6), 0x8C), 0), (WithAlpha(line, 0x2E), 0.35), (WithAlpha(line, 0x14), 0.7), (WithAlpha(baseColors["Accent"], 0x59), 1));
            r["AccentGlassFill"] = Gradient(new Point(0, 0), new Point(0, 1), (WithAlpha(baseColors["Accent"], 0x40), 0), (WithAlpha(baseColors["Accent"], 0x1A), 1));
            r["AccentText"] = Brush(Lerp(baseColors["Accent"], Colors.White, 0.7));
            r["OnDanger"] = Brush(Lerp(baseColors["Danger"], Colors.Black, 0.9));
            r["GlassFill"] = Gradient(new Point(0, 0), new Point(0, 1),
                (Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF), 0), (Color.FromArgb(0x07, 0xFF, 0xFF, 0xFF), 1));

            Current = t;
            if (save) Save();
            Changed?.Invoke();
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings { Theme = Current.Name }));
            }
            catch (Exception) { /* the theme still applies for this session */ }
        }

        public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
        private static Color Lerp(Color a, Color b, double t) =>
            Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        private static SolidColorBrush Brush(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        private static LinearGradientBrush Gradient(Point start, Point end, params (Color c, double o)[] stops)
        {
            var g = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            foreach (var (c, o) in stops) g.GradientStops.Add(new GradientStop(c, o));
            g.Freeze();
            return g;
        }
    }
}

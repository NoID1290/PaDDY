using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace PaDDY.Helpers
{
    /// <summary>
    /// Runtime appearance manager: overall colour themes, audio-meter skins and
    /// performance (software-render) mode. Themes are applied by mutating the
    /// <see cref="SolidColorBrush"/> resources defined in <c>Themes/AppTheme.xaml</c>
    /// in place, so every element bound to those resources updates live without a
    /// restart. Meter skins swap the gradient brushes referenced by the meters.
    /// </summary>
    public static class ThemeManager
    {
        public const string DefaultTheme = "dark";
        public const string DefaultMeterSkin = "default";

        public static bool PerformanceMode { get; private set; }

        /// <summary>Display name list for the overall theme selector (key, label).</summary>
        public static readonly IReadOnlyList<(string Key, string Label)> Themes =
        [
            ("dark",       "Dark"),
            ("light",      "Light"),
            ("dark-green", "Dark Green"),
            ("dark-blue",  "Dark Blue"),
            ("sepia",      "Sepia"),
            ("dark-pink",  "Dark Pink"),
        ];

        /// <summary>Display name list for the meter skin selector (key, label).</summary>
        public static readonly IReadOnlyList<(string Key, string Label)> MeterSkins =
        [
            ("default", "Default"),
            ("8bit",    "8-bit"),
            ("70s",     "70s Look"),
        ];

        // resourceKey -> hex colour, per theme.
        private static readonly Dictionary<string, Dictionary<string, string>> Palettes = new()
        {
            ["dark"] = new()
            {
                ["WindowBgBrush"] = "#FF0D0D14",
                ["CardBgBrush"] = "#FF1A1A28",
                ["CardBorderBrush"] = "#28FFFFFF",
                ["SubtleTextBrush"] = "#FF7070A0",
                ["PrimaryTextBrush"] = "#FFE8E8F4",
                ["SecondaryTextBrush"] = "#FFB0B0CC",
                ["AccentGreenBrush"] = "#FF4CAF50",
                ["AccentRedBrush"] = "#FFC62828",
                ["AccentAmberBrush"] = "#FFFFC107",
                ["InputBgBrush"] = "#FF141420",
                ["InputBorderBrush"] = "#2AFFFFFF",
                ["WindowEdgeBrush"] = "#2CFFFFFF",
                ["WindowGlowBrush"] = "#FF1A2030",
                ["ControlTextBrush"] = "#FFD0D0E0",
                ["ButtonBgBrush"] = "#FF1C1C2C",
                ["ButtonHoverBgBrush"] = "#FF282840",
                ["ButtonPressedBgBrush"] = "#FF0F0F1C",
                ["MenuBgBrush"] = "#FF10101E",
                ["MenuHighlightBrush"] = "#FF3E4A5A",
                ["MenuSelectedBrush"] = "#FF2A5A8E",
                ["DividerBrush"] = "#1AFFFFFF",
                ["BadgeBgBrush"] = "#FF0C0C18",
                ["AccentTitleBrush"] = "#FF5A9070",
                ["ChromeButtonFgBrush"] = "#FFB0B8D0",
                ["ScrollThumbBrush"] = "#FFE9EDF9",
            },
            ["light"] = new()
            {
                ["WindowBgBrush"] = "#FFF2F3F7",
                ["CardBgBrush"] = "#FFFFFFFF",
                ["CardBorderBrush"] = "#22000000",
                ["SubtleTextBrush"] = "#FF8A8AA0",
                ["PrimaryTextBrush"] = "#FF1A1A24",
                ["SecondaryTextBrush"] = "#FF4A4A5A",
                ["AccentGreenBrush"] = "#FF2E7D32",
                ["AccentRedBrush"] = "#FFC62828",
                ["AccentAmberBrush"] = "#FFEF9A00",
                ["InputBgBrush"] = "#FFFFFFFF",
                ["InputBorderBrush"] = "#33000000",
                ["WindowEdgeBrush"] = "#22000000",
                ["WindowGlowBrush"] = "#FFC7D0E0",
                ["ControlTextBrush"] = "#FF1A1A24",
                ["ButtonBgBrush"] = "#FFFFFFFF",
                ["ButtonHoverBgBrush"] = "#FFEDEFF5",
                ["ButtonPressedBgBrush"] = "#FFDDE0EA",
                ["MenuBgBrush"] = "#FFFFFFFF",
                ["MenuHighlightBrush"] = "#FFE3E8F2",
                ["MenuSelectedBrush"] = "#FFBCD4F0",
                ["DividerBrush"] = "#14000000",
                ["BadgeBgBrush"] = "#FFE9ECF3",
                ["AccentTitleBrush"] = "#FF2E7D32",
                ["ChromeButtonFgBrush"] = "#FF4A4A5A",
                ["ScrollThumbBrush"] = "#FF8A8AA0",
            },
            ["dark-green"] = new()
            {
                ["WindowBgBrush"] = "#FF0A140D",
                ["CardBgBrush"] = "#FF132019",
                ["CardBorderBrush"] = "#2A4CAF50",
                ["SubtleTextBrush"] = "#FF6FA080",
                ["PrimaryTextBrush"] = "#FFE6F4E8",
                ["SecondaryTextBrush"] = "#FFAFCFB6",
                ["AccentGreenBrush"] = "#FF66BB6A",
                ["AccentRedBrush"] = "#FFC62828",
                ["AccentAmberBrush"] = "#FFFFC107",
                ["InputBgBrush"] = "#FF0F1A13",
                ["InputBorderBrush"] = "#2A66BB6A",
                ["WindowEdgeBrush"] = "#2C66BB6A",
                ["WindowGlowBrush"] = "#FF12301C",
                ["ControlTextBrush"] = "#FFCFE6D5",
                ["ButtonBgBrush"] = "#FF16261C",
                ["ButtonHoverBgBrush"] = "#FF1E3528",
                ["ButtonPressedBgBrush"] = "#FF0E1A13",
                ["MenuBgBrush"] = "#FF0F1A13",
                ["MenuHighlightBrush"] = "#FF2A4A35",
                ["MenuSelectedBrush"] = "#FF2E6E40",
                ["DividerBrush"] = "#1A66BB6A",
                ["BadgeBgBrush"] = "#FF0B130D",
                ["AccentTitleBrush"] = "#FF66BB6A",
                ["ChromeButtonFgBrush"] = "#FFADCFB6",
                ["ScrollThumbBrush"] = "#FFCFE6D5",
            },
            ["dark-blue"] = new()
            {
                ["WindowBgBrush"] = "#FF0A0F1A",
                ["CardBgBrush"] = "#FF131A28",
                ["CardBorderBrush"] = "#2A42A5F5",
                ["SubtleTextBrush"] = "#FF6F84A8",
                ["PrimaryTextBrush"] = "#FFE6ECF8",
                ["SecondaryTextBrush"] = "#FFAFBEDA",
                ["AccentGreenBrush"] = "#FF42A5F5",
                ["AccentRedBrush"] = "#FFC62828",
                ["AccentAmberBrush"] = "#FFFFC107",
                ["InputBgBrush"] = "#FF0F1626",
                ["InputBorderBrush"] = "#2A42A5F5",
                ["WindowEdgeBrush"] = "#2C42A5F5",
                ["WindowGlowBrush"] = "#FF14233A",
                ["ControlTextBrush"] = "#FFCFD8EC",
                ["ButtonBgBrush"] = "#FF161E2E",
                ["ButtonHoverBgBrush"] = "#FF1E2C42",
                ["ButtonPressedBgBrush"] = "#FF0E1626",
                ["MenuBgBrush"] = "#FF0F1626",
                ["MenuHighlightBrush"] = "#FF2A3E5A",
                ["MenuSelectedBrush"] = "#FF2A5A8E",
                ["DividerBrush"] = "#1A42A5F5",
                ["BadgeBgBrush"] = "#FF0B1320",
                ["AccentTitleBrush"] = "#FF5A90C0",
                ["ChromeButtonFgBrush"] = "#FFAFBEDA",
                ["ScrollThumbBrush"] = "#FFCFD8EC",
            },
            ["sepia"] = new()
            {
                ["WindowBgBrush"] = "#FFF1E7D6", // or #ffeae7d6"
                ["CardBgBrush"] = "#dfdad1e6",
                ["CardBorderBrush"] = "#33805030",
                ["SubtleTextBrush"] = "#FF8A7350",
                ["PrimaryTextBrush"] = "#FF3A2C1A",
                ["SecondaryTextBrush"] = "#FF5E4A30",
                ["AccentGreenBrush"] = "#FFB5651D",
                ["AccentRedBrush"] = "#FFA63A24",
                ["AccentAmberBrush"] = "#FFCC8800",
                ["InputBgBrush"] = "#FFFDF6EA",
                ["InputBorderBrush"] = "#44805030",
                ["WindowEdgeBrush"] = "#33805030",
                ["WindowGlowBrush"] = "#FFD8C4A0",
                ["ControlTextBrush"] = "#FF3A2C1A",
                ["ButtonBgBrush"] = "#FFFBF3E6",
                ["ButtonHoverBgBrush"] = "#FFF2E6D2",
                ["ButtonPressedBgBrush"] = "#FFE6D5BC",
                ["MenuBgBrush"] = "#FFFBF3E6",
                ["MenuHighlightBrush"] = "#FFEDDCC2",
                ["MenuSelectedBrush"] = "#FFD8B98A",
                ["DividerBrush"] = "#22805030",
                ["BadgeBgBrush"] = "#FFEFE2CC",
                ["AccentTitleBrush"] = "#FFB5651D",
                ["ChromeButtonFgBrush"] = "#FF5E4A30",
                ["ScrollThumbBrush"] = "#FF8A7350",
            },
            ["dark-pink"] = new()
            {
                ["WindowBgBrush"] = "#FF140A10",
                ["CardBgBrush"] = "#FF20131B",
                ["CardBorderBrush"] = "#2AF54292",
                ["SubtleTextBrush"] = "#FFA86F8E",
                ["PrimaryTextBrush"] = "#FFF4E6EE",
                ["SecondaryTextBrush"] = "#FFD8AFC6",
                ["AccentGreenBrush"] = "#FFEC407A",
                ["AccentRedBrush"] = "#FFC62828",
                ["AccentAmberBrush"] = "#FFFFC107",
                ["InputBgBrush"] = "#FF1A0F16",
                ["InputBorderBrush"] = "#2AF54292",
                ["WindowEdgeBrush"] = "#2CF54292",
                ["WindowGlowBrush"] = "#FF301425",
                ["ControlTextBrush"] = "#FFE6CFDB",
                ["ButtonBgBrush"] = "#FF261620",
                ["ButtonHoverBgBrush"] = "#FF351E2C",
                ["ButtonPressedBgBrush"] = "#FF1A0F16",
                ["MenuBgBrush"] = "#FF1A0F16",
                ["MenuHighlightBrush"] = "#FF4A2A3C",
                ["MenuSelectedBrush"] = "#FF8E2A5E",
                ["DividerBrush"] = "#1AF54292",
                ["BadgeBgBrush"] = "#FF130B10",
                ["AccentTitleBrush"] = "#FFEC407A",
                ["ChromeButtonFgBrush"] = "#FFD8AFC6",
                ["ScrollThumbBrush"] = "#FFE6CFDB",
            },
        };

        /// <summary>Applies an overall colour theme by mutating the shared brush resources.</summary>
        public static void ApplyTheme(string? themeKey)
        {
            if (string.IsNullOrWhiteSpace(themeKey) || !Palettes.ContainsKey(themeKey))
                themeKey = DefaultTheme;

            var palette = Palettes[themeKey];
            var res = Application.Current?.Resources;
            if (res == null) return;

            foreach (var kvp in palette)
            {
                var color = ParseColor(kvp.Value);
                if (res[kvp.Key] is SolidColorBrush brush && !brush.IsFrozen)
                    brush.Color = color;        // mutable: updates Static + Dynamic consumers live
                else
                    res[kvp.Key] = new SolidColorBrush(color); // frozen/missing: shadow at app level
            }

            // Retheme the gradient chrome brushes in place so secondary windows
            // (Settings/Effects) and the title bar follow the active theme too.
            // Derived from the palette's window/card colours.
            if (palette.TryGetValue("CardBgBrush", out var cardHex) &&
                palette.TryGetValue("WindowBgBrush", out var winHex))
            {
                var card = ParseColor(cardHex);
                var win = ParseColor(winHex);
                var mid = Blend(card, win, 0.5);

                SetGradientStops(res, "TitleBarGradient", card, win);
                SetGradientStops(res, "SecondaryWindowBackgroundBrush", card, mid, win);
                SetGradientStops(res, "SecondaryFooterBackgroundBrush", card, win);
            }
        }

        private static void SetGradientStops(ResourceDictionary res, string key, params Color[] colors)
        {
            if (res[key] is not LinearGradientBrush brush) return;
            if (brush.GradientStops.Count != colors.Length) return;

            if (brush.IsFrozen)
            {
                // Frozen resources can't be mutated: clone, recolour, and shadow at app level.
                var clone = brush.Clone();
                for (int i = 0; i < colors.Length; i++)
                    clone.GradientStops[i].Color = colors[i];
                res[key] = clone;
                return;
            }

            for (int i = 0; i < colors.Length; i++)
                brush.GradientStops[i].Color = colors[i];
        }

        private static Color Blend(Color a, Color b, double t)
        {
            byte Lerp(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return Color.FromArgb(Lerp(a.A, b.A), Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
        }

        /// <summary>Replaces the meter gradient brush resources for the chosen skin.</summary>
        public static void ApplyMeterSkin(string? skin)
        {
            var res = Application.Current?.Resources;
            if (res == null) return;

            (Brush inB, Brush outB, Brush monB) = (skin?.ToLowerInvariant()) switch
            {
                "8bit" => (EightBit(MeterPalette.Green), EightBit(MeterPalette.Blue), EightBit(MeterPalette.Pink)),
                "70s" => (Seventies(), Seventies(), Seventies()),
                _ => (DefaultIn(), DefaultOut(), DefaultMon()),
            };

            res["MeterInBrush"] = inB;
            res["MeterOutBrush"] = outB;
            res["MeterMonBrush"] = monB;
        }

        /// <summary>Toggles CPU-only (software) rendering and records the flag for animation guards.</summary>
        public static void ApplyPerformanceMode(bool enabled)
        {
            PerformanceMode = enabled;
            RenderOptions.ProcessRenderMode = enabled ? RenderMode.SoftwareOnly : RenderMode.Default;
        }

        // ── meter gradient factories ─────────────────────────────────────────
        private enum MeterPalette { Green, Blue, Pink }

        private static LinearGradientBrush DefaultIn()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF2E7D32"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF4CAF50"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFDD835"), 0.70));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF9800"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFF44336"), 0.95));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFD50000"), 1.0));
            return b;
        }

        private static LinearGradientBrush DefaultOut()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF1565C0"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF42A5F5"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF80DEEA"), 0.70));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFDD835"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFF44336"), 0.95));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFD50000"), 1.0));
            return b;
        }

        private static LinearGradientBrush DefaultMon()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#75C01515"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#9CF54242"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFCA23BC"), 0.70));
            b.GradientStops.Add(new GradientStop(ParseColor("#CCFF00BF"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFFFF"), 1.0));
            return b;
        }

        // Hard-stepped, limited-palette retro look (blocky segments).
        private static LinearGradientBrush EightBit(MeterPalette p)
        {
            string lo = p switch { MeterPalette.Blue => "#FF1565C0", MeterPalette.Pink => "#FFD81B8C", _ => "#FF2E7D32" };
            string mid = p switch { MeterPalette.Blue => "#FF29B6F6", MeterPalette.Pink => "#FFF06292", _ => "#FF8BC34A" };
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            void Block(string hex, double a, double z)
            {
                var c = ParseColor(hex);
                b.GradientStops.Add(new GradientStop(c, a));
                b.GradientStops.Add(new GradientStop(c, z));
            }
            Block("#FF000000", 0.0, 0.02);
            Block(lo, 0.02, 0.45);
            Block(mid, 0.45, 0.70);
            Block("#FFFFEB3B", 0.70, 0.85);
            Block("#FFF44336", 0.85, 1.0);
            return b;
        }

        // Warm vintage analog VU look.
        private static LinearGradientBrush Seventies()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF2B1A06"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF7A4B12"), 0.10));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFC07A1E"), 0.45));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFE0A030"), 0.72));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFD2691E"), 0.88));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFB22222"), 1.0));
            return b;
        }

        private static Color ParseColor(string hex)
            => (Color)ColorConverter.ConvertFromString(hex);
    }
}

using System;
using System.Linq;
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
        public static string CurrentMeterSkin { get; private set; } = "default";
        public static bool MeterDigitalDots { get; private set; } = false;
        private static double _lastWidth = 0;

        /// <summary>Display name list for the overall theme selector (key, label).</summary>
        public static readonly IReadOnlyList<(string Key, string Label)> Themes =
        [
            ("system",     "Follow System"),
            ("dark",       "Dark"),
            ("light",      "Light"),
            ("dark-green", "Dark Green"),
            ("dark-blue",  "Dark Blue"),
            ("sepia",      "Sepia"),
            ("dark-pink",  "Dark Pink"),
            ("dark-sepia", "Dark Sepia"),
            ("cyberpunk",  "Cyberpunk"),
            ("nordic-frost","Nordic Frost"),
            ("sunset",     "Sunset Glow"),
            ("deep-teal",  "Deep Teal"),
            ("dracula",    "Obsidian Purple"),
        ];

        /// <summary>Display name list for the meter skin selector (key, label).</summary>
        public static readonly IReadOnlyList<(string Key, string Label)> MeterSkins =
        [
            ("default",    "Default"),
            ("8bit",       "8-bit"),
            ("70s",        "70s Look"),
            ("neon",       "Neon"),
            ("grayscale",  "Grayscale"),
            ("inferno",    "Inferno"),
            ("aurora",     "Aurora"),
            ("cyber-sunset","Cyber Sunset"),
            ("forest",     "Forest Moss"),
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
                ["WindowBgBrush"] = "#FFF1E7D6",
                ["CardBgBrush"] = "#FFE8DECA",       // warm parchment card surface
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
                ["ControlTextBrush"] = "#FF4A3820", // warm dark brown — readable on parchment backgrounds
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
            ["dark-sepia"] = new()
            {
                ["WindowBgBrush"] = "#FF1A1512",
                ["CardBgBrush"] = "#FF28201B",
                ["CardBorderBrush"] = "#2C8A7350",
                ["SubtleTextBrush"] = "#FFA89078",
                ["PrimaryTextBrush"] = "#FFF3ECE4",
                ["SecondaryTextBrush"] = "#FFCDBAA8",
                ["AccentGreenBrush"] = "#FF8C9B6E",
                ["AccentRedBrush"] = "#FFC25944",
                ["AccentAmberBrush"] = "#FFD19C38",
                ["InputBgBrush"] = "#FF15100D",
                ["InputBorderBrush"] = "#2C8A7350",
                ["WindowEdgeBrush"] = "#2C8A7350",
                ["WindowGlowBrush"] = "#FF362C24",
                ["ControlTextBrush"] = "#FFEBE3D8",
                ["ButtonBgBrush"] = "#FF221A16",
                ["ButtonHoverBgBrush"] = "#FF362A24",
                ["ButtonPressedBgBrush"] = "#FF181310",
                ["MenuBgBrush"] = "#FF15100D",
                ["MenuHighlightBrush"] = "#FF4D3C33",
                ["MenuSelectedBrush"] = "#FF7A5C43",
                ["DividerBrush"] = "#1C8A7350",
                ["BadgeBgBrush"] = "#FF100B08",
                ["AccentTitleBrush"] = "#FFE0A96D",
                ["ChromeButtonFgBrush"] = "#FFCDBAA8",
                ["ScrollThumbBrush"] = "#FFA89078",
            },
            ["cyberpunk"] = new()
            {
                ["WindowBgBrush"] = "#FF08080C",
                ["CardBgBrush"] = "#FF12121E",
                ["CardBorderBrush"] = "#2C00F5FF",
                ["SubtleTextBrush"] = "#FF00B4D8",
                ["PrimaryTextBrush"] = "#FFE6FFFF",
                ["SecondaryTextBrush"] = "#FF90E0EF",
                ["AccentGreenBrush"] = "#FF00F5FF",
                ["AccentRedBrush"] = "#FFFF2E93",
                ["AccentAmberBrush"] = "#FFFDFF00",
                ["InputBgBrush"] = "#FF0B0B14",
                ["InputBorderBrush"] = "#2C00F5FF",
                ["WindowEdgeBrush"] = "#2C00F5FF",
                ["WindowGlowBrush"] = "#FF280C30",
                ["ControlTextBrush"] = "#FFD2FFFF",
                ["ButtonBgBrush"] = "#FF161629",
                ["ButtonHoverBgBrush"] = "#FF282845",
                ["ButtonPressedBgBrush"] = "#FF0E0E1B",
                ["MenuBgBrush"] = "#FF0D0D19",
                ["MenuHighlightBrush"] = "#FF0077B6",
                ["MenuSelectedBrush"] = "#FF0096C7",
                ["DividerBrush"] = "#1C00F5FF",
                ["BadgeBgBrush"] = "#FF08080F",
                ["AccentTitleBrush"] = "#FFFF2E93",
                ["ChromeButtonFgBrush"] = "#FF90E0EF",
                ["ScrollThumbBrush"] = "#FF00F5FF",
            },
            ["nordic-frost"] = new()
            {
                ["WindowBgBrush"] = "#FF242933",
                ["CardBgBrush"] = "#FF2E3440",
                ["CardBorderBrush"] = "#2C88C0D0",
                ["SubtleTextBrush"] = "#FF7B889B",
                ["PrimaryTextBrush"] = "#FFE5E9F0",
                ["SecondaryTextBrush"] = "#FFD8DEE9",
                ["AccentGreenBrush"] = "#FF8FBCBB",
                ["AccentRedBrush"] = "#FFBF616A",
                ["AccentAmberBrush"] = "#FFEBCB8B",
                ["InputBgBrush"] = "#FF262B35",
                ["InputBorderBrush"] = "#2C8FBCBB",
                ["WindowEdgeBrush"] = "#2C88C0D0",
                ["WindowGlowBrush"] = "#FF3B4252",
                ["ControlTextBrush"] = "#FFE5E9F0",
                ["ButtonBgBrush"] = "#FF363D4D",
                ["ButtonHoverBgBrush"] = "#FF434C5E",
                ["ButtonPressedBgBrush"] = "#FF292E3A",
                ["MenuBgBrush"] = "#FF2A2F3A",
                ["MenuHighlightBrush"] = "#FF4C566A",
                ["MenuSelectedBrush"] = "#FF81A1C1",
                ["DividerBrush"] = "#1C88C0D0",
                ["BadgeBgBrush"] = "#FF1C1F27",
                ["AccentTitleBrush"] = "#FF88C0D0",
                ["ChromeButtonFgBrush"] = "#FFD8DEE9",
                ["ScrollThumbBrush"] = "#FF81A1C1",
            },
            ["sunset"] = new()
            {
                ["WindowBgBrush"] = "#FF160D1A",
                ["CardBgBrush"] = "#FF25172C",
                ["CardBorderBrush"] = "#2CFF6B4A",
                ["SubtleTextBrush"] = "#FFA38AA8",
                ["PrimaryTextBrush"] = "#FFFDEBF5",
                ["SecondaryTextBrush"] = "#FFD2A8C1",
                ["AccentGreenBrush"] = "#FFFD7E50",
                ["AccentRedBrush"] = "#FFE03A3A",
                ["AccentAmberBrush"] = "#FFFFB300",
                ["InputBgBrush"] = "#FF1D1123",
                ["InputBorderBrush"] = "#2CFF7E50",
                ["WindowEdgeBrush"] = "#2CFF6B4A",
                ["WindowGlowBrush"] = "#FF3C1844",
                ["ControlTextBrush"] = "#FFF0D5E5",
                ["ButtonBgBrush"] = "#FF2C1A34",
                ["ButtonHoverBgBrush"] = "#FF40264B",
                ["ButtonPressedBgBrush"] = "#FF1E1123",
                ["MenuBgBrush"] = "#FF201227",
                ["MenuHighlightBrush"] = "#FF5E3368",
                ["MenuSelectedBrush"] = "#FF9C27B0",
                ["DividerBrush"] = "#1CFF7E50",
                ["BadgeBgBrush"] = "#FF130A17",
                ["AccentTitleBrush"] = "#FFFD7E50",
                ["ChromeButtonFgBrush"] = "#FFD2A8C1",
                ["ScrollThumbBrush"] = "#FFFFB300",
            },
            ["deep-teal"] = new()
            {
                ["WindowBgBrush"] = "#FF061318",
                ["CardBgBrush"] = "#FF0E2127",
                ["CardBorderBrush"] = "#2C26A896",
                ["SubtleTextBrush"] = "#FF6C8E96",
                ["PrimaryTextBrush"] = "#FFE5F6F5",
                ["SecondaryTextBrush"] = "#FFA9DFD8",
                ["AccentGreenBrush"] = "#FF26A896",
                ["AccentRedBrush"] = "#FFE57373",
                ["AccentAmberBrush"] = "#FFFFD54F",
                ["InputBgBrush"] = "#FF09191E",
                ["InputBorderBrush"] = "#2C26A896",
                ["WindowEdgeBrush"] = "#2C26A896",
                ["WindowGlowBrush"] = "#FF0F2C35",
                ["ControlTextBrush"] = "#FFD5EFEB",
                ["ButtonBgBrush"] = "#FF132E35",
                ["ButtonHoverBgBrush"] = "#FF1B3F4A",
                ["ButtonPressedBgBrush"] = "#FF0A1D21",
                ["MenuBgBrush"] = "#FF0D252B",
                ["MenuHighlightBrush"] = "#FF1C4E5C",
                ["MenuSelectedBrush"] = "#FF008B8B",
                ["DividerBrush"] = "#1C26A896",
                ["BadgeBgBrush"] = "#FF040E11",
                ["AccentTitleBrush"] = "#FF00B4D8",
                ["ChromeButtonFgBrush"] = "#FFA9DFD8",
                ["ScrollThumbBrush"] = "#FF00B4D8",
            },
            ["dracula"] = new()
            {
                ["WindowBgBrush"] = "#FF110F18",
                ["CardBgBrush"] = "#FF1B1828",
                ["CardBorderBrush"] = "#2CBD7AF5",
                ["SubtleTextBrush"] = "#FF8C84A8",
                ["PrimaryTextBrush"] = "#FFF0ECF8",
                ["SecondaryTextBrush"] = "#FFCABEEA",
                ["AccentGreenBrush"] = "#FFBD7AF5",
                ["AccentRedBrush"] = "#FFFF5555",
                ["AccentAmberBrush"] = "#FFF1FA8C",
                ["InputBgBrush"] = "#FF15121F",
                ["InputBorderBrush"] = "#2CBD7AF5",
                ["WindowEdgeBrush"] = "#2CBD7AF5",
                ["WindowGlowBrush"] = "#FF291A38",
                ["ControlTextBrush"] = "#FFE2DAF2",
                ["ButtonBgBrush"] = "#FF221E33",
                ["ButtonHoverBgBrush"] = "#FF302B47",
                ["ButtonPressedBgBrush"] = "#FF181524",
                ["MenuBgBrush"] = "#FF1A1729",
                ["MenuHighlightBrush"] = "#FF443C68",
                ["MenuSelectedBrush"] = "#FF7B2CBF",
                ["DividerBrush"] = "#1CBD7AF5",
                ["BadgeBgBrush"] = "#FF0E0C14",
                ["AccentTitleBrush"] = "#FFFF79C6",
                ["ChromeButtonFgBrush"] = "#FFCABEEA",
                ["ScrollThumbBrush"] = "#FFBD7AF5",
            },
        };

        /// <summary>Returns the color palette dictionary for a given theme key, resolving 'system' automatically.</summary>
        public static Dictionary<string, string> GetPalette(string? themeKey)
        {
            if (string.IsNullOrWhiteSpace(themeKey) || themeKey == "system")
            {
                themeKey = IsWindowsDarkTheme() ? "dark" : "light";
            }

            if (Palettes.TryGetValue(themeKey, out var palette))
            {
                return palette;
            }

            return Palettes[DefaultTheme];
        }

        /// <summary>Applies an overall colour theme by mutating the shared brush resources.</summary>
        public static void ApplyTheme(string? themeKey)
        {
            if (string.IsNullOrWhiteSpace(themeKey))
                themeKey = DefaultTheme;

            string targetTheme = themeKey;
            if (themeKey == "system")
            {
                targetTheme = IsWindowsDarkTheme() ? "dark" : "light";
            }

            if (!Palettes.ContainsKey(targetTheme))
                targetTheme = DefaultTheme;

            var palette = Palettes[targetTheme];
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

        /// <summary>Queries the Windows registry to check if the OS theme is set to Dark.</summary>
        public static bool IsWindowsDarkTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var registryValueObject = key?.GetValue("AppsUseLightTheme");
                    if (registryValueObject != null)
                    {
                        return (int)registryValueObject == 0;
                    }
                }
            }
            catch
            {
                // Fallback to dark theme on error
            }
            return true;
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

        public static void ApplyMeterSkin(string? skin, bool digitalDots = false)
        {
            CurrentMeterSkin = skin?.ToLowerInvariant() ?? "default";
            MeterDigitalDots = digitalDots;
            var res = Application.Current?.Resources;
            if (res == null) return;

            (Brush inB, Brush outB, Brush monB) = CurrentMeterSkin switch
            {
                "8bit"      => (EightBit(MeterPalette.Green), EightBit(MeterPalette.Blue), EightBit(MeterPalette.Pink)),
                "70s"       => (Seventies(), Seventies(), Seventies()),
                "neon"      => (NeonCyan(), NeonMagenta(), NeonYellow()),
                "grayscale" => (Grayscale(), Grayscale(), Grayscale()),
                "inferno"   => (Inferno(), Inferno(), Inferno()),
                "aurora"    => (Aurora(), Aurora(), Aurora()),
                "cyber-sunset" => (CyberSunset(), CyberSunset(), CyberSunset()),
                "forest"    => (ForestMoss(), ForestMoss(), ForestMoss()),
                "toxic"     => (Toxic(), Toxic(), Toxic()),
                _           => (DefaultIn(), DefaultOut(), DefaultMon()),
            };

            if (digitalDots && _lastWidth > 0)
            {
                inB = QuantizeGradient((LinearGradientBrush)inB, _lastWidth);
                outB = QuantizeGradient((LinearGradientBrush)outB, _lastWidth);
                monB = QuantizeGradient((LinearGradientBrush)monB, _lastWidth);
            }

            res["MeterInBrush"] = inB;
            res["MeterOutBrush"] = outB;
            res["MeterMonBrush"] = monB;
        }

        public static void UpdateMeterSkinSize(double width)
        {
            _lastWidth = width;
            if (MeterDigitalDots)
            {
                ApplyMeterSkin(CurrentMeterSkin, MeterDigitalDots);
            }
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

        // Electric synthwave: cyan glow — used for the "In" channel in Neon skin.
        private static LinearGradientBrush NeonCyan()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF004D5E"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00BCD4"), 0.40));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF80FFFF"), 0.72));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFF00"), 0.88));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF4081"), 0.96));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF0000"), 1.0));
            return b;
        }

        // Electric synthwave: magenta glow — used for the "Out" channel in Neon skin.
        private static LinearGradientBrush NeonMagenta()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF5C0050"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFEE00CC"), 0.40));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF80F0"), 0.72));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFF00"), 0.88));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF4081"), 0.96));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF0000"), 1.0));
            return b;
        }

        // Electric synthwave: acid yellow — used for the "Mon" channel in Neon skin.
        private static LinearGradientBrush NeonYellow()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF3D3D00"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFCCFF00"), 0.40));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFF80"), 0.72));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF8800"), 0.88));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF2200"), 0.96));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF0000"), 1.0));
            return b;
        }

        // Clean monochrome professional look — all channels share the same gradient.
        private static LinearGradientBrush Grayscale()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF1A1A1A"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF606060"), 0.40));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFBDBDBD"), 0.72));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFE0E0E0"), 0.88));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFFFF"), 0.96));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFCCCCCC"), 1.0));
            return b;
        }

        // Volcanic hot inferno gradient.
        private static LinearGradientBrush Inferno()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF100000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF7F0000"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFD50000"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF5500"), 0.70));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFB300"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFE080"), 0.95));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFFFF"), 1.0));
            return b;
        }

        // Ethereal northern lights gradient.
        private static LinearGradientBrush Aurora()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF080010"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF3B0066"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00B48F"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00E5FF"), 0.70));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF80FFFF"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFAAF0FF"), 0.95));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFFFF"), 1.0));
            return b;
        }

        // Synthwave sunset gradient.
        private static LinearGradientBrush CyberSunset()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF050010"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF240046"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF7B2CBF"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFE0115F"), 0.65));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFD7E50"), 0.82));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFB300"), 0.94));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFDFF00"), 1.0));
            return b;
        }

        // Deep organic forest moss gradient.
        private static LinearGradientBrush ForestMoss()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF051007"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF1B382B"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF2D6A4F"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF52B788"), 0.70));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF74C69D"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFB7E4C7"), 0.95));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFE8F5E9"), 1.0));
            return b;
        }

        // Radioactive high-voltage gradient.
        private static LinearGradientBrush Toxic()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF000B05"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF003810"), 0.015));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00E676"), 0.40));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFCCFF00"), 0.75));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFD00"), 0.88));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF007F"), 0.96));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF00CC"), 1.0));
            return b;
        }

        // Converts a continuous gradient into segmented discrete dots, snapping to integer pixels
        private static Brush QuantizeGradient(LinearGradientBrush original, double width)
        {
            double gapRatio = 0.2;
            int blockWidth = 14;
            int numBlocks = Math.Max(1, (int)(width / blockWidth));

            Color GetColor(double t)
            {
                var stops = original.GradientStops.OrderBy(s => s.Offset).ToList();
                if (stops.Count == 0) return ParseColor("#FFFFFFFF");
                if (stops.Count == 1) return stops[0].Color;

                for (int i = 0; i < stops.Count - 1; i++)
                {
                    if (t >= stops[i].Offset && t <= stops[i + 1].Offset)
                    {
                        double range = stops[i + 1].Offset - stops[i].Offset;
                        double localT = range == 0 ? 0 : (t - stops[i].Offset) / range;
                        return Blend(stops[i].Color, stops[i + 1].Color, localT);
                    }
                }
                return stops[^1].Color;
            }

            var drawingGroup = new DrawingGroup();
            
            // Solid black background to cover the gaps and remaining space
            drawingGroup.Children.Add(new GeometryDrawing(
                new SolidColorBrush(ParseColor("#FF000000")), 
                null, 
                new RectangleGeometry(new Rect(0, 0, width, 100))));

            for (int i = 0; i < numBlocks; i++)
            {
                int startP = i * blockWidth;
                int ledEndP = startP + (int)Math.Round(blockWidth * (1 - gapRatio));
                
                double centerT = (startP + ledEndP) * 0.5 / width;
                Color c = GetColor(centerT);
                
                var rect = new Rect(startP, 0, ledEndP - startP, 100);
                var drawing = new GeometryDrawing(new SolidColorBrush(c), null, new RectangleGeometry(rect));
                drawingGroup.Children.Add(drawing);
            }
            
            var brush = new DrawingBrush(drawingGroup)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
                TileMode = TileMode.None,
                Viewport = new Rect(0, 0, width, 100),
                ViewportUnits = BrushMappingMode.Absolute
            };
            
            RenderOptions.SetEdgeMode(brush, EdgeMode.Aliased);
            return brush;
        }

        private static Color ParseColor(string hex)
            => (Color)ColorConverter.ConvertFromString(hex);
    }
}

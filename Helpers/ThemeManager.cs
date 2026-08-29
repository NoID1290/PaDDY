using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        public static bool AnimationsPaused { get; private set; }
        public static string CurrentMeterSkin { get; private set; } = "default";
        public static bool MeterDigitalDots { get; private set; } = false;
        private static double _lastWidth = 0;

        /// <summary>Fired when background animations are paused or resumed.</summary>
        public static event Action? AnimationsPausedChanged;

        /// <summary>Fired when the application theme is changed/reapplied.</summary>
        public static event Action? ThemeChanged;

        /// <summary>Display name list for the overall theme selector (key, label).</summary>
        public static readonly IReadOnlyList<(string Key, string Label)> Themes =
        [
            ("system",          "Follow System"),
            ("dark",            "Dark"),
            ("light",           "Light"),
            ("dark-green",      "Dark Green"),
            ("dark-blue",       "Dark Blue"),
            ("sepia",           "Sepia"),
            ("dark-pink",       "Dark Pink"),
            ("dark-sepia",      "Dark Sepia"),
            ("cyberpunk",       "Cyberpunk"),
            ("nordic-frost",    "Nordic Frost"),
            ("sunset",          "Sunset Glow"),
            ("deep-teal",       "Deep Teal"),
            ("dracula",         "Obsidian Purple"),
            ("vista-aero",      "Windows Vista Aero"),
            ("windows-xp",      "Windows XP"),
            ("windows-98",      "Windows 98"),
            ("midnight-oled",   "Midnight OLED"),
            ("emerald-matrix",  "Emerald Matrix"),
            ("amethyst-night",  "Amethyst Night"),
            ("tokyo-neon",      "Tokyo Neon"),
            ("solarized-dark",  "Solarized Dark"),
            ("rose-gold",       "Rose Gold & Charcoal"),
            ("ocean-abyss",     "Ocean Abyss"),
            ("crimson-ember",   "Crimson Ember"),
            ("pastel-dream",    "Pastel Dream"),
            ("mocha-latte",     "Mocha Latte"),
            ("acid-cyber",      "Acid Cyber"),
            ("monochrome-slate","Monochrome Slate"),
            ("synthwave-80s",   "Synthwave 80s"),
            ("bioluminescence", "Bioluminescence"),
            ("arctic-ice",      "Arctic Ice"),
        ];

        /// <summary>Display name list for the meter skin selector (key, label).</summary>
        public static readonly IReadOnlyList<(string Key, string Label)> MeterSkins =
        [
            ("default",     "Default"),
            ("8bit",        "8-bit"),
            ("70s",         "70s Look"),
            ("neon",        "Neon"),
            ("grayscale",   "Grayscale"),
            ("inferno",     "Inferno"),
            ("aurora",      "Aurora"),
            ("cyber-sunset","Cyber Sunset"),
            ("forest",      "Forest Moss"),
            ("toxic",       "Toxic"),
            ("vaporwave",   "Vaporwave"),
            ("plasma",      "Plasma Fire"),
            ("matrix",      "Cyber Matrix"),
            ("solar-flare", "Solar Flare"),
            ("ocean-wave",  "Ocean Wave"),
            ("sunset-strip","Synthwave Sunset"),
            ("vintage-led", "Vintage LED Studio"),
            ("acid-lime",   "Acid Lime"),
            ("blood-moon",  "Blood Moon"),
            ("rainbow",     "Rainbow Spectrum"),
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
            ["vista-aero"] = new()
            {
                ["WindowBgBrush"] = "#8010263A",
                ["CardBgBrush"] = "#90183850",
                ["CardBorderBrush"] = "#7088CEFA",
                ["SubtleTextBrush"] = "#FF84C4EE",
                ["PrimaryTextBrush"] = "#FFFFFFFF",
                ["SecondaryTextBrush"] = "#FFD0EEFF",
                ["AccentGreenBrush"] = "#FF32B814",
                ["AccentRedBrush"] = "#FFE81123",
                ["AccentAmberBrush"] = "#FFFF9900",
                ["InputBgBrush"] = "#680B1A28",
                ["InputBorderBrush"] = "#6078C8F0",
                ["WindowEdgeBrush"] = "#809CE0FF",
                ["WindowGlowBrush"] = "#75163C5D",
                ["ControlTextBrush"] = "#FFEBF6FF",
                ["ButtonBgBrush"] = "#851C3F5A",
                ["ButtonHoverBgBrush"] = "#BB2C6A94",
                ["ButtonPressedBgBrush"] = "#D50E263B",
                ["MenuBgBrush"] = "#E6122B40",
                ["MenuHighlightBrush"] = "#FF1E6FBA",
                ["MenuSelectedBrush"] = "#FF1058A2",
                ["DividerBrush"] = "#4080D0FF",
                ["BadgeBgBrush"] = "#800B1A28",
                ["AccentTitleBrush"] = "#FF46C8FF",
                ["ChromeButtonFgBrush"] = "#FFD4EEFF",
                ["ScrollThumbBrush"] = "#994AA5E0",
            },
            ["windows-xp"] = new()
            {
                ["WindowBgBrush"] = "#FFEBF3FC",
                ["CardBgBrush"] = "#FFFFFFFF",
                ["CardBorderBrush"] = "#FF0055EA",
                ["SubtleTextBrush"] = "#FF3A5A8C",
                ["PrimaryTextBrush"] = "#FF0F1D38",
                ["SecondaryTextBrush"] = "#FF284878",
                ["AccentGreenBrush"] = "#FF3CA028",
                ["AccentRedBrush"] = "#FFE81123",
                ["AccentAmberBrush"] = "#FFFF9900",
                ["InputBgBrush"] = "#FFFFFFFF",
                ["InputBorderBrush"] = "#FF7B9EBD",
                ["WindowEdgeBrush"] = "#FF0055EA",
                ["WindowGlowBrush"] = "#FFC5DBF7",
                ["ControlTextBrush"] = "#FF0F1D38",
                ["ButtonBgBrush"] = "#FFF4F8FF",
                ["ButtonHoverBgBrush"] = "#FFD0E5FE",
                ["ButtonPressedBgBrush"] = "#FFB9D1F3",
                ["MenuBgBrush"] = "#FFFFFFFF",
                ["MenuHighlightBrush"] = "#FF316AC5",
                ["MenuSelectedBrush"] = "#FF0055EA",
                ["DividerBrush"] = "#FFB5C7DE",
                ["BadgeBgBrush"] = "#FFD4D4D4",
                ["AccentTitleBrush"] = "#FF000080",
                ["ChromeButtonFgBrush"] = "#FF000000",
                ["ScrollThumbBrush"] = "#FFC0C0C0",
            },
            ["midnight-oled"] = new()
            {
                ["WindowBgBrush"] = "#FF000000",
                ["CardBgBrush"] = "#FF0C0C0E",
                ["CardBorderBrush"] = "#2CFFB300",
                ["SubtleTextBrush"] = "#FF808090",
                ["PrimaryTextBrush"] = "#FFFFFFFF",
                ["SecondaryTextBrush"] = "#FFD0D0E0",
                ["AccentGreenBrush"] = "#FF00E676",
                ["AccentRedBrush"] = "#FFFF3333",
                ["AccentAmberBrush"] = "#FFFFB300",
                ["InputBgBrush"] = "#FF060608",
                ["InputBorderBrush"] = "#2CFFB300",
                ["WindowEdgeBrush"] = "#2CFFB300",
                ["WindowGlowBrush"] = "#FF201A00",
                ["ControlTextBrush"] = "#FFFFE0A0",
                ["ButtonBgBrush"] = "#FF14141A",
                ["ButtonHoverBgBrush"] = "#FF242430",
                ["ButtonPressedBgBrush"] = "#FF0A0A0E",
                ["MenuBgBrush"] = "#FF08080C",
                ["MenuHighlightBrush"] = "#FF4A3800",
                ["MenuSelectedBrush"] = "#FF806000",
                ["DividerBrush"] = "#1CFFB300",
                ["BadgeBgBrush"] = "#FF050505",
                ["AccentTitleBrush"] = "#FFFFC107",
                ["ChromeButtonFgBrush"] = "#FFD0D0E0",
                ["ScrollThumbBrush"] = "#FFFFB300",
            },
            ["emerald-matrix"] = new()
            {
                ["WindowBgBrush"] = "#FF040B06",
                ["CardBgBrush"] = "#FF09160E",
                ["CardBorderBrush"] = "#2C00FF66",
                ["SubtleTextBrush"] = "#FF40A068",
                ["PrimaryTextBrush"] = "#FFE0FFE8",
                ["SecondaryTextBrush"] = "#FFA0E6B8",
                ["AccentGreenBrush"] = "#FF00FF66",
                ["AccentRedBrush"] = "#FFFF4444",
                ["AccentAmberBrush"] = "#FFFFC107",
                ["InputBgBrush"] = "#FF061009",
                ["InputBorderBrush"] = "#2C00FF66",
                ["WindowEdgeBrush"] = "#2C00FF66",
                ["WindowGlowBrush"] = "#FF082814",
                ["ControlTextBrush"] = "#FFB0F5C8",
                ["ButtonBgBrush"] = "#FF0E2216",
                ["ButtonHoverBgBrush"] = "#FF163422",
                ["ButtonPressedBgBrush"] = "#FF08140C",
                ["MenuBgBrush"] = "#FF06100A",
                ["MenuHighlightBrush"] = "#FF144828",
                ["MenuSelectedBrush"] = "#FF1E703E",
                ["DividerBrush"] = "#1C00FF66",
                ["BadgeBgBrush"] = "#FF030804",
                ["AccentTitleBrush"] = "#FF00FF66",
                ["ChromeButtonFgBrush"] = "#FFA0E6B8",
                ["ScrollThumbBrush"] = "#FF00FF66",
            },
            ["amethyst-night"] = new()
            {
                ["WindowBgBrush"] = "#FF0F0A1A",
                ["CardBgBrush"] = "#FF181028",
                ["CardBorderBrush"] = "#2CA855F7",
                ["SubtleTextBrush"] = "#FF8B6AA8",
                ["PrimaryTextBrush"] = "#FFF5F0FF",
                ["SecondaryTextBrush"] = "#FFD8B4FE",
                ["AccentGreenBrush"] = "#FFA855F7",
                ["AccentRedBrush"] = "#FFFF4B4B",
                ["AccentAmberBrush"] = "#FFFFB800",
                ["InputBgBrush"] = "#FF120C1F",
                ["InputBorderBrush"] = "#2CA855F7",
                ["WindowEdgeBrush"] = "#2CA855F7",
                ["WindowGlowBrush"] = "#FF2C1448",
                ["ControlTextBrush"] = "#FFE8D5FE",
                ["ButtonBgBrush"] = "#FF221638",
                ["ButtonHoverBgBrush"] = "#FF322050",
                ["ButtonPressedBgBrush"] = "#FF140E22",
                ["MenuBgBrush"] = "#FF120C20",
                ["MenuHighlightBrush"] = "#FF4A2478",
                ["MenuSelectedBrush"] = "#FF7C3AED",
                ["DividerBrush"] = "#1CA855F7",
                ["BadgeBgBrush"] = "#FF0B0713",
                ["AccentTitleBrush"] = "#FFC084FC",
                ["ChromeButtonFgBrush"] = "#FFD8B4FE",
                ["ScrollThumbBrush"] = "#FFA855F7",
            },
            ["tokyo-neon"] = new()
            {
                ["WindowBgBrush"] = "#FF0A0E1A",
                ["CardBgBrush"] = "#FF121A2D",
                ["CardBorderBrush"] = "#2CFF007F",
                ["SubtleTextBrush"] = "#FF5D8AA8",
                ["PrimaryTextBrush"] = "#FFF0F8FF",
                ["SecondaryTextBrush"] = "#FFB8E6FF",
                ["AccentGreenBrush"] = "#FF00F0FF",
                ["AccentRedBrush"] = "#FFFF007F",
                ["AccentAmberBrush"] = "#FFFFE600",
                ["InputBgBrush"] = "#FF0E1424",
                ["InputBorderBrush"] = "#2C00F0FF",
                ["WindowEdgeBrush"] = "#2CFF007F",
                ["WindowGlowBrush"] = "#FF28103A",
                ["ControlTextBrush"] = "#FFD0F0FF",
                ["ButtonBgBrush"] = "#FF18243C",
                ["ButtonHoverBgBrush"] = "#FF243454",
                ["ButtonPressedBgBrush"] = "#FF101828",
                ["MenuBgBrush"] = "#FF0E1424",
                ["MenuHighlightBrush"] = "#FF005580",
                ["MenuSelectedBrush"] = "#FFCC0066",
                ["DividerBrush"] = "#1C00F0FF",
                ["BadgeBgBrush"] = "#FF070B14",
                ["AccentTitleBrush"] = "#FFFF007F",
                ["ChromeButtonFgBrush"] = "#FFB8E6FF",
                ["ScrollThumbBrush"] = "#FF00F0FF",
            },
            ["solarized-dark"] = new()
            {
                ["WindowBgBrush"] = "#FF002B36",
                ["CardBgBrush"] = "#FF073642",
                ["CardBorderBrush"] = "#2C2AA198",
                ["SubtleTextBrush"] = "#FF586E75",
                ["PrimaryTextBrush"] = "#FF93A1A1",
                ["SecondaryTextBrush"] = "#FF839496",
                ["AccentGreenBrush"] = "#FF2AA198",
                ["AccentRedBrush"] = "#FFDC322F",
                ["AccentAmberBrush"] = "#FFB58900",
                ["InputBgBrush"] = "#FF00212B",
                ["InputBorderBrush"] = "#2C2AA198",
                ["WindowEdgeBrush"] = "#2C2AA198",
                ["WindowGlowBrush"] = "#FF0D4855",
                ["ControlTextBrush"] = "#FF93A1A1",
                ["ButtonBgBrush"] = "#FF0A404E",
                ["ButtonHoverBgBrush"] = "#FF115262",
                ["ButtonPressedBgBrush"] = "#FF052B34",
                ["MenuBgBrush"] = "#FF042731",
                ["MenuHighlightBrush"] = "#FF165E6D",
                ["MenuSelectedBrush"] = "#FF268BD2",
                ["DividerBrush"] = "#1C2AA198",
                ["BadgeBgBrush"] = "#FF001B22",
                ["AccentTitleBrush"] = "#FF268BD2",
                ["ChromeButtonFgBrush"] = "#FF839496",
                ["ScrollThumbBrush"] = "#FF2AA198",
            },
            ["rose-gold"] = new()
            {
                ["WindowBgBrush"] = "#FF141214",
                ["CardBgBrush"] = "#FF201C20",
                ["CardBorderBrush"] = "#2CE0A996",
                ["SubtleTextBrush"] = "#FFA68A92",
                ["PrimaryTextBrush"] = "#FFF8F0F2",
                ["SecondaryTextBrush"] = "#FFE2CFD4",
                ["AccentGreenBrush"] = "#FFE0A996",
                ["AccentRedBrush"] = "#FFA64444",
                ["AccentAmberBrush"] = "#FFE6A15C",
                ["InputBgBrush"] = "#FF1A171A",
                ["InputBorderBrush"] = "#2CE0A996",
                ["WindowEdgeBrush"] = "#2CE0A996",
                ["WindowGlowBrush"] = "#FF322226",
                ["ControlTextBrush"] = "#FFEEE0E4",
                ["ButtonBgBrush"] = "#FF282328",
                ["ButtonHoverBgBrush"] = "#FF383038",
                ["ButtonPressedBgBrush"] = "#FF1A171A",
                ["MenuBgBrush"] = "#FF181518",
                ["MenuHighlightBrush"] = "#FF503A40",
                ["MenuSelectedBrush"] = "#FF8C5A66",
                ["DividerBrush"] = "#1CE0A996",
                ["BadgeBgBrush"] = "#FF0E0C0E",
                ["AccentTitleBrush"] = "#FFE0A996",
                ["ChromeButtonFgBrush"] = "#FFE2CFD4",
                ["ScrollThumbBrush"] = "#FFE0A996",
            },
            ["ocean-abyss"] = new()
            {
                ["WindowBgBrush"] = "#FF060D18",
                ["CardBgBrush"] = "#FF0C182B",
                ["CardBorderBrush"] = "#2C00D2FF",
                ["SubtleTextBrush"] = "#FF4682B4",
                ["PrimaryTextBrush"] = "#FFE6F7FF",
                ["SecondaryTextBrush"] = "#FF99DDFF",
                ["AccentGreenBrush"] = "#FF00D2FF",
                ["AccentRedBrush"] = "#FFFF4848",
                ["AccentAmberBrush"] = "#FFFFB400",
                ["InputBgBrush"] = "#FF081220",
                ["InputBorderBrush"] = "#2C00D2FF",
                ["WindowEdgeBrush"] = "#2C00D2FF",
                ["WindowGlowBrush"] = "#FF0A2645",
                ["ControlTextBrush"] = "#FFC8EEFF",
                ["ButtonBgBrush"] = "#FF12223B",
                ["ButtonHoverBgBrush"] = "#FF1A3052",
                ["ButtonPressedBgBrush"] = "#FF091628",
                ["MenuBgBrush"] = "#FF081424",
                ["MenuHighlightBrush"] = "#FF164470",
                ["MenuSelectedBrush"] = "#FF0077B6",
                ["DividerBrush"] = "#1C00D2FF",
                ["BadgeBgBrush"] = "#FF040810",
                ["AccentTitleBrush"] = "#FF00E5FF",
                ["ChromeButtonFgBrush"] = "#FF99DDFF",
                ["ScrollThumbBrush"] = "#FF00D2FF",
            },
            ["crimson-ember"] = new()
            {
                ["WindowBgBrush"] = "#FF120A0A",
                ["CardBgBrush"] = "#FF1E1010",
                ["CardBorderBrush"] = "#2CFF3344",
                ["SubtleTextBrush"] = "#FFB25959",
                ["PrimaryTextBrush"] = "#FFFEE6E6",
                ["SecondaryTextBrush"] = "#FFFAA8A8",
                ["AccentGreenBrush"] = "#FFFF5500",
                ["AccentRedBrush"] = "#FFFF3344",
                ["AccentAmberBrush"] = "#FFFFB300",
                ["InputBgBrush"] = "#FF160C0C",
                ["InputBorderBrush"] = "#2CFF3344",
                ["WindowEdgeBrush"] = "#2CFF3344",
                ["WindowGlowBrush"] = "#FF3A1014",
                ["ControlTextBrush"] = "#FFFCD4D4",
                ["ButtonBgBrush"] = "#FF281515",
                ["ButtonHoverBgBrush"] = "#FF3A1E1E",
                ["ButtonPressedBgBrush"] = "#FF160B0B",
                ["MenuBgBrush"] = "#FF140A0A",
                ["MenuHighlightBrush"] = "#FF541B1B",
                ["MenuSelectedBrush"] = "#FF8B1A1A",
                ["DividerBrush"] = "#1CFF3344",
                ["BadgeBgBrush"] = "#FF0A0505",
                ["AccentTitleBrush"] = "#FFFF3344",
                ["ChromeButtonFgBrush"] = "#FFFAA8A8",
                ["ScrollThumbBrush"] = "#FFFF3344",
            },
            ["pastel-dream"] = new()
            {
                ["WindowBgBrush"] = "#FFF9F6FF",
                ["CardBgBrush"] = "#FFFFFFFF",
                ["CardBorderBrush"] = "#33D8B4FE",
                ["SubtleTextBrush"] = "#FF9370DB",
                ["PrimaryTextBrush"] = "#FF2D1F3F",
                ["SecondaryTextBrush"] = "#FF5B4575",
                ["AccentGreenBrush"] = "#FFA855F7",
                ["AccentRedBrush"] = "#FFF472B6",
                ["AccentAmberBrush"] = "#FFF59E0B",
                ["InputBgBrush"] = "#FFF5F0FF",
                ["InputBorderBrush"] = "#44C084FC",
                ["WindowEdgeBrush"] = "#33D8B4FE",
                ["WindowGlowBrush"] = "#FFE9D8FF",
                ["ControlTextBrush"] = "#FF2D1F3F",
                ["ButtonBgBrush"] = "#FFFFFFFF",
                ["ButtonHoverBgBrush"] = "#FFF3E8FF",
                ["ButtonPressedBgBrush"] = "#FFE9D5FF",
                ["MenuBgBrush"] = "#FFFFFFFF",
                ["MenuHighlightBrush"] = "#FFF3E8FF",
                ["MenuSelectedBrush"] = "#FFE9D5FF",
                ["DividerBrush"] = "#1CA855F7",
                ["BadgeBgBrush"] = "#FFF0E6FF",
                ["AccentTitleBrush"] = "#FFA855F7",
                ["ChromeButtonFgBrush"] = "#FF5B4575",
                ["ScrollThumbBrush"] = "#FFC084FC",
            },
            ["mocha-latte"] = new()
            {
                ["WindowBgBrush"] = "#FF1A1412",
                ["CardBgBrush"] = "#FF281E1A",
                ["CardBorderBrush"] = "#2CD4A373",
                ["SubtleTextBrush"] = "#FFA89280",
                ["PrimaryTextBrush"] = "#FFFDF8F5",
                ["SecondaryTextBrush"] = "#FFE6D5C3",
                ["AccentGreenBrush"] = "#FFD4A373",
                ["AccentRedBrush"] = "#FFC84B31",
                ["AccentAmberBrush"] = "#FFE09F3E",
                ["InputBgBrush"] = "#FF16100E",
                ["InputBorderBrush"] = "#2CD4A373",
                ["WindowEdgeBrush"] = "#2CD4A373",
                ["WindowGlowBrush"] = "#FF3C2A22",
                ["ControlTextBrush"] = "#FFF5E8DC",
                ["ButtonBgBrush"] = "#FF322621",
                ["ButtonHoverBgBrush"] = "#FF44342D",
                ["ButtonPressedBgBrush"] = "#FF1E1613",
                ["MenuBgBrush"] = "#FF1C1412",
                ["MenuHighlightBrush"] = "#FF594237",
                ["MenuSelectedBrush"] = "#FF8C634F",
                ["DividerBrush"] = "#1CD4A373",
                ["BadgeBgBrush"] = "#FF100B09",
                ["AccentTitleBrush"] = "#FFD4A373",
                ["ChromeButtonFgBrush"] = "#FFE6D5C3",
                ["ScrollThumbBrush"] = "#FFD4A373",
            },
            ["acid-cyber"] = new()
            {
                ["WindowBgBrush"] = "#FF0A0F0D",
                ["CardBgBrush"] = "#FF121C18",
                ["CardBorderBrush"] = "#2CCCFF00",
                ["SubtleTextBrush"] = "#FF458A38",
                ["PrimaryTextBrush"] = "#FFE8FFD0",
                ["SecondaryTextBrush"] = "#FFA3FF54",
                ["AccentGreenBrush"] = "#FFCCFF00",
                ["AccentRedBrush"] = "#FFFF1744",
                ["AccentAmberBrush"] = "#FFFFEA00",
                ["InputBgBrush"] = "#FF0B1411",
                ["InputBorderBrush"] = "#2CCCFF00",
                ["WindowEdgeBrush"] = "#2CCCFF00",
                ["WindowGlowBrush"] = "#FF183020",
                ["ControlTextBrush"] = "#FFD5FF9E",
                ["ButtonBgBrush"] = "#FF182822",
                ["ButtonHoverBgBrush"] = "#FF223A31",
                ["ButtonPressedBgBrush"] = "#FF0E1814",
                ["MenuBgBrush"] = "#FF0C1613",
                ["MenuHighlightBrush"] = "#FF2E541C",
                ["MenuSelectedBrush"] = "#FF4B8517",
                ["DividerBrush"] = "#1CCCFF00",
                ["BadgeBgBrush"] = "#FF060A08",
                ["AccentTitleBrush"] = "#FFCCFF00",
                ["ChromeButtonFgBrush"] = "#FFA3FF54",
                ["ScrollThumbBrush"] = "#FFCCFF00",
            },
            ["monochrome-slate"] = new()
            {
                ["WindowBgBrush"] = "#FF141619",
                ["CardBgBrush"] = "#FF1E2228",
                ["CardBorderBrush"] = "#2C94A1B0",
                ["SubtleTextBrush"] = "#FF64748B",
                ["PrimaryTextBrush"] = "#FFF8FAFC",
                ["SecondaryTextBrush"] = "#FFCBD5E1",
                ["AccentGreenBrush"] = "#FFE2E8F0",
                ["AccentRedBrush"] = "#FFEF4444",
                ["AccentAmberBrush"] = "#FFF59E0B",
                ["InputBgBrush"] = "#FF101215",
                ["InputBorderBrush"] = "#2C94A1B0",
                ["WindowEdgeBrush"] = "#2C94A1B0",
                ["WindowGlowBrush"] = "#FF2D333B",
                ["ControlTextBrush"] = "#FFE2E8F0",
                ["ButtonBgBrush"] = "#FF282E36",
                ["ButtonHoverBgBrush"] = "#FF363E48",
                ["ButtonPressedBgBrush"] = "#FF1A1E24",
                ["MenuBgBrush"] = "#FF161A1F",
                ["MenuHighlightBrush"] = "#FF3A4452",
                ["MenuSelectedBrush"] = "#FF475569",
                ["DividerBrush"] = "#1C94A1B0",
                ["BadgeBgBrush"] = "#FF0E1012",
                ["AccentTitleBrush"] = "#FFE2E8F0",
                ["ChromeButtonFgBrush"] = "#FFCBD5E1",
                ["ScrollThumbBrush"] = "#FF94A1B0",
            },
            ["synthwave-80s"] = new()
            {
                ["WindowBgBrush"] = "#FF160924",
                ["CardBgBrush"] = "#FF241038",
                ["CardBorderBrush"] = "#2CFF00D6",
                ["SubtleTextBrush"] = "#FFAA55CC",
                ["PrimaryTextBrush"] = "#FFFFF0FA",
                ["SecondaryTextBrush"] = "#FFFAA6EC",
                ["AccentGreenBrush"] = "#FFFF00D6",
                ["AccentRedBrush"] = "#FF8A00FF",
                ["AccentAmberBrush"] = "#FFFFD000",
                ["InputBgBrush"] = "#FF1A0B2C",
                ["InputBorderBrush"] = "#2CFF00D6",
                ["WindowEdgeBrush"] = "#2CFF00D6",
                ["WindowGlowBrush"] = "#FF401050",
                ["ControlTextBrush"] = "#FFFCD4F6",
                ["ButtonBgBrush"] = "#FF30164A",
                ["ButtonHoverBgBrush"] = "#FF421E66",
                ["ButtonPressedBgBrush"] = "#FF1D0C30",
                ["MenuBgBrush"] = "#FF1C0A30",
                ["MenuHighlightBrush"] = "#FF5E1E78",
                ["MenuSelectedBrush"] = "#FF9900B3",
                ["DividerBrush"] = "#1CFF00D6",
                ["BadgeBgBrush"] = "#FF0F0519",
                ["AccentTitleBrush"] = "#FF00F5FF",
                ["ChromeButtonFgBrush"] = "#FFFAA6EC",
                ["ScrollThumbBrush"] = "#FFFF00D6",
            },
            ["bioluminescence"] = new()
            {
                ["WindowBgBrush"] = "#FF041214",
                ["CardBgBrush"] = "#FF0A2024",
                ["CardBorderBrush"] = "#2C00FFC5",
                ["SubtleTextBrush"] = "#FF409B8A",
                ["PrimaryTextBrush"] = "#FFE6FFFF",
                ["SecondaryTextBrush"] = "#FFA8F5E5",
                ["AccentGreenBrush"] = "#FF00FFC5",
                ["AccentRedBrush"] = "#FFFF5252",
                ["AccentAmberBrush"] = "#FFFFD600",
                ["InputBgBrush"] = "#FF06181B",
                ["InputBorderBrush"] = "#2C00FFC5",
                ["WindowEdgeBrush"] = "#2C00FFC5",
                ["WindowGlowBrush"] = "#FF0E383F",
                ["ControlTextBrush"] = "#FFD0FFF6",
                ["ButtonBgBrush"] = "#FF102D33",
                ["ButtonHoverBgBrush"] = "#FF183E46",
                ["ButtonPressedBgBrush"] = "#FF081B1E",
                ["MenuBgBrush"] = "#FF071D20",
                ["MenuHighlightBrush"] = "#FF12544F",
                ["MenuSelectedBrush"] = "#FF008075",
                ["DividerBrush"] = "#1C00FFC5",
                ["BadgeBgBrush"] = "#FF020D0E",
                ["AccentTitleBrush"] = "#FF00FFC5",
                ["ChromeButtonFgBrush"] = "#FFA8F5E5",
                ["ScrollThumbBrush"] = "#FF00FFC5",
            },
            ["arctic-ice"] = new()
            {
                ["WindowBgBrush"] = "#FFF0F6FA",
                ["CardBgBrush"] = "#FFFFFFFF",
                ["CardBorderBrush"] = "#3338BDF8",
                ["SubtleTextBrush"] = "#FF64748B",
                ["PrimaryTextBrush"] = "#FF0F172A",
                ["SecondaryTextBrush"] = "#FF334155",
                ["AccentGreenBrush"] = "#FF0284C7",
                ["AccentRedBrush"] = "#FFE11D48",
                ["AccentAmberBrush"] = "#FFF59E0B",
                ["InputBgBrush"] = "#FFF8FAFC",
                ["InputBorderBrush"] = "#4438BDF8",
                ["WindowEdgeBrush"] = "#3338BDF8",
                ["WindowGlowBrush"] = "#FFE0F2FE",
                ["ControlTextBrush"] = "#FF0F172A",
                ["ButtonBgBrush"] = "#FFFFFFFF",
                ["ButtonHoverBgBrush"] = "#FFE0F2FE",
                ["ButtonPressedBgBrush"] = "#FFBAE6FD",
                ["MenuBgBrush"] = "#FFFFFFFF",
                ["MenuHighlightBrush"] = "#FFE0F2FE",
                ["MenuSelectedBrush"] = "#FFBAE6FD",
                ["DividerBrush"] = "#1C0284C7",
                ["BadgeBgBrush"] = "#FFE2E8F0",
                ["AccentTitleBrush"] = "#FF0284C7",
                ["ChromeButtonFgBrush"] = "#FF334155",
                ["ScrollThumbBrush"] = "#FF38BDF8",
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

            // Update CornerRadius resources based on active theme
            if (targetTheme == "windows-98")
            {
                res["CardCornerRadius"] = new CornerRadius(0);
                res["SecondaryWindowCornerRadius"] = new CornerRadius(0);
                res["TopWindowCornerRadius"] = new CornerRadius(0);
                res["ButtonCornerRadius"] = new CornerRadius(0);
                res["ControlCornerRadius"] = new CornerRadius(0);
                res["SmallCornerRadius"] = new CornerRadius(0);
                res["BadgeCornerRadius"] = new CornerRadius(0);
                res["PillCornerRadius"] = new CornerRadius(0);
                res["ThumbCornerRadius"] = new CornerRadius(0);
            }
            else
            {
                res["CardCornerRadius"] = new CornerRadius(9);
                res["SecondaryWindowCornerRadius"] = new CornerRadius(12);
                res["TopWindowCornerRadius"] = new CornerRadius(12, 12, 0, 0);
                res["ButtonCornerRadius"] = new CornerRadius(6);
                res["ControlCornerRadius"] = new CornerRadius(6);
                res["SmallCornerRadius"] = new CornerRadius(4);
                res["BadgeCornerRadius"] = new CornerRadius(5);
                res["PillCornerRadius"] = new CornerRadius(17);
                res["ThumbCornerRadius"] = new CornerRadius(3);
            }

            // Retheme the gradient chrome brushes in place so secondary windows
            // (Settings/Effects) and the title bar follow the active theme too.
            if (targetTheme == "windows-98")
            {
                SetGradientStops(res, "TitleBarGradient",
                    ParseColor("#FF000080"),
                    ParseColor("#FF1084D0"));

                SetGradientStops(res, "SecondaryWindowBackgroundBrush",
                    ParseColor("#FFC0C0C0"),
                    ParseColor("#FFC0C0C0"));

                SetGradientStops(res, "SecondaryFooterBackgroundBrush",
                    ParseColor("#FFC0C0C0"),
                    ParseColor("#FFC0C0C0"));
            }
            else if (targetTheme == "windows-xp")
            {
                SetGradientStops(res, "TitleBarGradient",
                    ParseColor("#FF0058E6"),
                    ParseColor("#FF2575F0"),
                    ParseColor("#FF0043C0"));

                SetGradientStops(res, "SecondaryWindowBackgroundBrush",
                    ParseColor("#FFEBF3FC"),
                    ParseColor("#FFE1EDFA"),
                    ParseColor("#FFD8E7F8"));

                SetGradientStops(res, "SecondaryFooterBackgroundBrush",
                    ParseColor("#FFD8E7F8"),
                    ParseColor("#FFCCDDF5"));
            }
            else if (targetTheme == "vista-aero")
            {
                SetGradientStops(res, "TitleBarGradient",
                    ParseColor("#9052ACEC"),
                    ParseColor("#7520547D"),
                    ParseColor("#75123450"),
                    ParseColor("#850C243A"));

                SetGradientStops(res, "SecondaryWindowBackgroundBrush",
                    ParseColor("#901E4260"),
                    ParseColor("#80143249"),
                    ParseColor("#900C2235"));

                SetGradientStops(res, "SecondaryFooterBackgroundBrush",
                    ParseColor("#881A3A54"),
                    ParseColor("#8810263A"));
            }
            else if (palette.TryGetValue("CardBgBrush", out var cardHex) &&
                palette.TryGetValue("WindowBgBrush", out var winHex))
            {
                var card = ParseColor(cardHex);
                var win = ParseColor(winHex);
                var mid = Blend(card, win, 0.5);

                SetGradientStops(res, "TitleBarGradient", card, win);
                SetGradientStops(res, "SecondaryWindowBackgroundBrush", card, mid, win);
                SetGradientStops(res, "SecondaryFooterBackgroundBrush", card, win);
            }

            // Update DWM glass blur for active application windows
            if (Application.Current != null)
            {
                foreach (Window win in Application.Current.Windows)
                {
                    ApplyWindowGlass(win, targetTheme);
                }
            }

            ThemeChanged?.Invoke();
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

            if (brush.GradientStops.Count != colors.Length)
            {
                var clone = new LinearGradientBrush
                {
                    StartPoint = brush.StartPoint,
                    EndPoint = brush.EndPoint
                };
                for (int i = 0; i < colors.Length; i++)
                {
                    double offset = colors.Length == 1 ? 0 : (double)i / (colors.Length - 1);
                    clone.GradientStops.Add(new GradientStop(colors[i], offset));
                }
                res[key] = clone;
                return;
            }

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

        // ── P/Invoke Definitions for Aero Glass / Acrylic Backdrop ─────────
        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public int GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_INVALID_STATE = 5
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        /// <summary>
        /// Applies native Windows DWM acrylic / glass blur backdrop to a WPF window.
        /// </summary>
        public static void ApplyWindowGlass(Window window, string? themeKey = null)
        {
            if (window == null) return;
            try
            {
                var windowHelper = new WindowInteropHelper(window);
                IntPtr hwnd = windowHelper.Handle;
                if (hwnd == IntPtr.Zero) return;

                var palette = GetPalette(themeKey ?? AppSettings.Load().Theme);
                int tintColor = 0x600D0D14;
                if (palette != null && palette.TryGetValue("WindowBgBrush", out var winBgHex))
                {
                    var color = ParseColor(winBgHex);
                    byte alpha = color.A < 255 ? color.A : (byte)0x60;
                    if (themeKey == "light" || themeKey == "sepia")
                    {
                        alpha = 0x90;
                    }
                    tintColor = (alpha << 24) | (color.B << 16) | (color.G << 8) | color.R;
                }

                var activeKey = themeKey ?? AppSettings.Load().Theme;
                var accent = new AccentPolicy
                {
                    AccentState = (activeKey == "windows-xp" || activeKey == "windows-98") ? AccentState.ACCENT_DISABLED : AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                    GradientColor = tintColor,
                    AccentFlags = 2
                };

                int accentStructSize = Marshal.SizeOf(accent);
                IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    SizeOfData = accentStructSize,
                    Data = accentPtr
                };

                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(accentPtr);
            }
            catch
            {
                // Fallback gracefully on non-Windows platforms or environments where DWM composition fails
            }
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
                "8bit" => (EightBit(MeterPalette.Green), EightBit(MeterPalette.Blue), EightBit(MeterPalette.Pink)),
                "70s" => (Seventies(), Seventies(), Seventies()),
                "neon" => (NeonCyan(), NeonMagenta(), NeonYellow()),
                "grayscale" => (Grayscale(), Grayscale(), Grayscale()),
                "inferno" => (Inferno(), Inferno(), Inferno()),
                "aurora" => (Aurora(), Aurora(), Aurora()),
                "cyber-sunset" => (CyberSunset(), CyberSunset(), CyberSunset()),
                "forest" => (ForestMoss(), ForestMoss(), ForestMoss()),
                "toxic" => (Toxic(), Toxic(), Toxic()),
                "vaporwave" => (Vaporwave(), Vaporwave(), Vaporwave()),
                "plasma" => (Plasma(), Plasma(), Plasma()),
                "matrix" => (Matrix(), Matrix(), Matrix()),
                "solar-flare" => (SolarFlare(), SolarFlare(), SolarFlare()),
                "ocean-wave" => (OceanWave(), OceanWave(), OceanWave()),
                "sunset-strip" => (SunsetStrip(), SunsetStrip(), SunsetStrip()),
                "vintage-led" => (VintageLed(), VintageLed(), VintageLed()),
                "acid-lime" => (AcidLime(), AcidLime(), AcidLime()),
                "blood-moon" => (BloodMoon(), BloodMoon(), BloodMoon()),
                "rainbow" => (Rainbow(), Rainbow(), Rainbow()),
                _ => (DefaultIn(), DefaultOut(), DefaultMon()),
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

            if (inB is LinearGradientBrush inLgb)
            {
                var vertB = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 1),
                    EndPoint = new Point(0, 0)
                };
                foreach (var gs in inLgb.GradientStops)
                {
                    vertB.GradientStops.Add(new GradientStop(gs.Color, gs.Offset));
                }
                res["MeterVerticalBrush"] = vertB;
            }
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

        /// <summary>Updates the global animation pause state for background/unfocused windows.</summary>
        public static void SetAnimationsPaused(bool paused)
        {
            if (AnimationsPaused == paused) return;
            AnimationsPaused = paused;
            AnimationsPausedChanged?.Invoke();
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

        // Electric vaporwave neon gradient.
        private static LinearGradientBrush Vaporwave()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF001020"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00E5FF"), 0.25));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF9D4EDD"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF007F"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF00FF"), 1.0));
            return b;
        }

        // Plasma fire energy gradient.
        private static LinearGradientBrush Plasma()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF0F001A"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF6A00F4"), 0.25));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFD80066"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF5500"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFFFF"), 1.0));
            return b;
        }

        // Cyber matrix code gradient.
        private static LinearGradientBrush Matrix()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF001405"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF007A2B"), 0.20));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00FF66"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFCCFF00"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFFFF"), 1.0));
            return b;
        }

        // Solar flare sunburst gradient.
        private static LinearGradientBrush SolarFlare()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF1F0000"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFB30000"), 0.25));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF5500"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFEA00"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFFFFF"), 1.0));
            return b;
        }

        // Ocean wave aqua gradient.
        private static LinearGradientBrush OceanWave()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF001026"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF0055A5"), 0.25));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00D2FF"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00FF99"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFEA00"), 1.0));
            return b;
        }

        // Synthwave sunset strip gradient.
        private static LinearGradientBrush SunsetStrip()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF10002B"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF5C0099"), 0.25));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFE6006C"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF6B4A"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFEA00"), 1.0));
            return b;
        }

        // Classic studio LED VU ladder.
        private static LinearGradientBrush VintageLed()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF0A290A"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF009900"), 0.35));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF33CC00"), 0.70));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFB300"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF0000"), 1.0));
            return b;
        }

        // High-voltage acid lime gradient.
        private static LinearGradientBrush AcidLime()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF051508"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF208000"), 0.25));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF76FF03"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFCCFF00"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF007F"), 1.0));
            return b;
        }

        // Deep crimson blood moon gradient.
        private static LinearGradientBrush BloodMoon()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF100003"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF5B0011"), 0.25));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFD90429"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF5A36"), 0.85));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFE5EC"), 1.0));
            return b;
        }

        // Full spectral rainbow gradient.
        private static LinearGradientBrush Rainbow()
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            b.GradientStops.Add(new GradientStop(ParseColor("#FF4A00E0"), 0.0));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF0055FF"), 0.20));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00E5FF"), 0.40));
            b.GradientStops.Add(new GradientStop(ParseColor("#FF00E676"), 0.60));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFFEA00"), 0.78));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFFF3300"), 0.90));
            b.GradientStops.Add(new GradientStop(ParseColor("#FFCC00FF"), 1.0));
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

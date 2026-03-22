namespace Paddy.Helpers
{
    /// <summary>
    /// Shared utility for converting Win32 virtual-key codes to human-readable labels.
    /// Used by both MainWindow (hotkey display) and SettingsWindow (hotkey capture).
    /// </summary>
    public static class KeyHelper
    {
        public static string VkToLabel(uint vk)
        {
            if (vk == 0) return "(none)";

            return vk switch
            {
                0x08 => "Backspace",
                0x09 => "Tab",
                0x0D => "Enter",
                0x1B => "Esc",
                0x20 => "Space",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x23 => "End",
                0x24 => "Home",
                0x25 => "Left",
                0x26 => "Up",
                0x27 => "Right",
                0x28 => "Down",
                0x2C => "PrintScreen",
                0x2D => "Insert",
                0x2E => "Delete",
                >= 0x30 and <= 0x39 => ((char)vk).ToString(),       // 0-9
                >= 0x41 and <= 0x5A => ((char)vk).ToString(),       // A-Z
                >= 0x60 and <= 0x69 => $"Num{vk - 0x60}",          // Numpad 0-9
                0x6A => "Num*",
                0x6B => "Num+",
                0x6D => "Num-",
                0x6E => "Num.",
                0x6F => "Num/",
                >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",            // F1-F24
                0x90 => "NumLock",
                0x91 => "ScrollLock",
                0xBA => ";",
                0xBB => "=",
                0xBC => ",",
                0xBD => "-",
                0xBE => ".",
                0xBF => "/",
                0xC0 => "`",
                0xDB => "[",
                0xDC => "\\",
                0xDD => "]",
                0xDE => "'",
                _ => $"0x{vk:X2}"
            };
        }

        public static string FormatHotkey(uint modifiers, uint vk)
        {
            string modStr = "";
            if ((modifiers & 0x0002) != 0) modStr += "Ctrl+";
            if ((modifiers & 0x0001) != 0) modStr += "Alt+";
            if ((modifiers & 0x0004) != 0) modStr += "Shift+";
            return modStr + VkToLabel(vk);
        }
    }
}

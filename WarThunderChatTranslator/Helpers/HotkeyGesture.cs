using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WarThunderChatTranslator.Helpers
{
    public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey, string NormalizedText)
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;
        public const uint ModNoRepeat = 0x4000;

        private static readonly Dictionary<string, uint> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPACE"] = 0x20,
            ["TAB"] = 0x09,
            ["ENTER"] = 0x0D,
            ["ESC"] = 0x1B,
            ["ESCAPE"] = 0x1B,
            ["BACKSPACE"] = 0x08,
            ["DELETE"] = 0x2E,
            ["INSERT"] = 0x2D,
            ["HOME"] = 0x24,
            ["END"] = 0x23,
            ["PAGEUP"] = 0x21,
            ["PAGEDOWN"] = 0x22,
            ["UP"] = 0x26,
            ["DOWN"] = 0x28,
            ["LEFT"] = 0x25,
            ["RIGHT"] = 0x27,
            ["NUM0"] = 0x60,
            ["NUM1"] = 0x61,
            ["NUM2"] = 0x62,
            ["NUM3"] = 0x63,
            ["NUM4"] = 0x64,
            ["NUM5"] = 0x65,
            ["NUM6"] = 0x66,
            ["NUM7"] = 0x67,
            ["NUM8"] = 0x68,
            ["NUM9"] = 0x69,
            ["+"] = 0xBB,
            ["-"] = 0xBD,
            [","] = 0xBC,
            ["."] = 0xBE,
            ["/"] = 0xBF,
            [";"] = 0xBA,
            ["'"] = 0xDE,
            ["["] = 0xDB,
            ["]"] = 0xDD,
            ["\\"] = 0xDC,
            ["`"] = 0xC0
        };

        public static bool TryParse(string value, out HotkeyGesture gesture, out string error)
        {
            gesture = default;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(value))
            {
                error = Localization.GetString("QuickTranslationHotkeyErrorEmpty");
                return false;
            }

            var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                error = Localization.GetString("QuickTranslationHotkeyErrorFormatExample");
                return false;
            }

            uint modifiers = 0;
            string keyPart = null;
            var normalizedModifiers = new List<string>();

            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                switch (part.ToUpperInvariant())
                {
                    case "CTRL":
                    case "CONTROL":
                        if ((modifiers & ModControl) == 0) normalizedModifiers.Add("Ctrl");
                        modifiers |= ModControl;
                        break;
                    case "ALT":
                        if ((modifiers & ModAlt) == 0) normalizedModifiers.Add("Alt");
                        modifiers |= ModAlt;
                        break;
                    case "SHIFT":
                        if ((modifiers & ModShift) == 0) normalizedModifiers.Add("Shift");
                        modifiers |= ModShift;
                        break;
                    case "WIN":
                    case "WINDOWS":
                        if ((modifiers & ModWin) == 0) normalizedModifiers.Add("Win");
                        modifiers |= ModWin;
                        break;
                    default:
                        if (keyPart != null)
                        {
                            error = Localization.GetString("QuickTranslationHotkeyErrorMultipleKeys");
                            return false;
                        }
                        keyPart = part;
                        break;
                }
            }

            if (modifiers == 0 || string.IsNullOrWhiteSpace(keyPart))
            {
                error = Localization.GetString("QuickTranslationHotkeyErrorModifierRequired");
                return false;
            }

            if (!TryGetVirtualKey(keyPart, out var virtualKey, out var normalizedKey))
            {
                error = string.Format(Localization.GetString("QuickTranslationHotkeyErrorUnsupportedKeyFormat"), keyPart);
                return false;
            }

            var normalized = string.Join('+', normalizedModifiers.Concat(new[] { normalizedKey }));
            gesture = new HotkeyGesture(modifiers | ModNoRepeat, virtualKey, normalized);
            return true;
        }


        public static bool TryCreateFromVirtualKey(uint modifiers, uint virtualKey, out HotkeyGesture gesture, out string error)
        {
            gesture = default;
            error = string.Empty;

            modifiers &= ModControl | ModAlt | ModShift | ModWin;
            if (modifiers == 0)
            {
                error = Localization.GetString("QuickTranslationHotkeyErrorModifierRequired");
                return false;
            }

            if (!TryGetNormalizedKeyFromVirtualKey(virtualKey, out var normalizedKey))
            {
                error = string.Format(
                    Localization.GetString("QuickTranslationHotkeyErrorUnsupportedKeyFormat"),
                    $"VK 0x{virtualKey:X2}");
                return false;
            }

            var normalizedModifiers = new List<string>();
            if ((modifiers & ModControl) != 0) normalizedModifiers.Add("Ctrl");
            if ((modifiers & ModAlt) != 0) normalizedModifiers.Add("Alt");
            if ((modifiers & ModShift) != 0) normalizedModifiers.Add("Shift");
            if ((modifiers & ModWin) != 0) normalizedModifiers.Add("Win");

            var normalized = string.Join('+', normalizedModifiers.Concat(new[] { normalizedKey }));
            gesture = new HotkeyGesture(modifiers | ModNoRepeat, virtualKey, normalized);
            return true;
        }

        private static bool TryGetNormalizedKeyFromVirtualKey(uint virtualKey, out string normalized)
        {
            normalized = string.Empty;

            if (virtualKey is >= 0x41 and <= 0x5A)
            {
                normalized = ((char)virtualKey).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (virtualKey is >= 0x30 and <= 0x39)
            {
                normalized = ((char)virtualKey).ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (virtualKey is >= 0x70 and <= 0x87)
            {
                normalized = $"F{virtualKey - 0x70 + 1}";
                return true;
            }

            normalized = virtualKey switch
            {
                0x20 => "Space",
                0x09 => "Tab",
                0x0D => "Enter",
                0x1B => "Esc",
                0x08 => "Backspace",
                0x2E => "Delete",
                0x2D => "Insert",
                0x24 => "Home",
                0x23 => "End",
                0x21 => "PageUp",
                0x22 => "PageDown",
                0x26 => "Up",
                0x28 => "Down",
                0x25 => "Left",
                0x27 => "Right",
                >= 0x60 and <= 0x69 => $"Num{virtualKey - 0x60}",
                0xBB => "+",
                0xBD => "-",
                0xBC => ",",
                0xBE => ".",
                0xBF => "/",
                0xBA => ";",
                0xDE => "'",
                0xDB => "[",
                0xDD => "]",
                0xDC => "\\",
                0xC0 => "`",
                _ => string.Empty
            };

            return normalized.Length > 0;
        }

        private static bool TryGetVirtualKey(string key, out uint virtualKey, out string normalized)
        {
            virtualKey = 0;
            normalized = key.ToUpperInvariant();

            if (key.Length == 1)
            {
                var ch = char.ToUpperInvariant(key[0]);
                if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                {
                    virtualKey = ch;
                    normalized = ch.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
            }

            if (key.Length >= 2 && key[0] is 'F' or 'f' && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
            {
                virtualKey = (uint)(0x70 + functionKey - 1);
                normalized = $"F{functionKey}";
                return true;
            }

            if (NamedKeys.TryGetValue(key.Replace(" ", string.Empty), out virtualKey))
            {
                normalized = key.ToUpperInvariant() switch
                {
                    "ESCAPE" => "Esc",
                    "PAGEUP" => "PageUp",
                    "PAGEDOWN" => "PageDown",
                    _ => key.Length > 1 ? char.ToUpperInvariant(key[0]) + key[1..].ToLowerInvariant() : key
                };
                return true;
            }

            return false;
        }
    }
}

namespace Kite.Core.Hotkeys;

/// <summary>modifier keys. the numbers match what windows expects for hotkeys.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>a keyboard shortcut such as alt and space, saved as plain text.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, string> KeyNameFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spacebar"] = "Space",
        ["Esc"] = "Escape",
        ["Return"] = "Enter",
        ["Del"] = "Delete",
        ["Ins"] = "Insert",
        ["PgUp"] = "PageUp",
        ["PgDn"] = "PageDown",
    };

    private static readonly HashSet<string> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl", "Control", "Alt", "Shift", "Win", "Windows", "LWin", "RWin",
        "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "LeftShift", "RightShift", "System",
    };

    public static bool TryParse(string? text, out HotkeyGesture? gesture, out string? error)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The shortcut is empty.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "":
                    continue;
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Control;
                    break;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    break;
                case "win":
                case "windows":
                    modifiers |= HotkeyModifiers.Windows;
                    break;
                default:
                    if (key is not null)
                    {
                        error = "A shortcut can only have one main key.";
                        return false;
                    }

                    key = NormalizeKey(part);
                    break;
            }
        }

        if (key is null || key.Length == 0)
        {
            error = "Add a main key, for example Space or K.";
            return false;
        }

        if (modifiers == HotkeyModifiers.None)
        {
            error = "Add at least one of Ctrl, Alt, Shift or Win.";
            return false;
        }

        if (!key.All(char.IsLetterOrDigit))
        {
            error = $"\"{key}\" is not a known key name.";
            return false;
        }

        if (modifiers == HotkeyModifiers.Shift && key.Length == 1)
        {
            error = "Shift with a single letter or digit would block normal typing.";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, key);
        error = null;
        return true;
    }

    /// <summary>true for key names that are only modifiers, like ctrl on its own.</summary>
    public static bool IsModifierKeyName(string keyName) => ModifierNames.Contains(keyName);

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key);
        return string.Join('+', parts);
    }

    private static string NormalizeKey(string key)
    {
        if (KeyNameFixes.TryGetValue(key, out var fixedName))
        {
            return fixedName;
        }

        // a single digit is called d0 to d9 in the key list
        if (key.Length == 1 && char.IsDigit(key[0]))
        {
            return "D" + key;
        }

        if (key.Length == 1)
        {
            return key.ToUpperInvariant();
        }

        return char.ToUpperInvariant(key[0]) + key[1..];
    }
}

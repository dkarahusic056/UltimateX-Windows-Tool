using System.Globalization;

namespace UltimateX.Core.Settings;

/// <summary>Modifizierer – Werte entsprechen den MOD_*-Konstanten von RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Windows = 0x8,
}

/// <summary>
/// Globales Tastenkürzel (v1.3 Overlay), z. B. „Strg+Umschalt+O“ oder „Ctrl+Shift+O“.
/// Erlaubt sind genau eine Taste (A–Z, 0–9, F1–F24, Einfg/Entf/Pos1/Ende/BildAuf/BildAb/Pause) und mindestens Strg, Alt oder Win
/// (Umschalt allein reicht nicht – sonst wäre z. B. ein Großbuchstabe systemweit blockiert).
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey, string KeyName)
{
    public static HotkeyGesture Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4F, "O");

    private static readonly Dictionary<string, (int Vk, string Name)> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Einfg"] = (0x2D, "Einfg"), ["Insert"] = (0x2D, "Einfg"), ["Ins"] = (0x2D, "Einfg"),
        ["Entf"] = (0x2E, "Entf"), ["Delete"] = (0x2E, "Entf"), ["Del"] = (0x2E, "Entf"),
        ["Pos1"] = (0x24, "Pos1"), ["Home"] = (0x24, "Pos1"),
        ["Ende"] = (0x23, "Ende"), ["End"] = (0x23, "Ende"),
        ["BildAuf"] = (0x21, "BildAuf"), ["PageUp"] = (0x21, "BildAuf"), ["PgUp"] = (0x21, "BildAuf"),
        ["BildAb"] = (0x22, "BildAb"), ["PageDown"] = (0x22, "BildAb"), ["PgDn"] = (0x22, "BildAb"),
        ["Pause"] = (0x13, "Pause"),
    };

    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 40) return false;
        var mods = HotkeyModifiers.None;
        int? vk = null;
        string? name = null;
        foreach (var raw in text.Split('+'))
        {
            var t = raw.Trim();
            if (t.Length == 0) return false;
            HotkeyModifiers? m = t.ToUpperInvariant() switch
            {
                "STRG" or "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "UMSCHALT" or "UMSCH" or "SHIFT" => HotkeyModifiers.Shift,
                "ALT" => HotkeyModifiers.Alt,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => null,
            };
            if (m is { } mod)
            {
                if (mods.HasFlag(mod)) return false; // doppelt
                mods |= mod;
                continue;
            }
            if (vk is not null) return false; // nur eine Taste
            if (!TryParseKey(t, out var k, out var n)) return false;
            vk = k;
            name = n;
        }
        if (vk is null || name is null) return false;
        if ((mods & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows)) == 0) return false;
        gesture = new HotkeyGesture(mods, vk.Value, name);
        return true;
    }

    private static bool TryParseKey(string t, out int vk, out string name)
    {
        vk = 0;
        name = "";
        if (t.Length == 1)
        {
            var c = char.ToUpperInvariant(t[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c; // VK_A..VK_Z = 'A'..'Z', VK_0..VK_9 = '0'..'9'
                name = c.ToString();
                return true;
            }
            return false;
        }
        if ((t[0] == 'F' || t[0] == 'f') && int.TryParse(t.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f) && f is >= 1 and <= 24)
        {
            vk = 0x70 + f - 1;
            name = "F" + f.ToString(CultureInfo.InvariantCulture);
            return true;
        }
        if (NamedKeys.TryGetValue(t, out var named))
        {
            (vk, name) = named;
            return true;
        }
        return false;
    }

    /// <summary>Anzeige in deutscher Schreibweise, z. B. „Strg+Umschalt+O“ (wird so auch gespeichert).</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Strg");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Umschalt");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName);
        return string.Join("+", parts);
    }
}

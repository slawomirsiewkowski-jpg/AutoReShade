namespace AutoReShade.Core.ReShade;

/// <summary>A keyboard shortcut in ReShade's own format: virtual-key code plus Ctrl/Shift/Alt flags.</summary>
public readonly record struct ShortcutKey(byte VirtualKey, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    public IEnumerable<int> ToReShadeNumbers()
    {
        yield return VirtualKey;
        yield return Ctrl ? 1 : 0;
        yield return Shift ? 1 : 0;
        yield return Alt ? 1 : 0;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Shift) parts.Add("Shift");
        if (Alt) parts.Add("Alt");
        parts.Add(VirtualKey is >= 0x7C and <= 0x87 ? $"F{VirtualKey - 0x7C + 13}" : $"VK 0x{VirtualKey:X2}");
        return string.Join('+', parts);
    }
}

/// <summary>
/// The keys AutoReShade hands out to presets. They are keys no keyboard has (F13-F24 and codes
/// Windows leaves unassigned), so they cannot clash with the game's or the player's bindings.
/// </summary>
public static class ShortcutKeyPool
{
    public static IReadOnlyList<ShortcutKey> Slots { get; } = Build();

    public static bool Contains(ShortcutKey key) => Slots.Contains(key);

    private static List<ShortcutKey> Build()
    {
        var keys = new List<ShortcutKey>();
        for (byte vk = 0x7C; vk <= 0x87; vk++) keys.Add(new ShortcutKey(vk)); // F13-F24
        for (byte vk = 0x3A; vk <= 0x40; vk++) keys.Add(new ShortcutKey(vk)); // unassigned
        for (byte vk = 0x97; vk <= 0x9F; vk++) keys.Add(new ShortcutKey(vk)); // unassigned
        keys.Add(new ShortcutKey(0x0E));
        keys.Add(new ShortcutKey(0x0F));
        keys.Add(new ShortcutKey(0xE8));
        // Overflow for players with very many presets: Alt / Ctrl+Alt with F13-F24.
        for (byte vk = 0x7C; vk <= 0x87; vk++) keys.Add(new ShortcutKey(vk, Alt: true));
        for (byte vk = 0x7C; vk <= 0x87; vk++) keys.Add(new ShortcutKey(vk, Ctrl: true, Alt: true));
        return keys;
    }
}

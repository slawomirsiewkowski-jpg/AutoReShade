using System.Windows.Input;
using System.Windows.Interop;
using AutoReShade.Native;

namespace AutoReShade.Services;

/// <summary>A key combination such as "Ctrl+Shift+F8".</summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, Key Key)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse<Key>(name, ignoreCase: true, out var parsed)) return false;
                    key = parsed;
                    break;
            }
        }
        if (key is null || IsModifierKey(key.Value)) return false;
        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    public static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        var keyName = Key.ToString();
        if (keyName.Length == 2 && keyName[0] == 'D' && char.IsDigit(keyName[1])) keyName = keyName[1..];
        parts.Add(keyName);
        return string.Join('+', parts);
    }
}

/// <summary>System-wide hotkeys (they work while the game is focused). Uses the standard Windows hotkey API.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private readonly List<(string Name, string? Text, Action Action)> _definitions = new();
    private readonly Dictionary<string, string> _errors = new();
    private int _nextId = 0xA000;
    private bool _suspended;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("AutoReShade hotkeys") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <summary>Hotkeys that could not be registered (usually because another program already uses them).</summary>
    public IReadOnlyDictionary<string, string> Errors => _errors;

    public void Set(IEnumerable<(string Name, string? Text, Action Action)> hotkeys)
    {
        _definitions.Clear();
        _definitions.AddRange(hotkeys);
        if (!_suspended) RegisterAll();
    }

    /// <summary>Temporarily releases all hotkeys, e.g. while the player is choosing a new key in the settings.</summary>
    public void Suspend()
    {
        _suspended = true;
        UnregisterAll();
    }

    public void Resume()
    {
        _suspended = false;
        RegisterAll();
    }

    private void RegisterAll()
    {
        UnregisterAll();
        _errors.Clear();
        foreach (var (name, text, action) in _definitions)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!HotkeyGesture.TryParse(text, out var gesture))
            {
                _errors[name] = $"\"{text}\" is not a valid key combination.";
                continue;
            }

            var id = _nextId++;
            var modifiers = Win32.MOD_NOREPEAT;
            if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= Win32.MOD_ALT;
            if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= Win32.MOD_CONTROL;
            if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= Win32.MOD_SHIFT;
            if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= Win32.MOD_WIN;
            var vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);

            if (Win32.RegisterHotKey(_source.Handle, id, modifiers, vk))
                _actions[id] = action;
            else
                _errors[name] = $"{gesture} is already used by another program. Please choose a different key.";
        }
    }

    private void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Win32.UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}

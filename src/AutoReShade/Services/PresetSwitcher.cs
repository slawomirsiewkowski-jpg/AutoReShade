using System.Runtime.InteropServices;
using AutoReShade.Core;
using AutoReShade.Core.ReShade;
using AutoReShade.Core.Settings;
using AutoReShade.Native;

namespace AutoReShade.Services;

/// <summary>
/// Switches ReShade to a preset by pressing that preset's shortcut key (configured in ReShade.ini).
/// The key is only pressed while the game is focused and no modifier key is held, so ReShade
/// sees exactly the shortcut it expects and nothing else receives it.
/// </summary>
public sealed class PresetSwitcher
{
    private readonly Func<AppSettings> _settings;
    private readonly GameMonitor _game;
    private string? _pending;
    private string? _lastSent;
    private bool _sending;

    public PresetSwitcher(Func<AppSettings> settings, GameMonitor game)
    {
        _settings = settings;
        _game = game;
    }

    public string? ActivePreset => _lastSent;
    public string? PendingPreset => _pending;
    public string? LastProblem { get; private set; }

    public event Action? StateChanged;

    public void Request(string? presetPath)
    {
        if (string.IsNullOrWhiteSpace(presetPath)) return;
        var full = Path.GetFullPath(presetPath);
        if (_lastSent is not null && ReShadeConfigurator.SamePath(_lastSent, full) && _pending is null) return;
        _pending = full;
        LastProblem = null;
        StateChanged?.Invoke();
        _ = TrySendAsync();
    }

    /// <summary>Forget what ReShade has active (e.g. after the game restarted and loaded its own last preset).</summary>
    public void Reset()
    {
        _lastSent = null;
        StateChanged?.Invoke();
    }

    /// <summary>Called regularly; sends a waiting switch as soon as the game is focused.</summary>
    public void Tick()
    {
        if (_pending is not null && !_sending) _ = TrySendAsync();
    }

    private async Task TrySendAsync()
    {
        if (_sending || _pending is null) return;
        var settings = _settings();
        if (!settings.PresetSwitchingEnabled) return;
        if (!_game.Current.IsForeground) return;
        if (AnyModifierHeld()) return;

        var preset = _pending;
        var key = settings.ShortcutFor(preset);
        if (key is null)
        {
            LastProblem = $"No shortcut key is assigned to {Path.GetFileName(preset)} yet.";
            _pending = null;
            StateChanged?.Invoke();
            return;
        }

        _sending = true;
        try
        {
            await PressAsync(key.Value);
            _lastSent = preset;
            if (ReferenceEquals(_pending, preset)) _pending = null;
            Log.Info($"Switched ReShade preset to {Path.GetFileName(preset)} ({key})");
        }
        finally
        {
            _sending = false;
            StateChanged?.Invoke();
        }
    }

    private static bool AnyModifierHeld() =>
        Win32.IsKeyDown(Win32.VK_SHIFT) || Win32.IsKeyDown(Win32.VK_CONTROL) || Win32.IsKeyDown(Win32.VK_MENU)
        || Win32.IsKeyDown(Win32.VK_LWIN) || Win32.IsKeyDown(Win32.VK_RWIN);

    private static async Task PressAsync(ShortcutKey key)
    {
        var modifiers = new List<ushort>();
        if (key.Ctrl) modifiers.Add(Win32.VK_CONTROL);
        if (key.Shift) modifiers.Add(Win32.VK_SHIFT);
        if (key.Alt) modifiers.Add(Win32.VK_MENU);

        foreach (var m in modifiers) Send(m, keyUp: false);
        Send(key.VirtualKey, keyUp: false);
        // ReShade checks keys once per frame; hold the key long enough for a few frames even at low FPS.
        await Task.Delay(80);
        Send(key.VirtualKey, keyUp: true);
        for (var i = modifiers.Count - 1; i >= 0; i--) Send(modifiers[i], keyUp: true);
    }

    private static void Send(ushort vk, bool keyUp)
    {
        var input = new Win32.INPUT
        {
            type = Win32.INPUT_KEYBOARD,
            u = new Win32.INPUTUNION
            {
                ki = new Win32.KEYBDINPUT
                {
                    wVk = vk,
                    wScan = (ushort)Win32.MapVirtualKey(vk, 0),
                    dwFlags = keyUp ? Win32.KEYEVENTF_KEYUP : 0,
                },
            },
        };
        if (Win32.SendInput(1, new[] { input }, Marshal.SizeOf<Win32.INPUT>()) != 1)
            Log.Warn($"SendInput failed for key 0x{vk:X2} (error {Marshal.GetLastWin32Error()})");
    }
}

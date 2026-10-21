using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace DiceModders.Shared;

// Values are Win32 virtual-key codes, so config files stay readable ("PageDown", "NumpadAdd", ...).
internal enum HotkeyModifier
{
    None = 0,
    LeftShift = 0xA0, RightShift = 0xA1,
    LeftControl = 0xA2, RightControl = 0xA3,
    LeftAlt = 0xA4, RightAlt = 0xA5,
}

internal enum HotkeyKey
{
    None = 0,
    Backspace = 0x08, Tab = 0x09, Space = 0x20,
    PageUp = 0x21, PageDown = 0x22, End = 0x23, Home = 0x24, Insert = 0x2D, Delete = 0x2E,
    D0 = 0x30, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    A = 0x41, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
    Numpad0 = 0x60, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
    NumpadMultiply = 0x6A, NumpadAdd = 0x6B, NumpadSubtract = 0x6D, NumpadDecimal = 0x6E, NumpadDivide = 0x6F,
    F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    Semicolon = 0xBA, Equal = 0xBB, Comma = 0xBC, Minus = 0xBD, Period = 0xBE, Slash = 0xBF, Backtick = 0xC0,
    LeftBracket = 0xDB, Backslash = 0xDC, RightBracket = 0xDD, Quote = 0xDE,
}

/// <summary>
/// Polls "modifier + key" bindings once per frame from a Unity Update (main thread).
/// Uses GetAsyncKeyState instead of UnityEngine.Input so it works regardless of which
/// input backend the game uses, and only fires while the game window is focused.
/// NOTE: F1, F2, F3 and F10 are used by the game itself - avoid binding them.
/// </summary>
internal sealed class Hotkeys
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private sealed class Binding
    {
        public string Name;          // config key name
        public string Label;         // short text shown in the help listing
        public ConfigEntry<HotkeyKey> Key;
        public Action Action;
        public Func<bool> State;     // non-null for toggles: reports ON/OFF
        public bool Prev;
    }

    private readonly ConfigEntry<HotkeyModifier> _modifier;
    private readonly ManualLogSource _log;
    private readonly string _title;
    private readonly List<Binding> _bindings = new();
    private readonly Binding _help;

    public Hotkeys(ConfigFile cfg, ManualLogSource log, string title)
    {
        _log = log;
        _title = title;
        ConsoleGuard.DisableQuickEdit(log);
        _modifier = cfg.Bind("Hotkeys", "Modifier", HotkeyModifier.RightControl,
            "Hold this key together with a hotkey below. Set to None to use the bare keys " +
            "(not recommended: F1/F2/F3/F10 are used by the game).");
        _help = new Binding
        {
            Name = "PrintHelp", Label = "help", Action = PrintHelp,
            Key = cfg.Bind("Hotkeys", "PrintHelp", HotkeyKey.Home, "Print this mod's hotkeys to the log / console (None = unbound)"),
        };
    }

    public void Bind(ConfigFile cfg, string name, HotkeyKey defaultKey, string description, Action action,
                     string label = null, Func<bool> state = null)
    {
        var entry = cfg.Bind("Hotkeys", name, defaultKey, description + " (None = unbound)");
        _bindings.Add(new Binding { Name = name, Label = label ?? name, Key = entry, Action = action, State = state });
    }

    /// <summary>Multiplier for "+/-" hotkeys: hold Left Shift for x10, Left Alt for x100, Left Ctrl for x1000.</summary>
    public static int StepMultiplier() => IsDown(0xA2) ? 1000 : IsDown(0xA4) ? 100 : IsDown(0xA0) ? 10 : 1;

    private string KeyText(Binding b)
    {
        if (b.Key.Value == HotkeyKey.None) return "unbound";
        return (_modifier.Value == HotkeyModifier.None ? "" : _modifier.Value + "+") + b.Key.Value;
    }

    /// <summary>Quiet startup listing (Info level).</summary>
    public void LogBindings()
    {
        foreach (var b in _bindings)
            if (b.Key.Value != HotkeyKey.None)
            {
                // Built as a plain string first: BepInEx's log-message string handler does not support ",-15" alignment.
                string line = $"[hotkey] {KeyText(b),-28} {b.Label}";
                _log.LogInfo(line);
            }
        _log.LogInfo("[hotkey] " + KeyText(_help) + " = print hotkeys");
    }

    /// <summary>On-demand listing (Message level, shown in the console) with the current ON/OFF state.</summary>
    public void PrintHelp()
    {
        _log.LogMessage("=== " + _title + " hotkeys ===");
        foreach (var b in _bindings)
        {
            string state = b.State == null ? "" : (b.State() ? "  [ON]" : "  [OFF]");
            string line = $"  {KeyText(b),-28} {b.Label}{state}";
            _log.LogMessage(line);
        }
    }

    public void Poll()
    {
        bool active = ProcessHasFocus() &&
                      (_modifier.Value == HotkeyModifier.None || IsDown((int)_modifier.Value));

        foreach (var b in _bindings) Check(b, active);
        Check(_help, active);
    }

    private void Check(Binding b, bool active)
    {
        int vk = (int)b.Key.Value;
        bool down = vk != 0 && IsDown(vk);
        bool edge = down && !b.Prev;
        b.Prev = down;
        if (!edge || !active) return;
        try { b.Action(); }
        catch (Exception e) { _log.LogError($"[hotkey] {b.Name} failed: {e}"); }
    }

    /// <summary>True while the key is held AND the game window has focus (for mods that poll single keys themselves).</summary>
    public static bool KeyIsDown(int vk) => ProcessHasFocus() && IsDown(vk);

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static bool ProcessHasFocus()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return false;
        GetWindowThreadProcessId(fg, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }
}

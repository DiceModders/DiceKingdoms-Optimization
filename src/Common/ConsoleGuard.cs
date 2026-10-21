using System;
using System.Runtime.InteropServices;
using BepInEx.Logging;

namespace DiceModders.Shared;

/// <summary>
/// Turns off "QuickEdit" on the BepInEx console window. With QuickEdit on, a mouse click inside the console starts a text
/// selection and Windows PAUSES every program that writes to that console until you press Enter or Esc - for a game that
/// logs through BepInEx this freezes the whole game (typical after clicking the console while using the Steam overlay or alt-tab).
/// Harmless if there is no console window (BepInEx.cfg: [Logging.Console] Enabled = false).
/// </summary>
internal static class ConsoleGuard
{
    private const uint GenericRead = 0x80000000, GenericWrite = 0x40000000;
    private const uint ShareRead = 1, ShareWrite = 2, OpenExisting = 3;
    private const uint EnableQuickEdit = 0x0040, EnableExtendedFlags = 0x0080;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetConsoleMode(IntPtr handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleMode(IntPtr handle, uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);

    public static void DisableQuickEdit(ManualLogSource log)
    {
        try
        {
            IntPtr handle = CreateFile("CONIN$", GenericRead | GenericWrite, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;   // no console window
            try
            {
                if (!GetConsoleMode(handle, out uint mode) || (mode & EnableQuickEdit) == 0) return;   // nothing to do
                if (SetConsoleMode(handle, (mode & ~EnableQuickEdit) | EnableExtendedFlags))
                    log.LogInfo("[console] QuickEdit switched off, so clicking the console window can no longer freeze the game.");
            }
            finally { CloseHandle(handle); }
        }
        catch (Exception) { /* purely a convenience */ }
    }
}

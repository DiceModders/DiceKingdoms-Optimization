using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace DiceModders.Optimization;

using Core = DiceModders.ModCore.ModCore;

/// <summary>
/// On a hard disk the first match after starting the game stutters while Unity reads models, textures and sounds from the game's data files
/// (about 2.6 GB). Windows keeps every file it has read in memory (the file cache) as long as memory is free, so reading the files once in the
/// background at the main menu makes those later reads come from memory. The reading runs on its own thread at the lowest priority and starts only
/// after the game has finished loading its own data, so it never competes with the game. It stops by itself when the drive is fast (an SSD gains
/// nothing) or when Windows has too little free memory to keep the files.
/// </summary>
internal static class DiskWarmup
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus { public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    private const long Chunk = 4 << 20;
    private const double FastDriveMBps = 400;   // above this (measured on the first 64 MB) the drive is an SSD: nothing to gain

    internal static void Install() => Core.Civilizations.WhenGameDataLoaded(Start);

    private static void Start()
    {
        if (!Plugin.On || !Options.WarmUpFiles.Value) return;
        var data = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName) ?? "", "Dice-Kingdoms_Data");
        if (!Directory.Exists(data)) return;
        new Thread(() => Run(data)) { IsBackground = true, Priority = ThreadPriority.Lowest, Name = "Optimization warm-up" }.Start();
    }

    private static void Run(string data)
    {
        try
        {
            // the big stream files first: they hold the meshes, textures and sounds a match loads
            var files = Directory.GetFiles(data, "*", SearchOption.AllDirectories).Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Extension is ".resS" or ".resource").ThenByDescending(f => f.Length).ToList();
            long total = files.Sum(f => f.Length);
            var mem = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (GlobalMemoryStatusEx(ref mem) && (long)mem.AvailPhys < total * 2)
            {
                Plugin.L.LogInfo($"warm-up skipped: {mem.AvailPhys >> 20} MB free memory, the game files need {total >> 20} MB");
                return;
            }

            var watch = Stopwatch.StartNew();
            var buffer = new byte[Chunk];
            long read = 0;
            bool measured = false;
            foreach (var f in files)
            {
                if (!Plugin.On || !Options.WarmUpFiles.Value) return;   // switched off meanwhile
                using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1, FileOptions.SequentialScan);
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0)
                {
                    read += n;
                    if (!measured && read >= 64 << 20)
                    {
                        measured = true;
                        double speed = read / 1048576.0 / Math.Max(0.001, watch.Elapsed.TotalSeconds);
                        if (speed > FastDriveMBps) { Plugin.L.LogInfo($"warm-up stopped: fast drive ({speed:0} MB/s), nothing to gain"); return; }
                    }
                }
            }
            Plugin.L.LogInfo($"warm-up: {read >> 20} MB of game files read in {watch.Elapsed.TotalSeconds:0.0} s; the first match loads them from memory");
        }
        catch (Exception e) { Plugin.L.LogWarning("warm-up stopped: " + e.Message); }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;

namespace DiceModders.Shared;

/// <summary>
/// Finds game types in the generated interop assemblies by simple name ("IslandGrid") or
/// full name ("DiceKingdoms.Game.IslandGrid"). The game's interop assembly is not loaded by
/// anything else, so we load every non-Unity / non-system DLL from BepInEx/interop ourselves.
/// If the game moves or renames a type, only this lookup is affected and the log says which one.
/// </summary>
internal static class GameTypes
{
    // Interop DLLs that never contain game code.
    private static readonly string[] SkipPrefixes =
    {
        "UnityEngine", "Unity.", "Il2Cpp", "System", "Microsoft", "mscorlib", "netstandard",
        "Mono.", "Newtonsoft", "BepInEx", "0Harmony", "MonoMod", "Cpp2IL", "LibCpp2IL", "AsmResolver",
        "Iced", "Disarm", "WasmDisassembler", "StableNameDotNet", "Accessibility", "Samboy", "Gee.", "Ionic",
    };

    private static readonly Dictionary<string, Type> Cache = new();
    private static List<Assembly> _gameAssemblies;

    public static string InteropDir => Path.Combine(Paths.BepInExRootPath, "interop");

    public static Type Find(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;

        Type found = null;
        foreach (var asm in GameAssemblies())
        {
            foreach (var t in SafeTypes(asm))
            {
                if (t.DeclaringType != null) continue; // top-level types only
                if (t.Name == name || t.FullName == name) { found = t; break; }
            }
            if (found != null) break;
        }
        Cache[name] = found;
        return found;
    }

    /// <summary>Up to 5 type names that merely contain <paramref name="name"/> - helps spot renames.</summary>
    public static string Suggest(string name)
    {
        var hits = new List<string>();
        foreach (var asm in GameAssemblies())
            foreach (var t in SafeTypes(asm))
            {
                if (t.DeclaringType == null && t.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    hits.Add(t.FullName);
                if (hits.Count >= 5) return string.Join(", ", hits);
            }
        return string.Join(", ", hits);
    }

    /// <summary>Up to 8 method names on <paramref name="type"/> that share a word with <paramref name="method"/>.</summary>
    public static string SuggestMethods(Type type, string method)
    {
        var tokens = Regex.Matches(method, "[A-Z][a-z0-9]+").Cast<Match>().Select(m => m.Value).Where(t => t.Length >= 3).ToList();
        var names = new SortedSet<string>();
        for (var t = type; t != null && !(t.Namespace ?? "").StartsWith("Il2Cpp"); t = t.BaseType)
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                           BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (!m.IsSpecialName && tokens.Any(tok => m.Name.IndexOf(tok, StringComparison.OrdinalIgnoreCase) >= 0))
                    names.Add(m.Name);
        return string.Join(", ", names.Take(8));
    }

    /// <summary>One-line summary of what was searched (logged once on the first failed lookup).</summary>
    public static string Describe()
    {
        var asms = GameAssemblies();
        if (asms.Count == 0) return "no game assemblies found in " + InteropDir;
        return "searched " + string.Join(", ", asms.Select(a => a.GetName().Name + " (" + SafeTypes(a).Count() + " types)"));
    }

    private static List<Assembly> GameAssemblies()
    {
        if (_gameAssemblies != null) return _gameAssemblies;
        _gameAssemblies = new List<Assembly>();

        string dir = InteropDir;
        if (!Directory.Exists(dir)) return _gameAssemblies;

        var loaded = AppDomain.CurrentDomain.GetAssemblies();
        foreach (string file in Directory.GetFiles(dir, "*.dll"))
        {
            string asmName = Path.GetFileNameWithoutExtension(file);
            if (SkipPrefixes.Any(p => asmName.StartsWith(p, StringComparison.Ordinal))) continue;

            Assembly asm = loaded.FirstOrDefault(a => a.GetName().Name == asmName);
            if (asm == null)
            {
                try { asm = Assembly.LoadFrom(file); }
                catch { continue; }
            }
            _gameAssemblies.Add(asm);
        }
        return _gameAssemblies;
    }

    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return Array.FindAll(e.Types, t => t != null); }
        catch { return Array.Empty<Type>(); }
    }
}

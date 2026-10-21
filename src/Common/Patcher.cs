using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace DiceModders.Shared;

/// <summary>
/// Applies Harmony patches to game methods looked up by type name + method name, logging
/// OK/FAIL per patch (the replacement for the old native "hook table").
/// Patch methods must be static; <paramref name="host"/> is the class that contains them.
/// </summary>
internal sealed class Patcher
{
    private readonly Harmony _harmony;
    private readonly ManualLogSource _log;
    private bool _describedTypes;
    public int Applied { get; private set; }
    public int Failed { get; private set; }

    public Patcher(string harmonyId, ManualLogSource log)
    {
        _harmony = new Harmony(harmonyId);
        _log = log;
    }

    public bool Patch(string typeName, string method, Type host, string prefix = null, string postfix = null)
    {
        string label = typeName + "." + method;
        try
        {
            var type = GameTypes.Find(typeName);
            if (type == null)
            {
                if (!_describedTypes)
                {
                    _describedTypes = true;
                    _log.LogWarning("[types] " + GameTypes.Describe());
                }
                string similar = GameTypes.Suggest(typeName);
                return Fail(label, "type not found in interop assemblies" +
                                   (similar.Length > 0 ? " (similar: " + similar + ")" : ""));
            }

            MethodBase target = AccessTools.Method(type, method);
            if (target == null)
            {
                string similar = GameTypes.SuggestMethods(type, method);
                return Fail(label, "method not found" + (similar.Length > 0 ? " (similar on " + type.Name + ": " + similar + ")" : ""));
            }

            HarmonyMethod pre = null, post = null;
            if (prefix != null)
            {
                var m = AccessTools.Method(host, prefix);
                if (m == null) return Fail(label, "prefix '" + prefix + "' missing in " + host.Name);
                pre = new HarmonyMethod(m);
            }
            if (postfix != null)
            {
                var m = AccessTools.Method(host, postfix);
                if (m == null) return Fail(label, "postfix '" + postfix + "' missing in " + host.Name);
                post = new HarmonyMethod(m);
            }

            _harmony.Patch(target, pre, post);
            Applied++;
            _log.LogInfo("[patch] OK   " + label);
            return true;
        }
        catch (Exception e)
        {
            return Fail(label, e.GetType().Name + ": " + e.Message);
        }
    }

    private bool Fail(string label, string why)
    {
        Failed++;
        _log.LogWarning("[patch] FAIL " + label + " - " + why);
        return false;
    }

    public void Summary() => _log.LogInfo($"[patch] {Applied}/{Applied + Failed} patches active.");
}

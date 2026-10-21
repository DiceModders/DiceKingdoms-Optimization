using System;
using System.Collections.Generic;
using System.Reflection;

namespace DiceModders.Shared;

/// <summary>Name-based calls on interop wrapper objects (works for private members too).</summary>
internal static class Reflect
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<(Type, string, int), MethodInfo> Cache = new();

    public static object Call(object target, string method, params object[] args)
    {
        if (target == null) return null;
        var type = target.GetType();
        var key = (type, method, args.Length);
        if (!Cache.TryGetValue(key, out var mi))
        {
            for (var t = type; t != null && mi == null; t = t.BaseType)
                foreach (var m in t.GetMethods(Flags))
                    if (m.Name == method && m.GetParameters().Length == args.Length) { mi = m; break; }
            Cache[key] = mi;
        }
        if (mi == null) throw new MissingMethodException(type.FullName, method);

        // interop enums are real C# enums: let callers pass a plain int (e.g. CountGround(5))
        var ps = mi.GetParameters();
        for (int i = 0; i < ps.Length && i < args.Length; i++)
            if (args[i] is int n && ps[i].ParameterType.IsEnum)
            {
                args = (object[])args.Clone();
                args[i] = Enum.ToObject(ps[i].ParameterType, n);
            }

        try { return mi.Invoke(target, args); }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Reads a property or field by name (private ones too). The interop assemblies expose IL2CPP fields as properties.</summary>
    public static bool TryGetMember(object target, string name, out object value)
    {
        value = null;
        if (target == null) return false;
        try
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, Flags);
                if (p != null && p.CanRead) { value = p.GetValue(target); return true; }
                var f = t.GetField(name, Flags);
                if (f != null) { value = f.GetValue(target); return true; }
            }
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>Writes a property or field by name (private ones too).</summary>
    public static bool TrySetMember(object target, string name, object value)
    {
        if (target == null) return false;
        try
        {
            for (var t = target.GetType(); t != null; t = t.BaseType)
            {
                var p = t.GetProperty(name, Flags);
                if (p != null && p.CanWrite) { p.SetValue(target, value); return true; }
                var f = t.GetField(name, Flags);
                if (f != null) { f.SetValue(target, value); return true; }
            }
        }
        catch (Exception) { }
        return false;
    }

    /// <summary>Calls a parameterless STATIC method (e.g. a static property getter) by name; null if there is none.</summary>
    public static object CallStatic(Type type, string method)
    {
        for (var t = type; t != null; t = t.BaseType)
            foreach (var m in t.GetMethods(Flags))
                if (m.Name == method && m.IsStatic && m.GetParameters().Length == 0)
                {
                    try { return m.Invoke(null, null); }
                    catch (TargetInvocationException e) when (e.InnerException != null)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
                        throw;
                    }
                }
        return null;
    }

    /// <summary>Creates an interop wrapper of <paramref name="type"/> around a native object pointer.</summary>
    public static object Wrap(Type type, IntPtr pointer)
        => pointer == IntPtr.Zero || type == null ? null : Activator.CreateInstance(type, pointer);
}

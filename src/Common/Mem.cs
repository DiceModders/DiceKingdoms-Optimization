using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace DiceModders.Shared;

/// <summary>
/// Raw IL2CPP object access by field offset. Offsets are build-specific (see the wiki's
/// "Internals Reference"); names are stable, numbers are not. Only use this for members
/// that are not exposed (or not reliably exposed) by the interop assemblies.
/// </summary>
internal static class Mem
{
    public static IntPtr PtrOf(object o) => o is Il2CppObjectBase b ? b.Pointer : IntPtr.Zero;

    // Plain (non-generic) helpers on purpose: the game's interop folder contains its own core-library
    // stubs, and a "where T : unmanaged" constraint then fails to compile (CS0656).
    public static unsafe int ReadInt(IntPtr obj, int offset)
        => *(int*)((byte*)obj + offset);

    public static unsafe void WriteInt(IntPtr obj, int offset, int value)
        => *(int*)((byte*)obj + offset) = value;

    public static unsafe IntPtr ReadPtr(IntPtr obj, int offset)
        => obj == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)((byte*)obj + offset);

    public static string Str(IntPtr il2cppString)
        => il2cppString == IntPtr.Zero ? "" : (IL2CPP.Il2CppStringToManaged(il2cppString) ?? "");

    // IL2CPP arrays: length at +0x18, elements start at +0x20.
    public static int ArrayLength(IntPtr arr) => arr == IntPtr.Zero ? 0 : ReadInt(arr, 0x18);
    public static IntPtr ArrayElem(IntPtr arr, int i) => ReadPtr(arr, 0x20 + i * 8);
}

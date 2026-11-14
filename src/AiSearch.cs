using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DiceModders.Optimization;

using AIPlay = DiceKingdoms.Game.AIPlay;
using BuildData = DiceKingdoms.Game.BuildData;
using BuildingType = DiceKingdoms.Game.BuildingType;

/// <summary>
/// A faster <c>AIPlay.WhereToPlaceBuilding</c>: where a computer player puts a building. The game's search tries every tile of the search square in
/// four rotations and allocates two hash sets and several LINQ objects per try (~13 ms per search on a 49 x 49 square, ~150 ms for a wonder); with
/// 11 computer players that is the 1-2 s freeze at the end of every build phase on big islands. This is the same search over the island's tile
/// memory, with the same rules, the same order, the same random start rotation and the same tie-breaks, so the computer players build exactly where
/// they built before (see <see cref="Verify"/>). Host only: the computer players run there and send their moves like a player.
///
/// The game's function returns a large struct through a hidden pointer, so it cannot be patched with Harmony (that broke the result before). Its
/// first bytes are replaced by a jump to <see cref="Search"/>, which has the same native signature; the original bytes are kept, so the game's own
/// search can still be called (<see cref="CallOriginal"/>) for the cases this file does not handle (the disease rule of the first turns) and when
/// the option is switched off.
/// </summary>
internal static unsafe class AiSearch
{
    // ---- memory layout (game v1.0.4; see the performance notes) ----
    private const int BuildDataIsland = 0xA0, IslandGrid = 0x98;
    private const int GridSize = 0xF0, GridRandom = 0xFC, GridBuildings = 0x108, GridRemains = 0x118, GridNature = 0x128, GridGround = 0x138;
    private const int TypeLocal = 0x18, TypeLocalWater = 0x20, TypeCost = 0x58, TypeBulk = 0x7C, TypeHealth = 0x80;
    private const int DataToolCost = 0xB8;
    private const int NatureFlower = 4;

    private static IntPtr _function;          // the game's WhereToPlaceBuilding
    private static readonly byte[] Original = new byte[12];
    private static bool _patched;

    private static delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr> OriginalFn => (delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)_function;

    /// <summary>Debug check: run both searches and compare (the game's result is used). Set from the config.</summary>
    internal static bool Verify;
    internal static int Searches, Fallbacks, Mismatches;

    internal static void Refresh()
    {
        bool on = Plugin.On && Options.AiFastSearch.Value;
        Verify = Options.AiFastSearchVerify.Value;
        try
        {
            if (on && !_patched) { Patch(); Plugin.L.LogInfo("[ai] fast building search on" + (Verify ? " (verify mode: both searches run, the game's result is used)" : "")); }
            else if (!on && _patched) Unpatch();
        }
        catch (Exception e) { Plugin.L.LogError("[ai] fast search: " + e.Message); }
    }

    private static void Patch()
    {
        if (_function == IntPtr.Zero)
        {
            var field = typeof(AIPlay).GetField(Array.Find(typeof(AIPlay).GetFields(BindingFlags.NonPublic | BindingFlags.Static),
                f => f.Name.StartsWith("NativeMethodInfoPtr_WhereToPlaceBuilding_", StringComparison.Ordinal))?.Name ?? "", BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) throw new InvalidOperationException("WhereToPlaceBuilding not found in the interop");
            IntPtr info = (IntPtr)field.GetValue(null);
            _function = *(IntPtr*)info;                     // MethodInfo.methodPointer
            if (_function == IntPtr.Zero) throw new InvalidOperationException("no native code for WhereToPlaceBuilding");
            Marshal.Copy(_function, Original, 0, Original.Length);
        }
        var code = new byte[12];
        code[0] = 0x48; code[1] = 0xB8;                     // mov rax, imm64
        BitConverter.GetBytes((long)(IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&Search).CopyTo(code, 2);
        code[10] = 0xFF; code[11] = 0xE0;                   // jmp rax
        Write(code);
        _patched = true;
    }

    private static void Unpatch()
    {
        Write(Original);
        _patched = false;
        Plugin.L.LogInfo("[ai] fast building search off");
    }

    private static void Write(byte[] code)
    {
        if (!VirtualProtect(_function, (UIntPtr)code.Length, 0x40, out uint old)) throw new InvalidOperationException("VirtualProtect failed");
        Marshal.Copy(code, 0, _function, code.Length);
        VirtualProtect(_function, (UIntPtr)code.Length, old, out _);
        FlushInstructionCache(GetCurrentProcess(), _function, (UIntPtr)code.Length);
    }

    /// <summary>The game's own search: the original bytes go back for the call and the jump is written again afterwards.</summary>
    private static IntPtr CallOriginal(IntPtr ret, IntPtr self, IntPtr data, IntPtr type, IntPtr wonder, IntPtr method)
    {
        Write(Original);
        try { return OriginalFn(ret, self, data, type, wonder, method); }
        finally { Patch(); }
    }

    // ---- the search ----

    [UnmanagedCallersOnly]
    private static IntPtr Search(IntPtr ret, IntPtr self, IntPtr data, IntPtr type, IntPtr wonder, IntPtr method)
    {
        try
        {
            Searches++;
            var ctx = new Context();
            if (!ctx.Init(self, data, type, wonder)) { Fallbacks++; return CallOriginal(ret, self, data, type, wonder, method); }
            if (!Verify) { ctx.Run((byte*)ret); return ret; }

            // verify: our search, then the game's from the same generator state; compare the 0x50 result bytes and the generator state
            uint* rng = (uint*)(ctx.Grid + GridRandom);
            uint before = *rng;
            byte* mine = stackalloc byte[0x50];
            ctx.Run(mine);
            uint after = *rng;
            *rng = before;
            CallOriginal(ret, self, data, type, wonder, method);
            bool same = after == *rng;
            for (int i = 0; i < 0x50 && same; i++) if (mine[i] != ((byte*)ret)[i]) same = false;
            if (!same)
            {
                Mismatches++;
                Plugin.L.LogWarning($"[ai-verify] DIFFERENT for {ctx.TypeName} (square {ctx.Side}): fast {Describe(mine)} rng {after:X8}, game {Describe((byte*)ret)} rng {*rng:X8}");
            }
            return ret;
        }
        catch (Exception e)
        {
            Fallbacks++;
            Plugin.L.LogWarning("[ai] fast search failed, the game's search is used: " + e.Message);
            return CallOriginal(ret, self, data, type, wonder, method);
        }
    }

    private static string Describe(byte* r) => r[0] == 0 ? "none" :
        $"({*(int*)(r + 0x10)},{*(int*)(r + 0x14)}) rot {*(int*)(r + 0x18)} cost {*(int*)(r + 0x30)}/{*(int*)(r + 0x34)}/{*(int*)(r + 0x38)}/{*(int*)(r + 0x3C)}/{*(int*)(r + 0x40)}";

    private struct Layer
    {
        public byte* Items; public int Width, Height, Stride;
        public static Layer Of(byte* grid, int offset, int stride)
        {
            var l = new Layer { Stride = stride };
            IntPtr layer = *(IntPtr*)(grid + offset);
            if (layer == IntPtr.Zero) return l;
            IntPtr array = *(IntPtr*)((byte*)layer + 0x10);
            l.Width = *(int*)((byte*)layer + 0x18);
            l.Height = *(int*)((byte*)layer + 0x1C);
            if (array != IntPtr.Zero) l.Items = (byte*)array + 0x20;
            return l;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte* At(int x, int y) => Items == null || x < 0 || y < 0 || x >= Width || y >= Height ? null : Items + (y * Width + x) * Stride;
    }

    private sealed class Context
    {
        public byte* Grid;
        public int Side;
        public string TypeName = "?";
        Layer _ground, _buildings, _remains, _nature;
        IntPtr _type, _amplified;
        int[] _local, _water;                 // pairs x, y
        bool _anchorInWater, _hasProx, _isWonder;
        int _proxKind;                        // 0 land extension, 1 amplifier, 2 nature producer
        int _proxNature;
        int _walls, _minDist, _health, _bulk, _sizeX, _sizeY;
        int[] _typeCost = new int[8], _tool = new int[8], _wallCost = new int[8];
        HashSet<long> _reserved;
        Dictionary<int, IntPtr> _buildingTypes;   // building id -> type, only for amplifiers

        public bool Init(IntPtr self, IntPtr data, IntPtr type, IntPtr wonder)
        {
            var ai = new AIPlay(self);
            var bt = new BuildingType(type);
            var types = ai.buildingTypes;
            _type = type;
            TypeName = bt.name.ToString();
            _isWonder = bt.TryCast<DiceKingdoms.Game.BuildingTypeWonder>() != null;
            if (!_isWonder)
            {
                // the disease rule of the first turns (AvoidBecauseOfDisease) is not copied: the game's search does those turns
                var state = new BuildData(data).GameState;
                if (state == null) return No("no game state");
                int turn = state.TurnCounter, diseaseTurns = ai.configuration.turnCountLimit;
                if (turn <= diseaseTurns) return No("turn " + turn + " <= disease turn limit " + diseaseTurns);
            }
            IntPtr island = *(IntPtr*)((byte*)data + BuildDataIsland);
            if (island == IntPtr.Zero) return No("no island");
            Grid = *(byte**)((byte*)island + IslandGrid);
            if (Grid == null) return No("no grid");
            _sizeX = *(int*)(Grid + GridSize); _sizeY = *(int*)(Grid + GridSize + 4);
            _ground = Layer.Of(Grid, GridGround, 4);
            _buildings = Layer.Of(Grid, GridBuildings, 8);
            _remains = Layer.Of(Grid, GridRemains, 8);
            _nature = Layer.Of(Grid, GridNature, 8);
            if (_ground.Items == null || _buildings.Items == null || _remains.Items == null || _nature.Items == null) return No("a tile layer is missing");

            _local = Positions(*(IntPtr*)((byte*)type + TypeLocal));
            _water = Positions(*(IntPtr*)((byte*)type + TypeLocalWater));
            if (_local.Length == 0) return No("no footprint");
            for (int i = 0; i < _water.Length; i += 2) if (_water[i] == 0 && _water[i + 1] == 0) _anchorInWater = true;
            for (int i = 0; i < 8; i++)
            {
                _typeCost[i] = *(int*)((byte*)type + TypeCost + 4 * i);
                _tool[i] = *(int*)((byte*)data + DataToolCost + 4 * i);
            }
            _health = *(int*)((byte*)type + TypeHealth);
            var wall = types.wall;
            for (int i = 0; i < 8; i++) _wallCost[i] = *(int*)((byte*)wall.Pointer + TypeCost + 4 * i);
            _bulk = Math.Max(1, *(int*)((byte*)wall.Pointer + TypeBulk));
            _walls = ai.GetWallsThickness(bt);
            _hasProx = ai.HasProximity(bt);
            _minDist = types.farm != null && types.farm.Pointer == type ? 15 : 0;
            if (_hasProx)
            {
                if (bt.TryCast<DiceKingdoms.Game.BuildingTypeLandExtension>() != null) _proxKind = 0;
                else if (bt.TryCast<DiceKingdoms.Game.BuildingTypeProducerAmplifier>() is { } amp)
                {
                    _proxKind = 1;
                    _amplified = amp.AmplifiedBuildingType?.Pointer ?? IntPtr.Zero;
                    _buildingTypes = new Dictionary<int, IntPtr>();
                    var e = new DiceKingdoms.Game.IslandGrid((IntPtr)Grid).Buildings.GetEnumerator();
                    while (e.MoveNext()) { var b = e.Current; _buildingTypes[b.id.value] = b.type?.Pointer ?? IntPtr.Zero; }
                }
                else if (bt.TryCast<DiceKingdoms.Game.BuildingTypeProducerNature>() is { } nat) { _proxKind = 2; _proxNature = (int)nat.natureTile; }
                else return No("unknown neighbour-counting type " + TypeName);
            }

            if (wonder != IntPtr.Zero)
            {
                var set = new Il2CppSystem.Collections.Generic.HashSet<Unity.Mathematics.int2>(wonder);
                if (set.Count > 0)
                {
                    _reserved = new HashSet<long>();
                    var e = set.GetEnumerator();
                    while (e.MoveNext()) { var p = e.Current; _reserved.Add(Key(p.x, p.y)); }
                }
            }

            Side = Math.Max(_sizeX, _sizeY);
            int limit = AiPlacement.CurrentLimit;           // the search square of SearchNearTown (the game's Enumerate size, lowered there)
            if (limit > 0 && limit < Side) Side = limit;
            return true;
        }

        static readonly HashSet<string> Said = new();
        bool No(string why)
        {
            if (Said.Add(why)) Plugin.L.LogInfo("[ai] game's search used: " + why);
            return false;
        }

        static int[] Positions(IntPtr array)
        {
            if (array == IntPtr.Zero) return Array.Empty<int>();
            int n = *(int*)((byte*)array + 0x18);
            var r = new int[n * 2];
            int* p = (int*)((byte*)array + 0x20);
            for (int i = 0; i < n * 2; i++) r[i] = p[i];
            return r;
        }

        static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        int Ground(int x, int y) { var p = _ground.At(x, y); return p == null ? 0 : *(int*)p; }
        bool HasBuilding(int x, int y) { var p = _buildings.At(x, y); return p != null && p[0] != 0; }
        int BuildingId(int x, int y) { var p = _buildings.At(x, y); return p != null && p[0] != 0 ? *(int*)(p + 4) : -1; }
        bool HasRemains(int x, int y) { var p = _remains.At(x, y); return p != null && p[0] != 0; }
        int Nature(int x, int y) { var p = _nature.At(x, y); return p != null && p[0] != 0 ? *(int*)(p + 4) : -1; }

        // GridTransform: rotate (cos, sin by orientation), then mirror x (mirroring is always 0 here)
        static readonly int[] Cos = { 1, 0, -1, 0 }, Sin = { 0, 1, 0, -1 };

        // scratch buffers, reused
        readonly List<long> _foot = new(), _ring = new();
        readonly HashSet<long> _footSet = new(), _ringSet = new(), _proxSet = new();
        readonly int[] _cost = new int[8], _bestCost = new int[8];

        public void Run(byte* ret)
        {
            uint* rng = (uint*)(Grid + GridRandom);
            int cx = (Math.Max(_sizeX, _sizeY) - 1) / 2, cy = cx;
            bool found = false; int bestProx = 0, bestX = 0, bestY = 0, bestO = 0;

            // SpiralGrid.Enumerate(center, Side): the centre, then rings walked +x, +y, -x, -y with growing lengths; the last length three times
            int x = cx, y = cy, step = 1, turnIndex = 0, dir = 0, progress = 0;
            bool first = true;
            while (true)
            {
                if (first) first = false;
                else
                {
                    if (step >= Side) break;
                    x += dir == 0 ? 1 : dir == 2 ? -1 : 0;
                    y += dir == 1 ? 1 : dir == 3 ? -1 : 0;
                    if (++progress >= step)
                    {
                        progress = 0;
                        dir = (dir + 1) & 3;
                        if (++turnIndex >= (step == Side - 1 ? 3 : 2)) { step++; turnIndex = 0; }
                    }
                }

                int g = Ground(x, y);
                if (_anchorInWater ? g != 0 : (g != 2 && g != 6)) continue;
                uint s = *rng;                                   // RandomExtensions.NextSample over the 4 orientations
                int o = (int)(((ulong)s * 4) >> 32);
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                *rng = s;

                for (int r = 0; r < 4; r++, o = (o + 1) & 3)
                {
                    int d = Math.Abs(x - _sizeX / 2) + Math.Abs(y - _sizeY / 2);
                    if (d < _minDist) continue;
                    if (!TotalCost(x, y, o)) continue;
                    int prox = _hasProx ? Proximity() : 0;
                    if (!found || prox > bestProx || (prox == bestProx && Total(_cost) < Total(_bestCost)))
                    {
                        found = true; bestProx = prox; bestX = x; bestY = y; bestO = o;
                        Array.Copy(_cost, _bestCost, 8);
                    }
                    if (!_hasProx && Same(_cost, _typeCost)) break;
                }
            }

            for (int i = 0; i < 0x50; i++) ret[i] = 0;
            if (!found || (_hasProx && bestProx == 0)) return;
            ret[0] = 1;
            *(IntPtr*)(ret + 0x08) = _type;
            *(int*)(ret + 0x10) = bestX; *(int*)(ret + 0x14) = bestY;
            *(int*)(ret + 0x18) = bestO; *(int*)(ret + 0x1C) = 0;
            *(IntPtr*)(ret + 0x20) = IntPtr.Zero;
            *(int*)(ret + 0x28) = _health; *(int*)(ret + 0x2C) = 0;
            for (int i = 0; i < 8; i++) *(int*)(ret + 0x30 + 4 * i) = _bestCost[i];
        }

        static int Total(int[] c) { int t = 0; for (int i = 0; i < 8; i++) t += c[i]; return t; }
        static bool Same(int[] a, int[] b) { for (int i = 0; i < 8; i++) if (a[i] != b[i]) return false; return true; }

        /// <summary>TotalCostToPlaceBuildingAt: false = no spot; otherwise the cost is in <see cref="_cost"/> and the footprint in <see cref="_foot"/>.</summary>
        bool TotalCost(int px, int py, int o)
        {
            _foot.Clear(); _footSet.Clear();
            int c = Cos[o], s = Sin[o];
            for (int i = 0; i < _local.Length; i += 2)
            {
                int lx = _local[i], ly = _local[i + 1];
                long k = Key(px + lx * c - ly * s, py + lx * s + ly * c);
                if (_footSet.Add(k)) _foot.Add(k);
            }
            for (int i = 0; i < 8; i++) _cost[i] = 0;

            foreach (long k in _foot)
            {
                int x = (int)(k >> 32), y = (int)k;
                if (HasBuilding(x, y)) return false;
                int g = Ground(x, y);
                bool inWater = false;
                for (int i = 0; i < _water.Length && !inWater; i += 2)
                {
                    int wx = _water[i], wy = _water[i + 1];
                    inWater = px + wx * c - wy * s == x && py + wx * s + wy * c == y;
                }
                if (inWater ? g != 0 : (g != 2 && g != 6)) return false;
                if (!_isWonder && _reserved != null && _reserved.Contains(k)) return false;
                int n = Nature(x, y);
                bool clean = false;
                if (n >= 0 && n != NatureFlower)
                {
                    if (n > 3) return false;
                    clean = true;
                }
                if (HasRemains(x, y) || clean) for (int i = 0; i < 8; i++) _cost[i] += _tool[i];
            }

            if (_walls > 0)
            {
                _ring.Clear(); _ringSet.Clear();
                foreach (long k in _foot)
                {
                    int fx = (int)(k >> 32), fy = (int)k;
                    for (int dy = -_walls; dy <= _walls; dy++)
                        for (int dx = -_walls; dx <= _walls; dx++)
                        {
                            int x = fx + dx, y = fy + dy;
                            long q = Key(x, y);
                            if (_ringSet.Contains(q) || _footSet.Contains(q)) continue;
                            if (HasBuilding(x, y)) continue;
                            int g = Ground(x, y);
                            if (g == 4 || g == 1) continue;
                            if (g != 2 && g != 6) return false;
                            int n = Nature(x, y);
                            if ((n >= 0 && n != NatureFlower) || HasRemains(x, y)) for (int i = 0; i < 8; i++) _cost[i] += _tool[i];
                            _ringSet.Add(q); _ring.Add(q);
                        }
                }
            }

            for (int i = 0; i < 8; i++) _cost[i] += _typeCost[i];
            int count = _walls > 0 ? _ring.Count : 0;
            int bulk = count < 1 ? 0 : (count - 1) / _bulk + 1;
            for (int i = 0; i < 8; i++) _cost[i] += _wallCost[i] * bulk;
            return true;
        }

        /// <summary>ProximityAt: distinct edge neighbours of the footprint (not in it) that pass the type's test.</summary>
        int Proximity()
        {
            _proxSet.Clear();
            int count = 0;
            foreach (long k in _foot)
            {
                int fx = (int)(k >> 32), fy = (int)k;
                for (int dir = 0; dir < 4; dir++)
                {
                    int x = fx + (dir == 0 ? 1 : dir == 2 ? -1 : 0), y = fy + (dir == 1 ? 1 : dir == 3 ? -1 : 0);
                    long t = Key(x, y);
                    if (_footSet.Contains(t) || !_proxSet.Add(t)) continue;
                    bool ok = _proxKind switch
                    {
                        0 => Ground(x, y) is 0 or 1,
                        1 => BuildingId(x, y) is int id && id >= 0 && _buildingTypes.TryGetValue(id, out var bt) && bt == _amplified && bt != IntPtr.Zero,
                        _ => Nature(x, y) == _proxNature,
                    };
                    if (ok) count++;
                }
            }
            return count;
        }
    }

    [DllImport("kernel32.dll")] private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);
    [DllImport("kernel32.dll")] private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
}

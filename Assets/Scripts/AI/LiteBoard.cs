using System.Collections.Generic;

/// <summary>
/// A bare-bones copy of the board for the robots to think on: one byte per cell, no Unity
/// objects, so a search can run on a background thread and try millions of positions.
/// Move rules match the pieces in Assets/Scripts/Pieces (checked by AISelfTest):
///   Rook: the 6 straight lines. Bishop: the 20 diagonals (two or three axes change).
///   Queen: all 26 lines. King: one step any of the 26 ways. Knight: 2 along one axis and 1
///   along another. Pawn: one step forward onto an empty cell, or a capture on any of the
///   8 cells around that one; it becomes a Queen on the far side.
/// </summary>
public sealed class LiteBoard
{
    // Cell values: 0 empty, otherwise (color + 1) * 8 + type
    public const int Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6;

    public readonly int N;
    public readonly int Cells;
    public readonly byte[] cell;
    public ulong Hash;

    public static int ColorOf(byte piece) => piece / 8 - 1;
    public static int TypeOf(byte piece) => piece & 7;
    public static byte Make(int color, int type) => (byte)((color + 1) * 8 + type);

    private readonly Tables t;

    public LiteBoard(int n)
    {
        N = n;
        Cells = n * n * n;
        cell = new byte[Cells];
        t = Tables.For(n);
    }

    public int Index(int x, int y, int z) => (x * N + y) * N + z;
    public void Coords(int sq, out int x, out int y, out int z) { z = sq % N; y = sq / N % N; x = sq / (N * N); }

    public void Put(int sq, byte piece)
    {
        if (cell[sq] != 0) Hash ^= t.zobrist[sq * 64 + cell[sq]];
        cell[sq] = piece;
        if (piece != 0) Hash ^= t.zobrist[sq * 64 + piece];
    }

    public ulong SideKey(int orderIndex) => t.side[orderIndex & 7];

    // ───────────────────────── Moves ─────────────────────────
    // A move is from | to << 10 | promotes << 20

    public static int From(int move) => move & 1023;
    public static int To(int move) => (move >> 10) & 1023;
    public static bool Promotes(int move) => (move >> 20) != 0;

    /// <summary>Every move for this color's pieces (may leave its own king open: the reply takes it)</summary>
    public void Moves(int color, List<int> moves, bool capturesOnly)
    {
        for (int sq = 0; sq < Cells; sq++)
        {
            byte p = cell[sq];
            if (p == 0 || ColorOf(p) != color) continue;
            PieceMoves(sq, moves, capturesOnly);
        }
    }

    /// <summary>The moves of the piece on this cell</summary>
    public void PieceMoves(int sq, List<int> moves, bool capturesOnly)
    {
        byte p = cell[sq];
        int color = ColorOf(p), type = TypeOf(p);
        switch (type)
        {
            case Pawn:
            {
                int ahead = t.pawnAhead[color * Cells + sq];
                if (!capturesOnly && ahead >= 0 && cell[ahead] == 0) Add(moves, sq, ahead, color);
                foreach (int c in t.pawnCaptures[color * Cells + sq])
                    if (cell[c] != 0 && ColorOf(cell[c]) != color) Add(moves, sq, c, color);
                break;
            }
            case Knight:
                foreach (int to in t.knight[sq]) Target(moves, sq, to, color, capturesOnly);
                break;
            case King:
                for (int d = 0; d < 26; d++)
                {
                    int[] ray = t.rays[sq * 26 + d];
                    if (ray.Length > 0) Target(moves, sq, ray[0], color, capturesOnly);
                }
                break;
            default:
                for (int d = 0; d < 26; d++)
                {
                    int axes = Tables.AxesChanged[d];
                    if (type == Rook && axes != 1) continue;
                    if (type == Bishop && axes == 1) continue;
                    foreach (int to in t.rays[sq * 26 + d])
                    {
                        if (cell[to] == 0) { if (!capturesOnly) moves.Add(sq | to << 10); continue; }
                        if (ColorOf(cell[to]) != color) moves.Add(sq | to << 10);
                        break;
                    }
                }
                break;
        }
    }

    private void Target(List<int> moves, int from, int to, int color, bool capturesOnly)
    {
        byte there = cell[to];
        if (there == 0) { if (!capturesOnly) moves.Add(from | to << 10); }
        else if (ColorOf(there) != color) moves.Add(from | to << 10);
    }

    private void Add(List<int> moves, int from, int to, int color)
    {
        moves.Add(from | to << 10 | (t.promotesAt[color * Cells + to] ? 1 << 20 : 0));
    }

    /// <summary>How many cells this piece reaches, and what it hits there (for the evaluation)</summary>
    public void Reach(int sq, out int count, List<int> hits)
    {
        count = 0;
        hits.Clear();
        byte p = cell[sq];
        int color = ColorOf(p), type = TypeOf(p);
        switch (type)
        {
            case Pawn:
            {
                // Like the game's pawns: the step ahead when empty, and captures that are there
                int ahead = t.pawnAhead[color * Cells + sq];
                if (ahead >= 0 && cell[ahead] == 0) count++;
                foreach (int c in t.pawnCaptures[color * Cells + sq])
                    if (cell[c] != 0 && ColorOf(cell[c]) != color) { count++; hits.Add(c); }
                break;
            }
            case Knight:
                foreach (int to in t.knight[sq]) { count++; if (cell[to] != 0) hits.Add(to); }
                break;
            case King:
                for (int d = 0; d < 26; d++)
                {
                    int[] ray = t.rays[sq * 26 + d];
                    if (ray.Length == 0) continue;
                    count++;
                    if (cell[ray[0]] != 0) hits.Add(ray[0]);
                }
                break;
            default:
                for (int d = 0; d < 26; d++)
                {
                    int axes = Tables.AxesChanged[d];
                    if (type == Rook && axes != 1) continue;
                    if (type == Bishop && axes == 1) continue;
                    foreach (int to in t.rays[sq * 26 + d])
                    {
                        count++;
                        if (cell[to] != 0) { hits.Add(to); break; }
                    }
                }
                break;
        }
    }

    /// <summary>Is any other color's piece able to take this cell's piece right now?</summary>
    public bool Attacked(int sq)
    {
        byte target = cell[sq];
        int color = ColorOf(target);
        var buffer = new List<int>(32);
        for (int from = 0; from < Cells; from++)
        {
            byte p = cell[from];
            if (p == 0 || ColorOf(p) == color) continue;
            buffer.Clear();
            PieceMoves(from, buffer, true);
            foreach (int m in buffer) if (To(m) == sq) return true;
        }
        return false;
    }

    public int KingOf(int color)
    {
        byte king = Make(color, King);
        for (int sq = 0; sq < Cells; sq++) if (cell[sq] == king) return sq;
        return -1;
    }

    // ───────────────────────── Precomputed tables per board size ─────────────────────────

    private sealed class Tables
    {
        public static readonly int[][] Dirs = BuildDirs();
        public static readonly int[] AxesChanged = BuildAxes();

        public int[][] rays;          // [sq * 26 + dir] cells along that line, nearest first
        public int[][] knight;        // [sq]
        public int[] pawnAhead;       // [color * cells + sq], -1 off the board
        public int[][] pawnCaptures;  // [color * cells + sq]
        public bool[] promotesAt;     // [color * cells + sq]
        public ulong[] zobrist;       // [sq * 64 + piece]
        public ulong[] side = new ulong[8];

        private static readonly Dictionary<int, Tables> cache = new Dictionary<int, Tables>();

        public static Tables For(int n)
        {
            lock (cache)
            {
                if (!cache.TryGetValue(n, out Tables made)) cache[n] = made = new Tables(n);
                return made;
            }
        }

        // Forward axis and direction per color: White +x, Black -x, Green +z, Purple -z, Yellow +y, Orange -y
        private static readonly int[][] Forward = { new[] { 1, 0, 0 }, new[] { -1, 0, 0 }, new[] { 0, 0, 1 }, new[] { 0, 0, -1 }, new[] { 0, 1, 0 }, new[] { 0, -1, 0 } };

        private Tables(int n)
        {
            int cells = n * n * n;
            int Idx(int x, int y, int z) => (x * n + y) * n + z;
            bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < n && y < n && z < n;

            rays = new int[cells * 26][];
            knight = new int[cells][];
            pawnAhead = new int[6 * cells];
            pawnCaptures = new int[6 * cells][];
            promotesAt = new bool[6 * cells];

            for (int x = 0; x < n; x++)
            for (int y = 0; y < n; y++)
            for (int z = 0; z < n; z++)
            {
                int sq = Idx(x, y, z);
                for (int d = 0; d < 26; d++)
                {
                    var line = new List<int>();
                    int[] v = Dirs[d];
                    for (int k = 1; ; k++)
                    {
                        int a = x + v[0] * k, b = y + v[1] * k, c = z + v[2] * k;
                        if (!In(a, b, c)) break;
                        line.Add(Idx(a, b, c));
                    }
                    rays[sq * 26 + d] = line.ToArray();
                }

                var jumps = new List<int>();
                for (int dx = -2; dx <= 2; dx++)
                for (int dy = -2; dy <= 2; dy++)
                for (int dz = -2; dz <= 2; dz++)
                {
                    int[] abs = { System.Math.Abs(dx), System.Math.Abs(dy), System.Math.Abs(dz) };
                    System.Array.Sort(abs);
                    if (abs[0] == 0 && abs[1] == 1 && abs[2] == 2 && In(x + dx, y + dy, z + dz)) jumps.Add(Idx(x + dx, y + dy, z + dz));
                }
                knight[sq] = jumps.ToArray();

                for (int color = 0; color < 6; color++)
                {
                    int[] f = Forward[color];
                    int ax = x + f[0], ay = y + f[1], az = z + f[2];
                    pawnAhead[color * cells + sq] = In(ax, ay, az) ? Idx(ax, ay, az) : -1;
                    var caps = new List<int>();
                    for (int d = 0; d < 26; d++)
                    {
                        int[] v = Dirs[d];
                        int dot = v[0] * f[0] + v[1] * f[1] + v[2] * f[2];
                        bool straight = v[0] == f[0] && v[1] == f[1] && v[2] == f[2];
                        // One step forward on the pawn's axis, plus a step sideways on at least one other axis
                        if (dot == 1 && !straight && In(x + v[0], y + v[1], z + v[2])) caps.Add(Idx(x + v[0], y + v[1], z + v[2]));
                    }
                    pawnCaptures[color * cells + sq] = caps.ToArray();
                    int along = f[0] != 0 ? x : f[1] != 0 ? y : z;
                    int sign = f[0] + f[1] + f[2];
                    promotesAt[color * cells + sq] = sign > 0 ? along == n - 1 : along == 0;
                }
            }

            var rng = new System.Random(3_141_592);
            zobrist = new ulong[cells * 64];
            for (int i = 0; i < zobrist.Length; i++) zobrist[i] = NextULong(rng);
            for (int i = 0; i < side.Length; i++) side[i] = NextULong(rng);
        }

        private static ulong NextULong(System.Random r)
        {
            var b = new byte[8];
            r.NextBytes(b);
            return System.BitConverter.ToUInt64(b, 0);
        }

        private static int[][] BuildDirs()
        {
            var list = new List<int[]>();
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
                if (x != 0 || y != 0 || z != 0) list.Add(new[] { x, y, z });
            return list.ToArray();
        }

        private static int[] BuildAxes()
        {
            var dirs = BuildDirs();
            var axes = new int[dirs.Length];
            for (int i = 0; i < dirs.Length; i++) axes[i] = (dirs[i][0] != 0 ? 1 : 0) + (dirs[i][1] != 0 ? 1 : 0) + (dirs[i][2] != 0 ? 1 : 0);
            return axes;
        }
    }
}

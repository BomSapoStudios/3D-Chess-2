using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queen Blitz movement with portals and ramps. This mirrors Tools/QueenBlitz/solver.py
/// (Board._step and Board.moves) exactly, so a puzzle plays in the app the way the solver graded it:
/// - Portals come in pairs, an in end and an out end (the out end is a block from every side)
///   unless the pair is two-way. Each is an upright disc on its own square facing a level axis.
///   She goes in only through the front (moving against its facing, straight or diagonally),
///   comes out of the paired portal heading straight out of its face, level, whatever angle she
///   went in at, and carries on: one continuous move that sweeps pawns on both
///   sides. She can't stop on a portal, and can't go in if the square after the exit is a block
///   or off the board. Portals may chain.
/// - Ramps rise toward one or more level directions. Moving straight along a rising direction she
///   climbs: from the ramp square she lands one floor up, one square on, still level. Every other
///   way in is a wall, as is a ramp whose landing square is a block, off the board, a portal or a
///   ramp. She can't stop on a ramp.
/// </summary>
public static partial class QueenBlitz
{
    public struct PortalPair
    {
        public BoardPosition a, b;           // a is the in end
        public Vector3Int faceA, faceB;      // level axis directions the faces point
        public bool twoWay;
    }

    public struct RampRise
    {
        public BoardPosition at;
        public Vector3Int dir;               // a level axis direction she climbs toward
    }

    /// <summary>Where a portal sends the queen: its own facing, the paired square and that facing</summary>
    private class PortalLink
    {
        public Vector3Int face, outFace;
        public BoardPosition other;
    }

    // A null link is an out end: a block
    private static readonly Dictionary<BoardPosition, PortalLink> portalAt = new Dictionary<BoardPosition, PortalLink>();
    private static readonly Dictionary<BoardPosition, HashSet<Vector3Int>> rampAt = new Dictionary<BoardPosition, HashSet<Vector3Int>>();

    public static bool IsPortal(BoardPosition p) => Active && portalAt.ContainsKey(p);
    public static bool IsRamp(BoardPosition p) => Active && rampAt.ContainsKey(p);

    private static void SetUpFeatures(Puzzle p)
    {
        portalAt.Clear();
        rampAt.Clear();
        routes.Clear();
        PendingRoute = null;
        foreach (PortalPair pair in p.portals ?? new PortalPair[0])
        {
            portalAt[pair.a] = new PortalLink { face = pair.faceA, other = pair.b, outFace = pair.faceB };
            portalAt[pair.b] = pair.twoWay ? new PortalLink { face = pair.faceB, other = pair.a, outFace = pair.faceA } : null;
        }
        foreach (RampRise r in p.ramps ?? new RampRise[0])
        {
            if (!rampAt.TryGetValue(r.at, out var dirs)) rampAt[r.at] = dirs = new HashSet<Vector3Int>();
            dirs.Add(r.dir);
        }
    }

    /// <summary>
    /// One way the queen gets from one square to another: what she sweeps on the way (every square
    /// passed or stopped on before the end), and the points she slides through, for the animation
    /// </summary>
    public class Route
    {
        public BoardPosition from, to;
        public readonly List<BoardPosition> swept = new List<BoardPosition>();
        public readonly List<Vector3Int> sweptDir = new List<Vector3Int>();   // her heading at each swept square
        public readonly List<Vector3> points = new List<Vector3>();           // board-local, floor level
        public readonly List<bool> teleport = new List<bool>();               // teleport[i]: points[i] to points[i + 1] is a jump
        public bool special;                                                    // through a portal or up a ramp
        public readonly List<BoardPosition> entered = new List<BoardPosition>(); // portals she goes into

        public Route Copy()
        {
            var r = new Route { from = from, to = to, special = special };
            r.swept.AddRange(swept); r.sweptDir.AddRange(sweptDir); r.points.AddRange(points); r.teleport.AddRange(teleport); r.entered.AddRange(entered);
            return r;
        }

        public void Point(Vector3 p, bool jumpFromLast = false)
        {
            if (points.Count > 0) teleport.Add(jumpFromLast);
            points.Add(p);
        }
    }

    // The routes from the queen's square, worked out with her moves; the one she takes animates
    private static readonly Dictionary<(BoardPosition, BoardPosition), Route> routes = new Dictionary<(BoardPosition, BoardPosition), Route>();
    public static Route PendingRoute { get; private set; }
    /// <summary>The portals the queen can go into from where she stands (lit up while she's picked up)</summary>
    public static readonly HashSet<BoardPosition> EnterablePortals = new HashSet<BoardPosition>();

    private static readonly Vector3Int Up = new Vector3Int(0, 1, 0);

    private static BoardPosition Add(BoardPosition c, Vector3Int d) => new BoardPosition(c.x + d.x, c.y + d.y, c.z + d.z);

    private static bool Open(BoardPosition c) => ChessBoard.Instance.IsValidPosition(c) && !blocked.Contains(c);

    private static Vector3 Floor(BoardPosition c) => ChessBoard.Instance.BoardToLocalPosition(c);

    /// <summary>
    /// One step from c heading d, through portals and up ramps (solver._step). Adds what it passes to
    /// the route and returns the landing square and her new heading; false when it's a wall.
    /// </summary>
    private static bool Step(BoardPosition c, Vector3Int d, Route route, out BoardPosition landing, out Vector3Int heading)
    {
        landing = c;
        heading = d;
        var seen = new HashSet<(BoardPosition, Vector3Int)>();
        var swept = new List<BoardPosition>();
        var sweptDir = new List<Vector3Int>();
        var pts = new List<(Vector3 p, bool jump)>();
        var entered = new List<BoardPosition>();
        float cell = ChessBoard.Instance.cellSize;
        while (true)
        {
            BoardPosition next = Add(c, d);
            if (!Open(next)) return false;
            if (portalAt.TryGetValue(next, out PortalLink link))
            {
                if (link == null) return false; // an out end: a block
                bool alongX = link.face.x != 0;
                int against = alongX ? -link.face.x : -link.face.z;
                if ((alongX ? d.x : d.z) != against || !seen.Add((next, d))) return false;
                swept.Add(next); sweptDir.Add(d);
                entered.Add(next);
                pts.Add((Floor(next), false));          // into the disc
                d = link.outFace; // always straight out of the exit disc, level
                swept.Add(link.other); sweptDir.Add(d);
                pts.Add((Floor(link.other), true));     // out of its partner
                c = link.other;
                continue;
            }
            if (rampAt.TryGetValue(next, out HashSet<Vector3Int> rises))
            {
                if (d.y != 0 || !rises.Contains(d)) return false;
                BoardPosition top = Add(Add(next, d), Up);
                if (!Open(top) || portalAt.ContainsKey(top) || rampAt.ContainsKey(top)) return false;
                swept.Add(next); sweptDir.Add(d);
                pts.Add((Floor(next) + Vector3.up * cell * 0.5f, false)); // up the slope
                landing = top;
                heading = d;
                Commit(top);
                return true;
            }
            landing = next;
            heading = d;
            Commit(next);
            return true;
        }

        void Commit(BoardPosition end)
        {
            if (pts.Count > 0) route.special = true;
            foreach (BoardPosition e in entered) EnterablePortals.Add(e);
            route.entered.AddRange(entered);
            route.swept.AddRange(swept);
            route.sweptDir.AddRange(sweptDir);
            foreach (var (p, jump) in pts) route.Point(p, jump);
            route.Point(Floor(end));
        }
    }

    /// <summary>Every square the queen can stop on from `from`, with how she gets there (solver.moves)</summary>
    private static void FindRoutes(BoardPosition from)
    {
        routes.Clear();
        EnterablePortals.Clear();
        int n = Current.size;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var d = new Vector3Int(dx, dy, dz);
            BoardPosition c = from;
            var route = new Route { from = from };
            route.Point(Floor(from));
            Route last = null;
            for (int k = 0; k < 4 * n * n; k++)
            {
                if (last != null) { route.swept.Add(c); route.sweptDir.Add(d); } // carrying on past a stop
                if (!Step(c, d, route, out BoardPosition landing, out Vector3Int heading)) break;
                c = landing;
                d = heading;
                if (c == from) break; // a portal loop back to the start
                last = route.Copy();
                last.to = c;
                if (!Current.ice) Keep(last);
            }
            if (Current.ice && last != null) Keep(last);
        }
    }

    /// <summary>Two ways to the same square: keep the one that sweeps more of the pawns still standing</summary>
    private static void Keep(Route r)
    {
        var key = (r.from, r.to);
        if (!routes.TryGetValue(key, out Route old) || Pawns(r) > Pawns(old)) routes[key] = r;
    }

    private static int Pawns(Route r)
    {
        int count = 0;
        foreach (BoardPosition p in r.swept)
        {
            ChessPiece piece = ChessBoard.Instance.GetPieceAt(p);
            if (piece != null && piece.pieceColor == PawnColor) count++;
        }
        ChessPiece end = ChessBoard.Instance.GetPieceAt(r.to);
        return count + (end != null && end.pieceColor == PawnColor ? 1 : 0);
    }

    /// <summary>Every move from a square, with its route (for the trails and portal taps)</summary>
    public static ICollection<Route> RoutesFrom(BoardPosition from)
    {
        bool fresh = routes.Count > 0;
        foreach (var key in routes.Keys) { fresh = key.Item1 == from; break; }
        if (!fresh) FindRoutes(from);
        return routes.Values;
    }

    /// <summary>The square in front of a portal's way in (where its floor pad is), if it's a way in</summary>
    public static BoardPosition? PortalApproach(BoardPosition portal)
    {
        if (!portalAt.TryGetValue(portal, out PortalLink link) || link == null) return null;
        return new BoardPosition(portal.x + link.face.x, portal.y + link.face.y, portal.z + link.face.z);
    }

    public static IEnumerable<BoardPosition> PortalSquares => portalAt.Keys;

    /// <summary>The route for a move the queen is making (worked out again if her moves weren't asked for first)</summary>
    public static Route RouteFor(BoardPosition from, BoardPosition to)
    {
        if (!routes.TryGetValue((from, to), out Route r))
        {
            FindRoutes(from);
            routes.TryGetValue((from, to), out r);
        }
        return r;
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>BlitzDemo's check against the solver: every stop from a square, and the pawns each sweeps</summary>
    public static string DebugMoves(BoardPosition from)
    {
        FindRoutes(from);
        var parts = new List<string>();
        foreach (Route r in routes.Values)
        {
            var pawns = new List<string>();
            foreach (BoardPosition p in r.swept) if (System.Array.IndexOf(Current.pawns, p) >= 0) pawns.Add($"{p.x}{p.y}{p.z}");
            if (System.Array.IndexOf(Current.pawns, r.to) >= 0) pawns.Add($"{r.to.x}{r.to.y}{r.to.z}");
            pawns.Sort();
            parts.Add($"{r.to.x}{r.to.y}{r.to.z}:{string.Join(".", pawns)}");
        }
        parts.Sort();
        return string.Join(" ", parts);
    }
#endif

    /// <summary>The queen's slide animation takes the route she was given (once)</summary>
    public static Route TakeRoute(BoardPosition to)
    {
        Route r = PendingRoute;
        PendingRoute = null;
        return r != null && r.to == to ? r : null;
    }
}

"""Queen Blitz solver: the fewest queen sweeps that clear every pawn.

A move slides the queen any distance along one of the 26 directions (axes, flat diagonals,
3D diagonals), stopping before a blocker or the board edge. Every pawn on the cells it passes
through or stops on is taken. Placing the queen (when the puzzle asks) is not a move.

On ice the queen can't stop partway: every move slides to the edge or the square before a block.

Portals (Brandon, Oct 26) come in pairs, each pair its own colour, with a distinct in and out
end: she can only go in the in end (the out end is a block from every side). Each is a vertical
disc on its own square, facing one level direction. The queen enters only through the front (moving against its facing, straight
or diagonally); from the back or the side it's a block. She comes out of the paired portal
heading away from its face: her move is turned the way the entry facing turns into the exit
facing, and her climb or fall is kept. One trip through is one continuous move, sweeping pawns
on both sides; she can't stop on a portal square. If the square after the exit is a block or
the edge, she can't go in. Portals may chain.

Ramps sit on a square and rise toward one or more level directions. Moving straight along a
rising direction she climbs: from the ramp square she carries on one floor up, still level.
Every other way in (the high side, the flat sides, diagonals, from above or below) is a wall,
and she can't stop on a ramp. If the square she'd climb to is a block or off the board, the
ramp is a wall too.

UP is the axis that counts as up (y).
"""
import itertools

DIRS = [d for d in itertools.product((-1, 0, 1), repeat=3) if d != (0, 0, 0)]
UP = 1


def _turn(d, face_in, face_out):
    """d turned about the up axis by the turn that takes -face_in onto face_out"""
    a, b = [i for i in range(3) if i != UP]
    fi = (-face_in[a], -face_in[b])
    fo = (face_out[a], face_out[b])
    # rotations by 0/90/180/270 degrees in the level plane
    for rot in ((1, 0, 0, 1), (0, -1, 1, 0), (-1, 0, 0, -1), (0, 1, -1, 0)):
        if (rot[0] * fi[0] + rot[1] * fi[1], rot[2] * fi[0] + rot[3] * fi[1]) == fo:
            out = list(d)
            out[a], out[b] = rot[0] * d[a] + rot[1] * d[b], rot[2] * d[a] + rot[3] * d[b]
            return tuple(out)
    raise ValueError("portal facings must be level")


class Board:
    def __init__(self, n, pawns, blockers=(), ice=False, portals=(), ramps=None, two_way=False):
        """portals: [(in cell, facing, out cell, facing)]; ramps: {cell: [rising directions]}.
        two_way lets her go in either end."""
        self.n = n
        self.ice = ice
        self.portal = {}
        for a, fa, b, fb in portals:
            self.portal[tuple(a)] = (tuple(fa), tuple(b), tuple(fb))
            self.portal[tuple(b)] = (tuple(fb), tuple(a), tuple(fa)) if two_way else None
        self.ramp = {tuple(c): {tuple(d) for d in ds} for c, ds in (ramps or {}).items()}
        self.pawns = [tuple(p) for p in pawns]
        self.blockers = {tuple(b) for b in blockers}
        self.bit = {p: 1 << i for i, p in enumerate(self.pawns)}
        self.full = (1 << len(self.pawns)) - 1
        self._moves = {}

    def inside(self, c):
        return all(0 <= v < self.n for v in c)

    def _open(self, c):
        return self.inside(c) and c not in self.blockers

    def _step(self, c, d):
        """Where one step from c in direction d lands, through portals and up ramps:
        (cells passed on the way, landing cell, new direction), or None when it's a wall.
        Only the landing cell can be stopped on; the cells passed are swept too."""
        passed, seen = [], set()
        while True:
            nxt = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
            if not self._open(nxt):
                return None
            if nxt in self.portal:
                if self.portal[nxt] is None:
                    return None  # an out end: a block
                face, other, out_face = self.portal[nxt]
                ax = [i for i in range(3) if face[i]][0]
                if d[ax] != -face[ax] or (nxt, d) in seen:
                    return None
                seen.add((nxt, d))
                passed.append(nxt)
                d = _turn(d, face, out_face)
                passed.append(other)
                c = other
                continue
            if nxt in self.ramp:
                up = tuple(1 if i == UP else 0 for i in range(3))
                if d[UP] != 0 or d not in self.ramp[nxt]:
                    return None
                top = (nxt[0] + d[0] + up[0], nxt[1] + d[1] + up[1], nxt[2] + d[2] + up[2])
                if not self._open(top) or top in self.portal or top in self.ramp:
                    return None
                passed.append(nxt)
                return passed, top, d
            return passed, nxt, d

    def cells(self):
        return list(itertools.product(range(self.n), repeat=3))

    def moves(self, at):
        """[(destination, mask of pawns swept, direction, distance)] from a cell. direction is
        the one she set off in (portals may turn her on the way)."""
        if at in self._moves:
            return self._moves[at]
        out = []
        for d0 in DIRS:
            mask, c, d, k, last = 0, at, d0, 0, None
            while k < 4 * self.n * self.n:
                step = self._step(c, d)
                if step is None:
                    break
                passed, c, d = step
                k += 1
                for x in passed:
                    mask |= self.bit.get(x, 0)
                mask |= self.bit.get(c, 0)
                if c == at:
                    break  # a portal loop back to the start
                last = c
                if not self.ice:
                    out.append((c, mask, d0, k))
            if self.ice and last is not None:
                out.append((last, mask, d0, k))
        self._moves[at] = out
        return out

    def starts(self):
        return [c for c in self.cells() if c not in self.blockers and c not in self.bit
                and c not in self.portal and c not in self.ramp]

    def solve(self, start=None, limit=8):
        """BFS: (par, one optimal line [(from, to)], start used). start=None: the queen may start on any empty cell."""
        begin = [start] if start else self.starts()
        frontier = {(s, self.full): None for s in begin}
        parent = dict(frontier)
        for depth in range(1, limit + 1):
            nxt = {}
            for (at, left) in frontier:
                for to, swept, _, _ in self.moves(at):
                    state = (to, left & ~swept)
                    if state in parent or state in nxt:
                        continue
                    nxt[state] = (at, left)
                    if state[1] == 0:
                        parent.update(nxt)
                        line, s = [], state
                        while parent[s] is not None:
                            p = parent[s]
                            line.append((p[0], s[0]))
                            s = p
                        line.reverse()
                        return depth, line, s[0]
            parent.update(nxt)
            frontier = nxt
            if not frontier:
                break
        return None, None, None

    def solve_from(self, at, left, limit):
        """Fewest moves to clear the pawns in mask `left` with the queen on `at` (None past limit)"""
        if left == 0:
            return 0
        frontier, seen = {(at, left)}, {(at, left)}
        for depth in range(1, limit + 1):
            nxt = set()
            for (a, l) in frontier:
                for to, swept, _, _ in self.moves(a):
                    state = (to, l & ~swept)
                    if state[1] == 0:
                        return depth
                    if state not in seen:
                        seen.add(state)
                        nxt.add(state)
            frontier = nxt
        return None

    def greedy(self, start=None, limit=20):
        """Moves a player taking the biggest bite each time would need. With nothing in reach it
        repositions to the cell with the biggest bite next. Best over all allowed starts."""
        def bite(at, left):
            return max((bin(m[1] & left).count("1"), m) for m in self.moves(at))
        best_total = None
        for s in ([start] if start else self.starts()):
            at, left, count = s, self.full, 0
            while left and count < limit:
                size, (to, swept, _, _) = bite(at, left)
                if size == 0:
                    to = max(self.moves(at), key=lambda m: bite(m[0], left)[0])[0]
                    swept = 0
                left &= ~swept
                at = to
                count += 1
            if best_total is None or count < best_total:
                best_total = count
        return best_total

    def optimal_starts(self, par):
        """Cells the queen can start from and still clear the board in par moves"""
        return [s for s in self.starts() if self.solve(s, par)[0] == par]

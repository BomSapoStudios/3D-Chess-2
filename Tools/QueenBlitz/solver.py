"""Queen Blitz solver: the fewest queen sweeps that clear every pawn.

A move slides the queen any distance along one of the 26 directions (axes, flat diagonals,
3D diagonals), stopping before a blocker or the board edge. Every pawn on the cells it passes
through or stops on is taken. Placing the queen (when the puzzle asks) is not a move.

On ice the queen can't stop partway: every move slides to the edge or the square before a block.

Portals (Brandon, Oct 26) come in pairs, each pair its own colour, with a distinct in and out
end: she can only go in the in end (the out end is a block from every side). A pair can
instead be two-way, with its own look, and then either end takes her in. Each is a vertical
disc on its own square, facing one level direction. The queen enters only through the front (moving against its facing, straight
or diagonally); from the back or the side it's a block. She comes out of the paired portal
heading straight out of its face, level, whatever angle she went in at (Brandon, Oct 27). One trip through is one continuous move, sweeping pawns
on both sides; she can't stop on a portal square. If the square after the exit is a block or
the edge, she can't go in. Portals may chain.

Ramps sit on a square and rise toward one or more level directions. Moving straight along a
rising direction she climbs: from the ramp square she carries on one floor up, still level.
Every other way in (the high side, the flat sides, diagonals, from above or below) is a wall,
and she can't stop on a ramp. If the square she'd climb to is a block or off the board, the
ramp is a wall too.

Bombs (Brandon, Oct 30) sit on empty squares. One goes off when the queen passes over it or stops
on it, taking every pawn on the 26 squares around it (the 3x3x3 cube), and is gone. A blast that
reaches another bomb sets it off too. Bombs don't stop her; blocks, portals and ramps survive a
blast. Puzzles can have bombs already placed, and bombs for the player to place before the first
move (placing one isn't a move): solve_bombs tries every placement. A bomb the Mirror Queen passes
goes off too.

UP is the axis that counts as up (y).

Mirror Queen (Brandon, Oct 30): a second queen starts on the mirror image of the queen's square
(flipped on the axes in `mirror`, (1, 1, 1) by default: through the cube's centre, so she can't
share the queen's square). Each queen move, the mirror sets off the opposite way (each flipped
axis of the direction reversed) and resolves her own move against the board: on a normal floor she
goes as many steps as the queen did unless something stops her sooner, on ice she slides as far as
she can. She sweeps pawns the same way. Each queen is a block to the other: the queen moves first
and can't pass the mirror; the mirror then can't pass where the queen stopped. The player only
moves the queen, so a move that sweeps nothing can still steer the mirror.
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
    def __init__(self, n, pawns, blockers=(), ice=False, portals=(), ramps=None, mirror=None, bombs=(), _paths=None):
        """portals: [(in cell, facing, out cell, facing[, two_way])]; ramps: {cell: [rising
        directions]}. A two-way pair lets her go in either end (its own look in the app).
        mirror: None, or the axes the Mirror Queen flips, e.g. (1, 1, 1). bombs: cells with a bomb.

        A state's mask holds a bit per pawn still standing and, above those, a bit per bomb not yet
        gone off. after(left, swept) moves a state on; it's clear when left & pawn_mask is 0."""
        self.n = n
        self.ice = ice
        self.mirror = tuple(mirror) if mirror else None
        self.portal = {}
        for a, fa, b, fb, *rest in portals:
            two_way = bool(rest and rest[0])
            self.portal[tuple(a)] = (tuple(fa), tuple(b), tuple(fb))
            self.portal[tuple(b)] = (tuple(fb), tuple(a), tuple(fa)) if two_way else None
        self.ramp = {tuple(c): {tuple(d) for d in ds} for c, ds in (ramps or {}).items()}
        self.pawns = [tuple(p) for p in pawns]
        self.bombs = [tuple(b) for b in bombs]
        self.blockers = {tuple(b) for b in blockers}
        self.bit = {p: 1 << i for i, p in enumerate(self.pawns + self.bombs)}
        self.pawn_mask = (1 << len(self.pawns)) - 1
        self.bomb_mask = ((1 << len(self.bombs)) - 1) << len(self.pawns)
        self.full = self.pawn_mask | self.bomb_mask
        # What each bomb takes when it goes off: the pawns and bombs on the 26 squares around it
        self.blast = {}
        for b in self.bombs:
            m = 0
            for d in DIRS:
                m |= self.bit.get((b[0] + d[0], b[1] + d[1], b[2] + d[2]), 0)
            self.blast[self.bit[b]] = m
        self._moves = {}
        self._paths = {} if _paths is None else _paths  # shared by boards that differ only in bombs

    def with_bombs(self, bombs):
        """This board with these bombs instead (shares the worked-out lines, so it's cheap)"""
        b = Board(self.n, self.pawns, self.blockers, self.ice, (), None, self.mirror, bombs, self._paths)
        b.portal, b.ramp = self.portal, self.ramp
        return b

    def after(self, left, swept):
        """The state after a move sweeping `swept`: those pawns go, and any bomb passed goes
        off, setting off any bomb in its blast"""
        hit = swept & left & self.bomb_mask
        left &= ~swept
        while hit:
            b = hit & -hit
            hit ^= b
            blast = self.blast[b]
            hit |= blast & left & self.bomb_mask
            left &= ~blast
        return left

    def inside(self, c):
        return all(0 <= v < self.n for v in c)

    def _open(self, c, extra=None):
        return self.inside(c) and c not in self.blockers and c != extra

    def _step(self, c, d, extra=None):
        """Where one step from c in direction d lands, through portals and up ramps:
        (cells passed on the way, landing cell, new direction), or None when it's a wall.
        Only the landing cell can be stopped on; the cells passed are swept too."""
        passed, seen = [], set()
        while True:
            nxt = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
            if not self._open(nxt, extra):
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
                d = out_face  # always straight out of the exit disc
                passed.append(other)
                c = other
                continue
            if nxt in self.ramp:
                up = tuple(1 if i == UP else 0 for i in range(3))
                if d[UP] != 0 or d not in self.ramp[nxt]:
                    return None
                top = (nxt[0] + d[0] + up[0], nxt[1] + d[1] + up[1], nxt[2] + d[2] + up[2])
                if not self._open(top, extra) or top in self.portal or top in self.ramp:
                    return None
                passed.append(nxt)
                return passed, top, d
            return passed, nxt, d

    def cells(self):
        return list(itertools.product(range(self.n), repeat=3))

    def _slide(self, at, d0, extra=None):
        """Every square she could stop on setting off from `at` in direction d0, in order:
        [(square, mask swept to there, steps)]. extra: one more square that's a block (the other queen)."""
        stops, mask = [], 0
        for c, cells, k in self._path(at, d0, extra):
            for x in cells:
                mask |= self.bit.get(x, 0)
            stops.append((c, mask, k))
        return stops

    def _path(self, at, d0, extra=None):
        """The stops setting off from `at` in d0: [(square, squares newly passed up to and including it, steps)].
        Bombs don't change where she can go, so boards that differ only in bombs share these."""
        key = (at, d0, extra)
        if key in self._paths:
            return self._paths[key]
        out, c, d, k = [], at, d0, 0
        while k < 4 * self.n * self.n:
            step = self._step(c, d, extra)
            if step is None:
                break
            passed, c, d = step
            k += 1
            if c == at:
                break  # a portal loop back to the start
            out.append((c, tuple(passed) + (c,), k))
        self._paths[key] = out
        return out

    def moves(self, at, extra=None):
        """[(destination, mask of pawns swept, direction, distance)] from a cell. direction is
        the one she set off in (portals may turn her on the way)."""
        key = (at, extra)
        if key in self._moves:
            return self._moves[key]
        out = []
        for d0 in DIRS:
            stops = self._slide(at, d0, extra)
            if self.ice:
                stops = stops[-1:]
            out += [(c, mask, d0, k) for c, mask, k in stops]
        self._moves[key] = out
        return out

    # ── Mirror Queen ──

    def reflect(self, c):
        return tuple(self.n - 1 - v if f else v for v, f in zip(c, self.mirror))

    def flip(self, d):
        return tuple(-v if f else v for v, f in zip(d, self.mirror))

    def mirror_follow(self, m, d0, k, q):
        """Where the mirror on m ends up when the queen set off in d0 for k steps and stopped on q,
        and the pawns she sweeps: (square, mask)"""
        stops = self._slide(m, self.flip(d0), q)
        if not stops:
            return m, 0
        c, mask, _ = stops[-1] if self.ice else stops[min(k, len(stops)) - 1]
        return c, mask

    def pair_moves(self, q, m):
        """Both queens' moves from (queen, mirror): [(queen to, mirror to, mask swept by both, direction, distance)]"""
        key = ("pair", q, m)
        if key in self._moves:
            return self._moves[key]
        out = []
        for to, mask, d0, k in self.moves(q, m):
            m2, mm = self.mirror_follow(m, d0, k, to)
            out.append((to, m2, mask | mm, d0, k))
        self._moves[key] = out
        return out

    def ambiguous(self, q, m):
        """Two of the queen's moves land on one square but leave the mirror (or the sweep) somewhere
        different: a tap picks a square, not a route, so such a puzzle can't be played as graded"""
        seen = {}
        for to, m2, mask, _, _ in self.pair_moves(q, m):
            if seen.setdefault(to, (m2, mask)) != (m2, mask):
                return True
        return False

    def _empty(self, c):
        return c not in self.blockers and c not in self.bit and c not in self.portal and c not in self.ramp

    def starts(self):
        """Squares the queen can start on (with a mirror, her mirror image must be empty too and not her own square)"""
        return [c for c in self.cells() if self._empty(c)
                and (not self.mirror or (self.reflect(c) != c and self._empty(self.reflect(c))))]

    def _begin(self, s):
        """The search state a start square gives: the queen's square, or (queen, mirror)"""
        return (s, self.reflect(s)) if self.mirror else s

    def _next(self, at):
        """[(next state, mask swept)] from a state"""
        if self.mirror:
            return [((to, m2), mask) for to, m2, mask, _, _ in self.pair_moves(*at)]
        return [(to, mask) for to, mask, _, _ in self.moves(at)]

    def queen(self, state):
        return state[0] if self.mirror else state

    def solve(self, start=None, limit=8):
        """BFS: (par, one optimal line [(from, to)], start used). start=None: the queen may start on any empty cell.
        The line and start are the queen's squares (the mirror's follow from them)."""
        begin = [start] if start else self.starts()
        frontier = {(self._begin(s), self.full): None for s in begin}
        parent = dict(frontier)
        for depth in range(1, limit + 1):
            nxt = {}
            for (at, left) in frontier:
                for to, swept in self._next(at):
                    state = (to, self.after(left, swept))
                    if state[1] & self.pawn_mask == 0:
                        state = (to, 0)
                    if state in parent or state in nxt:
                        continue
                    nxt[state] = (at, left)
                    if state[1] == 0:
                        parent.update(nxt)
                        line, s = [], state
                        while parent[s] is not None:
                            p = parent[s]
                            line.append((self.queen(p[0]), self.queen(s[0])))
                            s = p
                        line.reverse()
                        return depth, line, self.queen(s[0])
            parent.update(nxt)
            frontier = nxt
            if not frontier:
                break
        return None, None, None

    def solve_from(self, at, left, limit):
        """Fewest moves to clear the pawns in mask `left` from state `at` (None past limit). A state
        is the queen's square, or (queen, mirror) with a mirror."""
        if left & self.pawn_mask == 0:
            return 0
        frontier, seen = {(at, left)}, {(at, left)}
        for depth in range(1, limit + 1):
            nxt = set()
            for (a, l) in frontier:
                for to, swept in self._next(a):
                    state = (to, self.after(l, swept))
                    if state[1] & self.pawn_mask == 0:
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
            return max(((bin(left & self.pawn_mask & ~self.after(left, m[1])).count("1"), i, m) for i, m in enumerate(self._next(at))),
                       default=(0, 0, None))
        best_total = None
        for s in ([start] if start else self.starts()):
            at, left, count = self._begin(s), self.full, 0
            while left & self.pawn_mask and count < limit:
                size, _, mv = bite(at, left)
                if mv is None:
                    count = limit
                    break
                to, swept = mv
                if size == 0:
                    to = max(self._next(at), key=lambda m: bite(m[0], left)[0])[0]
                    swept = 0
                left = self.after(left, swept)
                at = to
                count += 1
            if best_total is None or count < best_total:
                best_total = count
        return best_total

    def optimal_starts(self, par):
        """Cells the queen can start from and still clear the board in par moves"""
        return [s for s in self.starts() if self.solve(s, par)[0] == par]

    def bomb_spots(self):
        """Squares a bomb could usefully go on: empty, with a pawn somewhere around it"""
        pawns = set(self.pawns)
        spots = []
        for c in self.starts():
            if c in self.bit:
                continue
            if any((c[0] + d[0], c[1] + d[1], c[2] + d[2]) in pawns for d in DIRS):
                spots.append(c)
        return spots

    def _lines(self, at):
        """[(destination, every square passed on the way and the landing, mask swept)] (no mirror)"""
        out = []
        for d0 in DIRS:
            stops, cells = [], ()
            for c, new, k in self._path(at, d0):
                cells += new
                stops.append((c, cells))
            if self.ice:
                stops = stops[-1:]
            for c, cl in stops:
                m = 0
                for x in cl:
                    m |= self.bit.get(x, 0)
                out.append((c, cl, m))
        return out

    def _around(self, c):
        return [(c[0] + d[0], c[1] + d[1], c[2] + d[2]) for d in DIRS]

    def bombs_lower(self, count, start=None, limit=8):
        """A quick lower bound on par with `count` bombs to place: as if each were put down the
        moment it goes off, on any empty square the queen passes or a blast reaches. Setting a bomb
        off earlier is never worse, so this only differs from the real par by ignoring that a bomb
        can't go on the queen's start; solve_bombs uses it to cut its search short."""
        pawn_cells = set(self.pawns)
        fixed = set(self.bombs)

        def placeable(c):
            return (self.inside(c) and c not in pawn_cells and c not in fixed and c not in self.blockers
                    and c not in self.portal and c not in self.ramp)

        def blast_at(left, c):
            m = 0
            for x in self._around(c):
                m |= self.bit.get(x, 0)
            hit = m & left & self.bomb_mask
            left &= ~m
            while hit:  # fixed bombs caught in it go off too
                b = hit & -hit
                hit ^= b
                hit |= self.blast[b] & left & self.bomb_mask
                left &= ~self.blast[b]
            return left

        def options(left, swept, cells, k):
            left1 = self.after(left, swept)
            out = {(left1, k)}
            if k == 0:
                return out
            # where a bomb could be put to go off this move: her path, and anywhere a blast reached
            zone = set(cells)
            for b in self.bombs:
                if self.bit[b] & left & ~left1:
                    zone.update(self._around(b))
            stack = [(left1, k, frozenset(zone))]
            while stack:
                l, kk, z = stack.pop()
                if kk == 0:
                    continue
                for c in z:
                    if not placeable(c):
                        continue
                    l2 = blast_at(l, c)
                    if (l2, kk - 1) in out:
                        continue
                    out.add((l2, kk - 1))
                    stack.append((l2, kk - 1, z | frozenset(self._around(c))))
            return out

        begin = [start] if start else self.starts()
        frontier = {(s, self.full, count) for s in begin}
        seen = set(frontier)
        for depth in range(1, limit + 1):
            nxt = set()
            for at, left, k in frontier:
                for to, cells, swept in self._lines(at):
                    for l2, k2 in options(left, swept, cells, k):
                        if l2 & self.pawn_mask == 0:
                            return depth
                        st = (to, l2, k2)
                        if st not in seen:
                            seen.add(st)
                            nxt.add(st)
            frontier = nxt
            if not frontier:
                break
        return None

    def solve_bombs(self, count, start=None, limit=8, spots=None):
        """The player places `count` more bombs before the first move (on top of this board's own).
        Returns (par, bomb cells placed, line, start, every placement reaching par) for the best
        placement; with several, the first found."""
        if count == 0:
            par, line, s = self.solve(start, limit)
            return par, [], line, s, [()]
        # The quick lower bound doesn't know the Mirror Queen: with one, try every par from 1
        lower = self.bombs_lower(count, start, limit) if not self.mirror else 1
        if lower is None:
            return None, None, None, None, []
        spots = self.bomb_spots() if spots is None else spots
        if start:
            spots = [c for c in spots if c != start]
        for par in range(lower, limit + 1):
            best, found = None, []
            for combo in itertools.combinations(spots, count):
                b = self.with_bombs(self.bombs + list(combo))
                got, line, s = b.solve(start, par)
                if got is None:
                    continue
                if got < par:
                    raise AssertionError(f"bombs_lower {lower} isn't a lower bound: {combo} reaches {got}")
                if best is None:
                    best = (par, list(combo), line, s)
                found.append(combo)
            if best:
                return best + (found,)
        return None, None, None, None, []

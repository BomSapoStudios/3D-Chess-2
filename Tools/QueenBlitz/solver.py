"""Queen Blitz solver: the fewest queen sweeps that clear every pawn.

A move slides the queen any distance along one of the 26 directions (axes, flat diagonals,
3D diagonals), stopping before a blocker or the board edge. Every pawn on the cells it passes
through or stops on is taken. Placing the queen (when the puzzle asks) is not a move.

On ice the queen can't stop partway: every move slides to the edge or the square before a block.
"""
import itertools

DIRS = [d for d in itertools.product((-1, 0, 1), repeat=3) if d != (0, 0, 0)]


class Board:
    def __init__(self, n, pawns, blockers=(), ice=False):
        self.n = n
        self.ice = ice
        self.pawns = [tuple(p) for p in pawns]
        self.blockers = {tuple(b) for b in blockers}
        self.bit = {p: 1 << i for i, p in enumerate(self.pawns)}
        self.full = (1 << len(self.pawns)) - 1
        self._moves = {}

    def inside(self, c):
        return all(0 <= v < self.n for v in c)

    def cells(self):
        return list(itertools.product(range(self.n), repeat=3))

    def moves(self, at):
        """[(destination, mask of pawns swept, direction, distance)] from a cell"""
        if at in self._moves:
            return self._moves[at]
        out = []
        for d in DIRS:
            mask, c, k = 0, at, 0
            while True:
                c = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
                if not self.inside(c) or c in self.blockers:
                    break
                k += 1
                mask |= self.bit.get(c, 0)
                if not self.ice:
                    out.append((c, mask, d, k))
            if self.ice and k > 0:
                last = (at[0] + d[0] * k, at[1] + d[1] * k, at[2] + d[2] * k)
                out.append((last, mask, d, k))
        self._moves[at] = out
        return out

    def starts(self):
        return [c for c in self.cells() if c not in self.blockers and c not in self.bit]

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

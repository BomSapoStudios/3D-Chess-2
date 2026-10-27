"""Shortcut players: simulated solvers that each lean on one habit fast humans have.

A puzzle is tricky when these players miss par, and trickier when they find out late.

Two kinds:
  lures   pick moves by a rule of thumb (biggest sweep, finish the line, best two-step).
          Ties are all tried; the player keeps the best result any tie-break gets.
  habits  play perfectly but never do one thing (stop on an empty square, use a 3D diagonal,
          make a move that takes nothing, leave the floor the pawns are on).

    python3 players.py PACK.json...      # how every player does on every puzzle, plus a score
"""
import json, sys
from solver import Board


def pawn_count(mask):
    return bin(mask).count("1")


# ---------- habits: optimal play under a restriction ----------

def solve_with(b, starts, limit, allow):
    """Fewest moves to clear the board from any of `starts` using only moves where
    allow(at, left, move) holds (None past limit)"""
    frontier = {(s, b.full) for s in starts}
    seen = set(frontier)
    for depth in range(1, limit + 1):
        nxt = set()
        for at, left in frontier:
            for m in b.moves(at):
                if not allow(at, left, m):
                    continue
                state = (m[0], left & ~m[1])
                if state[1] == 0:
                    return depth
                if state not in seen:
                    seen.add(state)
                    nxt.add(state)
        frontier = nxt
        if not frontier:
            break
    return None


def no_empty_stop(b):
    """Stops only on a pawn it takes (the last pawn of its line)"""
    return lambda at, left, m: (b.bit.get(m[0], 0) & left) != 0


def no_3d_diagonal(b):
    return lambda at, left, m: 0 in m[2]


def no_quiet_move(b):
    """Every move takes at least one pawn"""
    return lambda at, left, m: (m[1] & left) != 0


def pawn_plane(b):
    """The floor or wall holding every pawn, if there is one: (axis, value)"""
    for axis in range(3):
        vals = {p[axis] for p in b.pawns}
        if len(vals) == 1:
            return axis, vals.pop()
    return None


def stay_on_plane(b):
    plane = pawn_plane(b)
    if plane is None:
        return None
    axis, v = plane
    return lambda at, left, m: m[0][axis] == v


HABITS = {
    "stopper": no_empty_stop,     # never stops on an empty square
    "flat": no_3d_diagonal,       # never uses a 3D diagonal
    "eager": no_quiet_move,       # never makes a move that takes nothing
    "floor": stay_on_plane,       # never leaves the pawns' floor
}


# ---------- lures: rule-of-thumb play ----------

def ray_rest(b, at, left, d):
    """Pawns still on the board along direction d from `at` (up to a block or the edge)"""
    mask, c = 0, at
    for _ in range(4 * b.n * b.n):
        step = b._step(c, d)  # through portals and up ramps, like the move itself
        if step is None:
            return mask
        passed, c, d = step
        for x in passed + [c]:
            mask |= b.bit.get(x, 0) & left
    return mask


def greedy_pick(b, at, left):
    best = max(pawn_count(m[1] & left) for m in b.moves(at))
    return [m for m in b.moves(at) if pawn_count(m[1] & left) == best] if best else None


def liner_pick(b, at, left):
    """Ride the longest visible line to its last pawn and stop there"""
    best, picks = 0, []
    for m in b.moves(at):
        took = m[1] & left
        if not took or took != ray_rest(b, at, left, m[2]) or not (b.bit.get(m[0], 0) & left):
            continue
        n = pawn_count(took)
        if n > best:
            best, picks = n, [m]
        elif n == best:
            picks.append(m)
    return picks or None


_bite = {}


def best_bite(b, at, left):
    key = (id(b), at, left)
    if key not in _bite:
        if len(_bite) > 200000:
            _bite.clear()
        _bite[key] = max((pawn_count(n[1] & left) for n in b.moves(at)), default=0)
    return _bite[key]


def lookahead_pick(b, at, left):
    """Most pawns over this move and the best next one"""
    best, picks = 0, []
    for m in b.moves(at):
        after = left & ~m[1]
        nxt = best_bite(b, m[0], after)
        score = pawn_count(m[1] & left) + nxt
        if score > best:
            best, picks = score, [m]
        elif score == best:
            picks.append(m)
    return picks if best else None


LURES = {"greedy": greedy_pick, "liner": liner_pick, "lookahead": lookahead_pick}


def play_lure(b, pick, start, par, cap=6):
    """(best move count any tie-break reaches, how many of the player's moves kept par before
    the first one that couldn't). Out of ideas = the player finds the rest perfectly."""
    best = [None]
    kept_par = [par]  # the deepest point a tie-break got while still on a par route

    def go(at, left, used, on_par):
        if best[0] is not None and best[0] <= used:
            return
        if left == 0:
            best[0] = used
            return
        if used >= par + cap:
            return
        picks = pick(b, at, left)
        if picks is None:
            rest = b.solve_from(at, left, par + cap - used)
            if rest is not None and (best[0] is None or used + rest < best[0]):
                best[0] = used + rest
            return
        for m in picks[:6]:
            after = left & ~m[1]
            still = on_par and b.solve_from(m[0], after, par - used - 1) is not None if after else on_par
            if on_par and not still:
                kept_par[0] = min(kept_par[0], used)
            go(m[0], after, used + 1, still)

    go(start, b.full, 0, True)
    return best[0], (kept_par[0] if best[0] is None or best[0] > par else par)


# ---------- the report ----------

def board_of(p):
    import packs
    return packs.board(p)


def edge_starts(b):
    """Where a player places the queen by instinct: next to a pawn"""
    near = set()
    for p in b.pawns:
        for d in [(x, y, z) for x in (-1, 0, 1) for y in (-1, 0, 1) for z in (-1, 0, 1)]:
            c = (p[0] + d[0], p[1] + d[1], p[2] + d[2])
            if b.inside(c) and c not in b.bit and c not in b.blockers and c not in b.portal and c not in b.ramp:
                near.add(c)
    return sorted(near)


def assess(p):
    b = board_of(p)
    par = p["par"]
    given = tuple(p["queen"]) if p.get("queen") else None
    starts = [given] if given else b.starts()
    out = {}
    for name, make in HABITS.items():
        allow = make(b)
        if allow is None:
            continue
        got = solve_with(b, starts, par + 2, allow)
        out[name] = got if got is not None else par + 3
    for name, pick in LURES.items():
        if given:
            got, kept = play_lure(b, pick, given, par)
        else:
            # placement: the lure player also places by its rule (best start for its rule)
            got, kept = None, 0
            for s in edge_starts(b):
                g, k = play_lure(b, pick, s, par, cap=3)
                if g is not None and (got is None or g < got):
                    got, kept = g, k
        out[name] = got if got is not None else par + 6
        out[name + "_kept"] = kept
    if not given:
        got = solve_with(b, edge_starts(b), par + 2, lambda *a: True)
        out["placer"] = got if got is not None else par + 3
    return out


PLAYERS = list(HABITS) + list(LURES) + ["placer"]


def score(p, r):
    """Trickiness. Lure players count most: Brandon's tricky picks (Fork 6, 9, 10) beat all three
    lures, the easy ones fell to at least one. A lure that kept par for a while before going wrong
    scores more (it felt right, then wasn't). Habits that miss par add a little."""
    par, s = p["par"], 0.0
    for name in LURES:
        over = r[name] - par
        if over > 0:
            s += 2 + 0.5 * min(over, 3) + 0.5 * r[name + "_kept"]
    for name in list(HABITS) + ["placer"]:
        if name in r and r[name] > par:
            s += 0.5
    return round(s, 2)


def lured(p, r):
    """How many lure players miss par"""
    return sum(r[name] > p["par"] for name in LURES)


if __name__ == "__main__":
    for path in sys.argv[1:]:
        pack = json.load(open(path))
        print(f"== {pack['name']}")
        print(f"{'puzzle':16} par " + " ".join(f"{n[:7]:>7}" for n in PLAYERS) + "   score")
        for p in pack["puzzles"]:
            r = assess(p)
            cells = " ".join(f"{(r[n] - p['par']) if n in r else '':>+7}" if n in r else f"{'-':>7}" for n in PLAYERS)
            print(f"{p['name'][:16]:16} {p['par']:>3} {cells}   {score(p, r)}")

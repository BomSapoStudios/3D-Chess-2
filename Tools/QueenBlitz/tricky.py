"""Tricky Queen Blitz puzzles: set up an expectation, then make the only par route ignore it.

    python3 tricky.py search SIZE PAR TRIES SEED OUT.json [bait,wander,plane]   # graded tricky candidates
    python3 tricky.py pack OUT.json CANDIDATES.json...      # pick 10 varied ones into a test pack

The lies, each measured by the solver (see `deceive`):
  bait      a long, obvious row of pawns that no par route rides in one move
  stop      the queen must stop on an empty square, short of or past the last pawn of her line
  quiet     a par move that takes at most one pawn, only to get into position
  start     (place the queen) every par start touches no pawn row of 3 or more
  greedy    taking the biggest sweep each time runs at least 2 over par
  narrow    only one or two first moves (start and first sweep) still reach par
  plane     every pawn sits on one floor or wall, but staying on it can't make par: the
            route has to leave that plane and come back through it
  false     most of the tempting moves (the biggest sweeps, at the start and at the second
            stop of the real route) are dead ends
"""
import itertools, json, random, sys
from solver import Board, DIRS
from gen import route_puzzle
import packs


def lines_through(n):
    """Every full straight line of the cube with at least 3 cells, as a list of cells"""
    seen, out = set(), []
    for c in itertools.product(range(n), repeat=3):
        for d in DIRS:
            # Walk back to the line's first cell so each line is found once
            s = c
            while all(0 <= s[i] - d[i] < n for i in range(3)):
                s = tuple(s[i] - d[i] for i in range(3))
            cells, p = [], s
            while all(0 <= v < n for v in p):
                cells.append(p)
                p = tuple(p[i] + d[i] for i in range(3))
            key = frozenset(cells)
            if len(cells) >= 3 and key not in seen:
                seen.add(key)
                out.append(cells)
    return out


_LINES = {}


def bait_rows(n, pawns, least=4):
    """Rows of `least` or more pawns on one straight line: what the eye locks onto first"""
    if n not in _LINES:
        _LINES[n] = lines_through(n)
    ps = set(pawns)
    rows = [frozenset(c for c in line if c in ps) for line in _LINES[n]]
    return sorted({r for r in rows if len(r) >= least}, key=len, reverse=True)


def par_first_moves(b, starts, par):
    """(start, pawns swept) pairs that can still clear the board in par moves"""
    out = set()
    for s in starts:
        for to, swept, _, _ in b.moves(s):
            if swept and (to, swept) and b.solve_from(to, b.full & ~swept, par - 1) is not None:
                out.add((s, swept))
            elif not swept and b.solve_from(to, b.full, par - 1) is not None:
                out.add((s, 0))
    return out


def deceive(n, queen, pawns, blocks=(), ice=False, limit=7):
    """Everything that makes a puzzle lie, or None if it doesn't solve within `limit`"""
    b = Board(n, pawns, blocks, ice)
    par, line, start = b.solve(queen, limit=limit)
    if par is None:
        return None
    starts = [queen] if queen else b.optimal_starts(par)
    m = dict(par=par, line=line, start=start, starts=len(starts), greedy=b.greedy(queen))
    m["firsts"] = len(par_first_moves(b, starts, par))

    # Bait: the biggest row of pawns can't be taken in one sweep on the way to par
    rows = bait_rows(n, pawns)
    m["bait"] = 0
    for row in rows:
        mask = sum(b.bit[p] for p in row)
        ride = False
        for s in ([queen] if queen else b.starts()):
            for to, swept, _, _ in b.moves(s):
                if swept & mask == mask and b.solve_from(to, b.full & ~swept, par - 1) is not None:
                    ride = True
                    break
            if ride:
                break
        # ... nor later in the solution we found
        ride = ride or any(_swept(b, a, c) & mask == mask for a, c in line)
        if not ride:
            m["bait"] = len(row)
            break

    # Stops on empty squares that aren't where the pawns of that move end
    m["stop"] = m["quiet"] = 0
    for a, c in line:
        swept = _swept(b, a, c)
        if bin(swept).count("1") <= 1:
            m["quiet"] += 1
        taken = [p for p in b.pawns if swept & b.bit[p]]
        far = max(taken, key=lambda p: _dist(a, p)) if taken else None
        if c not in b.bit and (far is None or c != far) and not ice:
            m["stop"] += 1

    # Placing: no par start sits on a row of 3+ pawns
    if not queen:
        rows3 = bait_rows(n, pawns, 3)
        on_row = lambda s: any(_collinear(s, r) for r in rows3)
        m["start_lie"] = all(not on_row(s) for s in starts)
    else:
        m["start_lie"] = False
    m["diag3"] = sum(1 for a, c in line if all(a[i] != c[i] for i in range(3)))
    m["pawns"] = len(pawns)

    # Plane: all pawns on one plane, and par can't be made with every stop on it
    m["plane"] = 0
    for axis in range(3):
        values = {p[axis] for p in pawns}
        if len(values) == 1:
            v = values.pop()
            off = [c for c in itertools.product(range(n), repeat=3) if c[axis] != v]
            flat = Board(n, pawns, list(blocks) + off, ice)
            q = queen if queen and queen[axis] == v else None
            if queen and q is None:
                break  # she starts off the plane: no lie left to tell
            flat_par = flat.solve(q, limit=par + 2)[0]
            m["plane"] = (flat_par or par + 3) - par
            break

    # False paths: of the tempting moves (biggest sweeps) at the start and at the real route's
    # second stop, the share that can no longer make par
    def dead_share(at, left, moves_left):
        bites = [(bin(swept & left).count("1"), to, swept) for to, swept, _, _ in b.moves(at)]
        top = max((k for k, _, _ in bites), default=0)
        tempting = {(to, swept & left) for k, to, swept in bites if k >= max(2, top - 1)}
        if not tempting:
            return 0.0, 0
        dead = sum(1 for to, sw in tempting if b.solve_from(to, left & ~sw, moves_left - 1) is None)
        return dead / len(tempting), len(tempting)
    first_at = queen or start
    share1, n1 = dead_share(first_at, b.full, par)
    a, c = line[0]
    share2, n2 = dead_share(c, b.full & ~_swept(b, a, c), par - 1) if par > 2 else (0.0, 0)
    m["false"] = round((share1 * n1 + share2 * n2) / max(1, n1 + n2), 2)

    # Fork: pawns on many sides of where she starts, each about as tempting, only one right.
    # Counted from the given square, or from the best start when she's placed.
    rays = []
    for d in DIRS:
        c, k = first_at, 0
        while True:
            c = tuple(c[i] + d[i] for i in range(3))
            if not all(0 <= x < n for x in c) or c in b.blockers:
                break
            k += c in b.bit
        if k:
            rays.append((d, k))
    right = line[0][1]
    right_dir = tuple((right[i] > first_at[i]) - (right[i] < first_at[i]) for i in range(3))
    right_k = next((k for d, k in rays if d == right_dir), 0)
    # Greed trap: the right first move takes fewer pawns than other sweeps on offer
    bites = [bin(sw).count("1") for _, sw, _, _ in b.moves(first_at)]
    m["right_bite"] = bin(_swept(b, *line[0])).count("1")
    m["bigger"] = len({(to, sw) for to, sw, _, _ in b.moves(first_at) if bin(sw).count("1") > m["right_bite"]})
    m["top_bite"] = max(bites, default=0)
    m["fork"] = len(rays)                                  # directions with pawns to take
    m["fork_rivals"] = sum(1 for d, k in rays if d != right_dir and k >= right_k)  # as tempting or more
    m["tempting"] = n1 + n2
    return m


def _dist(a, b):
    return max(abs(a[i] - b[i]) for i in range(3))


def _swept(b, a, c):
    for to, swept, _, _ in b.moves(a):
        if to == c:
            return swept
    return 0


def _collinear(s, row):
    """Is s on the straight line through the cells of row"""
    cells = sorted(row)
    p, q = cells[0], cells[1]
    d = tuple((q[i] > p[i]) - (q[i] < p[i]) for i in range(3))
    k = _dist(p, s)
    return all(s[i] == p[i] + d[i] * k for i in range(3)) or all(s[i] == p[i] - d[i] * k for i in range(3))


def lies(m):
    """The tricks this puzzle plays, by name"""
    out = []
    if m["bait"]:
        out.append("bait")
    if m["stop"]:
        out.append("stop")
    if m["quiet"]:
        out.append("quiet")
    if m["start_lie"]:
        out.append("start")
    if m["greedy"] - m["par"] >= 2:
        out.append("greedy")
    if m["firsts"] <= 2:
        out.append("narrow")
    if m["diag3"] >= 2:
        out.append("diagonal")
    if m.get("plane", 0) >= 1:
        out.append("plane")
    if m.get("false", 0) >= 0.75 and m.get("tempting", 0) >= 4:
        out.append("false")
    if m.get("fork", 0) >= 4 and m.get("fork_rivals", 0) >= 2:
        out.append("fork")
    return out


# ───────────────────────── Making them ─────────────────────────

def bait_route(n, par, rng):
    """Plant a row of pawns along one line, then a par route that crosses that row once per move
    (taking one of its pawns each time) and picks up a couple of pawns off the row. Riding the
    row looks like the fast way; the crossings are."""
    if n not in _LINES:
        _LINES[n] = lines_through(n)
    row_line = rng.choice([l for l in _LINES[n] if len(l) == n])
    k = min(par, n)
    targets = set(rng.sample(row_line, k))
    at = rng.choice([c for c in itertools.product(range(n), repeat=3) if c not in row_line])
    start, pawns, left = at, set(), set(targets)
    for move in range(par):
        options = []
        for d in DIRS:
            path, c = [], at
            while True:
                c = tuple(c[i] + d[i] for i in range(3))
                if not all(0 <= v < n for v in c):
                    break
                path.append(c)
            on = [i for i, c in enumerate(path) if c in left]
            # each move crosses the row at exactly one wanted cell and doesn't run along it
            if len(on) == 1 and sum(1 for c in path if c in row_line) == 1:
                options.append((path, on[0]))
        if not options:
            if left:
                return None
            # row done: a free move off the row
            for d in DIRS:
                path, c = [], at
                while True:
                    c = tuple(c[i] + d[i] for i in range(3))
                    if not all(0 <= v < n for v in c):
                        break
                    path.append(c)
                if len(path) >= 2 and not any(c in row_line for c in path):
                    options.append((path, None))
            if not options:
                return None
        path, hit = rng.choice(options)
        if hit is not None:
            left.discard(path[hit])
        stop = rng.randint(max(hit or 0, 0), len(path) - 1)
        seg = path[:stop + 1]
        off = [c for c in seg if c not in row_line and c != start]
        pawns.update(rng.sample(off, min(len(off), rng.randint(0, 2))))
        at = seg[-1]
    if left:
        return None
    pawns |= targets
    pawns.discard(start)
    return start, sorted(pawns), []


def wander_route(n, par, rng):
    """A route with quiet moves and stops on empty squares"""
    at = tuple(rng.randrange(n) for _ in range(3))
    start, pawns = at, set()
    quiet = rng.randrange(par)
    for move in range(par):
        d = rng.choice(DIRS)
        path, c = [], at
        while True:
            c = tuple(c[i] + d[i] for i in range(3))
            if not all(0 <= v < n for v in c):
                break
            path.append(c)
        if len(path) < 2:
            return None
        stop = rng.randrange(len(path))
        seg = path[:stop + 1]
        cand = [c for c in seg if c != start]
        if move == quiet:
            take = rng.sample(cand, min(1, len(cand)))
        else:
            # leave the stop square empty most of the time
            inner = [c for c in cand if c != seg[-1]] or cand
            take = rng.sample(inner, min(len(inner), rng.randint(2, 3)))
        pawns.update(take)
        at = seg[-1]
    pawns.discard(start)
    return start, sorted(pawns), []


def plane_route(n, par, rng):
    """Every pawn on one plane, with a few blocks on it. On an open board staying on the plane is
    never worse (any route squashed flat onto the plane takes the same pawns in as many moves),
    so the blocks are what make it a lie: the planted route hops over them through the
    neighbouring planes, while on the plane they're in the way."""
    axis, v = rng.randrange(3), rng.randrange(n)
    on = lambda c: c[axis] == v
    flat = lambda c: tuple(v if i == axis else c[i] for i in range(3))
    plane = [c for c in itertools.product(range(n), repeat=3) if on(c)]
    at = start = rng.choice(plane)
    pawns, walked, shadow, leaves = set(), {start}, set(), 0
    for move in range(par):
        options = []
        for d in DIRS:
            path, c = [], at
            while True:
                c = tuple(c[i] + d[i] for i in range(3))
                if not all(0 <= x < n for x in c):
                    break
                path.append(c)
            for stop in range(len(path)):
                seg = path[:stop + 1]
                hits = [x for x in seg if on(x) and x != start and x not in pawns]
                if hits or d[axis] != 0:
                    options.append((d, seg, hits))
        if not options:
            return None
        d, seg, hits = rng.choice(options)
        if d[axis] != 0:
            leaves += 1
            shadow.update(flat(x) for x in seg if not on(x))  # where it would run on the plane
        else:
            walked.update(seg)
        if hits:
            k = 1 if d[axis] != 0 else rng.randint(1, min(3, len(hits)))
            pawns.update(rng.sample(hits, k))
        walked.update(x for x in seg if on(x))
        at = seg[-1]
    if leaves < 2:
        return None
    pawns.discard(start)
    spots = sorted(shadow - walked - pawns - {start})
    if not spots:
        return None
    blocks = rng.sample(spots, min(len(spots), rng.randint(1, 4)))
    return start, sorted(pawns), sorted(blocks)


def fork_route(n, par, rng):
    """A planted route, then decoy pawns on the other lines out of her starting square, placed
    only where a later move of the route passes. Every side looks like a way to go; the decoys
    get swept up later from directions nobody expects."""
    made = None
    for _ in range(50):
        made = (wander_route(n, par, rng) if rng.random() < 0.5 else route_puzzle(n, par, 0, rng, per_move=(1, 3)))
        if made:
            break
    if not made:
        return None
    start, pawns, _ = made
    # Re-walk the planted route: the cells each move after the first passes through
    b = Board(n, pawns)
    par0, line, _ = b.solve(start, limit=par)
    if par0 != par:
        return None
    first_dir = _dir(start, line[0][1])
    later = set()
    for a, c in line[1:]:
        d = _dir(a, c)
        x = a
        while x != c:
            x = tuple(x[i] + d[i] for i in range(3))
            later.add(x)
    by_ray = {}
    for x in later - set(pawns) - {start}:
        d = _dir(start, x)
        if d != first_dir and all((x[i] - start[i]) in (0, d[i] * _dist(start, x)) for i in range(3)):
            by_ray.setdefault(d, []).append(x)
    if len(by_ray) < 2:
        return None
    rays = rng.sample(sorted(by_ray), min(len(by_ray), rng.randint(2, 4)))
    decoys = [rng.choice(by_ray[d]) for d in rays]
    return start, sorted(set(pawns) | set(decoys)), []


def _dir(a, c):
    return tuple((c[i] > a[i]) - (c[i] < a[i]) for i in range(3))


def search(n, par, tries, seed, path=None, kinds=("bait", "wander")):
    rng = random.Random(seed)
    out, seen = [], set()
    for t in range(tries):
        kind = kinds[t % len(kinds)]
        made = {"bait": bait_route, "wander": wander_route, "plane": plane_route, "fork": fork_route,
                "greedfork": fork_route}[kind](n, par, rng)
        if not made:
            continue
        start, pawns, blocks = made
        if not (par + 2 if kind == "plane" else 2 * par) <= len(pawns) <= 3 * par + 2:
            continue
        given = rng.random() < 0.5
        queen = start if given else None
        key = (n, tuple(sorted(pawns)))
        if key in seen:
            continue
        seen.add(key)
        m = deceive(n, queen, pawns, blocks, limit=par)
        if not m or m["par"] != par:
            continue
        # Brandon's rule: if a free start beats par, the queen must be given
        if given and Board(n, pawns, blocks).solve(None, limit=par - 1)[0] is not None:
            continue
        found = lies(m)
        if kind == "plane":
            if "plane" not in found or m["firsts"] > 2 or m["false"] < 0.6:
                continue
        elif kind == "fork":
            if "fork" not in found or m["firsts"] > 2 or m["false"] < 0.6:
                continue
        elif kind == "greedfork":
            # Brandon's picks (Tricky 2 #6, #9, #10): a fork where bigger sweeps than the right
            # one are on offer, and the route stops on empty squares
            if "fork" not in found or m["firsts"] > 2 or m["false"] < 0.75 or m["bigger"] < 2 or m["stop"] < 2:
                continue
        elif "greedy" not in found or len(found) < 3 or m["firsts"] > 3 or m["false"] < 0.6:
            continue
        entry = packs.solved("", n, queen, pawns, blocks, "", False)
        entry.update(lies=found, greedy=m["greedy"], firsts=m["firsts"], starts=m["starts"], bait=m["bait"],
                     plane=m["plane"], false=m["false"], tempting=m["tempting"], fork=m["fork"],
                     rivals=m["fork_rivals"], bigger=m["bigger"], right_bite=m["right_bite"], top_bite=m["top_bite"],
                     stop=m["stop"])
        out.append(entry)
        if path:
            json.dump(out, open(path, "w"))  # saved as we go, so a timeout keeps what was found
        print(f"#{t} {n}³ par {par} pawns {len(pawns)} {'given' if given else 'place'} greedy {m['greedy']} "
              f"firsts {m['firsts']} bigger {m['bigger']} ({m['right_bite']}<{m['top_bite']}) stop {m['stop']} false {m['false']}/{m['tempting']} fork {m['fork']}/{m['fork_rivals']} plane +{m['plane']} {' '.join(found)}", flush=True)
    return out


# ───────────────────────── Round 3: one generator per trick ─────────────────────────
# Each returns (start, pawns, blocks, ice, queen given?) or None; each trick has its own check.

def _any_route(n, par, rng, blockers=0, ice=False):
    for _ in range(30):
        made = route_puzzle(n, par, blockers, rng, per_move=(1, 3), ice=ice)
        if made:
            return made
    return None


def twostep_make(n, par, rng):
    made = fork_route(n, par, rng) or _any_route(n, par, rng)
    return made and (*made, False, True)


def twostep_check(n, queen, pawns, blocks, ice, m):
    """The obvious first move (the one biggest sweep) is right; at the next stop the biggest
    sweep is a dead end and bigger than the right second move"""
    if m["bigger"] != 0 or m["firsts"] > 2:
        return False
    b = Board(n, pawns, blocks, ice)
    (a1, c1), (a2, c2) = m["line"][0], m["line"][1]
    top_first = [sw for to, sw, _, _ in b.moves(a1) if bin(sw).count("1") == m["top_bite"]]
    if len(top_first) != 1:
        return False
    left = b.full & ~_swept(b, a1, c1)
    right2 = bin(_swept(b, a2, c2) & left).count("1")
    seconds = [(bin(sw & left).count("1"), to, sw) for to, sw, _, _ in b.moves(c1)]
    big = [(k, to, sw) for k, to, sw in seconds if k > right2]
    return len(big) >= 2 and all(b.solve_from(to, left & ~sw, m["par"] - 2) is None for k, to, sw in big)


def cold_make(n, par, rng):
    made = fork_route(n, par, rng) or _any_route(n, par, rng)
    return made and (*made, False, False)


def cold_check(n, queen, pawns, blocks, ice, m):
    """Place the queen: every par start is away from the pawns (no pawn next to it, and not on
    any line holding 3+ pawns), while the squares that look right fail"""
    b = Board(n, pawns, blocks, ice)
    starts = b.optimal_starts(m["par"])
    if not starts or len(starts) > 3:
        return False
    rows3 = bait_rows(n, pawns, 3)
    for st in starts:
        if any(_dist(st, p) <= 1 for p in pawns) or any(_collinear(st, r) for r in rows3):
            return False
    return m["false"] >= 0.6


def symmetry_make(n, par, rng):
    """Half a layout, mirrored across the middle of the cube"""
    made = _any_route(n, max(2, par - 1), rng)
    if not made:
        return None
    start, pawns, _ = made
    axis = rng.randrange(3)
    mirror = lambda c: tuple(n - 1 - c[i] if i == axis else c[i] for i in range(3))
    full = sorted(set(pawns) | {mirror(p) for p in pawns})
    if len(full) > 3 * par + 2:
        return None
    return start, full, [], False, False


def symmetry_check(n, queen, pawns, blocks, ice, m):
    """The layout is a mirror image, but no par route is: the mirror of the route found is a
    different route, and 'the same on both sides' (each half on its own) costs more"""
    if m["firsts"] > 2 or m["false"] < 0.6:
        return False
    for axis in range(3):
        mirror = lambda c: tuple(n - 1 - c[i] if i == axis else c[i] for i in range(3))
        if set(map(mirror, pawns)) == set(pawns):
            half = [p for p in pawns if p[axis] < (n - 1) / 2]
            if not half:
                return False
            hb = Board(n, half)
            half_par = hb.solve(None, limit=m["par"])[0]
            return half_par is None or 2 * half_par > m["par"]
    return False


def icebrake_make(n, par, rng):
    made = _any_route(n, par, rng, blockers=rng.randint(2, 4), ice=True)
    return made and (*made, True, True)


def icebrake_check(n, queen, pawns, blocks, ice, m):
    """On ice the blocks look like obstacles, but they're the brakes: without any one of them par
    is out of reach"""
    if m["firsts"] > 2:
        return False
    for blk in blocks:
        rest = [x for x in blocks if x != blk]
        if Board(n, pawns, rest, True).solve(queen, limit=m["par"])[0] is not None:
            return False
    return True


def planeblock_make(n, par, rng):
    made = plane_route(n, par, rng)
    return made and (*made, False, rng.random() < 0.6)


def planeblock_check(n, queen, pawns, blocks, ice, m):
    return m.get("plane", 0) >= 1 and m["firsts"] <= 2


KINDS = {
    "twostep": (twostep_make, twostep_check, "The first move is easy. The second isn't."),
    "cold": (cold_make, cold_check, "She doesn't start anywhere near the pawns."),
    "symmetry": (symmetry_make, symmetry_check, "It looks the same on both sides. The route isn't."),
    "icebrake": (icebrake_make, icebrake_check, "On ice, the blocks are your brakes."),
    "planeblock": (planeblock_make, planeblock_check, "Every pawn is on one floor. She isn't."),
}


def search3(n, par, tries, seed, path, kind):
    make, check, tip = KINDS[kind]
    rng = random.Random(seed)
    out, seen = [], set()
    for t in range(tries):
        made = make(n, par, rng)
        if not made:
            continue
        start, pawns, blocks, ice, given = made
        if not par + 1 <= len(pawns) <= 3 * par + 2:
            continue
        key = (tuple(sorted(pawns)), tuple(sorted(blocks)))
        if key in seen:
            continue
        seen.add(key)
        queen = start if given else None
        if queen and (queen in pawns or queen in blocks):
            continue
        m = deceive(n, queen, pawns, blocks, ice, limit=par)
        if not m or m["par"] != par:
            continue
        if given and Board(n, pawns, blocks, ice).solve(None, limit=par - 1)[0] is not None:
            continue  # Brandon's rule
        if not check(n, queen, pawns, blocks, ice, m):
            continue
        entry = packs.solved("", n, queen, pawns, blocks, tip, ice)
        entry.update(trick=kind, lies=lies(m), firsts=m["firsts"], false=m["false"], bigger=m["bigger"],
                     right_bite=m["right_bite"], top_bite=m["top_bite"], stop=m["stop"], fork=m["fork"])
        out.append(entry)
        json.dump(out, open(path, "w"))
        print(f"#{t} {kind} {n}³ par {par} pawns {len(pawns)} blocks {len(blocks)} {'ice ' if ice else ''}"
              f"{'given' if given else 'place'} firsts {m['firsts']} bigger {m['bigger']} false {m['false']}", flush=True)
    return out


TIPS = {
    "bait": "That long row is not your friend.",
    "start": "She doesn't start where the pawns are.",
    "stop": "Stop where nothing is.",
    "quiet": "One move takes almost nothing.",
    "narrow": "There is exactly one way. Find it.",
    "diagonal": "Turn the cube. The lines go through the middle.",
    "greedy": "Big sweeps lose.",
    "plane": "Every pawn is on one floor. She isn't.",
    "false": "Most good-looking moves are dead ends.",
    "fork": "Pawns on every side. Only one way out works.",
}


def pick(cands, count=10):
    """Most lies first; no layout of tricks more than twice, both sizes, and each tip names a
    trick its neighbour's tip doesn't"""
    pool = sorted(cands, key=lambda p: (-len(p["lies"]), p["firsts"], -(p["greedy"] - p["par"])))
    chosen = []
    for p in pool:
        same = sum(1 for q in chosen if q["lies"] == p["lies"])
        sizes = [q["size"] for q in chosen]
        if same >= 2 or (len(chosen) >= count // 2 and sizes.count(p["size"]) >= count - 3):
            continue
        chosen.append(p)
        if len(chosen) == count:
            break
    chosen.sort(key=lambda p: (p["par"], p["size"], len(p["lies"])))
    used = {}
    last = None
    for p in chosen:
        options = [l for l in ["bait", "stop", "quiet", "start", "diagonal", "narrow", "greedy"] if l in p["lies"] and l != last]
        main = min(options, key=lambda l: used.get(l, 0))
        p["tip"] = TIPS[main]
        p["trick"] = main
        used[main] = used.get(main, 0) + 1
        last = main
    return chosen


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "search":
        n, par, tries, seed, path = int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), sys.argv[6]
        kinds = tuple(sys.argv[7].split(",")) if len(sys.argv) > 7 else ("bait", "wander")
        found = search(n, par, tries, seed, path, kinds)
        json.dump(found, open(path, "w"))
        print(len(found), "found")
    elif cmd == "search3":
        n, par, tries, seed, path, kind = int(sys.argv[2]), int(sys.argv[3]), int(sys.argv[4]), int(sys.argv[5]), sys.argv[6], sys.argv[7]
        print(len(search3(n, par, tries, seed, path, kind)), "found")
    elif cmd == "forkpack":
        # Round 2: every puzzle is a fork. Most rival directions and false paths first, a few
        # place-the-queen ones, both sizes, no two neighbours with the same size and par.
        out = sys.argv[2]
        cands = []
        for f in sys.argv[3:]:
            cands += json.load(open(f))
        cands = [p for p in cands if "fork" in p["lies"]]
        cands.sort(key=lambda p: (-p["rivals"], -p["false"], p["firsts"], -(p["greedy"] - p["par"])))
        chosen = []
        for want_place in (True, False):
            for p in cands:
                if p in chosen or bool(p["queen"]) == want_place:
                    continue
                if want_place and sum(1 for q in chosen if not q["queen"]) >= 3:
                    break
                if not want_place and len(chosen) >= 10:
                    break
                chosen.append(p)
        chosen.sort(key=lambda p: (p["par"], p["size"], -p["rivals"]))
        for i, p in enumerate(chosen, 1):
            p["name"] = f"Fork {i}"
            p["tip"] = TIPS["fork"] if i % 2 else TIPS["false"]
        print(packs.write({"id": "tricky-2", "name": "Tricky 2 (test)", "free": True, "puzzles": chosen}, out))
        for p in chosen:
            print(p["name"], f"{p['size']}³ par {p['par']} pawns {len(p['pawns']) // 3} directions {p['fork']} rivals {p['rivals']} false {p['false']} firsts {p['firsts']}",
                  "given" if p["queen"] else "place")
    elif cmd == "pack":
        out = sys.argv[2]
        cands = []
        for f in sys.argv[3:]:
            cands += json.load(open(f))
        chosen = pick(cands)
        for i, p in enumerate(chosen, 1):
            p["name"] = f"Tricky {i}"
        print(packs.write({"id": "tricky", "name": "Tricky (test)", "free": True, "puzzles": chosen}, out))
        for p in chosen:
            print(p["name"], f"{p['size']}³ par {p['par']} pawns {len(p['pawns']) // 3} greedy {p['greedy']} firsts {p['firsts']}",
                  "given" if p["queen"] else "place", p["lies"], "|", p["tip"])
    else:
        print(__doc__)

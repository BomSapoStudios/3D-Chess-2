"""Bomb puzzles for Queen Blitz (Brandon, Oct 30): see the bomb rules in solver.py.

A bomb earns its place when the board can't be cleared in par without it, and placing it is a real
choice: only a few squares reach par, and (for the harder ones) the square with the most pawns
around it isn't one of them.

    python3 bombs.py            # build the Bombs (test) pack into Assets/Resources/QueenBlitz/bombs-test.json
    python3 bombs.py --check    # re-check it with packs.py

Each recipe is (name, size, queen given?, bombs given, bombs to place, ice, wants). Candidates are
pawn clumps around a few centres (a blast takes a whole clump that no line runs through) plus loose
pawns on lines, sometimes a block or two.
"""
import itertools, json, multiprocessing, os, random, sys
from solver import Board, DIRS
import packs

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(packs.APP_DIR, "bombs-test.json")


def around(c, n):
    return [(c[0] + d[0], c[1] + d[1], c[2] + d[2]) for d in DIRS
            if all(0 <= c[i] + d[i] < n for i in range(3))]


def layout(rng, n, clumps, clump_size, loose, blocks):
    """Pawns in clumps around random centres (the centres stay empty), loose pawns, and blocks"""
    pawns, centres = set(), []
    for _ in range(clumps):
        for _ in range(50):
            c = tuple(rng.randrange(1, n - 1) for _ in range(3))
            if c not in pawns and all(max(abs(c[i] - o[i]) for i in range(3)) >= 2 for o in centres):
                break
        centres.append(c)
        ring = [x for x in around(c, n) if x not in centres]
        pawns.update(rng.sample(ring, min(len(ring), rng.randint(*clump_size))))
    added = 0
    while added < loose:
        p = tuple(rng.randrange(n) for _ in range(3))
        if p not in centres and p not in pawns:
            pawns.add(p)
            added += 1
    free = [c for c in itertools.product(range(n), repeat=3) if c not in pawns and c not in centres]
    blockers = rng.sample(free, blocks)
    return sorted(pawns), sorted(blockers)


def grade(args):
    """One candidate for a recipe: the pack entry, or None when it doesn't make the cut"""
    recipe, seed = args
    name, n, given_queen, given, place, ice, want = recipe
    rng = random.Random(seed)
    pawns, blockers = layout(rng, n, want["clumps"], want["clump"], want["loose"], want.get("blocks", 0))
    base = Board(n, pawns, blockers, ice)
    spots = base.bomb_spots()
    if len(spots) < given + place:
        return None
    # Given bombs go where the clumps are
    fixed = []
    if given:
        rich = sorted(spots, key=lambda c: -sum(1 for x in around(c, n) if x in base.bit))
        fixed = rich[:given] if given == 1 else rng.sample(rich[:6], given)
    b = base.with_bombs(fixed)
    start = None
    if given_queen:
        empty = [c for c in b.starts() if c not in fixed]
        start = rng.choice(empty)
    if place:
        lower = b.bombs_lower(place, start, limit=want["par"][1])  # quick, so most candidates stop here
        if lower is None or lower < want["par"][0]:
            return None
    # Squares with only one pawn around them hardly ever pay: leaving them out keeps big boards quick
    # (packs.py re-checks par against every square)
    rich = [c for c in b.bomb_spots() if sum(1 for x in around(c, n) if x in base.bit) >= 2]
    par, placed, line, s, found = b.solve_bombs(place, start, limit=want["par"][1], spots=rich)
    if par is None or not want["par"][0] <= par <= want["par"][1]:
        return None
    # Without any bombs (placed or given), how long does it take?
    plain = base.solve(start, limit=par + want["gap"] - 1)[0]
    if plain is not None and plain - par < want["gap"]:
        return None  # the bombs don't matter enough
    if place and len(found) > want.get("spots", 99):
        return None  # too many places work: no real choice
    if want.get("not_obvious") and place == 1:
        # The square with the most pawns around it must not be a par square
        crowd = max(b.bomb_spots(), key=lambda c: sum(1 for x in around(c, n) if x in b.bit and x in base.pawns))
        if (crowd,) in found:
            return None
    if not given_queen:
        starts = b.with_bombs(fixed + placed).optimal_starts(par)
        if len(starts) > want.get("starts", 99):
            return None
    every = [c for combo in found for c in combo]
    # Every move should sweep or blast at least one pawn
    bb = b.with_bombs(fixed + placed)
    left = bb.full
    stops = [s] + [to for _, to in line]
    for a, c in zip(stops, stops[1:]):
        m = max((m for m in bb.moves(a) if m[0] == c), key=lambda m: bin(m[1] & left).count("1"))
        nxt = bb.after(left, m[1])
        if bin((left & ~nxt) & bb.pawn_mask).count("1") < want.get("per_move", 1):
            return None
        left = nxt
    used = sum(1 for c in fixed + placed if not (bb.bit[c] & left))  # bombs that went off
    if used < len(fixed) + len(placed):
        return None  # a bomb the solution never touches
    entry = {
        "name": name, "size": n, "queen": list(start) if start else [], "best": list(s),
        "pawns": packs.flat(pawns), "blocks": packs.flat(blockers), "par": par, "tip": want["tip"], "ice": bool(ice),
        "line": packs.flat(stops),
        "bombs": packs.flat(fixed), "placeBombs": place, "bestBombs": packs.flat(placed),
    }
    return {"entry": entry, "plain": plain, "spots": len(set(every)), "found": len(found), "seed": seed}


# (name, size, queen given, bombs given, bombs to place, ice, wants)
RECIPES = [
    ("Fuse Box", 5, True, 1, 0, False, dict(clumps=1, clump=(5, 7), loose=1, par=(2, 2), gap=2,
        tip="Run the queen over the bomb: it takes every pawn around it.")),
    ("Two Clumps", 5, True, 1, 0, False, dict(clumps=2, clump=(4, 6), loose=1, par=(3, 3), gap=2,
        tip="One clump has a bomb. The other one you'll have to sweep.")),
    ("Your Bomb", 5, True, 0, 1, False, dict(clumps=1, clump=(5, 7), loose=2, par=(3, 3), gap=2, spots=3,
        tip="Place the bomb, then set it off with the queen.")),
    ("Drop Zone", 5, True, 0, 1, False, dict(clumps=2, clump=(4, 6), loose=2, par=(3, 4), gap=2, spots=2,
        tip="Placing the bomb isn't a move. Choose which clump it's worth more in.")),
    ("Off Centre", 5, True, 0, 1, False, dict(clumps=1, clump=(6, 9), loose=3, par=(3, 4), gap=2, spots=2, not_obvious=True,
        tip="The middle of the crowd isn't always the best spot.")),
    ("Double Up", 5, False, 0, 1, False, dict(clumps=2, clump=(4, 6), loose=2, par=(3, 4), gap=2, spots=3, starts=6,
        tip="Place the bomb and the queen. Only placing them together reaches par.")),
    ("Long Fuse", 5, True, 0, 1, False, dict(clumps=2, clump=(4, 7), loose=3, par=(4, 5), gap=2, spots=2, blocks=2,
        tip="Plan the whole route before you place anything.")),
    ("Cold Snap", 5, True, 0, 1, True, dict(clumps=2, clump=(4, 6), loose=2, par=(3, 4), gap=2, spots=2,
        tip="On ice she slides to the edge, so the bomb must sit on her slide.")),
    ("Chain Reaction", 5, True, 1, 1, False, dict(clumps=2, clump=(4, 6), loose=1, par=(2, 3), gap=2, spots=3,
        tip="A blast that reaches another bomb sets it off too.")),
    # Two bombs to place (combinations of squares) is too slow to search for now
]


CACHE = os.path.join(HERE, ".bombs-picks.json")  # picks so far, so a stopped run carries on


def build(tries=400, seed0=0):
    pool = multiprocessing.Pool()
    puzzles = []
    try:
        cache = json.load(open(CACHE))
    except (OSError, ValueError):
        cache = {}
    for k, r in enumerate(RECIPES):
        if r[0] in cache:
            puzzles.append(cache[r[0]])
            continue
        jobs = [(r, seed0 + 1000 * k + i) for i in range(tries)]
        got = []
        for res in pool.imap_unordered(grade, jobs, chunksize=2):
            if res:
                got.append(res)
                if len(got) >= (6 if r[1] < 6 else 2):
                    break
        if not got:
            print(f"{r[0]}: nothing made the cut", flush=True)
            continue
        # Prefer fewer working bomb squares, then the biggest gap over playing without bombs
        got.sort(key=lambda g: (g["found"], -((g["plain"] or 99) - g["entry"]["par"]), g["seed"]))
        pick = got[0]
        puzzles.append(pick["entry"])
        cache[r[0]] = pick["entry"]
        json.dump(cache, open(CACHE, "w"))
        print(f"{r[0]}: par {pick['entry']['par']} (without bombs {pick['plain']}), {pick['found']} bomb placements reach par, "
              f"{len(got)} candidates, seed {pick['seed']}", flush=True)
    pool.terminate()
    if os.path.exists(CACHE) and len(puzzles) == len(RECIPES):
        os.remove(CACHE)
    return packs.write({"id": "bombs-test", "name": "Bombs (test)", "free": True, "puzzles": puzzles}, OUT)


if __name__ == "__main__":
    if "--check" in sys.argv:
        problems = packs.check(OUT)
        print("\n".join(problems) or "OK")
        sys.exit(1 if problems else 0)
    print(build())

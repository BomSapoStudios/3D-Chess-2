"""New Tricks: a free 10-puzzle pack after Starter that brings in each newer mechanic on its own,
as gently as Starter does (Brandon, Oct 30): portals, ramps, the Mirror Queen and bombs. Every
puzzle is 5³ with the queen given, par 2-3 (the last one 4), and its mechanic is needed: without it
par is out of reach.

    python3 intro.py     # writes Assets/Resources/QueenBlitz/new-tricks.json

Each recipe is (name, kind, par range, pawns, tip). Candidates are random small layouts around a
random feature; one is kept when par is in range, taking the feature away raises par (or makes it
unsolvable), every move takes a pawn, and packs.check passes (the app plays it the way it's graded).
"""
import json, multiprocessing, os, random, sys, tempfile
from solver import Board, DIRS, UP
import packs
import portals as portals_mod

OUT = os.path.join(packs.APP_DIR, "new-tricks.json")
N = 5
LEVEL = [d for d in DIRS if d[UP] == 0 and sum(map(abs, d)) == 1]

RECIPES = [
    ("Wormhole", "portal", (2, 2), (3, 4), "Slide into the dark swirl and you come out of the bright one."),
    ("Round Trip", "portal", (3, 3), (4, 6), "A trip through a portal sweeps pawns on both sides."),
    ("Back Door", "portal2", (3, 3), (5, 7), "Two pairs of portals. Each colour goes to its own partner."),
    ("Step Up", "ramp", (2, 2), (3, 5), "Slide straight up a ramp to climb one floor."),
    ("Upstairs", "ramp", (3, 3), (5, 7), "The pink sides of a ramp are walls. Only the slope takes you up."),
    ("Reflection", "mirror", (2, 2), (4, 6), "Every move you make, the Mirror Queen makes the opposite way."),
    ("Out of Step", "mirror_block", (3, 3), (5, 7), "A block can stop one queen and not the other."),
    ("Fuse", "bomb", (2, 2), (5, 7), "Run the queen over the bomb: it takes every pawn around it."),
    ("Your Bomb", "place_bomb", (2, 3), (6, 8), "Place the bomb, then set it off. Placing isn't a move."),
    ("Chain Reaction", "chain", (3, 4), (8, 11), "A blast that reaches another bomb sets it off too."),
]

CELLS = [(x, y, z) for x in range(N) for y in range(N) for z in range(N)]


def around(c):
    return [(c[0] + d[0], c[1] + d[1], c[2] + d[2]) for d in DIRS if all(0 <= c[i] + d[i] < N for i in range(3))]


def candidate(args):
    recipe, seed = args
    name, kind, (lo, hi), (pmin, pmax), tip = recipe
    rng = random.Random(seed)
    portals, ramps, blocks, mirror, bombs, place = [], {}, [], None, [], 0
    used = set()
    if kind in ("portal", "portal2"):
        for _ in range(2 if kind == "portal2" else 1):
            a, c = rng.sample([x for x in CELLS if x not in used], 2)
            portals.append((a, rng.choice(LEVEL), c, rng.choice(LEVEL), False))
            used |= {a, c}
    if kind == "ramp":
        r = rng.choice([c for c in CELLS if c[UP] < N - 1])
        d = rng.choice([d for d in LEVEL if all(0 <= r[i] + d[i] < N for i in range(3))])
        ramps[r] = [d]
        used.add(r)
    if kind in ("mirror", "mirror_block"):
        mirror = (1, 1, 1)
        if kind == "mirror_block":
            blocks = rng.sample([c for c in CELLS if c != (2, 2, 2)], 1)
            used |= set(blocks)
    pawns = set()
    if kind in ("bomb", "place_bomb", "chain"):
        centres = []
        for _ in range(2 if kind == "chain" else 1):
            c = tuple(rng.randrange(1, N - 1) for _ in range(3))
            if c in centres or c in pawns:
                return None
            centres.append(c)
            ring = [x for x in around(c) if x not in centres]
            pawns.update(rng.sample(ring, rng.randint(4, 6)))
        pawns -= set(centres)
        if kind == "bomb":
            bombs = centres
        elif kind == "place_bomb":
            place = 1
        else:  # one bomb down, one to place: the blast has to reach it
            bombs, place = centres[:1], 1
        used |= set(centres)
    target = rng.randint(pmin, pmax)
    free = [c for c in CELLS if c not in used]
    starts = [c for c in free if c not in pawns and (not mirror or (c != (2, 2, 2) and tuple(N - 1 - v for v in c) in free))]
    if not starts:
        return None
    start = rng.choice(starts)
    if ramps:
        # Start in line with the ramp's slope, so she can ride up it
        (r, (d,)), = ramps.items()
        line = [c for c in (tuple(r[i] - j * d[i] for i in range(3)) for j in range(1, N)) if c in starts]
        if not line:
            return None
        start = rng.choice(line)
    if not (bombs or place):
        # Plant the answer: walk lo..hi random moves (at least one using the feature) and put the
        # pawns on squares those moves sweep. A probe board with a "pawn" on every free square
        # shows which squares each move sweeps.
        reserved = {start} | ({tuple(N - 1 - v for v in start)} if mirror else set())
        probe_cells = [c for c in free if c not in reserved]
        probe = Board(N, probe_cells, blocks, False, portals, ramps, mirror)
        cell_of = {probe.bit[c]: c for c in probe_cells}
        state = (start, probe.reflect(start)) if mirror else start
        sweeps = []
        steps = rng.randint(lo, hi)
        twist = 0 if ramps else rng.randrange(steps)  # the move that uses the portal or ramp
        for i in range(steps):
            options = probe._next(state)
            if (portals or ramps) and i == twist:
                options = [(to, mask) for to, mask, d, k in probe.moves(state) if portals_mod.special(probe, state, d, to)]
            if not options:
                return None
            state, mask = rng.choice(options)
            cells = [cell_of[1 << i] for i in range(len(probe_cells)) if mask >> i & 1]
            if not cells:
                return None
            sweeps.append(cells)
        per = max(1, target // len(sweeps))
        for cells in sweeps:
            pawns.update(rng.sample(cells, min(len(cells), per)))
    else:
        while len(pawns) < target:
            c = rng.choice(free)
            if c != start:
                pawns.add(c)
    pawns = sorted(set(pawns) - used - {start})
    b = Board(N, pawns, blocks, False, portals, ramps, mirror, bombs)
    if place:
        par, placed, line, s, found = b.solve_bombs(place, start, limit=hi)
        if par is None or len(found) > 3:
            return None
    else:
        par, line, s = b.solve(start, limit=hi)
        placed = []
    if par is None or par < lo:
        return None
    # The feature is needed: without it par is out of reach
    plain = Board(N, pawns, blocks).solve(start, limit=par)[0]
    if plain is not None and plain <= par:
        return None
    if kind == "chain":
        # Both bombs matter: the placed one alone, or the given one alone, isn't enough
        if Board(N, pawns, blocks, bombs=bombs).solve(start, limit=par)[0] is not None:
            return None
    entry = {
        "name": name, "size": N, "queen": list(start), "best": list(start), "pawns": packs.flat(pawns),
        "blocks": packs.flat(blocks), "par": par, "tip": tip, "ice": False,
        "line": packs.flat([start] + [to for _, to in line]),
    }
    if portals:
        entry["portals"] = [v for a, fa, c, fc, two in portals for v in (*a, *fa, *c, *fc, int(two))]
    if ramps:
        entry["ramps"] = [v for c, ds in ramps.items() for d in ds for v in (*c, *d)]
    if mirror:
        entry["mirror"] = list(mirror)
    if bombs or place:
        entry["bombs"], entry["placeBombs"], entry["bestBombs"] = packs.flat(bombs), place, packs.flat(placed)
    # The app plays it the way it was graded, and every move takes a pawn
    with tempfile.NamedTemporaryFile("w", suffix=".json", delete=False) as f:
        json.dump({"id": "t", "puzzles": [entry]}, f)
    problems = packs.check(f.name)
    os.remove(f.name)
    if problems:
        return None
    bb = b.with_bombs(b.bombs + placed) if placed else b
    left, m = bb.full, (bb.reflect(start) if mirror else None)
    stops = packs.triples(entry["line"])
    for a, c in zip(stops, stops[1:]):
        if mirror:
            moves = [(x[0], x[2], x[1]) for x in bb.pair_moves(a, m) if x[0] == c]
        else:
            moves = [x for x in bb.moves(a) if x[0] == c]
        mv = max(moves, key=lambda x: bin(x[1] & left & bb.pawn_mask).count("1"))
        nxt = bb.after(left, mv[1])
        if not (left & ~nxt & bb.pawn_mask):
            return None
        left = nxt
        if mirror:
            m = mv[2]
    return {"entry": entry, "plain": plain, "pawns": len(pawns), "seed": seed}


def build(tries=3000):
    pool = multiprocessing.Pool()
    puzzles = []
    for k, r in enumerate(RECIPES):
        got = []
        for res in pool.imap_unordered(candidate, [(r, 10000 * k + i) for i in range(tries)], chunksize=8):
            if res:
                got.append(res)
                if len(got) >= 8:
                    break
        if not got:
            print(f"{r[0]}: nothing made the cut", flush=True)
            continue
        # Gentle: fewest pawns, then the biggest gap over doing without the mechanic
        got.sort(key=lambda g: (g["pawns"], -((g["plain"] or 99) - g["entry"]["par"]), g["seed"]))
        pick = got[0]
        puzzles.append(pick["entry"])
        print(f"{r[0]}: par {pick['entry']['par']} (without {pick['plain']}), {pick['pawns']} pawns, seed {pick['seed']}", flush=True)
    pool.terminate()
    return packs.write({"id": "new-tricks", "name": "New Tricks", "free": True, "puzzles": puzzles}, OUT)


if __name__ == "__main__":
    print(build())

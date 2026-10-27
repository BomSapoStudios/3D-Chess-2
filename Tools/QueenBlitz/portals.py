"""Portal and ramp puzzles where every portal and ramp is needed (Brandon, Oct 27: no decoy
portals early in a pack; decoys only later, deliberately).

    python3 portals.py gen SECONDS SEED OUT.json SIZES PARS     # candidates
    python3 portals.py pack OUT_PACK.json ID "Name" CANDIDATES.json...

A candidate plants a route that goes through a portal or up a ramp, puts pawns only on that
route, and is kept when:
  - par is exact from the given queen, with at most 2 openings that keep par
  - taking away any portal pair or ramp raises par or makes it unsolvable (it's needed)
  - every move of the solution is the one the app picks for that square (biggest sweep)
Late puzzles in a pack may add one decoy: a portal pair proven to change neither par nor
the openings.
"""
import json, random, sys, time
from multiprocessing import Pool
from solver import Board, DIRS, UP
import engine, packs, players, tricky

LEVEL = [d for d in DIRS if d[UP] == 0 and sum(map(abs, d)) == 1]


def path_of(b, at, d, dest):
    """Every cell a move passes or lands on, through portals and ramps"""
    cells, c = [], at
    for _ in range(4 * b.n * b.n):
        step = b._step(c, d)
        if step is None:
            return None
        passed, c, d = step
        cells += passed + [c]
        if c == dest:
            return cells
    return None


def special(b, at, d, dest):
    """Does this move go through a portal or up a ramp?"""
    cells = path_of(b, at, d, dest) or []
    return any(c in b.portal or c in b.ramp for c in cells)


def random_features(n, rng, want):
    cells = [(x, y, z) for x in range(n) for y in range(n) for z in range(n)]
    portals, ramps, used = [], {}, set()
    if want in ("portal", "both"):
        a, c = rng.sample(cells, 2)
        portals.append((a, rng.choice(LEVEL), c, rng.choice(LEVEL)))
        used |= {a, c}
    if want in ("ramp", "both"):
        for _ in range(rng.randint(1, 2)):
            r = rng.choice([c for c in cells if c not in used and c[UP] < n - 1])
            ramps[r] = [rng.choice(LEVEL)]
            used.add(r)
    return portals, ramps, used


def plant(n, par, rng, want, ice):
    portals, ramps, used = random_features(n, rng, want)
    blocks = []
    b0 = Board(n, [], blocks, ice, portals, ramps)
    start = rng.choice([c for c in b0.cells() if c not in used])
    at, pawns, specials = start, set(), 0
    for k in range(par):
        opts = [(to, d) for to, _, d, _ in b0.moves(at)]
        if not opts:
            return None
        sp = [(to, d) for to, d in opts if special(b0, at, d, to)]
        to, d = rng.choice(sp) if sp and (specials == 0 or rng.random() < 0.4) else rng.choice(opts)
        cells = [c for c in path_of(b0, at, d, to) if c not in used and c != start]
        if not cells:
            return None
        if special(b0, at, d, to):
            specials += 1
        pawns.update(rng.sample(cells, min(len(cells), rng.randint(1, 3))))
        at = to
    if specials == 0:
        return None
    pawns.discard(start)
    return start, sorted(pawns), blocks, portals, ramps


def solve_par(n, pawns, blocks, ice, portals, ramps, start, par, budget=200000):
    return engine.fewest(Board(n, pawns, blocks, ice, portals, ramps), [start], par + 1, budget)


def all_needed(n, pawns, blocks, ice, portals, ramps, start, par):
    for i in range(len(portals)):
        got = solve_par(n, pawns, blocks, ice, portals[:i] + portals[i + 1:], ramps, start, par)
        if got is not None and got <= par:
            return False
    for r in ramps:
        rest = {k: v for k, v in ramps.items() if k != r}
        got = solve_par(n, pawns, blocks, ice, portals, rest, start, par)
        if got is not None and got <= par:
            return False
    return True


def taps_ok(b, line, left):
    """Each move of the line is the biggest sweep to its square, as the app plays a tap"""
    for a, c in line:
        ms = [m for m in b.moves(a) if m[0] == c]
        best = max(ms, key=lambda m: bin(m[1]).count("1"))
        mine = [m for m in ms if bin(m[1]).count("1") == bin(best[1]).count("1")]
        if len({m[1] for m in mine}) > 1:
            return False
        left &= ~best[1]
    return left == 0


def flat(cells):
    return [v for c in cells for v in c]


def to_entry(n, start, pawns, blocks, ice, portals, ramps):
    b = Board(n, pawns, blocks, ice, portals, ramps)
    par, line, s = b.solve(start, limit=8)
    e = {"name": "", "size": n, "queen": list(start), "best": list(s), "pawns": flat(pawns), "blocks": flat(blocks),
         "par": par, "tip": "", "ice": bool(ice), "line": flat([s] + [to for _, to in line]),
         "portals": [v for a, fa, c, fc in portals for v in (*a, *fa, *c, *fc, 0)],
         "ramps": [v for r, ds in ramps.items() for d in ds for v in (*r, *d)]}
    return e, b, line


def attempt(job):
    try:
        return _attempt(job)
    except engine.TooBig:
        return None


def _attempt(job):
    n, par, want, ice, seed = job
    rng = random.Random(seed)
    for _ in range(30):
        made = plant(n, par, rng, want, ice)
        if made and par + 2 <= len(made[1]) <= 3 * par + 1:
            break
    else:
        return None
    start, pawns, blocks, portals, ramps = made
    if solve_par(n, pawns, blocks, ice, portals, ramps, start, par) != par:
        return None
    b = Board(n, pawns, blocks, ice, portals, ramps)
    if len(tricky.par_first_moves(b, [start], par)) > 2:
        return None
    if not all_needed(n, pawns, blocks, ice, portals, ramps, start, par):
        return None
    e, b, line = to_entry(n, start, pawns, blocks, ice, portals, ramps)
    if e["par"] != par or not taps_ok(b, line, b.full):
        return None
    e.update(want=want, lured=None)
    return e


def gen(seconds, seed, out, sizes, pars):
    rng = random.Random(seed)
    found, seen, tried = [], set(), 0
    end = time.time() + seconds
    with Pool(4) as pool:
        while time.time() < end:
            batch = [(rng.choice(sizes), rng.choice(pars), rng.choice(("portal", "portal", "ramp", "both")),
                      rng.random() < 0.25, rng.randrange(1 << 30)) for _ in range(16)]
            for e in pool.imap_unordered(attempt, batch):
                tried += 1
                if not e:
                    continue
                key = (e["size"], tuple(e["pawns"]), tuple(e["portals"]), tuple(e["ramps"]))
                if key in seen:
                    continue
                seen.add(key)
                found.append(e)
                json.dump(found, open(out, "w"))
                print(f"[{tried}] {e['size']}³ par {e['par']} {e['want']} {'ice' if e['ice'] else ''}", flush=True)
    print(f"done: {len(found)} kept of {tried} tried", flush=True)


def add_decoy_portal(e, rng):
    """One more portal pair, in a new colour, proven to change neither par nor the openings"""
    b = packs.board(e)
    q = tuple(e["queen"])
    base = (b.solve(q, limit=8)[0], frozenset(tricky.par_first_moves(b, [q], e["par"])))
    taken = set(packs.triples(e["pawns"])) | set(packs.triples(e["blocks"])) | set(b.portal) | set(b.ramp) | {q}
    line = set(packs.triples(e["line"]))
    free = [c for c in b.cells() if c not in taken and c not in line]
    for _ in range(200):
        a, c = rng.sample(free, 2)
        trial = dict(e, portals=e["portals"] + [*a, *rng.choice(LEVEL), *c, *rng.choice(LEVEL), 0])
        tb = packs.board(trial)
        if (tb.solve(q, limit=8)[0], frozenset(tricky.par_first_moves(tb, [q], e["par"])))[0] == base[0] and \
                frozenset(tricky.par_first_moves(tb, [q], e["par"])) == base[1]:
            # and the queen can actually reach it, so it tempts
            if any(special(tb, x, d, to) and any(p in tb.portal and p not in b.portal for p in path_of(tb, x, d, to) or [])
                   for x in line for to, _, d, _ in tb.moves(x)):
                return trial
    return None


TIPS = {"portal": "Step into a portal and come out somewhere else.",
        "ramp": "Ramps lift the queen up a floor.",
        "both": "Portals and ramps work together.",
        "ice": "On ice she keeps sliding, even through a portal.",
        "decoy": "Not every portal goes somewhere useful."}


def make_pack(path, pid, name, files):
    rng = random.Random(27)
    cands = []
    for f in files:
        cands += json.load(open(f))
    # A gentle climb: portal, ramp, both, then ice, then longer; decoys only in the last three
    plan = [("portal", 4, False), ("ramp", 4, False), ("portal", 5, False), ("ramp", 5, False), ("both", 5, False),
            ("portal", 5, True), ("both", 6, False), ("portal", 6, False), ("both", 6, True), ("ramp", 6, False)]
    chosen, used = [], set()
    for i, (want, par, ice) in enumerate(plan):
        fits = [c for c in cands if id(c) not in used and c["want"] == want and c["ice"] == ice and c["par"] == par]
        fits = fits or [c for c in cands if id(c) not in used and c["want"] == want and c["par"] >= par - 1]
        fits = fits or [c for c in cands if id(c) not in used]
        c = rng.choice(fits)
        used.add(id(c))
        e = dict(c)
        tip = TIPS["ice"] if e["ice"] else TIPS[e["want"]]
        if i >= len(plan) - 3:
            d = add_decoy_portal(e, rng)
            if d:
                e, tip = d, TIPS["decoy"]
        e.pop("want", None)
        e.pop("lured", None)
        e["name"] = f"Portals {i + 1}"
        e["tip"] = tip
        chosen.append(e)
    print(packs.write({"id": pid, "name": name, "free": True, "puzzles": chosen}, path))
    for e in chosen:
        print(e["name"], e["size"], "par", e["par"], "ice" if e["ice"] else "", len(e["portals"]) // 13, "portal pairs",
              len(e["ramps"]) // 6, "ramp faces", e["tip"])


if __name__ == "__main__":
    if sys.argv[1] == "gen":
        gen(float(sys.argv[2]), int(sys.argv[3]), sys.argv[4],
            tuple(int(x) for x in sys.argv[5].split(",")), tuple(int(x) for x in sys.argv[6].split(",")))
    elif sys.argv[1] == "pack":
        make_pack(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5:])

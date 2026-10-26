"""Queen Blitz puzzle engine: make candidates with every trick generator, keep the ones the
shortcut players (players.py) can't solve in par, and tag each with the tricks it uses.

    python3 engine.py run SECONDS SEED OUT.json [SIZES] [PARS]   # e.g. run 1200 1 out.json 5,6 4,5,6
    python3 engine.py pack OUT_PACK.json ID "Name" CANDIDATES.json...   # 10 varied puzzles, trickiest first-pass

A candidate is kept when all three lure players (greedy, liner, lookahead) miss par. Its tags
(the recipe) say which tricks fooled which player, so a pack can avoid repeating a recipe.
"""
import json, os, random, sys, time
from multiprocessing import Pool
from solver import Board
import packs, players, tricky

MAKERS = {
    "fork": lambda n, par, rng: tricky.fork_route(n, par, rng),
    "bait": lambda n, par, rng: tricky.bait_route(n, par, rng),
    "wander": lambda n, par, rng: tricky.wander_route(n, par, rng),
    "plane": lambda n, par, rng: tricky.plane_route(n, par, rng),
    "ice": lambda n, par, rng: (lambda m: m and m[:3])(tricky.icebrake_make(n, par, rng)),
    "blocks": lambda n, par, rng: tricky._any_route(n, par, rng, blockers=rng.randint(2, 5)),
    "mirror": lambda n, par, rng: (lambda m: m and m[:3])(tricky.symmetry_make(n, par, rng)),
}
ICE = {"ice"}


def mirrored(n, pawns):
    for axis in range(3):
        m = {tuple(n - 1 - c[i] if i == axis else c[i] for i in range(3)) for c in pawns}
        if m == set(pawns):
            return True
    return False


def tags(n, p, r):
    """The recipe: board and setup features plus every player the puzzle beats"""
    t = {f"{n}³"}
    t.add("given" if p["queen"] else "place")
    if p["ice"]:
        t.add("ice")
    if p["blocks"]:
        t.add("blocks")
    pawns = [tuple(p["pawns"][i:i + 3]) for i in range(0, len(p["pawns"]), 3)]
    if mirrored(n, pawns):
        t.add("mirror")
    for name in players.PLAYERS:
        if name in r and r[name] > p["par"]:
            t.add("beats-" + name)
    return sorted(t)


class TooBig(Exception):
    pass


def fewest(b, starts, limit, budget=200000):
    """Fewest moves to clear the board from any of `starts` (None past limit). Gives up with
    TooBig past `budget` states, so one sprawling candidate can't stall the run."""
    frontier = {(s, b.full) for s in starts}
    seen = set(frontier)
    for depth in range(1, limit + 1):
        nxt = set()
        for at, left in frontier:
            for to, swept, _, _ in b.moves(at):
                state = (to, left & ~swept)
                if state[1] == 0:
                    return depth
                if state not in seen:
                    seen.add(state)
                    nxt.add(state)
        if len(seen) > budget:
            raise TooBig
        frontier = nxt
    return None


def attempt(job):
    try:
        return _attempt(job)
    except TooBig:
        return None


def _attempt(job):
    n, par, kind, seed = job
    rng = random.Random(seed)
    for _ in range(40):  # most makers fail cheaply; keep trying until one gives a fair pawn count
        made = MAKERS[kind](n, par, rng)
        if made and par + 3 <= len(made[1]) <= 3 * par + 2:
            break
    else:
        return None
    start, pawns, blocks = made
    ice = kind in ICE
    b = Board(n, pawns, blocks, ice)
    if fewest(b, [start], par) != par:
        return None
    free_par = fewest(b, b.starts(), par)
    # Brandon's rule: if a free start beats the planted route, the queen must be given
    given = free_par < par or rng.random() < 0.7
    queen = start if given else None
    if given:
        starts = [queen]
    else:
        starts = []
        for s in b.starts():
            if fewest(b, [s], par, budget=60000) == par:
                starts.append(s)
                if len(starts) > 3:
                    return None
    if given and len(tricky.par_first_moves(b, starts, par)) > 2:
        return None
    entry = packs.solved("", n, queen, pawns, blocks, "", ice)
    r = players.assess(entry)
    if players.lured(entry, r) < 3:
        return None
    entry.update(kind=kind, score=players.score(entry, r), tags=tags(n, entry, r),
                 players={k: (v - par if not k.endswith("_kept") else v) for k, v in r.items()})
    return entry


def run(seconds, seed, out, sizes=(5, 6), pars=(4, 5, 6)):
    rng = random.Random(seed)
    kinds = list(MAKERS)
    found, seen, tried = [], set(), 0
    end = time.time() + seconds

    with Pool(4) as pool:
        while time.time() < end:
            batch = [(rng.choice(sizes), rng.choice(pars), rng.choice(kinds), rng.randrange(1 << 30)) for _ in range(16)]
            for e in pool.imap_unordered(attempt, batch):
                tried += 1
                if not e:
                    continue
                key = (e["size"], tuple(e["pawns"]), tuple(e["blocks"]))
                if key in seen:
                    continue
                seen.add(key)
                found.append(e)
                json.dump(found, open(out, "w"))
                print(f"[{tried}] {e['kind']:6} {e['size']}³ par {e['par']} score {e['score']} {' '.join(e['tags'])}", flush=True)
    print(f"done: {len(found)} kept of {tried} tried", flush=True)


TIPS = {
    "ice": "On ice, the blocks are your brakes.",
    "mirror": "It looks the same on both sides. The route isn't.",
    "place": "Where you start matters more than it looks.",
    "beats-stopper": "Sometimes the best stop has no pawn on it.",
    "beats-greedy": "The biggest sweep isn't always the best one.",
    "beats-liner": "Don't ride the obvious row.",
    "beats-floor": "Every pawn is on one floor. She isn't.",
}


def pick_pack(cands, count=10):
    """Trickiest first, but never the same recipe twice and a mix of setups; then ordered
    easiest to hardest by score"""
    chosen, recipes = [], set()
    for c in sorted(cands, key=lambda c: -c["score"]):
        recipe = tuple(c["tags"])
        if recipe in recipes:
            continue
        setup = (c["size"], bool(c["queen"]), c["ice"], bool(c["blocks"]))
        if sum(1 for x in chosen if (x["size"], bool(x["queen"]), x["ice"], bool(x["blocks"])) == setup) >= 3:
            continue
        chosen.append(c)
        recipes.add(recipe)
        if len(chosen) == count:
            break
    return sorted(chosen, key=lambda c: c["score"])


def make_pack(path, pid, name, files):
    cands = []
    for f in files:
        cands += json.load(open(f))
    chosen = pick_pack(cands)
    puzzles = []
    for i, c in enumerate(chosen, 1):
        tip = next((TIPS[t] for t in ("ice", "mirror", "beats-floor", "place", "beats-stopper", "beats-liner", "beats-greedy") if t in c["tags"]), "")
        p = {k: v for k, v in c.items() if k not in ("kind",)}
        p["name"] = f"{name.split(' (')[0]} {i}"
        p["tip"] = tip
        puzzles.append(p)
    pack = {"id": pid, "name": name, "free": True, "puzzles": puzzles}
    print(packs.write(pack, path))
    for p in puzzles:
        print(f"{p['name']:12} {p['size']}³ par {p['par']} score {p['score']:5} {' '.join(p['tags'])}")


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "run":
        sizes = tuple(int(x) for x in sys.argv[5].split(",")) if len(sys.argv) > 5 else (5, 6)
        pars = tuple(int(x) for x in sys.argv[6].split(",")) if len(sys.argv) > 6 else (4, 5, 6)
        run(float(sys.argv[2]), int(sys.argv[3]), sys.argv[4], sizes, pars)
    elif cmd == "pack":
        make_pack(sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5:])

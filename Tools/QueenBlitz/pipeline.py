"""Queen Blitz puzzle pipeline: generate, check, grade, name and write puzzles for packs and dailies.

    python3 pipeline.py candidates RECIPE COUNT SEED OUT.json   # make graded candidates for one recipe
    python3 pipeline.py grade PACK.json                          # print the grade of every puzzle in a file
    python3 pipeline.py assemble ID "Name" NAMES.txt OUT.json CANDIDATES.json...   # pick 50 into 5 chapters

A recipe is a board size, floor, number of blocks, whether the queen is given, and a target par
(see RECIPES). Every candidate passes the rules in `check`, gets a difficulty score from 1 to 100
(`grade`) and a tier, and is unique up to rotating or mirroring the cube (`canonical`).
"""
import itertools, json, random, sys
from solver import Board, DIRS
from gen import route_puzzle
import packs

TIERS = [(20, "Warm-up"), (40, "Easy"), (60, "Medium"), (80, "Hard"), (100, "Devious")]

# name: (size, ice, blocks, queen given, par, pawns (min, max), max par starts when placing)
RECIPES = {
    "warmup-4":    (4, False, 0, True, 2, (3, 6), None),
    "easy-4":      (4, False, 0, False, 2, (4, 7), 6),
    "medium-4":    (4, False, 0, False, 3, (5, 9), 4),
    "medium-6":    (6, False, 0, False, 3, (6, 10), 4),
    "hard-6":      (6, False, 0, False, 4, (8, 12), 3),
    "devious-6":   (6, False, 0, False, 5, (9, 14), 2),
    "ice-easy-4":  (4, True, 1, True, 2, (3, 7), None),
    "ice-med-6":   (6, True, 4, False, 3, (6, 10), 4),
    "ice-hard-6":  (6, True, 6, False, 4, (8, 12), 3),
    "block-med-6": (6, False, 2, False, 3, (6, 10), 4),
    "block-hard-6": (6, False, 3, False, 4, (8, 12), 3),
    "devious-8":   (8, False, 0, False, 5, (10, 15), 2),
}


# ───────────────────────── Symmetry ─────────────────────────

def _symmetries(n):
    """The 48 ways to rotate or mirror a cube, as functions on cells"""
    out = []
    for perm in itertools.permutations(range(3)):
        for flips in itertools.product((False, True), repeat=3):
            def f(c, perm=perm, flips=flips):
                v = [c[perm[0]], c[perm[1]], c[perm[2]]]
                return tuple(n - 1 - v[i] if flips[i] else v[i] for i in range(3))
            out.append(f)
    return out


def canonical(n, ice, queen, pawns, blocks):
    """The same key for a puzzle and all its rotations and mirror images"""
    best = None
    for f in _symmetries(n):
        key = (n, ice, f(queen) if queen else None, tuple(sorted(map(f, pawns))), tuple(sorted(map(f, blocks))))
        if best is None or key < best:
            best = key
    return json.dumps(best)


# ───────────────────────── Grading ─────────────────────────

def measure(n, ice, queen, pawns, blocks):
    """Everything the grade and the rules need, from the solver. None if it doesn't solve in 8."""
    b = Board(n, pawns, blocks, ice)
    par, line, start = b.solve(queen, limit=8)
    if par is None:
        return None
    starts = [queen] if queen else b.optimal_starts(par)
    free_par = b.solve(None, limit=par)[0] if queen else par
    greedy = b.greedy(queen)
    # Trap openings: of the biggest-sweep first moves, the share that can no longer reach par
    firsts = []
    for s in ([queen] if queen else b.starts()):
        for to, swept, _, _ in b.moves(s):
            firsts.append((bin(swept).count("1"), s, to, swept))
    top = max(f[0] for f in firsts) if firsts else 0
    big = [f for f in firsts if f[0] == top and top > 0]
    traps = sum(1 for _, s, to, swept in big if b.solve_from(to, b.full & ~swept, par - 1) is None)
    # 3D diagonals in the solution: the hardest lines to see on a phone
    diag3 = sum(1 for a, c in line if all(a[i] != c[i] for i in range(3)))
    no_blocks = Board(n, pawns, (), ice).solve(queen, limit=par)[0] if blocks else par
    return dict(par=par, line=line, start=start, starts=len(starts), free_par=free_par, greedy=greedy,
                trap_share=traps / len(big) if big else 0.0, diag3=diag3, no_blocks=no_blocks,
                pawns=len(pawns), size=n, ice=ice, blocks=len(blocks), given=queen is not None)


def grade(m):
    """Difficulty from 1 to 100. Weights are first guesses, to tune from Brandon's playtests."""
    score = 0.0
    score += {1: 0, 2: 8, 3: 22, 4: 36, 5: 48, 6: 56, 7: 62, 8: 66}[m["par"]]
    score += min(m["greedy"] - m["par"], 3) * 7           # deceptive: biggest sweep first doesn't work
    score += m["trap_share"] * 10                          # the obvious opening fails
    if not m["given"]:
        score += 4 + max(0, 6 - m["starts"]) * 1.5         # few good starting squares
    score += {4: 0, 6: 4, 8: 9}.get(m["size"], 0)
    score += 4 if m["ice"] else 0
    score += 3 if m["blocks"] else 0
    score += min(m["diag3"], 3) * 2
    return max(1, min(100, round(score)))


def tier(score):
    return next(name for top, name in TIERS if score <= top)


# ───────────────────────── Rules ─────────────────────────

def check(m, recipe):
    """Reasons a candidate is thrown out (empty list: it's good)"""
    size, ice, nblocks, given, par, (lo, hi), max_starts = recipe
    why = []
    if m["par"] != par:
        why.append(f"par {m['par']} not {par}")
    if not lo <= m["pawns"] <= hi:
        why.append("pawn count")
    if m["pawns"] / m["par"] < 2:
        why.append("under 2 pawns a move")
    # Brandon's rule: if choosing the start beats the intended route, the queen must be given.
    # Placement puzzles take their par with free placement, so only given ones can break it.
    if not given and max_starts is not None and m["starts"] > max_starts:
        why.append(f"{m['starts']} par starts")
    if nblocks and m["no_blocks"] is not None and m["no_blocks"] >= m["par"] and not ice:
        why.append("blocks don't matter")
    return why


# ───────────────────────── Generating ─────────────────────────

def _blocks_that_matter(n, pawns, count, rng, start=None):
    """Blocks that break every shorter route: hit every (par - 1)-move solution's path"""
    b = Board(n, pawns)
    short = b.solve(start, limit=6)[0]
    if short is None or short < 1:
        return None
    pset = set(pawns)

    def walk(a, d, k):
        return [(a[0] + d[0] * i, a[1] + d[1] * i, a[2] + d[2] * i) for i in range(1, k + 1)]

    paths = []

    def dfs(at, left, depth, cells):
        if left == 0:
            paths.append(frozenset(cells))
            return
        if depth == 0 or len(paths) > 4000:
            return
        for to, swept, d, k in b.moves(at):
            if left & swept:
                dfs(to, left & ~swept, depth - 1, cells | {c for c in walk(at, d, k) if c not in pset})

    for s in ([start] if start else b.starts()):
        dfs(s, b.full, short, {s} if not start else set())
    if not paths or len(paths) > 4000:
        return None
    chosen, rem = [], list(paths)
    while rem and len(chosen) < count:
        tally = {}
        for p in rem:
            for c in p:
                tally[c] = tally.get(c, 0) + 1
        topv = max(tally.values())
        c = rng.choice([c for c, v in tally.items() if v >= topv * 0.8])
        chosen.append(c)
        rem = [p for p in rem if c not in p]
    return chosen if not rem else None


def candidate(recipe, rng):
    size, ice, nblocks, given, par, (lo, hi), _ = recipe
    if nblocks and not ice:
        # Plant a route one move shorter, then add blocks that force the longer way round
        made = route_puzzle(size, par - 1, 0, rng, per_move=(2, 3))
        if not made:
            return None
        start, pawns, _ = made
        blocks = _blocks_that_matter(size, pawns, nblocks, rng, start if given else None)
        if blocks is None:
            return None
    else:
        made = route_puzzle(size, par, nblocks, rng, per_move=(2, 3) if par > 2 else (1, 3), ice=ice)
        if not made:
            return None
        start, pawns, blocks = made
    return (start if given else None), pawns, blocks


def describe(m):
    """A one-line tip from what makes the puzzle tick"""
    if m["ice"] and m["blocks"]:
        return "On ice, blocks are your brakes."
    if m["ice"]:
        return "Ice! Every slide runs to the edge or a block."
    if m["blocks"] and m["no_blocks"] is not None and m["no_blocks"] < m["par"]:
        return "A block is in the way of the short route."
    if m["trap_share"] >= 0.5:
        return "The biggest sweep is a trap."
    if not m["given"] and m["starts"] <= 2:
        return f"Only {'one starting square works' if m['starts'] == 1 else 'two starting squares work'}."
    if m["diag3"]:
        return "Look for the corner-to-corner lines."
    return "Plan the whole route before the first move."


def make(recipe_name, count, seed, seen=None, tries=200000):
    """Up to `count` graded puzzles for one recipe, none a copy (or mirror) of each other or of `seen`"""
    recipe = RECIPES[recipe_name]
    rng = random.Random(seed)
    seen = set() if seen is None else seen
    out = []
    for _ in range(tries):
        c = candidate(recipe, rng)
        if not c:
            continue
        queen, pawns, blocks = c
        key = canonical(recipe[0], recipe[1], queen, pawns, blocks)
        if key in seen:
            continue
        m = measure(recipe[0], recipe[1], queen, pawns, blocks)
        if not m or check(m, recipe):
            continue
        seen.add(key)
        score = grade(m)
        entry = packs.solved("", recipe[0], queen, pawns, blocks, describe(m), recipe[1])
        entry.update(score=score, tier=tier(score), recipe=recipe_name, greedy=m["greedy"],
                     starts=m["starts"], trap=round(m["trap_share"], 2))
        out.append(entry)
        if len(out) >= count:
            break
    return out


# Each chapter's easiest and hardest target score: every chapter climbs, and starts a little
# above the last one started (the sawtooth). The last three of chapter 5 should be Devious.
CHAPTER_TARGETS = [(6, 32), (20, 46), (34, 58), (46, 72), (60, 96)]


def assemble(candidates, chapters=CHAPTER_TARGETS, size=10):
    """Pick puzzles for each slot nearest its target score, each candidate used once, sorted easy
    to hard within a chapter. Returns the list in play order."""
    pool = sorted(candidates, key=lambda p: p["score"])
    chosen = []
    for lo, hi in chapters:
        picks = []
        for i in range(size):
            target = lo + (hi - lo) * i / (size - 1)
            best = min(pool, key=lambda p: abs(p["score"] - target))
            pool.remove(best)
            picks.append(best)
        chosen += sorted(picks, key=lambda p: p["score"])
    return chosen


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "candidates":
        recipe, count, seed, path = sys.argv[2], int(sys.argv[3]), int(sys.argv[4]), sys.argv[5]
        made = make(recipe, count, seed)
        for i, p in enumerate(made, 1):
            p["name"] = f"{recipe} {i}"
        print(packs.write({"id": recipe, "name": recipe, "free": False, "puzzles": made}, path), len(made))
    elif cmd == "grade":
        pack = json.load(open(sys.argv[2], encoding="utf-8"))
        for i, p in enumerate(pack["puzzles"], 1):
            t = packs.triples
            m = measure(p["size"], p.get("ice", False), tuple(p["queen"]) if p["queen"] else None,
                        t(p["pawns"]), t(p["blocks"]))
            s = grade(m)
            print(f"{i:3} {p['name']:18} score={s:3} {tier(s):8} par={m['par']} greedy={m['greedy']} "
                  f"starts={m['starts']} trap={m['trap_share']:.2f} diag3={m['diag3']}")
    elif cmd == "assemble":
        pack_id, name, names_path, out = sys.argv[2], sys.argv[3], sys.argv[4], sys.argv[5]
        candidates = []
        for f in sys.argv[6:]:
            candidates += json.load(open(f, encoding="utf-8"))["puzzles"]
        names = [l.strip() for l in open(names_path, encoding="utf-8") if l.strip()]
        chosen = assemble(candidates)
        for i, p in enumerate(chosen):
            p["name"] = names[i] if i < len(names) else f"Puzzle {i + 1}"
        print(packs.write({"id": pack_id, "name": name, "free": False, "puzzles": chosen}, out))
        for i, p in enumerate(chosen, 1):
            print(f"{i:3} ch{(i - 1) // 10 + 1} {p['name']:20} {p['score']:3} {p['tier']:8} {p['size']}³ par {p['par']} "
                  f"pawns {len(p['pawns']) // 3:2} {'given' if p['queen'] else 'place'} {p['tip']}")
    else:
        print(__doc__)

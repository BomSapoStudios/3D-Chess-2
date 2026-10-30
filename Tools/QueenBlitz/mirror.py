"""Mirror Queen puzzles (Brandon, Oct 30): a second queen on the far side of the cube copies every
queen move the opposite way, and each resolves her own move against the board (solver.py).

Puzzles are built backwards: a random board (blocks, maybe ice), a start, then a few random moves
of the pair; pawns go on squares those moves sweep. The real par is then solved, and a puzzle is
kept only if the mirror earns her place:
- she sweeps at least `share` of the pawns on the par line, and the puzzle is harder without her
  thinking (the queen alone, from the same start, needs more moves or can't do it in 8),
- at least once the two queens fall out of step (one is stopped or slides on where the other
  doesn't), so the board's lopsidedness matters, when `desync` is asked for,
- a "remote" move (the queen sweeps nothing, only the mirror does) is on every par line, when asked,
- the greedy player misses par, and one tap never means two different mirror moves.

Run: python3 mirror.py  (writes Assets/Resources/QueenBlitz/mirror-test.json)
"""
import random, sys
import packs
from solver import Board, DIRS

FLIP = (1, 1, 1)  # through the centre (Brandon's pick pending; one setting)


def bits(m):
    return bin(m).count("1")


def walk(b, start, k, rng):
    """k random moves of the pair from start: [(queen, mirror, queen's sweep squares, mirror's)].
    With portals or ramps, moves where either queen goes through one are picked about half the time."""
    q, m = start, b.reflect(start)
    steps = []
    feature = set(b.portal) | set(b.ramp)
    for _ in range(k):
        opts, special = [], []
        for d0 in DIRS:
            stops = b._slide(q, d0, m)
            if b.ice:
                stops = stops[-1:]
            for to, _, kk in stops:
                m2, _ = b.mirror_follow(m, d0, kk, to)
                opts.append((to, m2, d0, kk))
                if feature and feature & set(path_cells(b, q, d0, kk, m, b.ice) + path_cells(b, m, b.flip(d0), kk, to, b.ice)):
                    special.append(opts[-1])
        if not opts:
            return None
        to, m2, d0, kk = rng.choice(special if special and rng.random() < 0.5 else opts)
        steps.append((q, m, to, m2, d0, kk))
        q, m = to, m2
    return steps


def path_cells(b, at, d, k, extra, ice):
    """The squares a slide passes and stops on (pawn candidates)"""
    out, c = [], at
    n = 0
    while True:
        st = b._step(c, d, extra)
        if st is None:
            break
        passed, c, d = st
        out += passed + [c]
        n += 1
        if not ice and n >= k:
            break
    return out


def line_stats(b, start, line):
    """Replay a par line: pawns the mirror sweeps, whether they ever fell out of step, remote moves"""
    q, m, left = start, b.reflect(start), b.full
    by_mirror, desync, remote = 0, False, 0
    for a, c in line:
        mv = max((x for x in b.pair_moves(q, m) if x[0] == c), key=lambda x: bits(x[2]))
        to, m2, mask, d0, k = mv
        qmask = next(x[1] for x in b.moves(q, m) if x[0] == c and x[2] == d0 and x[3] == k)
        new_q = bits(qmask & left)
        new_all = bits(mask & left)
        by_mirror += new_all - new_q
        if new_q == 0 and new_all > 0:
            remote += 1
        if m2 != b.reflect(to):
            desync = True
        left &= ~mask
        q, m = to, m2
    return by_mirror, desync, remote


def needed(n, pawns, blocks, ice, portals, ramps, start, par):
    """Every portal pair and ramp is needed: without any one of them par can't be reached"""
    for i in range(len(portals)):
        got = Board(n, pawns, blocks, ice, portals[:i] + portals[i + 1:], ramps, FLIP).solve(start, limit=par)[0]
        if got is not None:
            return False
    for r in ramps:
        rest = {k: v for k, v in ramps.items() if k != r}
        if Board(n, pawns, blocks, ice, portals, rest, FLIP).solve(start, limit=par)[0] is not None:
            return False
    return True


def make(rng, n, ice, nblocks, k, npawns, share=0.3, desync=True, remote=False, given=True, par_range=(3, 5), want=None, tries=4000):
    """want: None, or "portal", "ramp" or "both" for a puzzle mixing the mirror with portals and ramps"""
    import portals as pt
    cells = [(x, y, z) for x in range(n) for y in range(n) for z in range(n)]
    for _ in range(tries):
        portals, ramps, used = pt.random_features(n, rng, want) if want else ([], {}, set())
        blocks = set(rng.sample([c for c in cells if c not in used], nblocks))
        b0 = Board(n, [], blocks, ice, portals, ramps, FLIP)
        starts = b0.starts()
        if not starts:
            continue
        start = rng.choice(starts)
        steps = walk(b0, start, k, rng)
        if not steps:
            continue
        # pawns on the squares the moves sweep, at least one new pawn a move
        pawns = set()
        occupied = {start, b0.reflect(start)} | used
        for q, m, to, m2, d0, kk in steps:
            qc = [c for c in path_cells(b0, q, d0, kk, m, ice) if c not in occupied]
            mc = [c for c in path_cells(b0, m, b0.flip(d0), kk, to, ice) if c not in occupied]
            pool = qc + mc
            if not pool:
                break
            for c in rng.sample(pool, min(len(pool), rng.randint(1, 3))):
                pawns.add(c)
        else:
            pawns = sorted(pawns)[:npawns + 3]
            if len(pawns) < npawns:
                continue
            b = Board(n, pawns, blocks, ice, portals, ramps, FLIP)
            par, line, s = b.solve(start if given else None, limit=par_range[1])
            if par is None or par < par_range[0]:
                continue
            if not given and len(b.optimal_starts(par)) > 3:
                continue
            st = s
            solo = Board(n, pawns, blocks, ice, portals, ramps).solve(st, limit=8)[0]
            if solo is not None and solo <= par:
                continue  # the mirror isn't needed
            if (b.greedy(st) or 99) <= par:
                continue
            q, m, amb = st, b.reflect(st), False
            for a, c in line:
                if b.ambiguous(q, m):
                    amb = True
                    break
                mv = max((x for x in b.pair_moves(q, m) if x[0] == c), key=lambda x: bits(x[2]))
                q, m = mv[0], mv[1]
            if amb:
                continue
            by_mirror, ds, rem = line_stats(b, st, line)
            if by_mirror < share * len(pawns):
                continue
            if desync and not ds:
                continue
            if remote and not rem:
                continue
            if want and not needed(n, pawns, blocks, ice, portals, ramps, st, par):
                continue
            return {"size": n, "queen": st if given else None, "best": st, "pawns": pawns, "blocks": sorted(blocks),
                    "ice": ice, "par": par, "line": [st] + [c for _, c in line], "solo": solo,
                    "portals": [v for a, fa, c, fc in portals for v in (*a, *fa, *c, *fc, 0)],
                    "ramps": [v for r, ds in ramps.items() for d in ds for v in (*r, *d)],
                    "mirror_share": round(by_mirror / len(pawns), 2), "desync": ds, "remote": rem}
    return None


# (name, tip, size, ice, blocks, walk moves, pawns, desync, remote, given, par range)
PLAN = [
    ("Reflection", "Every move you make, she makes backwards.", 5, False, 0, 3, 6, False, False, True, (2, 3)),
    ("Two Hands", "Your pawns and hers. Clear them all.", 5, False, 0, 3, 7, False, False, True, (3, 3)),
    ("Out of Step", "A block stops her, not you.", 5, False, 2, 3, 7, True, False, True, (3, 4)),
    ("Puppet", "Some moves are just for her.", 5, False, 2, 4, 7, True, True, True, (3, 4)),
    ("Cold Glass", "On ice she slides as far as she can.", 5, True, 2, 3, 6, True, False, True, (3, 4)),
    ("Steady Hand", "Short moves keep her short too.", 5, False, 3, 4, 8, True, True, True, (4, 5)),
    ("Black Mirror", "Ice and walls. Count her slide.", 5, True, 3, 4, 8, True, False, True, (4, 5)),
    ("Strings", "Steer her first.", 6, False, 3, 4, 9, True, True, True, (4, 5)),
    ("Hall of Mirrors", "Pick your square. She picks hers.", 5, False, 2, 4, 8, True, False, False, (4, 5)),
    ("Shatter", "Two queens, one plan.", 6, True, 4, 5, 9, True, True, True, (4, 5)),
]


# Brandon, Oct 30: the mirror can go in multi-mechanic puzzles too. (name, tip, size, ice, blocks,
# walk moves, pawns, want, given, par range); bombs join once that mechanic is merged.
MIX = [
    ("Looking Glass", "She goes through portals too.", 5, False, 1, 3, 7, "portal", True, (3, 4)),
    ("Up and Over", "One climbs while the other dives.", 5, False, 1, 3, 7, "ramp", True, (3, 4)),
    ("Funhouse", "Portal for you, wall for her?", 5, False, 2, 4, 8, "portal", True, (4, 5)),
    ("Frost Gate", "Ice, a portal, and two queens.", 5, True, 2, 3, 7, "portal", True, (3, 5)),
    ("Hill Climb", "Short steps up the ramp.", 6, False, 2, 4, 8, "ramp", True, (4, 5)),
    ("Black Ice Ramp", "Count both slides.", 5, True, 2, 4, 8, "ramp", True, (4, 5)),
    ("Wormhole Twins", "Every trick at once.", 6, False, 2, 4, 9, "both", True, (4, 5)),
    ("Kaleidoscope", "Pick your square. Pick her route.", 5, False, 1, 4, 8, "portal", False, (4, 5)),
]


# Mirror Queen with bombs already on the board (a bomb either queen passes goes off; blasts chain).
# (name, tip, size, ice, blocks, walk moves, pawns on paths, bombs, par range)
BOMBS = [
    ("Mirror Blast", "Her bombs go off too.", 5, False, 1, 3, 4, 1, (3, 4)),
    ("Twin Fuse", "One bomb each.", 5, False, 2, 3, 4, 2, (3, 4)),
    ("Cold Fuse", "Slide her into the bomb.", 5, True, 2, 3, 4, 1, (3, 4)),
    ("Chain Reflection", "Let her light the chain.", 6, False, 2, 4, 5, 2, (4, 5)),
    ("Powder Mirror", "Steer first, then boom.", 6, True, 3, 4, 5, 2, (4, 5)),
]


def make_bombs(rng, n, ice, nblocks, k, npawns, nbombs, par_range, tries=3000):
    """A walk of the pair, pawns on its paths, and bombs on its paths with a few extra pawns only a
    blast reaches. Kept when: the mirror and every bomb are needed, the mirror sets a bomb off on the
    par line, the greedy player misses par, and no tap is ambiguous."""
    cells = [(x, y, z) for x in range(n) for y in range(n) for z in range(n)]
    for _ in range(tries):
        blocks = set(rng.sample(cells, nblocks))
        b0 = Board(n, [], blocks, ice, mirror=FLIP)
        starts = b0.starts()
        if not starts:
            continue
        start = rng.choice(starts)
        steps = walk(b0, start, k, rng)
        if not steps:
            continue
        occupied = {start, b0.reflect(start)}
        qpath, mpath = set(), set()
        for q, m, to, m2, d0, kk in steps:
            qpath |= set(path_cells(b0, q, d0, kk, m, ice))
            mpath |= set(path_cells(b0, m, b0.flip(d0), kk, to, ice))
        qpath -= occupied
        mpath -= occupied
        if not mpath:
            continue
        # at least one bomb on the mirror's path
        bombs = [rng.choice(sorted(mpath))]
        rest = sorted((qpath | mpath) - set(bombs))
        bombs += rng.sample(rest, min(len(rest), nbombs - 1))
        lanes = (qpath | mpath) - set(bombs)
        pawns = set(rng.sample(sorted(lanes), min(len(lanes), npawns)))
        # pawns only a blast reaches: off every path, next to a bomb
        for bm in bombs:
            near = [c for c in b0._around(bm) if b0.inside(c) and c not in blocks and c not in occupied
                    and c not in qpath and c not in mpath and c not in bombs]
            pawns |= set(rng.sample(near, min(len(near), rng.randint(2, 3))))
        pawns = sorted(pawns)
        b = Board(n, pawns, blocks, ice, mirror=FLIP, bombs=bombs)
        par, line, st = b.solve(start, limit=par_range[1])
        if par is None or par < par_range[0]:
            continue
        solo = Board(n, pawns, blocks, ice, bombs=bombs).solve(start, limit=8)[0]
        if solo is not None and solo <= par:
            continue  # the mirror isn't needed
        if any(b.with_bombs([x for x in bombs if x != bm]).solve(start, limit=par)[0] is not None for bm in bombs):
            continue  # a bomb isn't needed
        if (b.greedy(start) or 99) <= par:
            continue
        q, m, left, ok, mirror_fuse = start, b.reflect(start), b.full, True, False
        for a, c in line:
            if b.ambiguous(q, m):
                ok = False
                break
            mv = max((x for x in b.pair_moves(q, m) if x[0] == c), key=lambda x: bits(x[2] & b.pawn_mask))
            qm = next(x[1] for x in b.moves(q, m) if x[0] == c and x[2] == mv[3] and x[3] == mv[4])
            if mv[2] & ~qm & left & b.bomb_mask:
                mirror_fuse = True
            left = b.after(left, mv[2])
            q, m = mv[0], mv[1]
        if not ok or not mirror_fuse or left & b.pawn_mask:
            continue
        return {"size": n, "queen": start, "best": start, "pawns": pawns, "blocks": sorted(blocks), "bombs": bombs,
                "ice": ice, "par": par, "line": [start] + [c for _, c in line], "solo": solo}
    return None


def build_bombs(seed=1):
    rng = random.Random(seed)
    out = []
    for name, tip, n, ice, nb, k, np_, nbm, pr in BOMBS:
        p = None
        while p is None:
            p = make_bombs(rng, n, ice, nb, k, np_, nbm, pr)
            if p is None:
                print(f"  {name}: retrying", file=sys.stderr)
        print(f"{name}: {n}³{' ice' if ice else ''} par {p['par']} (alone {p['solo']}), {len(p['pawns'])} pawns, "
              f"{len(p['bombs'])} bombs", file=sys.stderr)
        e = {"name": name, "size": n, "queen": list(p["queen"]), "best": list(p["best"]),
             "pawns": packs.flat(p["pawns"]), "blocks": packs.flat(p["blocks"]), "bombs": packs.flat(p["bombs"]),
             "par": p["par"], "tip": tip, "ice": ice, "line": packs.flat(p["line"]), "mirror": list(FLIP)}
        out.append(rate(e))
    return {"id": "mirror-bombs", "name": "Mirror Bombs (test)", "free": True, "puzzles": out}


def rate(e):
    """Score and tier: Mirror Queen puzzles are difficult, Hard at least (Brandon, Oct 30; sized.MIRROR_FLOOR)"""
    import sized, pipeline
    e["tricks"] = ["mirror"] + (["portal"] if e.get("portals") else []) + (["ramp"] if e.get("ramps") else []) \
        + (["bomb"] if e.get("bombs") else [])
    e["lured"] = 1  # every one is built so the greedy player misses par
    e["score"] = sized.score_entry(e)
    e["tier"] = pipeline.tier(e["score"])
    return e


def build(seed=1):
    rng = random.Random(seed)
    out = []
    for name, tip, n, ice, nb, k, np_, ds, rem, given, pr in PLAN:
        p = None
        while p is None:
            p = make(rng, n, ice, nb, k, np_, desync=ds, remote=rem, given=given, par_range=pr)
            if p is None:
                print(f"  {name}: retrying", file=sys.stderr)
        print(f"{name}: {n}³{' ice' if ice else ''} par {p['par']} (alone {p['solo']}), {len(p['pawns'])} pawns, "
              f"mirror sweeps {p['mirror_share']:.0%}, out of step {p['desync']}, remote {p['remote']}", file=sys.stderr)
        out.append({
            "name": name, "size": n, "queen": list(p["queen"]) if p["queen"] else [], "best": list(p["best"]),
            "pawns": packs.flat(p["pawns"]), "blocks": packs.flat(p["blocks"]), "par": p["par"], "tip": tip,
            "ice": ice, "line": packs.flat(p["line"]), "mirror": list(FLIP),
        })
        rate(out[-1])
    return {"id": "mirror-test", "name": "Mirror (test)", "free": True, "puzzles": out}


def build_mix(seed=1):
    rng = random.Random(seed)
    out = []
    for name, tip, n, ice, nb, k, np_, want, given, pr in MIX:
        p = None
        while p is None:
            p = make(rng, n, ice, nb, k, np_, given=given, par_range=pr, want=want, tries=1500)
            if p is None:
                print(f"  {name}: retrying", file=sys.stderr)
        print(f"{name}: {n}³{' ice' if ice else ''} {want} par {p['par']} (alone {p['solo']}), {len(p['pawns'])} pawns, "
              f"mirror sweeps {p['mirror_share']:.0%}, out of step {p['desync']}, remote {p['remote']}", file=sys.stderr)
        e = {"name": name, "size": n, "queen": list(p["queen"]) if p["queen"] else [], "best": list(p["best"]),
             "pawns": packs.flat(p["pawns"]), "blocks": packs.flat(p["blocks"]), "par": p["par"], "tip": tip,
             "ice": ice, "line": packs.flat(p["line"]), "mirror": list(FLIP)}
        if p["portals"]:
            e["portals"] = p["portals"]
        if p["ramps"]:
            e["ramps"] = p["ramps"]
        out.append(rate(e))
    return {"id": "mirror-mix", "name": "Mirror Mix (test)", "free": True, "puzzles": out}


if __name__ == "__main__":
    kind = sys.argv[1] if len(sys.argv) > 1 and sys.argv[1] in ("mix", "bombs") else None
    args = sys.argv[2:] if kind else sys.argv[1:]
    seed = int(args[0]) if args else 1
    path = packs.write({"mix": build_mix, "bombs": build_bombs, None: build}[kind](seed))
    problems = packs.check(path)
    print(path, "OK" if not problems else "\n".join(problems))

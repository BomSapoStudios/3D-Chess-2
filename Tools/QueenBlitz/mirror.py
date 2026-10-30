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
    """k random moves of the pair from start: [(queen, mirror, queen's sweep squares, mirror's)]"""
    q, m = start, b.reflect(start)
    steps = []
    for _ in range(k):
        opts = []
        for d0 in DIRS:
            stops = b._slide(q, d0, m)
            if b.ice:
                stops = stops[-1:]
            for to, _, kk in stops:
                m2, _ = b.mirror_follow(m, d0, kk, to)
                opts.append((to, m2, d0, kk))
        if not opts:
            return None
        to, m2, d0, kk = rng.choice(opts)
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


def make(rng, n, ice, nblocks, k, npawns, share=0.3, desync=True, remote=False, given=True, par_range=(3, 5)):
    cells = [(x, y, z) for x in range(n) for y in range(n) for z in range(n)]
    for _ in range(4000):
        blocks = set(rng.sample(cells, nblocks))
        b0 = Board(n, [], blocks, ice, mirror=FLIP)
        starts = b0.starts()
        if not starts:
            continue
        start = rng.choice(starts)
        steps = walk(b0, start, k, rng)
        if not steps:
            continue
        # pawns on the squares the moves sweep, at least one new pawn a move
        pawns = set()
        occupied = {start, b0.reflect(start)}
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
            b = Board(n, pawns, blocks, ice, mirror=FLIP)
            par, line, s = b.solve(start if given else None, limit=par_range[1])
            if par is None or par < par_range[0]:
                continue
            if not given and len(b.optimal_starts(par)) > 3:
                continue
            st = s
            solo = Board(n, pawns, blocks, ice).solve(st, limit=8)[0]
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
            return {"size": n, "queen": st if given else None, "best": st, "pawns": pawns, "blocks": sorted(blocks),
                    "ice": ice, "par": par, "line": [st] + [c for _, c in line], "solo": solo,
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


def rate(e):
    """Score and tier: Mirror Queen puzzles are difficult, Hard at least (Brandon, Oct 30; sized.MIRROR_FLOOR)"""
    import sized, pipeline
    e["tricks"], e["lured"] = ["mirror"], 1  # every one is built so the greedy player misses par
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


if __name__ == "__main__":
    path = packs.write(build(int(sys.argv[1]) if len(sys.argv) > 1 else 1))
    problems = packs.check(path)
    print(path, "OK" if not problems else "\n".join(problems))

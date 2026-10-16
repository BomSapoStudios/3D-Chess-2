"""Search for Queen Blitz puzzles: plant pawns along a hidden queen route, then keep the ones
whose true par matches the route and where greedy play (biggest sweep first) falls short."""
import random, sys, time
from solver import Board, DIRS


def route_puzzle(n, moves, blockers, rng, per_move=(1, 3), ice=False):
    cells = [(x, y, z) for x in range(n) for y in range(n) for z in range(n)]
    blocks = set(rng.sample(cells, blockers))
    free = [c for c in cells if c not in blocks]
    at = rng.choice(free)
    start = at
    pawns = set()
    for _ in range(moves):
        options = []
        for d in DIRS:
            path, c = [], at
            while True:
                c = (c[0] + d[0], c[1] + d[1], c[2] + d[2])
                if not all(0 <= v < n for v in c) or c in blocks:
                    break
                path.append(c)
            if len(path) >= 2:
                options.append(path)
        if not options:
            return None
        path = rng.choice(options)
        k = len(path) if ice else (rng.randint(max(2, per_move[0] + 1), len(path)) if len(path) > 2 else len(path))
        path = path[:k]
        new = [c for c in path if c != start and c not in pawns]
        if not new:
            return None
        take = rng.sample(new, min(len(new), rng.randint(*per_move)))
        pawns.update(take)
        at = path[-1]
    pawns.discard(start)
    return start, sorted(pawns), sorted(blocks)


def evaluate(n, start, pawns, blocks, given, ice=False):
    b = Board(n, pawns, blocks, ice)
    par, line, s = b.solve(start if given else None, limit=7)
    if par is None:
        return None
    greedy = b.greedy(start if given else None)
    return dict(par=par, greedy=greedy, line=line, start=s, board=b)


if __name__ == "__main__":
    n, moves, blockers, given, tries, seed = int(sys.argv[1]), int(sys.argv[2]), int(sys.argv[3]), sys.argv[4] == "given", int(sys.argv[5]), int(sys.argv[6])
    ice = len(sys.argv) > 7 and sys.argv[7] == "ice"
    rng = random.Random(seed)
    found = 0
    t0 = time.time()
    for i in range(tries):
        made = route_puzzle(n, moves, blockers, rng, ice=ice)
        if not made:
            continue
        start, pawns, blocks = made
        e = evaluate(n, start, pawns, blocks, given, ice)
        if not e or e["par"] < moves or e["greedy"] - e["par"] < 1:
            continue
        starts = len(e["board"].optimal_starts(e["par"])) if not given else 1
        print(f"#{i} par={e['par']} greedy={e['greedy']} pawns={len(pawns)} optimalStarts={starts} start={start} pawns={pawns} blocks={blocks} line={e['line']}", flush=True)
        found += 1
        if found >= 6:
            break
    print("time", round(time.time() - t0, 1), file=sys.stderr)

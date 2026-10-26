"""Packs that climb in difficulty (Brandon, Oct 26): easy and medium puzzles use exactly one
trick, hard ones combine two or more. A trick only counts when it actually fools the shortcut
player it's aimed at (players.py):

    greed      the biggest sweep is a dead end               (beats the greedy player)
    bait       riding the long visible row loses              (beats the liner)
    stop       par needs a stop on an empty square           (beats the stopper)
    quiet      par needs a move that takes no pawn           (beats the eager player)
    diagonal   par needs a 3D diagonal                       (beats the flat player)
    cold       the squares next to the pawns are the wrong start (beats the placer)
    walls      blocks that force a different route (no ice)  (removing one changes par or openings)

    python3 ladder.py gen SECONDS SEED OUT.json SIZES PARS         # candidates with their tricks
    python3 ladder.py assemble ID "Name" NAMES.txt OUT.json SIZE_PLAN CANDIDATES.json...
        SIZE_PLAN: one size per chapter, e.g. 4,6,6,6,6 or 5,5,5,5,5
"""
import json, random, sys, time
from multiprocessing import Pool
from solver import Board
import engine, packs, pipeline, players, tricky

TRICKS = {"greed": "greedy", "bait": "liner", "stop": "stopper", "quiet": "eager", "diagonal": "flat", "cold": "placer"}
TIPS = {
    "greed": "The biggest sweep isn't always the best one.",
    "bait": "Don't ride the obvious row.",
    "stop": "Sometimes the best stop has no pawn on it.",
    "quiet": "Sometimes a move that takes nothing sets up everything.",
    "diagonal": "Think in three dimensions.",
    "cold": "Where you start matters more than it looks.",
    "walls": "The blocks are there for a reason.",
}
MAKERS = ["fork", "wander", "bait", "walls", "blocks", "plain"]


def plain(n, par, rng):
    return tricky._any_route(n, par, rng)


def tricks_of(p, r, walls):
    out = [t for t, who in TRICKS.items() if who in r and r[who] > p["par"]]
    if walls:
        out.append("walls")
    return sorted(out)


def attempt(job):
    try:
        return _attempt(job)
    except engine.TooBig:
        return None


def _attempt(job):
    n, par, kind, seed = job
    rng = random.Random(seed)
    lo = 4 if par <= 2 else par + 2
    for _ in range(40):
        made = plain(n, par, rng) if kind == "plain" else engine.MAKERS[kind](n, par, rng)
        if made and lo <= len(made[1]) <= 3 * par + 2:
            break
    else:
        return None
    start, pawns, blocks = made
    b = Board(n, pawns, blocks)
    if engine.fewest(b, [start], par) != par:
        return None
    free_par = engine.fewest(b, b.starts(), par)
    given = free_par < par or rng.random() < 0.6
    queen = start if given else None
    if given:
        starts = [queen]
        if len(tricky.par_first_moves(b, starts, par)) > 3:
            return None
    else:
        starts = []
        for s in b.starts():
            if engine.fewest(b, [s], par, budget=60000) == par:
                starts.append(s)
                if len(starts) > 4:
                    return None
    walls = engine.working_blocks(n, queen, pawns, blocks, False, par, starts)
    entry = packs.solved("", n, queen, pawns, walls, "", False)
    r = players.assess(entry)
    found = tricks_of(entry, r, bool(walls))
    if not found and par > 2:
        return None
    m = pipeline.measure(n, False, queen, pawns, walls)
    entry.update(kind=kind, tricks=found, lured=players.lured(entry, r), trick_score=players.score(entry, r),
                 grade=pipeline.grade(m),
                 players={k: (v - par if not k.endswith("_kept") else v) for k, v in r.items()})
    return entry


def gen(seconds, seed, out, sizes, pars):
    rng = random.Random(seed)
    found, seen, tried = [], set(), 0
    end = time.time() + seconds
    with Pool(4) as pool:
        while time.time() < end:
            batch = [(rng.choice(sizes), rng.choice(pars), rng.choice(MAKERS), rng.randrange(1 << 30)) for _ in range(16)]
            for e in pool.imap_unordered(attempt, batch):
                tried += 1
                if not e:
                    continue
                key = pipeline.layout_key(e)
                if key in seen:
                    continue
                seen.add(key)
                found.append(e)
                json.dump(found, open(out, "w"))
                print(f"[{tried}] {e['size']}³ par {e['par']} {'given' if e['queen'] else 'place'} "
                      f"tricks {','.join(e['tricks']) or '-'} lured {e['lured']} grade {e['grade']}", flush=True)
    print(f"done: {len(found)} kept of {tried} tried", flush=True)


# Each chapter: (slots as (min tricks, max tricks, pars), ...). Ten slots per chapter.
CHAPTERS = [
    [(0, 1, (2,))] * 2 + [(1, 1, (3,))] * 8,                              # 1: warm-ups, then one trick, 3 moves
    [(1, 1, (3,))] * 4 + [(1, 1, (4,))] * 6,                              # 2: one trick, 3-4 moves
    [(1, 1, (4,))] * 4 + [(1, 1, (5,))] * 2 + [(2, 2, (4,))] * 4,         # 3: one trick at length, first combos
    [(2, 2, (4, 5))] * 6 + [(3, 9, (4, 5))] * 4,                          # 4: two tricks, then three
    [(2, 9, (5, 6))] * 4 + [(3, 9, (5, 6))] * 6,                          # 5: combos at length; the end beats every lure
]


def difficulty(p):
    return len(p["tricks"]) * 10 + p["lured"] * 6 + p["par"] * 4 + p["trick_score"]


def assemble(cands, sizes):
    pool = sorted(cands, key=difficulty)
    chosen, used = [], set()
    for c, slots in enumerate(CHAPTERS):
        picks = []
        for i, (lo, hi, pars) in enumerate(slots):
            last_in_pack = c == len(CHAPTERS) - 1 and i >= len(slots) - 3
            fits = [p for p in pool if id(p) not in used and p["size"] == sizes[c] and p["par"] in pars
                    and lo <= len(p["tricks"]) <= hi and (not last_in_pack or p["lured"] == 3)]
            if not fits:
                # Nearest fallback: same size, any par in reach, closest trick count
                fits = [p for p in pool if id(p) not in used and p["size"] == sizes[c] and p["par"] >= min(pars) - 1
                        and len(p["tricks"]) >= lo]
            if not fits:
                raise SystemExit(f"chapter {c + 1} slot {i + 1}: no candidate ({lo}-{hi} tricks, par {pars}, {sizes[c]}³)")
            # Prefer a trick (single-trick slots) or recipe the chapter hasn't used, and a new shape
            have = [t for q in picks for t in q["tricks"]]
            shapes = [pipeline.shape(q) for q in picks]
            best = min(fits, key=lambda p: (sum(have.count(t) for t in p["tricks"]) * 3
                                            + (4 if pipeline.shape(p) in shapes else 0)
                                            + (2 if p["queen"] and sum(bool(q["queen"]) for q in picks) > i * 0.7 else 0),
                                            difficulty(p)))
            used.add(id(best))
            picks.append(best)
        warm = picks[:2] if c == 0 else []
        rest = sorted(picks[len(warm):], key=difficulty)
        chosen += warm + rest
    for p in chosen:
        p["tip"] = TIPS[p["tricks"][0]] if len(p["tricks"]) == 1 else ("Two tricks at once." if len(p["tricks"]) == 2 else "Nothing here is what it seems." if p["tricks"] else "")
    return chosen


if __name__ == "__main__":
    cmd = sys.argv[1]
    if cmd == "gen":
        gen(float(sys.argv[2]), int(sys.argv[3]), sys.argv[4],
            tuple(int(x) for x in sys.argv[5].split(",")), tuple(int(x) for x in sys.argv[6].split(",")))
    elif cmd == "assemble":
        pid, name, names_path, out, plan = sys.argv[2:7]
        sizes = [int(x) for x in plan.split(",")]
        cands = []
        for f in sys.argv[7:]:
            cands += json.load(open(f))
        chosen = assemble(cands, sizes)
        names = [l.strip() for l in open(names_path, encoding="utf-8") if l.strip()]
        for i, p in enumerate(chosen):
            p["name"] = names[i] if i < len(names) else f"Puzzle {i + 1}"
        print(packs.write({"id": pid, "name": name, "free": True, "puzzles": chosen}, out))
        for i, p in enumerate(chosen, 1):
            print(f"{i:3} ch{(i - 1) // 10 + 1} {p['name']:18} {p['size']}³ par {p['par']} {'given' if p['queen'] else 'place'} "
                  f"lured {p['lured']} tricks {','.join(p['tricks']) or '-'}")

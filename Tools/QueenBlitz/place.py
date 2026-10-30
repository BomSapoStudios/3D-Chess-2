"""Place-the-queen versions of given-queen candidates (Oct 29: placing her yourself doubled
Brandon's solve times, and the free packs had only 2-4 such puzzles in 50).

A given-queen puzzle becomes a place-the-queen one only when no start beats its par (Brandon's
rule: if choosing the start could beat the intended route, the queen must be given) and at most
3 starts reach par, so finding the start is part of the puzzle.

    python3 place.py OUT.json CANDIDATES.json...
    python3 place.py decoys OUT.json CANDIDATES.json...   # versions with 1-2 proven decoy blocks
"""
import json, sys
from multiprocessing import Pool
from solver import Board
import engine, packs, players


def placed(p):
    if not p.get("queen") or p.get("portals") or p.get("ramps") or p["par"] < 4:
        return None
    try:
        n, ice, par = p["size"], p.get("ice", False), p["par"]
        pawns, blocks = packs.triples(p["pawns"]), packs.triples(p["blocks"])
        b = Board(n, pawns, blocks, ice)
        if engine.fewest(b, b.starts(), par) != par:
            return None
        starts = []
        for s in b.starts():
            if engine.fewest(b, [s], par, budget=60000) == par:
                starts.append(s)
                if len(starts) > 3:
                    return None
    except engine.TooBig:
        return None
    e = packs.solved("", n, None, pawns, blocks, "", ice)
    r = players.assess(e)
    out = dict(p, queen=[], best=e["best"], line=e["line"], lured=players.lured(e, r), trick_score=players.score(e, r),
               par_starts=len(starts))
    if "tricks" in out and "cold" not in out["tricks"] and "placer" in r and r["placer"] > par:
        out["tricks"] = sorted(out["tricks"] + ["cold"])
    return out


def decoyed(p):
    """The same puzzle with 1-2 decoy blocks that change neither par nor the openings"""
    if not p.get("queen") or p.get("portals") or p.get("ramps") or p.get("decoys") or p["par"] < 5:
        return None
    import random
    rng = random.Random(len(p["pawns"]) * 7919 + p["par"])
    n, ice, par = p["size"], p.get("ice", False), p["par"]
    pawns, blocks, queen = packs.triples(p["pawns"]), packs.triples(p["blocks"]), tuple(p["queen"])
    try:
        d = engine.add_decoys(n, queen, pawns, blocks, ice, par, rng, rng.choice((1, 2)))
    except engine.TooBig:
        return None
    if not d:
        return None
    return dict(p, blocks=p["blocks"] + packs.flat(d), decoys=packs.flat(d))


def main_decoys(out, files):
    cands = [p for f in files for p in json.load(open(f))]
    with Pool(4) as pool:
        made = [e for e in pool.map(decoyed, cands, chunksize=1) if e]
    json.dump(made, open(out, "w"))
    print(f"{len(made)} decoy versions of {len(cands)} candidates")


if __name__ == "__main__" and sys.argv[1] == "decoys":
    main_decoys(sys.argv[2], sys.argv[3:])
elif __name__ == "__main__":
    cands = [p for f in sys.argv[2:] for p in json.load(open(f))]
    with Pool(4) as pool:
        out = [e for e in pool.map(placed, cands, chunksize=1) if e]
    json.dump(out, open(sys.argv[1], "w"))
    print(f"{len(out)} place-the-queen versions of {len(cands)} candidates")

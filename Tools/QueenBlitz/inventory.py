"""Solution inventory: one saved record per Queen Blitz puzzle (every pack and every daily) with the
mechanics it uses, the par route move by move, and flags worked out once, so reviews are lookups
instead of re-solving everything.

Files: Tools/QueenBlitz/inventory/<pack id>.json and daily.json (not shipped in the app).
Each record carries a fingerprint of the puzzle; a refresh only re-solves puzzles that changed.

    python3 inventory.py refresh [pack.json ... | daily]   # rebuild changed records (default: all)
    python3 inventory.py summary                           # counts per pack
    python3 inventory.py find doubles_forced               # puzzles where a flag is true
    python3 inventory.py find opener=up-diag               # or field=value
    python3 inventory.py csv out.csv                       # everything, one row per puzzle
    python3 inventory.py cleanlines pack.json ...          # swap avoidable doubling-back routes for clean ones

Record fields
  pack, n, name, size, par, tier, score, tricks, lured
  mechanics       ice / blocks / portals / ramps / bombs / place_bombs / mirror / place_queen
  start           the queen's start in the shown route (given, or best when the player places her)
  par_starts      how many starts reach par (1 when the queen is given)
  route           per move: from, to, dir, steps, kind (axis/flat-diag/3d-diag), swept, total so far,
                  portal (went through one), ramp (rode one), mirror (the mirror queen's square after)
  opener          shape of the first move, e.g. "axis", "up-diag", "flat-diag"
  doubles_back    edges of board the shown route rides a second time (0 = never)
  reversal        the shown route goes straight back the way the previous move came
  doubles_forced  every par route doubles back (no clean route exists)
  clean_route     a par route that never doubles back, when the shown one does and one exists
"""
import csv, glob, hashlib, json, multiprocessing, os, sys
from packs import APP_DIR, board, triples
import backtrack

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(HERE, "inventory")
VERSION = 2  # bump when the record format or a flag's meaning changes: everything re-solves


def fingerprint(p):
    keep = {k: p.get(k) for k in ("size", "queen", "pawns", "blocks", "ice", "portals", "ramps", "mirror", "bombs",
                                  "placeBombs", "bestBombs", "par", "line")}
    return hashlib.sha1(json.dumps([VERSION, keep], sort_keys=True).encode()).hexdigest()[:12]


def kind(d):
    k = sum(1 for v in d if v)
    return ["", "axis", "flat-diag", "3d-diag"][k]


def opener(d):
    up = d[1]
    base = kind(d)
    if base == "axis":
        return "up" if up > 0 else "down" if up < 0 else "axis"
    return ("up-" if up > 0 else "down-" if up < 0 else "") + base


def route(b, p):
    line = triples(p["line"])
    left, state = b.full, b._begin(line[0])
    out, total = [], 0
    for c in line[1:]:
        if b.mirror:
            cand = [(to, mask, d0, k, m2) for to, m2, mask, d0, k in b.pair_moves(*state) if to == c]
        else:
            cand = [(to, mask, d0, k, None) for to, mask, d0, k in b.moves(state) if to == c]
        to, mask, d0, k, m2 = max(cand, key=lambda m: bin(left & b.pawn_mask & ~b.after(left, m[1])).count("1"))
        nl = b.after(left, mask)
        swept = bin(left & b.pawn_mask & ~nl).count("1")
        total += swept
        q = b.queen(state)
        cells = []
        for stop, passed, _ in b._path(q, d0, state[1] if b.mirror else None):
            cells += passed
            if stop == to:
                break
        out.append({"from": list(q), "to": list(to), "dir": list(d0), "steps": k, "kind": kind(d0), "swept": swept,
                    "total": total, "portal": any(x in b.portal for x in cells), "ramp": any(x in b.ramp for x in cells),
                    "mirror": list(m2) if m2 else None})
        state, left = ((to, m2) if b.mirror else to), nl
    return out


def record(pack_id, n, p):
    b = board(p)
    r = backtrack.analyse(p)
    placed = triples(p.get("bestBombs") or [])
    if placed:
        b = b.with_bombs(b.bombs + placed)
    rt = route(b, p)
    return {
        "pack": pack_id, "n": n, "name": p["name"], "fp": fingerprint(p), "size": p["size"], "par": p["par"],
        "tier": p.get("tier"), "score": p.get("score"), "tricks": p.get("tricks") or p.get("tags") or [], "lured": p.get("lured"),
        "mechanics": {"ice": bool(p.get("ice")), "blocks": len(p.get("blocks") or []) // 3,
                      "portals": len(p.get("portals") or []) // 13, "ramps": len(p.get("ramps") or []) // 6,
                      "bombs": len(p.get("bombs") or []) // 3, "place_bombs": p.get("placeBombs", 0),
                      "mirror": bool(p.get("mirror")), "place_queen": not p["queen"]},
        "start": p["line"][:3], "par_starts": r["par_starts"], "route": rt, "opener": opener(rt[0]["dir"]) if rt else None,
        "doubles_back": r["edges"], "reversal": r["reverse"], "doubles_forced": r["forced"],
        "search_gave_up": r["unknown"], "clean_route": r["clean"],
    }


def sources(args):
    files = [a for a in args if a != "daily"] or (sorted(glob.glob(os.path.join(APP_DIR, "*.json"))) if not args else [])
    for f in files:
        pack = json.load(open(f, encoding="utf-8"))
        yield pack["id"], pack["puzzles"]
    if not args or "daily" in args:
        ds = sorted(glob.glob(os.path.join(backtrack.DAILY_DIR, "*.json")))
        yield "daily", [dict(json.load(open(x)), name=os.path.basename(x)[:-5]) for x in ds]


def load(pid):
    path = os.path.join(OUT_DIR, pid + ".json")
    return json.load(open(path)) if os.path.exists(path) else []


def refresh(args):
    os.makedirs(OUT_DIR, exist_ok=True)
    for pid, puzzles in sources(args):
        old = {r["fp"]: r for r in load(pid)}
        todo = [(pid, i, p) for i, p in enumerate(puzzles, 1) if fingerprint(p) not in old]
        with multiprocessing.Pool() as pool:  # puzzles solve independently: use every core
            fresh = {r["n"]: r for r in pool.starmap(record, todo, chunksize=1)}
        recs = [fresh.get(i) or dict(old[fingerprint(p)], n=i, name=p["name"]) for i, p in enumerate(puzzles, 1)]
        redone = len(fresh)
        with open(os.path.join(OUT_DIR, pid + ".json"), "w") as f:
            f.write("[\n" + ",\n".join(json.dumps(r, separators=(",", ":")) for r in recs) + "\n]\n")
        print(f"{pid}: {len(recs)} puzzles, {redone} re-solved", flush=True)


def every():
    for f in sorted(glob.glob(os.path.join(OUT_DIR, "*.json"))):
        yield from json.load(open(f))


def summary():
    by = {}
    for r in every():
        by.setdefault(r["pack"], []).append(r)
    print(f"{'pack':<14}{'puzzles':>8}{'doubles back':>14}{'reversal':>10}{'forced':>8}")
    for pid, rs in by.items():
        print(f"{pid:<14}{len(rs):>8}{sum(r['doubles_back'] > 0 for r in rs):>14}{sum(r['reversal'] for r in rs):>10}"
              f"{sum(r['doubles_forced'] for r in rs):>8}")


def find(expr):
    key, _, want = expr.partition("=")
    for r in every():
        v = r.get(key, r["mechanics"].get(key))
        if (str(v) == want) if want else bool(v):
            print(f"{r['pack']:<14}#{r['n']:<4}{r['name'][:24]:<24} par {r['par']}  {r['opener']:<12} "
                  f"back {r['doubles_back']}{'  FORCED' if r['doubles_forced'] else ''}")


def to_csv(path):
    rows = list(every())
    with open(path, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["pack", "n", "name", "size", "par", "tier", "tricks", "ice", "blocks", "portals", "ramps", "bombs",
                    "mirror", "place_queen", "par_starts", "opener", "route", "doubles_back", "reversal", "doubles_forced"])
        for r in rows:
            m = r["mechanics"]
            w.writerow([r["pack"], r["n"], r["name"], r["size"], r["par"], r["tier"] or "", " ".join(r["tricks"]),
                        m["ice"], m["blocks"], m["portals"], m["ramps"], m["bombs"], m["mirror"], m["place_queen"],
                        r["par_starts"], r["opener"],
                        " ".join(f"{s['kind']}{s['steps']}:{s['swept']}" for s in r["route"]),
                        r["doubles_back"], r["reversal"], r["doubles_forced"]])


def clean_lines(files):
    """Swap each shown route that doubles back for the inventory's clean par route (the puzzle itself
    doesn't change; BlitzDemo, Auto-place's start and hints follow the new line). Re-checks the pack."""
    import packs
    for f in files:
        pack = json.load(open(f, encoding="utf-8"))
        recs = {r["n"]: r for r in load(pack["id"])}
        changed = 0
        for i, p in enumerate(pack["puzzles"], 1):
            r = recs.get(i)
            if r and r["fp"] == fingerprint(p) and r["clean_route"]:
                p["line"] = [v for c in r["clean_route"] for v in c]
                if not p["queen"]:
                    p["best"] = list(r["clean_route"][0])
                changed += 1
        packs.write(pack, f)
        problems = packs.check(f)
        print(f"{pack['id']}: {changed} routes swapped, check {'OK' if not problems else problems}")


if __name__ == "__main__":
    cmd, args = (sys.argv[1] if len(sys.argv) > 1 else "summary"), sys.argv[2:]
    {"refresh": lambda: refresh(args), "summary": summary, "find": lambda: find(args[0]), "csv": lambda: to_csv(args[0]),
     "cleanlines": lambda: clean_lines(args)}[cmd]()

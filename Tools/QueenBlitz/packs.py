"""Puzzle packs as the app reads them: one JSON file per pack in Assets/Resources/QueenBlitz.

Squares are flat x,y,z lists: "pawns": [1,1,1, 2,1,1] is two pawns. "queen" is empty when the
player places her; "best" is a start that reaches par (Auto-place uses it); "line" is the queen's
start then every square she stops on in one par solution (the in-app BlitzDemo plays it).

Run this file to re-solve every puzzle in every pack and check its par, best and line.
"""
import glob, json, os, sys
from solver import Board

APP_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Assets", "Resources", "QueenBlitz")


def flat(cells):
    return [v for c in cells for v in c]


def triples(values):
    return [tuple(values[i:i + 3]) for i in range(0, len(values), 3)]


def solved(name, n, start, pawns, blocks, tip, ice=False):
    """One puzzle as a pack entry, with its par, best start and solution line from the solver"""
    b = Board(n, pawns, blocks, ice)
    par, line, s = b.solve(start, limit=8)
    if par is None:
        raise ValueError(f"{name}: no solution in 8 moves")
    return {
        "name": name, "size": n, "queen": list(start) if start else [], "best": list(s),
        "pawns": flat(pawns), "blocks": flat(blocks), "par": par, "tip": tip, "ice": bool(ice),
        "line": flat([s] + [to for _, to in line]),
    }


def write(pack, path=None):
    """Pretty enough to read and diff: one line per field of each puzzle"""
    path = path or os.path.join(APP_DIR, pack["id"] + ".json")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    head = {k: v for k, v in pack.items() if k != "puzzles"}
    lines = ["{"] + [f"  {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}," for k, v in head.items()] + ['  "puzzles": [']
    for i, p in enumerate(pack["puzzles"]):
        body = ",\n".join(f"      {json.dumps(k)}: {json.dumps(v, ensure_ascii=False)}" for k, v in p.items())
        lines.append("    {\n" + body + "\n    }" + ("," if i < len(pack["puzzles"]) - 1 else ""))
    lines += ["  ]", "}"]
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(lines) + "\n")
    return os.path.normpath(path)


def export(pack_id, name, free, puzzles, path=None):
    """puzzles: (name, size, queen start or None, pawns, blocks, tip, ice?) tuples"""
    entries = [solved(p[0], p[1], p[2], p[3], p[4], p[5], bool(p[6]) if len(p) > 6 else False) for p in puzzles]
    return write({"id": pack_id, "name": name, "free": free, "puzzles": entries}, path)


def board(p):
    """The solver Board for a pack entry, with its portals and ramps"""
    v = p.get("portals") or []
    portals = [(tuple(v[i:i + 3]), tuple(v[i + 3:i + 6]), tuple(v[i + 6:i + 9]), tuple(v[i + 9:i + 12]), bool(v[i + 12]))
               for i in range(0, len(v), 13)]
    r = p.get("ramps") or []
    ramps = {}
    for i in range(0, len(r), 6):
        ramps.setdefault(tuple(r[i:i + 3]), []).append(tuple(r[i + 3:i + 6]))
    return Board(p["size"], triples(p["pawns"]), triples(p["blocks"]), p.get("ice", False), portals, ramps)


def check(path):
    """Re-solve every puzzle in a pack file. Returns a list of problems (empty means all good)."""
    pack = json.load(open(path, encoding="utf-8"))
    problems = []
    for i, p in enumerate(pack["puzzles"], 1):
        n, ice = p["size"], p.get("ice", False)
        start = tuple(p["queen"]) if p["queen"] else None
        b = board(p)
        par = b.solve(start, limit=8)[0]
        tag = f"{pack['id']} {i} {p['name']}"
        if par != p["par"]:
            problems.append(f"{tag}: par is {par}, file says {p['par']}")
            continue
        if b.solve(tuple(p["best"]), limit=par)[0] != par:
            problems.append(f"{tag}: best start {p['best']} doesn't reach par")
        line = triples(p["line"])
        if start and line[0] != start:
            problems.append(f"{tag}: line doesn't start on the queen")
        left = b.full
        for a, c in zip(line, line[1:]):
            move = [m for m in b.moves(a) if m[0] == c]
            if not move:
                problems.append(f"{tag}: {a} > {c} isn't a legal move")
                break
            # A tap picks a square, not a route: like the app, take the biggest sweep there
            left &= ~max(move, key=lambda m: bin(m[1]).count("1"))[1]
        if left or len(line) - 1 != par:
            problems.append(f"{tag}: line doesn't clear the board in par")
    return problems


if __name__ == "__main__":
    files = sys.argv[1:] or sorted(glob.glob(os.path.join(APP_DIR, "*.json")))
    bad = 0
    for f in files:
        problems = check(f)
        bad += len(problems)
        print(f"{os.path.basename(f)}: {'OK' if not problems else ''}")
        for p in problems:
            print("  " + p)
    sys.exit(1 if bad else 0)

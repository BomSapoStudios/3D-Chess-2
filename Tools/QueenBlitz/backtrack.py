"""Doubling back: how many Queen Blitz par solutions ride back over a line the queen already travelled.

A move "doubles back" when it runs along a stretch of the board (one step between two neighbouring
squares) that an earlier move already ran along, either way. Crossing an old line at one square
doesn't count. "Reverse" is the sharpest kind: straight back the way the last move came.

For each puzzle:
  shown   - the pack's own solution line doubles back
  forced  - EVERY par solution doubles back (from the given queen, or any par start when placing)
  reverse - the shown line has a straight-back reversal

    python3 backtrack.py [pack.json ...]       # default: every pack + the dailies
"""
import glob, json, os, sys
from packs import APP_DIR, board, triples

DAILY_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "server-cf", "public", "daily")
CAP = 200000  # DFS node cap per puzzle


def edges_of(b, a, d0, stop, extra=None):
    """The board edges (unordered neighbour pairs) a move from a in d0 to `stop` runs along"""
    cells = [a]
    for c, passed, _ in b._path(a, d0, extra):
        cells += list(passed)
        if c == stop:
            break
    out = set()
    for x, y in zip(cells, cells[1:]):
        if x in b.portal and y in b.portal:
            continue  # a portal jump, not a ride
        out.add(frozenset((x, y)))
    return out


def options(b, state, left):
    """[(next state, new left, edges, direction)] from a search state. A tap picks a square, not a
    route, so like the app only the biggest sweep to each square counts."""
    out = []
    if b.mirror:
        q, m = state
        for to, m2, mask, d0, k in b.pair_moves(q, m):
            out.append(((to, m2), b.after(left, mask), edges_of(b, q, d0, to, m), d0))
    else:
        for to, mask, d0, k in b.moves(state):
            out.append((to, b.after(left, mask), edges_of(b, state, d0, to), d0))
    best = {}
    for o in out:
        k = b.queen(o[0])
        if k not in best or bin(left & b.pawn_mask & ~o[1]).count("1") > bin(left & b.pawn_mask & ~best[k][1]).count("1"):
            best[k] = o
    return list(best.values())


def shown_line(b, p):
    """Edge sets and directions of the pack's own line, played the way the app plays it"""
    line = triples(p["line"])
    left = b.full
    state = b._begin(line[0])
    moves = []
    for c in line[1:]:
        cand = [o for o in options(b, state, left) if b.queen(o[0]) == c]
        best = max(cand, key=lambda o: bin(left & b.pawn_mask & ~o[1]).count("1"))
        moves.append((best[2], best[3]))
        state, left = best[0], best[1]
    return moves


def doubles(moves):
    """(edges ridden again, a straight-back reversal?) for a list of (edges, direction)"""
    seen, again, rev = set(), 0, False
    for i, (e, d) in enumerate(moves):
        again += len(e & seen)
        if i and e & moves[i - 1][0] and tuple(-v for v in moves[i - 1][1]) == d:
            rev = True
        seen |= e
    return again, rev


def clean_line(b, starts, par):
    """A par solution that never rides an edge twice, as the queen's squares [start, stop, ...];
    False when there is none, None when the search ran out"""
    dist = {}

    def togo(st, left):
        k = (st, left)
        if k not in dist:
            dist[k] = b.solve_from(st, left, par)
        return dist[k]

    nodes = [0]

    def dfs(st, left, rem, seen):
        if left & b.pawn_mask == 0:
            return [b.queen(st)]
        if rem == 0:
            return False
        nodes[0] += 1
        if nodes[0] > CAP:
            raise TimeoutError
        for to, l2, e, _ in options(b, st, left):
            if e & seen:
                continue
            if l2 & b.pawn_mask and (togo(to, l2) or 99) > rem - 1:
                continue
            rest = dfs(to, l2, rem - 1, seen | e)
            if rest:
                return [b.queen(st)] + rest
        return False

    try:
        for s in starts:
            line = dfs(b._begin(s), b.full, par, frozenset())
            if line:
                return line
        return False
    except TimeoutError:
        return None


def analyse(p):
    b = board(p)
    placed = triples(p.get("bestBombs") or [])
    if placed:
        b = b.with_bombs(b.bombs + placed)  # the pack's own bomb spots (players could pick others)
    par = p["par"]
    moves = shown_line(b, p)
    again, rev = doubles(moves)
    if p["queen"]:
        starts = [tuple(p["queen"])]
    else:
        starts = [s for s in b.starts() if b.solve_from(b._begin(s), b.full, par) == par]
    clean = clean_line(b, starts, par) if again else True
    return {"shown": again > 0, "edges": again, "reverse": rev, "forced": clean is False, "unknown": clean is None,
            "par": par, "clean": clean if isinstance(clean, list) else None, "par_starts": len(starts)}


def report(label, puzzles):
    rows = []
    for i, p in enumerate(puzzles, 1):
        r = analyse(p)
        r.update(n=i, name=p["name"], size=p["size"], ice=p.get("ice", False))
        rows.append(r)
    shown = sum(r["shown"] for r in rows)
    forced = sum(r["forced"] for r in rows)
    rev = sum(r["reverse"] for r in rows)
    unk = sum(r["unknown"] for r in rows)
    print(f"{label}: {len(rows)} puzzles | shown line doubles back {shown} | reversal {rev} | every par solution doubles back {forced}"
          + (f" | search gave up {unk}" if unk else ""))
    for r in rows:
        if r["shown"]:
            flag = "FORCED" if r["forced"] else ("?" if r["unknown"] else "avoidable")
            print(f"   #{r['n']:>2} {r['name'][:22]:<22} {r['size']}³{' ice' if r['ice'] else '    '} par {r['par']}  "
                  f"{r['edges']} edge(s) again{' REVERSE' if r['reverse'] else ''}  -> {flag}")
    sys.stdout.flush()
    return rows


if __name__ == "__main__":
    out = {}
    files = sys.argv[1:] or sorted(glob.glob(os.path.join(APP_DIR, "*.json"))) + ["daily"]
    for f in files:
        if f == "daily":
            ds = [json.load(open(x)) for x in sorted(glob.glob(os.path.join(DAILY_DIR, "*.json")))]
            out["daily"] = report("Dailies", ds)
            continue
        pack = json.load(open(f, encoding="utf-8"))
        out[pack["id"]] = report(f"{pack['name']} ({pack['id']})", pack["puzzles"])
    json.dump(out, open(os.environ.get("BACKTRACK_OUT", "/dev/null"), "w"), default=str)

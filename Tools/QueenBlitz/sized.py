"""One 50-puzzle pack per board size, easy to very hard (Brandon, Oct 27: split packs by board
size, an 8x8x8 hard feels harder than a 5x5x5 hard). Every mechanic goes in, introduced in order:

    1  warm-ups, then one trick at a time on an open board
    2  one trick, longer; walls; the first ice boards
    3  regular hards: two and three tricks at once, ice; decoy blocks start here
    4  portals and ramps step back to easy (each needed), then portal brakes and traps
    5  every lure player fails, old and new mechanics mixed; decoy portals; the hardest last

    python3 sized.py ID "Name" NAMES.txt OUT.json SIZE CANDIDATES.json...

Candidates come from ladder.py (one-trick and graded open boards, ice), engine.py (every lure
fails) and portals.py (portals, ramps, brakes).
"""
import json, random, sys
import engine, ladder, packs, pipeline, players, portals

TIERS = ["Easy", "Medium", "Hard", "Hard", "Devious"]


def normalise(e):
    if "tricks" not in e and (e.get("portals") or e.get("ramps")):  # an older portals.py candidate
        r = players.assess(e)
        e.update(lured=players.lured(e, r), trick_score=players.score(e, r),
                 tricks=sorted(t for t, who in portals.TRICKS.items() if who in r and r[who] > e["par"]))
    if "tricks" not in e:  # engine.py: every lure fails
        e["tricks"] = sorted({ladder.ENGINE_TAGS[t] for t in e["tags"] if t in ladder.ENGINE_TAGS})
        e["lured"], e["trick_score"] = 3, e["score"]
    e["tricks"] = ladder.effective(e) if e["tricks"] else []
    if e.get("portals") or e.get("ramps"):
        e["mech"] = "warp"
        e.setdefault("want", "portal")
    else:
        e["mech"] = "ice" if e.get("ice") else "classic"
    e["has_decoys"] = bool(e.get("decoys"))
    return e


def difficulty(p):
    return len(p["tricks"]) * 10 + (p["lured"] or 0) * 6 + p["par"] * 4 + (p["trick_score"] or 0) \
        + (4 if p["mech"] != "classic" else 0) + (3 if not p.get("queen") else 0)


def S(mech, n, pars, tricks=(0, 9), lured=(0, 3), wants=None, ice=None, pick=None):
    """n slots of one kind; pick: take the easiest, middle or hardest that fits (default by chapter)"""
    return [dict(mech=mech, pars=pars, tricks=tricks, lured=lured, wants=wants, ice=ice, pick=pick)] * n


CHAPTERS = [
    S("classic", 2, (2,), (0, 1), (0, 1)) + S("classic", 8, (3,), (1, 1), (0, 1)),
    S("classic", 4, (3, 4), (1, 1), (0, 2)) + S("ice", 2, (3, 4), (0, 2), (0, 2)) + S("classic", 4, (4,), (1, 1), (0, 2)),
    # Regular hards before any new mechanic (Brandon, Oct 27: something to ramp up to)
    S("classic", 4, (4, 5), (2, 2), (1, 3)) + S("ice", 1, (4, 5), (1, 9), (1, 3))
    + S("classic", 3, (5,), (3, 9), (3, 3), pick="mid") + S("ice", 1, (5, 6), (1, 9), (3, 3), pick="mid")
    + S("classic", 1, (5, 6), (2, 9), (3, 3), pick="mid"),
    # Portals and ramps step back to easy, then climb
    S("warp", 2, (3, 4), lured=(0, 1), wants={"portal"}, ice=False, pick="easy")
    + S("warp", 2, (3, 4), lured=(0, 1), wants={"ramp"}, ice=False, pick="easy")
    + S("warp", 1, (4, 5), lured=(0, 2), wants={"both"}, pick="easy") + S("warp", 1, (4, 5), lured=(0, 2), wants={"portal"}, ice=True, pick="easy")
    + S("warp", 1, (4, 5), lured=(0, 2), wants={"brake"}, pick="easy")
    + S("warp", 3, (4, 5), lured=(2, 3), wants={"portal", "ramp", "both"}, pick="mid"),
    S("classic", 2, (6,), (2, 9), (3, 3)) + S("ice", 1, (5, 6), (1, 9), (3, 3))
    + S("warp", 4, (5, 6), lured=(3, 3), wants={"portal", "ramp", "both"}) + S("warp", 1, (5, 6), lured=(2, 3), wants={"brake"})
    + S("classic", 1, (6,), (3, 9), (3, 3)) + S("warp", 1, (6,), lured=(3, 3)),
]


def longer(n, c, i, s):
    """On 5x5x5, one-trick puzzles with 3 moves are over too fast (Brandon, Oct 27: High Five 6-10
    felt too easy). There, chapter 1 goes to 4 moves halfway through and chapter 2 is a move longer."""
    if n != 5 or c > 1 or (c == 0 and i < 5):
        return s
    return dict(s, pars=tuple(p + 1 for p in s["pars"]))


def fits(p, s, c, relax):
    if p["mech"] != s["mech"]:
        return False
    if p["has_decoys"] and c < 2:
        return False  # decoys only late in a pack (Brandon)
    pars = s["pars"] if relax < 2 else tuple(range(min(s["pars"]) - 1, max(s["pars"]) + 2))
    if p["par"] not in pars or (p["par"] == 2 and 2 not in s["pars"]):
        return False  # two-move puzzles are warm-ups only (Brandon)
    if relax < 3 and not s["tricks"][0] <= len(p["tricks"]) <= s["tricks"][1]:
        return False
    if relax < 1 and not s["lured"][0] <= (p["lured"] or 0) <= s["lured"][1]:
        return False
    if s["wants"] and p.get("want") not in s["wants"] and relax < 3:
        return False
    if s["ice"] is not None and p.get("ice", False) != s["ice"] and relax < 3:
        return False
    return True


def assemble(cands, n):
    seen, pool = set(), []
    for p in cands:
        if p["size"] != n:
            continue
        p = normalise(p)
        key = (pipeline.layout_key(p), tuple(p.get("portals") or []), tuple(p.get("ramps") or []))
        if p["size"] == n and key not in seen:
            seen.add(key)
            pool.append(p)
    chosen, used = [], set()
    for c, slots in enumerate(CHAPTERS):
        picks = []
        for i, s in enumerate(slots):
            s = longer(n, c, i, s)
            for relax in range(4):
                flat = sum(q["tricks"] == ["diagonal"] for q in picks)
                # 3D diagonals are in nearly every route, so at most 3 diagonal-only puzzles a chapter
                ok = [p for p in pool if id(p) not in used and fits(p, s, c, relax)
                      and not (relax < 3 and p["tricks"] == ["diagonal"] and flat >= 3)]
                if ok:
                    break
            else:
                raise SystemExit(f"{n}³ chapter {c + 1} slot {i + 1}: no candidate for {s}")
            if relax:
                print(f"  chapter {c + 1} slot {i + 1}: relaxed {relax}", file=sys.stderr)
            have = [t for q in picks for t in q["tricks"]] + [q.get("want") for q in picks]
            shapes = [pipeline.shape(q) for q in picks]
            places = sum(not q.get("queen") for q in picks)
            # Early chapters take the easiest that fit, late ones the hardest; always prefer a trick,
            # mechanic and solution shape the chapter hasn't had yet
            how = s["pick"] or ("easy" if c < 2 else "hard")
            sign = {"easy": 1, "mid": 0, "hard": -1}[how]
            mid = sorted(difficulty(p) for p in ok)[len(ok) // 2]
            best = min(ok, key=lambda p: (sum(have.count(t) for t in p["tricks"] + [p.get("want")]) * 3
                                          + (4 if pipeline.shape(p) in shapes else 0)
                                          + (2 if not p.get("queen") and places >= 3 else 0),
                                          sign * difficulty(p) if sign else abs(difficulty(p) - mid)))
            used.add(id(best))
            picks.append(best)
        warm = picks[:2] if c == 0 else []
        rest = sorted(picks[len(warm):], key=difficulty)
        chosen += [(c, p) for p in warm + rest]
    return chosen


def tip(p, first_seen):
    if p["mech"] == "warp":
        w = "ice" if p.get("ice") and p["want"] != "brake" else p["want"]
        if p.get("decoy_portal"):
            w = "decoy"
        return portals.TIPS[w]
    if p["mech"] == "ice" and "ice" not in first_seen:
        first_seen.add("ice")
        return engine.TIPS["ice"]
    if p["has_decoys"]:
        return "Not every block is in the way."
    t = p["tricks"]
    return ladder.TIPS[t[0]] if len(t) == 1 else "Two tricks at once." if len(t) == 2 else \
        "Nothing here is what it seems." if t else ""


class Names:
    """Names grouped by mechanic so an icy name lands on an ice board: a NAMES file has lines
    "# classic", "# ice", "# warp", each followed by its names"""
    def __init__(self, path):
        self.pools, cur = {}, None
        for line in open(path, encoding="utf-8"):
            line = line.strip()
            if line.startswith("#"):
                cur = self.pools.setdefault(line[1:].strip(), [])
            elif line:
                cur.append(line)

    def pick(self, mech, i):
        pool = self.pools.get(mech) or self.pools.get("classic")
        return pool.pop(0) if pool else f"Puzzle {i + 1}"


def build(pid, name, names, out, n, cands):
    chosen = assemble(cands, n)
    rng = random.Random(n * 1000 + 27)
    # Decoy portals on the two hardest portal puzzles of the last chapter
    late = [k for k, (c, p) in enumerate(chosen) if c == 4 and p["mech"] == "warp" and p.get("want") != "brake"]
    for k in late[-2:]:
        c, p = chosen[k]
        d = portals.add_decoy_portal(dict(p), rng)
        if d:
            d["decoy_portal"] = True
            chosen[k] = (c, d)
    puzzles, first_seen = [], set()
    for i, (c, p) in enumerate(chosen):
        e = {k: v for k, v in p.items() if k in ("size", "queen", "best", "pawns", "blocks", "par", "ice", "line",
                                                    "portals", "ramps")}
        e = {"name": names.pick(p["mech"], i), **e}
        e["tip"] = tip(p, first_seen)
        e["tricks"] = p["tricks"] + ([p["want"]] if p["mech"] == "warp" else []) + (["ice"] if p.get("ice") else []) \
            + (["decoy"] if p["has_decoys"] or p.get("decoy_portal") else [])
        e["lured"] = p["lured"]
        e["score"] = max(1, min(100, int(difficulty(p))))
        e["tier"] = "Warm-up" if i < 2 else TIERS[c]
        puzzles.append(e)
    print(packs.write({"id": pid, "name": name, "free": True, "puzzles": puzzles}, out))
    for i, (e, (c, p)) in enumerate(zip(puzzles, chosen), 1):
        print(f"{i:3} ch{c + 1} {e['name']:16} par {e['par']} {'given' if e['queen'] else 'place'} {p['mech']:7} "
              f"lured {p['lured']} {','.join(e['tricks']) or '-'}")


if __name__ == "__main__":
    pid, name, names_path, out, size = sys.argv[1:6]
    cands = []
    for f in sys.argv[6:]:
        cands += json.load(open(f))
    names = Names(names_path)
    build(pid, name, names, out, int(size), cands)

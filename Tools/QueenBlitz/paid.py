"""The paid Queen Blitz packs (Brandon, Oct 29: four packs for sale, built on what the free packs
taught us). Each pack is one board size and one theme, and starts where the free packs leave off.

What the free packs taught (solve times from Brandon's phone, Oct 28-29, and his playtests):
  - Par predicts time; the number of tricks adds little once par is known. Ice, portals or ramps,
    and placing the queen yourself each multiply the time (see predicted_seconds).
  - Bait and greed bite only sometimes, so they are tie-breakers, not the difficulty itself.
  - Par-2 puzzles are warm-ups only, and a paid pack has none.
  - Openers must vary, and no two puzzles in a pack may share a solution shape.
  - Chapters climb inside themselves and the whole pack climbs; the last puzzle is the hardest.
  - Place-the-queen puzzles about 1 in 4 (the free packs had 2-4 in 50).
  - Decoy blocks only from chapter 3, decoy portals only in the last three puzzles.
  - Tips are per puzzle and rarely repeat; late chapters hint less.

    python3 paid.py PACK OUT.json NAMES.txt CANDIDATES.json...     # PACK: cuberun, deepfreeze, blockade, feast
"""
import json, math, random, sys
import packs, pipeline, portals, sized

# log(seconds) fitted to Brandon's 55 first clears (R² 0.59, rank correlation 0.73)
FIT = dict(base=0.84, par=0.61, ice=0.35, warp=0.42, place=0.73)


def predicted_seconds(p):
    warp = bool(p.get("portals") or p.get("ramps"))
    return math.exp(FIT["base"] + FIT["par"] * p["par"] + FIT["ice"] * bool(p.get("ice")) + FIT["warp"] * warp
                    + FIT["place"] * (not p.get("queen")))


# Each chapter's band of predicted solve times, in seconds. Chapter 1 sits about where the free
# packs' chapter 3 was; the end of chapter 5 is Tricky 3 territory and beyond.
BANDS = [(35, 70), (50, 110), (70, 170), (100, 260), (150, 600)]

# Slot kinds a theme can ask for
KINDS = {
    "classic": lambda p: p["mech"] == "classic",
    "walls": lambda p: p["mech"] == "classic" and ("walls" in p["tricks"] or p["has_decoys"]),
    "ice": lambda p: p["mech"] == "ice",
    "warp": lambda p: p["mech"] == "warp" and not p.get("ice"),
    "warp-ice": lambda p: p["mech"] == "warp" and p.get("ice"),
    "brake": lambda p: p["mech"] == "warp" and p.get("want") == "brake",
    "portal": lambda p: p["mech"] == "warp" and not p.get("ramps") and not p.get("ice"),
    "ramp": lambda p: p["mech"] == "warp" and not p.get("portals") and not p.get("ice"),
    "both": lambda p: p["mech"] == "warp" and p.get("portals") and p.get("ramps"),
    "blocked": lambda p: len(p["blocks"]) >= 3 and p["mech"] != "warp",  # at least one block that matters
    "big": lambda p: len(p["pawns"]) // 3 >= 9 and p["mech"] == "classic",
    "any": lambda p: True,
}


def C(*parts):
    """A chapter: ("kind", count) pairs, 10 slots in all"""
    out = [k for k, n in parts for _ in range(n)]
    assert len(out) == 10, parts
    return out


THEMES = {
    # Portals and ramps from the first puzzle: the free packs introduced them, this pack is about them
    "cuberun": dict(name="Cube Run", size=6, chapters=[
        C(("portal", 5), ("ramp", 4), ("both", 1)),
        C(("portal", 4), ("ramp", 3), ("both", 1), ("warp", 2)),
        C(("warp", 6), ("both", 2), ("warp-ice", 2)),
        C(("warp", 4), ("both", 2), ("warp-ice", 2), ("brake", 2)),
        C(("warp", 4), ("both", 2), ("warp-ice", 2), ("brake", 2)),
    ]),
    # Every board is ice, the biggest difficulty driver; portals as brakes only after regular hards
    "deepfreeze": dict(name="Deep Freeze", size=6, chapters=[
        C(("ice", 10)),
        C(("ice", 10)),
        C(("ice", 10)),
        C(("ice", 5), ("brake", 3), ("warp-ice", 2)),
        C(("ice", 5), ("brake", 2), ("warp-ice", 3)),
    ]),
    # Blocks that matter in every puzzle; decoy blocks from chapter 3, icy brakes late
    "blockade": dict(name="Blockade", size=5, chapters=[
        C(("walls", 10)),
        C(("walls", 10)),
        C(("walls", 8), ("blocked", 2)),
        C(("walls", 6), ("blocked", 2), ("ice", 2)),
        C(("walls", 5), ("blocked", 3), ("ice", 2)),
    ]),
    # The 8x8x8 cube with lots of pawns: long sweeps, long routes
    # (Oct 30: 8x8x8 candidates take minutes each to prove and almost none came out icy, so
    # Feast is classic boards, big ones more often as it goes)
    "feast": dict(name="Feast", size=8, bands=[(25, 50), (40, 80), (50, 100), (70, 140), (90, 200)], chapters=[
        C(("big", 4), ("classic", 6)),
        C(("big", 5), ("classic", 5)),
        C(("big", 6), ("any", 4)),
        C(("big", 6), ("any", 4)),
        C(("big", 7), ("any", 3)),
    ]),
}

PLACE_PER_CHAPTER = (2, 4)


def opener(p):
    cells = packs.triples(p["line"])
    return sum(a != b for a, b in zip(cells[0], cells[1]))  # 1 axis, 2 flat diagonal, 3 3D diagonal


def assemble(cands, theme, rng):
    n = theme["size"]
    seen, pool = set(), []
    for p in cands:
        if p["size"] != n or p["par"] < 3:
            continue
        p = sized.normalise(p)
        # A decoyed or placed version of a layout counts as the same puzzle (place.py)
        decoys = set(packs.triples(p.get("decoys") or []))
        plain = dict(p, blocks=packs.flat([c for c in packs.triples(p["blocks"]) if c not in decoys]))
        key = (pipeline.layout_key(plain), tuple(p.get("portals") or []), tuple(p.get("ramps") or []))
        if (key, bool(p.get("queen")), bool(decoys)) in seen or key in EXISTING:
            continue
        seen.add((key, bool(p.get("queen")), bool(decoys)))
        p["key"], p["secs"] = key, predicted_seconds(p)  # a layout can come given and placed (place.py)
        pool.append(p)
    chosen, used, shapes = [], set(), set()
    for c, kinds in enumerate(theme["chapters"]):
        lo, hi = theme.get("bands", BANDS)[c]
        picks = []
        # Spread the chapter across its band, easiest first
        targets = [lo * (hi / lo) ** (i / 9) for i in range(10)]
        order = list(range(10))
        rng.shuffle(order)  # fill slots in a random order so no kind always gets first pick
        slot = {}
        for i in order:
            kind, target = kinds[i], targets[i]
            places = sum(not q.get("queen") for q in slot.values())
            have = [t for q in slot.values() for t in q["tricks"]]
            openers = [opener(q) for q in slot.values()]

            def cost(p):
                k = abs(math.log(p["secs"] / target)) * 4
                k += sum(have.count(t) for t in p["tricks"]) * 0.6           # tricks the chapter has had
                k += 0.8 * openers.count(opener(p))                           # vary the first move
                if not p.get("queen"):
                    k += 3 if places >= PLACE_PER_CHAPTER[1] else -0.8 if places < PLACE_PER_CHAPTER[0] else 0
                k -= 0.8 * (p.get("lured") or 0) * (c / 4)                     # deceive more as the pack goes on
                return k

            ok = [p for p in pool if p["key"] not in used and pipeline.shape(p) not in shapes and KINDS[kind](p)
                  and (c >= 2 or not p["has_decoys"])]
            if not ok:
                ok = [p for p in pool if p["key"] not in used and KINDS[kind](p) and (c >= 2 or not p["has_decoys"])]
                print(f"  ch{c + 1} slot {i + 1} ({kind}): repeats a shape", file=sys.stderr)
            if not ok:
                raise SystemExit(f"{theme['name']} chapter {c + 1} slot {i + 1}: no {kind} candidate left")
            best = min(ok, key=cost)
            used.add(best["key"])
            shapes.add(pipeline.shape(best))
            slot[i] = best
        picks = sorted(slot.values(), key=lambda p: p["secs"])
        chosen += [(c, p) for p in picks]
    return chosen


# Hints, several ways of saying each so a pack rarely repeats one; chapters 4-5 hint less
HINTS = {
    "greed": ["The biggest sweep isn't always the best one.", "Grab less now, more later.",
              "The juiciest line is a dead end.", "Don't take it all at once."],
    "bait": ["Don't ride the obvious row.", "That long row is there to tempt you.",
             "Cross the row instead of riding it.", "The straight line is the trap."],
    "stop": ["Sometimes the best stop has no pawn on it.", "Stop short.", "Park on an empty square.",
             "Where you stop matters more than what you take."],
    "quiet": ["Sometimes a move that takes nothing sets up everything.", "One move takes nothing at all.",
              "Be patient: reposition first."],
    "diagonal": ["Think in three dimensions.", "Corner to corner, through the middle.",
                 "Try a diagonal that changes floors."],
    "cold": ["Where you start matters more than it looks.", "The obvious starting square is wrong.",
             "Start far from the crowd."],
    "walls": ["The blocks are there for a reason.", "Find the way around.", "Use the walls, don't fight them.",
              "Go around, not through.", "Look for the gap.", "One block changes everything.",
              "The wall points the way.", "Every block here matters."],
    "ice": ["On ice, the blocks are your brakes.", "Plan where you'll stop before you slide.",
            "Count the slides, not the squares.", "No stopping halfway on ice."],
    "brake": ["On ice, the back of a portal is as good as a wall.", "A portal can be a brake.",
              "Not every portal is a door."],
    "portal": ["Step into a portal and come out somewhere else.", "Watch which way the exit faces.",
               "The way out is not the way in."],
    "ramp": ["Ramps lift the queen up a floor.", "Up one floor, then keep going.", "Take the stairs."],
    "both": ["Portals and ramps work together.", "Through, then up.", "Chain them."],
    "decoy": ["Not every block is in the way.", "Some of these blocks are just scenery."],
    "decoy_portal": ["Not every portal goes somewhere useful.", "One of these portals is a red herring."],
    "place": ["Where you start matters more than it looks.", "Choose your landing square carefully."],
}
LATE = ["Nothing here is what it seems.", "Trust nothing.", "The first idea is the wrong one.",
        "Look again.", "Count before you move.", "There's a shorter way.", "Slow down.", ""]


def hint(p, c, used, rng):
    keys = []
    if p.get("decoy_portal"):
        keys.append("decoy_portal")
    if p["has_decoys"]:
        keys.append("decoy")
    if p["mech"] == "warp":
        keys.append(p.get("want") if p.get("want") in HINTS else "portal")
    # On ice she can't stop partway, so "stop short" hints would mislead there
    keys += [t for t in p["tricks"] if t in HINTS and t != "diagonal" and not (t == "stop" and p.get("ice"))]
    if p["mech"] == "ice":
        keys.append("ice")
    if not p.get("queen"):
        keys.append("place")
    if "diagonal" in p["tricks"] or not keys:
        keys.append("diagonal")
    options = [] if c >= 3 and rng.random() < 0.5 else [h for k in keys for h in HINTS[k]]
    options += LATE if c >= 3 else []
    least = min(used.get(h, 0) for h in options)
    fresh = [h for h in options if used.get(h, 0) == least]
    h = fresh[0] if c < 3 else rng.choice(fresh)
    used[h] = used.get(h, 0) + (3 if not h else 1)  # a few puzzles late on go without a tip
    return h


def build(pid, out, names_path, cands):
    theme = THEMES[pid]
    rng = random.Random(sum(map(ord, pid)))
    chosen = assemble(cands, theme, rng)
    # Decoy portals only on the last three portal puzzles that aren't brakes
    late = [k for k, (c, p) in enumerate(chosen) if k >= 40 and p["mech"] == "warp" and p.get("want") != "brake"]
    for k in late[-3:] if pid == "cuberun" else late[-1:]:
        c, p = chosen[k]
        d = portals.add_decoy_portal(dict(p), rng)
        if d:
            d["decoy_portal"] = True
            chosen[k] = (c, d)
    names = sized.Names(names_path)
    puzzles, used = [], {}
    for i, (c, p) in enumerate(chosen):
        e = {k: v for k, v in p.items() if k in ("size", "queen", "best", "pawns", "blocks", "par", "ice", "line",
                                                    "portals", "ramps")}
        e = {"name": names.pick(p["mech"], i), **e}
        e["tip"] = hint(p, c, used, rng)
        e["tricks"] = p["tricks"] + ([p.get("want", "portal")] if p["mech"] == "warp" else []) \
            + (["ice"] if p.get("ice") else []) + (["decoy"] if p["has_decoys"] or p.get("decoy_portal") else [])
        e["lured"] = p["lured"]
        e["score"] = sized.score_entry(e)
        e["tier"] = sized.TIERS[c]
        e["seconds"] = round(predicted_seconds(e))
        puzzles.append(e)
    print(packs.write({"id": pid, "name": theme["name"], "free": False, "puzzles": puzzles}, out))
    for i, e in enumerate(puzzles, 1):
        print(f"{i:3} ch{(i - 1) // 10 + 1} {e['name']:18} par {e['par']} {'given' if e['queen'] else 'place'} "
              f"~{e['seconds']:4}s lured {e['lured']} {','.join(e['tricks']) or '-'} | {e['tip']}")


def existing_keys(files):
    keys = set()
    for f in files:
        for p in json.load(open(f))["puzzles"]:
            keys.add((pipeline.layout_key(p), tuple(p.get("portals") or []), tuple(p.get("ramps") or [])))
    return keys


EXISTING = set()

if __name__ == "__main__":
    import glob, os
    pid, out, names_path = sys.argv[1:4]
    EXISTING = existing_keys(f for f in glob.glob(os.path.join(packs.APP_DIR, "*.json"))
                             if os.path.basename(f)[:-5] != pid)
    cands = []
    for f in sys.argv[4:]:
        cands += json.load(open(f))
    build(pid, out, names_path, cands)

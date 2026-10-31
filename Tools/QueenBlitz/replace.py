"""Swap single puzzles in a pack for fresh candidates that fit the same slot, leaving the rest of the
pack (and players' saved clears on it) alone. Brandon, Oct 31: cap forced doubling back (backtrack.py).

    python3 replace.py PACK.json N[,N...] CANDIDATES.json...

A replacement keeps the slot's name, size, ice, given/placed queen and par, needs no doubling back,
isn't a look-alike of any pack puzzle or daily, opens differently from its neighbours, doesn't
repeat a solution shape in its chapter, and scores as close as it can to the puzzle it replaces
(sharing its tricks where possible).
"""
import glob, json, os, sys
import backtrack, daily, ladder, packs, paid, pipeline, sized


def taken_keys():
    keys = set()
    for f in glob.glob(os.path.join(packs.APP_DIR, "*.json")):
        keys |= {pipeline.layout_key(p) for p in json.load(open(f, encoding="utf-8"))["puzzles"]}
    keys |= {pipeline.layout_key(p) for p in daily.load_dailies().values()}
    return keys


def first_kind(p):
    """The first move's kind as she sets off (axis, flat or 3D diagonal), through ramps and portals"""
    import inventory
    b = packs.board(p)
    return inventory.route(b, p)[0]["kind"]


def entry(p, old):
    e = {k: p[k] for k in ("size", "queen", "best", "pawns", "blocks", "par", "ice", "line") if k in p}
    for k in ("portals", "ramps"):
        if p.get(k) or k in old:
            e[k] = p.get(k) or []
    e = {"name": old["name"], **e}
    tricks = p["tricks"] + (["ice"] if p.get("ice") else [])
    # Keep the old tip when it was about a trick this puzzle also has, otherwise the trick's own tip
    keep = [t for t in old.get("tricks", []) if t in tricks and t != "ice"]
    e["tip"] = old["tip"] if keep or not p["tricks"] else ladder.TIPS.get(p["tricks"][0], old["tip"])
    e["tricks"] = tricks
    e["lured"] = p["lured"]
    e["score"] = sized.score_entry(e)
    e["tier"] = old.get("tier")
    if "seconds" in old:
        e["seconds"] = round(paid.predicted_seconds(e))
    return {k: e[k] for k in old if k in e} | {k: v for k, v in e.items() if k not in old}


def main(path, slots, cand_files):
    pack = json.load(open(path, encoding="utf-8"))
    ps = pack["puzzles"]
    pool = []
    for f in cand_files:
        pool += [sized.normalise(p) for p in json.load(open(f))]
    taken = taken_keys()
    for n in slots:
        old = ps[n - 1]
        c = (n - 1) // 10
        chapter = [q for i, q in enumerate(ps[c * 10:c * 10 + 10], c * 10 + 1) if i != n]
        shapes = {pipeline.shape(q) for q in chapter}
        near = {first_kind(ps[i - 1]) for i in (n - 1, n + 1) if 1 <= i <= len(ps)}
        ok = [p for p in pool if p["size"] == old["size"] and bool(p.get("ice")) == bool(old.get("ice"))
              and bool(p.get("queen")) == bool(old["queen"]) and p["par"] == old["par"]
              and not p.get("portals") and not p.get("ramps") and not p.get("mirror")
              and pipeline.layout_key(p) not in taken and pipeline.shape(p) not in shapes
              and first_kind(p) not in near]
        want = set(t for t in old.get("tricks", []) if t != "ice")
        ranked = sorted(ok, key=lambda p: (abs(sized.score_entry(entry(p, old)) - old.get("score", 0))
                                           - 4 * len(want & set(p["tricks"]))))
        pick = next((p for p in ranked if not backtrack.forced(p)
                     and backtrack.cap_ok(p, c, [q for q in chapter])), None)
        if not pick:
            print(f"#{n} {old['name']}: no candidate fits ({len(ok)} matched size/ice/par)")
            continue
        new = entry(pick, old)
        ps[n - 1] = new
        taken.add(pipeline.layout_key(pick))
        print(f"#{n} {old['name']}: score {old.get('score')} {','.join(old.get('tricks', []))} -> "
              f"score {new['score']} {','.join(new['tricks'])} lured {new['lured']} ({len(ok)} matched)")
    packs.write(pack, path)
    problems = packs.check(path)
    print("check:", "OK" if not problems else problems)


if __name__ == "__main__":
    main(sys.argv[1], [int(x) for x in sys.argv[2].split(",")], sys.argv[3:])

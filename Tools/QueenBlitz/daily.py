"""Daily puzzles: one per date, made in advance and served as static files by the Worker.

    python3 daily.py make 2026-11-01 365     # make dailies for 365 days from that date
    python3 daily.py check                    # re-solve every daily file
    python3 daily.py status                   # how many days are left in the queue

Files go to server-cf/public/daily/YYYY-MM-DD.json and reach players with the next `wrangler
deploy` (Brandon's OK needed). Cloudflare serves static files without running the Worker, so
they don't count against its daily request limit.

Difficulty follows the week like a newspaper crossword: easy on Monday, devious on Sunday.
Nothing is ever a copy (or rotation or mirror image) of a pack puzzle or an earlier daily.
"""
import datetime, glob, json, os, sys
from multiprocessing import Pool
import packs, pipeline

DAILY_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "server-cf", "public", "daily")
EPOCH = datetime.date(2026, 11, 1)   # Daily #1 (QueenBlitz.DailyEpoch in the app must match)

# Monday = 0. Some days alternate between recipes by week.
WEEK = {
    0: ["easy-4", "warmup-4"],
    1: ["easy-4"],
    2: ["medium-4"],
    3: ["medium-6"],
    4: ["ice-med-6", "block-med-6"],
    5: ["hard-6", "ice-hard-6", "block-hard-6"],
    6: ["devious-6"],
}


def recipe_for(day):
    options = WEEK[day.weekday()]
    week = (day - EPOCH).days // 7
    return options[week % len(options)]


def path_for(day):
    return os.path.join(DAILY_DIR, day.isoformat() + ".json")


def known_keys():
    """Canonical keys of every pack puzzle and every daily already made"""
    keys = set()
    files = glob.glob(os.path.join(packs.APP_DIR, "*.json"))
    puzzles = []
    for f in files:
        puzzles += json.load(open(f, encoding="utf-8"))["puzzles"]
    for f in glob.glob(os.path.join(DAILY_DIR, "*.json")):
        puzzles.append(json.load(open(f, encoding="utf-8")))
    for p in puzzles:
        t = packs.triples
        keys.add(pipeline.canonical(p["size"], p.get("ice", False), tuple(p["queen"]) if p["queen"] else None,
                                    t(p["pawns"]), t(p["blocks"])))
    return keys


def _make(job):
    recipe, count, seed, seen = job
    return recipe, pipeline.make(recipe, count, seed, set(seen))


def make(start, days):
    os.makedirs(DAILY_DIR, exist_ok=True)
    todo = [start + datetime.timedelta(d) for d in range(days)]
    todo = [d for d in todo if not os.path.exists(path_for(d))]
    by_recipe = {}
    for d in todo:
        by_recipe.setdefault(recipe_for(d), []).append(d)
    seen = known_keys()
    # One process per recipe; each makes a few spares in case two recipes ever collide
    jobs = [(r, len(ds) + 3, 1000 + i, seen) for i, (r, ds) in enumerate(sorted(by_recipe.items()))]
    with Pool(len(jobs) or 1) as pool:
        results = dict(pool.map(_make, jobs))
    written = 0
    for recipe, ds in by_recipe.items():
        made = results[recipe]
        for d in ds:
            while made:
                p = made.pop(0)
                key = pipeline.canonical(p["size"], p["ice"], tuple(p["queen"]) if p["queen"] else None,
                                         packs.triples(p["pawns"]), packs.triples(p["blocks"]))
                if key not in seen:
                    seen.add(key)
                    break
            else:
                print("ran out of", recipe, "for", d)
                continue
            number = (d - EPOCH).days + 1
            p.update(name=f"Daily #{number}", date=d.isoformat(), number=number)
            with open(path_for(d), "w", encoding="utf-8") as f:
                json.dump(p, f, ensure_ascii=False)
            written += 1
    # Tips that don't repeat day after day
    files = sorted(glob.glob(os.path.join(DAILY_DIR, "*.json")))
    all_days = [json.load(open(f, encoding="utf-8")) for f in files]
    pipeline.vary_tips(all_days)
    for f, p in zip(files, all_days):
        with open(f, "w", encoding="utf-8") as out:
            json.dump(p, out, ensure_ascii=False)
    return written


def check():
    bad = 0
    for f in sorted(glob.glob(os.path.join(DAILY_DIR, "*.json"))):
        p = json.load(open(f, encoding="utf-8"))
        tmp = f + ".pack"
        json.dump({"id": "daily", "puzzles": [p]}, open(tmp, "w"))
        problems = packs.check(tmp)
        os.remove(tmp)
        for problem in problems:
            print(os.path.basename(f), problem)
        bad += len(problems)
    return bad


def status():
    files = sorted(glob.glob(os.path.join(DAILY_DIR, "*.json")))
    if not files:
        print("no dailies yet")
        return
    last = datetime.date.fromisoformat(os.path.basename(files[-1])[:10])
    left = (last - datetime.date.today()).days
    print(f"{len(files)} dailies, {os.path.basename(files[0])[:10]} to {last}; {left} days left")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "make":
        print("wrote", make(datetime.date.fromisoformat(sys.argv[2]), int(sys.argv[3])))
    elif cmd == "check":
        sys.exit(1 if check() else 0)
    elif cmd == "status":
        status()
    else:
        print(__doc__)

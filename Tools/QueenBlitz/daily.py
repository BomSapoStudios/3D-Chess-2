"""Daily puzzles: one per date, made in advance and served as static files by the Worker.

The monthly workflow (a scheduled Claude task runs the first two steps and asks Brandon for the third):

    python3 daily.py next                     # remake the coming weeks with the newest engine, then check
    python3 daily.py check                    # re-solve every daily and apply the rules below
    python3 daily.py launch                   # on the Mac, on main: confirm it's safe, then print the deploy command
    python3 daily.py verify                   # after the deploy: the server has the new files

    python3 daily.py status                   # how far ahead the queue runs and what made it
    python3 daily.py next --days 35 --minutes 24 --from 2026-12-01   # a set window instead
    python3 daily.py next --reuse             # pick again from the last run's candidates (after a rules fix)

Files go to server-cf/public/daily/YYYY-MM-DD.json and reach players with `wrangler deploy` from
current main (Brandon's OK needed). Cloudflare serves static files without running the Worker, so
they don't count against its daily request limit, and the app asks for each date at most once a day.

`next` carries on from the last day it made, up to AHEAD_DAYS ahead. It never touches the next FREEZE_DAYS days: phones download six days ahead, and a day someone
already has must stay the same for everyone. Older dailies further out stay as a safety net until a
later run remakes them, so a missed month never leaves a gap.

Difficulty follows the week like a newspaper crossword, easy on Monday to devious on Sunday, using
the newest generators (ladder.py one-trick and graded boards, ice; engine.py boards every lure
fails) and sized.py's difficulty weights, which come from players' solve times. The rules:
  - never a look-alike: no layout (rotated, mirrored, queen given or not) of any pack puzzle or other daily
  - never two moves (warm-ups only, Brandon Oct 26)
  - openers vary: never the same first move direction two days running, and no solution shape twice in a week
  - at most two days a week whose only trick is a 3D diagonal
  - no portals or ramps unless --warp (builds from before Oct 27 can't show them)
"""
import argparse, datetime, glob, json, os, subprocess, sys, tempfile
import packs, pipeline, sized, stats

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
DAILY_DIR = os.path.join(ROOT, "server-cf", "public", "daily")
EPOCH = datetime.date(2026, 11, 1)   # Daily #1 (QueenBlitz.DailyEpoch in the app must match)
FREEZE_DAYS = 14
AHEAD_DAYS = 49                      # `next` tops the new-engine queue up to this many days ahead
ENGINE = "sized-2026-10"             # written into each daily this script makes

# Monday = 0. A day with several entries takes them in turn, week by week.
# slot: (mech, sizes, pars, tricks (min, max), lured (min, max), pick)
WEEK = {
    0: [("classic", (5,), (3, 4), (1, 1), (0, 1), "easy")],
    1: [("classic", (5,), (4,), (1, 1), (0, 2), "mid")],
    2: [("classic", (6,), (4,), (1, 1), (0, 2), "mid"), ("ice", (5,), (3, 4), (0, 1), (0, 2), "easy")],
    3: [("classic", (5, 6), (4, 5), (2, 2), (1, 3), "mid")],
    4: [("ice", (6,), (4, 5), (1, 9), (1, 3), "mid"), ("classic", (6,), (5,), (2, 9), (2, 3), "mid")],
    5: [("classic", (6,), (5, 6), (2, 9), (2, 3), "hard")],
    6: [("classic", (6,), (5, 6), (2, 9), (3, 3), "hard"), ("ice", (6,), (5, 6), (1, 9), (3, 3), "hard")],
}
WARP_SATURDAY = ("warp", (5, 6), (4, 5), (0, 9), (1, 3), "mid")
TIER = {0: "Easy", 1: "Easy", 2: "Medium", 3: "Medium", 4: "Hard", 5: "Hard", 6: "Devious"}


def slot_for(day, warp=False):
    if warp and day.weekday() == 5 and ((day - EPOCH).days // 7) % 2:
        return WARP_SATURDAY
    options = WEEK[day.weekday()]
    return options[((day - EPOCH).days // 7) % len(options)]


def path_for(day):
    return os.path.join(DAILY_DIR, day.isoformat() + ".json")


def load_dailies():
    out = {}
    for f in sorted(glob.glob(os.path.join(DAILY_DIR, "*.json"))):
        out[datetime.date.fromisoformat(os.path.basename(f)[:10])] = json.load(open(f, encoding="utf-8"))
    return out


def pack_puzzles():
    out = []
    for f in sorted(glob.glob(os.path.join(packs.APP_DIR, "*.json"))):
        pack = json.load(open(f, encoding="utf-8"))
        out += [(pack["id"], i + 1, p) for i, p in enumerate(pack["puzzles"])]
    return out


def opener(p):
    """The first move's direction, up to turning the cube: which axes it changes"""
    s = pipeline.shape(p)
    return s[0] if s else ""


# ───────────────────────── Making candidates ─────────────────────────

def generate(minutes, seed, work, warp):
    """Run the generators in turn and return every candidate (each normalised by sized.py)"""
    runs = [
        ("ladder.py", ["gen", None, None, None, "5,6", "3,4,5"], {"ICE_SHARE": "0"}, 0.35),
        ("ladder.py", ["gen", None, None, None, "5,6", "4,5,6"], {"ICE_SHARE": "1"}, 0.25),
        ("engine.py", ["run", None, None, None, "5,6", "4,5,6"], {}, 0.4),
    ]
    if warp:
        runs.append(("portals.py", ["gen", None, None, None, "5,6", "4,5"], {}, 0.15))
    cands = []
    for i, (script, args, env, share) in enumerate(runs):
        out = os.path.join(work, f"cands-{seed}-{i}.json")
        args = list(args)
        args[1], args[2], args[3] = str(round(minutes * 60 * share)), str(seed * 10 + i), out
        print(f"  {script} {' '.join(args[:1] + args[4:])} for {args[1]} s", flush=True)
        subprocess.run([sys.executable, os.path.join(HERE, script)] + args, cwd=HERE, env={**os.environ, **env},
                       stdout=subprocess.DEVNULL, check=False)
        if os.path.exists(out):
            got = json.load(open(out))
            print(f"    {len(got)} candidates", flush=True)
            cands += got
    return [sized.normalise(c) for c in cands]


def fits(p, slot, relax):
    mech, sizes, pars, tricks, lured, _ = slot
    if p["mech"] != mech or p["size"] not in sizes or p["par"] < 3:
        return False
    if p["has_decoys"] and slot[5] != "hard":
        return False  # decoy blocks only on the hardest days
    lo, hi = (min(pars), max(pars)) if relax < 2 else (min(pars) - 1, max(pars) + 1)
    if not lo <= p["par"] <= hi:
        return False
    t = [x for x in p["tricks"] if x not in sized.MECHANICS]
    if relax < 3 and not tricks[0] <= len(t) <= tricks[1]:
        return False
    if relax < 1 and not lured[0] <= (p["lured"] or 0) <= lured[1]:
        return False
    return True


def tip(p):
    if p.get("ice"):
        return "On ice, the blocks are your brakes." if p.get("blocks") else "Ice! Every slide runs to the edge or a block."
    return sized.tip(p, {"ice"})


def assign(days, pool, seen, before, warp):
    """Pick a candidate for each day, in date order. before: the dailies just ahead of the window
    (for the openers and shapes rule). Returns {day: candidate} and the days left unfilled."""
    chosen, missing, used = {}, [], set()
    recent = list(before)
    for day in days:
        slot = slot_for(day, warp)
        last_week = recent[-6:]
        shapes = {pipeline.shape(q) for q in last_week}
        yesterday = recent[-1] if recent else None
        # 3D diagonals are in nearly every route, so at most two diagonal-only days a week
        flat = sum(q.get("tricks") == ["diagonal"] for q in last_week)
        for relax in range(4):
            ok = [p for p in pool if id(p) not in used and pipeline.layout_key(p) not in seen and fits(p, slot, relax)
                  and pipeline.shape(p) not in shapes and not (yesterday and opener(p) == opener(yesterday))
                  and not (relax < 3 and p["tricks"] == ["diagonal"] and flat >= 2)]
            if ok:
                break
        else:
            missing.append(day)
            continue
        if relax:
            print(f"  {day} {day:%a}: relaxed {relax}", file=sys.stderr)
        sign = {"easy": 1, "mid": 0, "hard": -1}[slot[5]]
        mid = sorted(sized.difficulty(p) for p in ok)[len(ok) // 2]
        y_tricks = set(yesterday.get("tricks", [])) if yesterday else set()
        # Prefer tricks yesterday didn't have, and a mix of given and placed queens
        best = min(ok, key=lambda p: (len(set(p["tricks"]) & y_tricks) * 3
                                      + (2 if yesterday and bool(p.get("queen")) == bool(yesterday.get("queen")) else 0),
                                      sign * sized.difficulty(p) if sign else abs(sized.difficulty(p) - mid)))
        used.add(id(best))
        seen.add(pipeline.layout_key(best))
        chosen[day] = best
        recent.append(best)
    return chosen, missing


def entry_for(day, p):
    e = {k: p[k] for k in ("size", "queen", "best", "pawns", "blocks", "par", "ice", "line") if k in p}
    for k in ("portals", "ramps"):
        if p.get(k):
            e[k] = p[k]
    number = (day - EPOCH).days + 1
    e = {"name": f"Daily #{number}", **e, "tip": tip(p)}
    e["tricks"] = p["tricks"] + ([p["want"]] if p["mech"] == "warp" else []) + (["ice"] if p.get("ice") else []) \
        + (["decoy"] if p["has_decoys"] else [])
    e["lured"] = p["lured"]
    e["score"] = sized.score_entry(e)
    e["tier"] = TIER[day.weekday()]
    e.update(date=day.isoformat(), number=number, engine=ENGINE)
    return e


def next_batch(start, days, minutes, seed, warp, pool_path, reuse=False, rounds=3):
    today = datetime.date.today()
    dailies = load_dailies()
    made = [d for d in dailies if dailies[d].get("engine")]
    # Carry on after the last day this script made (each month tops up; nothing is remade twice)
    first = max(start or (max(made) + datetime.timedelta(1) if made else today), today + datetime.timedelta(FREEZE_DAYS), EPOCH)
    days = days or (today + datetime.timedelta(AHEAD_DAYS) - first).days + 1
    if days <= 0:
        print(f"Nothing to do: the new engine already runs to {max(made)}")
        return [], {}
    window = [first + datetime.timedelta(d) for d in range(days)]
    # Look-alikes: every pack puzzle and every daily outside the window being remade
    seen = {pipeline.layout_key(p) for _, _, p in pack_puzzles()}
    seen |= {pipeline.layout_key(p) for d, p in dailies.items() if d not in window}
    before = [dailies[d] for d in sorted(dailies) if first - datetime.timedelta(7) <= d < first]
    print(f"Remaking {window[0]} to {window[-1]} ({days} days); {first - today} frozen ahead of today", flush=True)
    # Candidates are kept in pool_path, so a rerun (after a fix to the rules) can skip making them again
    pool = [sized.normalise(c) for c in json.load(open(pool_path))] if reuse and os.path.exists(pool_path) else []
    chosen = {}
    with tempfile.TemporaryDirectory() as work:
        for r in range(rounds):
            if r or not pool:
                pool += generate(minutes if not pool else minutes / 3, seed + r, work, warp)
                json.dump(pool, open(pool_path, "w"))
            chosen, missing = assign(window, pool, set(seen), before, warp)
            if not missing:
                break
            print(f"  round {r + 1}: {len(missing)} days still need a puzzle, making more", flush=True)
    for day in window:
        if day not in chosen:
            print(f"  {day}: no candidate, kept the old daily" + ("" if day in dailies else " (none: day is empty!)"))
            continue
        with open(path_for(day), "w", encoding="utf-8") as f:
            json.dump(entry_for(day, chosen[day]), f, ensure_ascii=False)
    return window, chosen


# ───────────────────────── Checks ─────────────────────────

def check(quiet=False):
    """Re-solve every daily, then the rules. Returns the problems."""
    dailies = load_dailies()
    problems = []
    for day, p in dailies.items():
        tmp = tempfile.NamedTemporaryFile("w", suffix=".json", delete=False)
        json.dump({"id": "daily", "puzzles": [p]}, tmp)
        tmp.close()
        problems += [f"{day}: {x}" for x in packs.check(tmp.name)]
        os.remove(tmp.name)
        number = (day - EPOCH).days + 1
        if p.get("number") != number or p.get("date") != day.isoformat() or p.get("name") != f"Daily #{number}":
            problems.append(f"{day}: number, date or name doesn't match the file name")
    days = sorted(dailies)
    for a, b in zip(days, days[1:]):
        if (b - a).days != 1:
            problems.append(f"gap: nothing between {a} and {b}")
    # The rules apply to dailies made by this script; the first year's (older generator) are only re-solved
    new = [d for d in days if dailies[d].get("engine")]
    packs_by_key = {pipeline.layout_key(p): f"{pid} #{i}" for pid, i, p in pack_puzzles()}
    keys = {}
    for d in days:
        k = pipeline.layout_key(dailies[d])
        if k in keys and (d in new or keys[k] in new):
            problems.append(f"{d}: looks like the daily of {keys[k]}")
        keys.setdefault(k, d)
    for d in new:
        p = dailies[d]
        k = pipeline.layout_key(p)
        if k in packs_by_key:
            problems.append(f"{d}: looks like pack puzzle {packs_by_key[k]}")
        if p["par"] < 3:
            problems.append(f"{d}: two-move puzzles are warm-ups only")
        prev = dailies.get(d - datetime.timedelta(1))
        if prev and opener(prev) == opener(p):
            problems.append(f"{d}: opens the same way as the day before")
        week = [dailies.get(d - datetime.timedelta(i)) for i in range(1, 7)]
        if any(q and pipeline.shape(q) == pipeline.shape(p) for q in week):
            problems.append(f"{d}: same solution shape as a day in the week before")
    if not quiet:
        for x in problems:
            print(x)
        print(f"{len(dailies)} dailies checked ({len(new)} from the new engine): "
              + ("all good" if not problems else f"{len(problems)} problems"))
    return problems


def status():
    dailies = load_dailies()
    if not dailies:
        print("no dailies yet")
        return
    days = sorted(dailies)
    today = datetime.date.today()
    new = [d for d in days if dailies[d].get("engine")]
    print(f"{len(days)} dailies, {days[0]} to {days[-1]}; {(days[-1] - today).days} days left")
    if new:
        print(f"newest engine: {new[0]} to {new[-1]} ({len(new)} days)")
    print(f"frozen through {today + datetime.timedelta(FREEZE_DAYS - 1)} (phones may already have these)")


def summary(window, chosen, path):
    """A Markdown table of the batch for the pull request and Brandon"""
    rows = ["| Date | Day | # | Tier | Board | Par | Queen | Tricks | Score |", "|---|---|---|---|---|---|---|---|---|"]
    for day in window:
        if day not in chosen:
            rows.append(f"| {day} | {day:%a} | | kept old daily | | | | | |")
            continue
        e = json.load(open(path_for(day), encoding="utf-8"))
        rows.append(f"| {day} | {day:%a} | {e['number']} | {e['tier']} | {e['size']}³{' ice' if e['ice'] else ''} | {e['par']} "
                    f"| {'given' if e['queen'] else 'place'} | {', '.join(e['tricks']) or '-'} | {e['score']} |")
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(rows) + "\n")
    print("\n".join(rows))


def times_by_weekday():
    """Median solve time per weekday from the server's stats (one request; needs the stats key)"""
    key = os.environ.get("BLITZ_STATS_KEY") or (open(stats.KEY_FILE).read().strip() if os.path.exists(stats.KEY_FILE) else "")
    if not key:
        print("(no stats key here: skipping solve times)")
        return
    dailies = load_dailies()
    by_day = {}
    for s in stats.fetch(stats.SERVER, key):
        if not s["puzzle"].startswith("daily-") or s["solveSeconds"]["median"] is None:
            continue
        day = datetime.date.fromisoformat(s["puzzle"][6:16])
        if day in dailies and stats.sig(dailies[day]) == s["sig"]:
            by_day.setdefault(day.weekday(), []).append((s["solveSeconds"]["median"], s["clearRate"]))
    for wd in range(7):
        got = sorted(by_day.get(wd, []))
        if got:
            print(f"  {['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'][wd]}: {len(got)} dailies, median of medians "
                  f"{stats.clock(got[len(got) // 2][0])}, clear rate {round(100 * sum(c for _, c in got) / len(got))}%")


def git(*args):
    return subprocess.run(["git", "-C", ROOT] + list(args), capture_output=True, text=True).stdout.strip()


def launch():
    """Safe to deploy? On main, matching GitHub, nothing changed locally, every daily checks out."""
    ok = True
    subprocess.run(["git", "-C", ROOT, "fetch", "-q", "origin", "main"])
    if git("rev-parse", "--abbrev-ref", "HEAD") != "main":
        print("Not on main: deploys come from current main only (git checkout main && git pull)")
        ok = False
    elif git("rev-parse", "HEAD") != git("rev-parse", "origin/main"):
        print("This checkout isn't the same commit as GitHub main: git pull first")
        ok = False
    if git("status", "--porcelain", "--", "server-cf"):
        print("server-cf has local changes: they would ship too")
        ok = False
    if check(quiet=True):
        print("daily.py check found problems")
        ok = False
    if ok:
        print(f"Ready: main {git('rev-parse', '--short', 'HEAD')}. Deploy with Brandon's OK:\n    cd server-cf && npx wrangler deploy\n"
              "then: python3 Tools/QueenBlitz/daily.py verify")
    return ok


def verify():
    """Fetch the newest daily (a static file: no Worker run) from both addresses and compare"""
    import urllib.request
    dailies = load_dailies()
    new = [d for d in sorted(dailies) if dailies[d].get("engine")] or sorted(dailies)
    good = True
    for host in (stats.SERVER, "https://3d-chess.bomsapostudios.workers.dev"):
        for day in (new[0], new[-1]):
            try:
                with urllib.request.urlopen(f"{host}/daily/{day}.json", timeout=15) as r:
                    live = json.load(r)
                same = stats.sig(live) == stats.sig(dailies[day])
            except Exception:
                same = False
            print(f"{host} {day}: {'matches' if same else 'DIFFERENT or missing'}")
            good &= same
    return good


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["next", "check", "status", "launch", "verify", "times"])
    ap.add_argument("--from", dest="start", type=datetime.date.fromisoformat)
    ap.add_argument("--days", type=int, help=f"default: up to {AHEAD_DAYS} days ahead of today")
    ap.add_argument("--minutes", type=float, default=36)
    ap.add_argument("--seed", type=int, default=int(datetime.date.today().strftime("%Y%m")))
    ap.add_argument("--warp", action="store_true", help="allow portal/ramp Saturdays (only once old builds are gone)")
    ap.add_argument("--summary", default=os.path.join(tempfile.gettempdir(), "daily-batch.md"))
    ap.add_argument("--pool", default=os.path.join(tempfile.gettempdir(), "daily-pool.json"), help="where candidates are kept")
    ap.add_argument("--reuse", action="store_true", help="pick from the candidates in --pool instead of making new ones")
    a = ap.parse_args()
    if a.cmd == "next":
        window, chosen = next_batch(a.start, a.days, a.minutes, a.seed, a.warp, a.pool, a.reuse)
        summary(window, chosen, a.summary)
        sys.exit(1 if check() else 0)
    elif a.cmd == "check":
        sys.exit(1 if check() else 0)
    elif a.cmd == "status":
        status()
    elif a.cmd == "launch":
        sys.exit(0 if launch() else 1)
    elif a.cmd == "verify":
        sys.exit(0 if verify() else 1)
    elif a.cmd == "times":
        times_by_weekday()

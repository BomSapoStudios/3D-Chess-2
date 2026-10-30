"""How hard Queen Blitz puzzles really are: every player's solve times, totalled per puzzle by the server.

The app sends each player's time up to their first clear of a puzzle (all attempts added up, so leaving
and coming back never resets it) along with its wallet sync; the server keeps totals per puzzle and
version. This reads them (one request) and prints a table per pack, with names and current versions
taken from the pack files here.

    python3 stats.py                    every pack
    python3 stats.py blitz-5 daily      just these ("daily" = the daily puzzles)
    python3 stats.py --by median        hardest first within each pack (median, clear, tries, p90)
    python3 stats.py --csv times.csv    also write every row for a spreadsheet
    python3 stats.py --old              include versions of puzzles that have since changed

The key is the server's STATS_KEY secret: set BLITZ_STATS_KEY, or put it in
~/.appstore-publisher/keys/blitz-stats-key.txt.

Columns: players = started it; cleared = cleared at least once; median/p75/p90 = total clock time up to
the first clear (over every attempt); tries = median attempts up to the first clear; stuck = players
who haven't cleared it yet, and their median time so far; auto = clears where Auto-place put the queen down.
"""
import argparse, csv, glob, json, os, sys, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
APP_DIR = os.path.join(HERE, "..", "..", "Assets", "Resources", "QueenBlitz")
DAILY_DIR = os.path.join(HERE, "..", "..", "server-cf", "public", "daily")
SERVER = "https://chess-api.bomsapostudios.com"
KEY_FILE = os.path.expanduser("~/.appstore-publisher/keys/blitz-stats-key.txt")


def sig(p):
    """The app's QueenBlitz.Sig: FNV-1a over size|queen|pawns|blocks|par|ice|portals|ramps[|mirror][|bombs|placeBombs]"""
    csv_ = lambda a: ",".join(str(v) for v in (a or []))
    text = "|".join([str(p.get("size", 0)), csv_(p.get("queen")), csv_(p.get("pawns")), csv_(p.get("blocks")),
                     str(p.get("par", 0)), "1" if p.get("ice") else "0", csv_(p.get("portals")), csv_(p.get("ramps"))])
    if p.get("mirror") and len(p["mirror"]) == 3:
        text += "|" + csv_(p["mirror"])
    if p.get("bombs") or p.get("placeBombs"):
        text += f"|{csv_(p.get('bombs'))}|{p.get('placeBombs', 0)}"  # bomb puzzles only, so older sigs stay put
    h = 2166136261
    for ch in text.encode("ascii"):
        h = ((h ^ ch) * 16777619) & 0xFFFFFFFF
    return f"{h:08x}"


def local_puzzles():
    """Server puzzle id -> (pack name, number shown in the app, puzzle name, current sig)"""
    known = {}
    for path in sorted(glob.glob(os.path.join(APP_DIR, "*.json"))):
        with open(path) as f:
            pack = json.load(f)
        for i, p in enumerate(pack.get("puzzles", [])):
            known[f"{pack['id'].lower()}.{i}"] = (pack.get("name", pack["id"]), i + 1, p.get("name", ""), sig(p))
    for path in sorted(glob.glob(os.path.join(DAILY_DIR, "*.json"))):
        date = os.path.basename(path)[:-5]
        with open(path) as f:
            p = json.load(f)
        known[f"daily-{date}.0"] = ("Daily", date, p.get("name", ""), sig(p))
    return known


def clock(s):
    if s is None:
        return "-"
    s = int(round(s))
    return f"{s // 3600}:{s // 60 % 60:02}:{s % 60:02}" if s >= 3600 else f"{s // 60}:{s % 60:02}"


def fetch(server, key):
    req = urllib.request.Request(server.rstrip("/") + "/blitz/stats", headers={"X-Stats-Key": key, "User-Agent": "blitz-stats"})
    try:
        with urllib.request.urlopen(req, timeout=20) as r:
            return json.load(r)["puzzles"]
    except urllib.error.HTTPError as e:
        sys.exit("The server didn't recognise the key (or STATS_KEY isn't set there yet)." if e.code == 404 else f"Server error {e.code}")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("packs", nargs="*", help="pack ids (blitz-5, starter, daily...); default every pack")
    ap.add_argument("--by", choices=["number", "median", "clear", "tries", "p90"], default="number")
    ap.add_argument("--csv", help="also write every row to this CSV file")
    ap.add_argument("--old", action="store_true", help="include versions of puzzles that have since changed")
    ap.add_argument("--min-players", type=int, default=1)
    ap.add_argument("--server", default=SERVER)
    a = ap.parse_args()

    key = os.environ.get("BLITZ_STATS_KEY") or (open(KEY_FILE).read().strip() if os.path.exists(KEY_FILE) else "")
    if not key:
        sys.exit(f"No key: set BLITZ_STATS_KEY or put the STATS_KEY secret in {KEY_FILE}")
    known = local_puzzles()
    wanted = [p.lower() for p in a.packs]

    rows = []
    for s in fetch(a.server, key):
        pack_id, _, index = s["puzzle"].rpartition(".")
        group = "daily" if pack_id.startswith("daily-") else pack_id
        if wanted and group not in wanted:
            continue
        info = known.get(s["puzzle"])
        current = info is not None and info[3] == s["sig"]
        if not current and not a.old:
            continue
        if s["players"] < a.min_players:
            continue
        rows.append({
            "pack": info[0] if info else pack_id, "group": group, "number": info[1] if info else int(index) + 1,
            "name": (info[2] if info else "?") + ("" if current else " (old version)"), "puzzle": s["puzzle"], "sig": s["sig"],
            "par": s["par"], "players": s["players"], "cleared": s["cleared"], "clear_rate": s["clearRate"],
            "p25": s["solveSeconds"]["p25"], "median": s["solveSeconds"]["median"], "p75": s["solveSeconds"]["p75"], "p90": s["solveSeconds"]["p90"],
            "tries": s["attempts"]["median"], "tries_mean": s["attempts"]["mean"], "stuck": s["stuck"]["players"],
            "stuck_median": s["stuck"]["medianSeconds"], "auto": s["autoPlaced"],
        })
    if not rows:
        print("No solve times yet" + (f" for {', '.join(wanted)}" if wanted else "") + ".")
        return

    order = {
        "number": lambda r: (str(r["number"]),) if r["group"] == "daily" else (r["number"],),
        "median": lambda r: (-(r["median"] or 1e12),),
        "clear": lambda r: (r["clear_rate"],),
        "tries": lambda r: (-(r["tries"] or 0),),
        "p90": lambda r: (-(r["p90"] or 1e12),),
    }[a.by]
    for group in sorted({r["group"] for r in rows}):
        part = sorted((r for r in rows if r["group"] == group), key=order)
        print(f"\n{part[0]['pack'] if group != 'daily' else 'Daily puzzles'}  ({sum(r['players'] for r in part)} puzzle starts)")
        print(f"  {'#':>10}  {'name':<26} {'par':>3} {'players':>7} {'cleared':>8} {'median':>7} {'p75':>7} {'p90':>7} {'tries':>5} {'stuck':>12} {'auto':>4}")
        for r in part:
            stuck = f"{r['stuck']} @ {clock(r['stuck_median'])}" if r["stuck"] else "-"
            print(f"  {str(r['number']):>10}  {r['name'][:26]:<26} {r['par']:>3} {r['players']:>7} {round(r['clear_rate'] * 100):>7}% "
                  f"{clock(r['median']):>7} {clock(r['p75']):>7} {clock(r['p90']):>7} {(r['tries'] if r['tries'] is not None else '-'):>5} {stuck:>12} {r['auto']:>4}")
    if a.csv:
        with open(a.csv, "w", newline="") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            w.writeheader()
            w.writerows(rows)
        print(f"\nWrote {len(rows)} rows to {a.csv}")


if __name__ == "__main__":
    main()

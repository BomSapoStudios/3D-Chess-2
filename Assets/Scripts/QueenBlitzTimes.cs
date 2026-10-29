using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Queen Blitz solve times, for gauging how hard each puzzle really is. A clock counts up from
/// the moment a puzzle starts until it is cleared (the bottom-left corner and the CLEARED panel show it when
/// ShowTimer is on). Every clear goes into a log on the device: pack and puzzle, the time of the
/// clearing attempt, the time across every attempt since the last clear, moves and attempts.
/// The Playtest page in the Queen Blitz picker lists it.
/// Every player's time up to their first clear also goes to the server (blitzstats.ts), so packs can be
/// tuned by how hard puzzles really are: one record per puzzle, time and attempts adding up across
/// every visit (leaving, restarting or closing the app never resets it), sent along with the wallet's sync.
/// </summary>
public static partial class QueenBlitz
{
    /// <summary>Seconds on this attempt's clock (the director counts it while the puzzle is in play)</summary>
    public static float Elapsed { get; set; }

    /// <summary>The clock is shown to players: on every puzzle while the packs are being timed</summary>
    public static bool ShowTimer => true;

    /// <summary>The clear just logged (null until one is)</summary>
    public static Solve LastSolve { get; private set; }

    [System.Serializable]
    public class Solve
    {
        public string pack;
        public int puzzle;          // 1-based, as shown in the picker
        public string name;
        public float seconds;       // the clearing attempt, from its start
        public float totalSeconds;  // every attempt since the last clear (this one included)
        public int moves;
        public int par;
        public int attempts;        // starts since the last clear, this one included
        public bool first;          // the first clear of this puzzle
        public string date;         // when, local time
    }

    [System.Serializable]
    private class SolveFile { public List<Solve> solves = new List<Solve>(); }

    private const int MaxSolves = 2000;
    private static SolveFile solveFile;
    private static string SolvePath => Path.Combine(Application.persistentDataPath, "queen-blitz-solves.json");

    /// <summary>"1:07", or "1:02:07" past an hour</summary>
    public static string Clock(float seconds)
    {
        int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
    }

    /// <summary>The part of Elapsed already added to the saved totals (Bank)</summary>
    private static float banked;

    /// <summary>A new attempt at this puzzle starts (Begin)</summary>
    private static void StartAttempt()
    {
        Elapsed = 0f;
        banked = 0f;
        string key = Key("Attempts", Index);
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PuzzleStat stat = Tracked();
        if (stat != null) { stat.attempts++; stat.rev++; SaveStats(); }
    }

    /// <summary>Save the time on the clock so far, so closing or killing the app can't lose or reset it</summary>
    public static void Bank()
    {
        if (Cleared) return;
        float add = Elapsed - banked;
        if (add <= 0f) return;
        banked = Elapsed;
        string key = Key("TimeSpent", Index);
        PlayerPrefs.SetFloat(key, PlayerPrefs.GetFloat(key, 0f) + add);
        PuzzleStat stat = Tracked();
        if (stat != null) { stat.seconds += add; stat.rev++; }
        SaveStats();
    }

    /// <summary>An attempt ended without a clear (left, restarted, out of moves): its time counts towards the next clear</summary>
    public static void EndAttempt()
    {
        if (Cleared) return;
        Bank();
        Elapsed = 0f;
        banked = 0f;
    }

    /// <summary>Log a clear (before RecordClear marks it cleared, so the first clear is known)</summary>
    private static void LogSolve()
    {
        string attemptsKey = Key("Attempts", Index), spentKey = Key("TimeSpent", Index);
        float unbanked = Mathf.Max(0f, Elapsed - banked);
        banked = Elapsed;
        PuzzleStat stat = Tracked();
        if (stat != null)
        {
            stat.seconds += unbanked;
            stat.cleared = true;
            stat.moves = Moves;
            stat.auto = AutoPlaced;
            stat.rev++;
            SaveStats();
        }
        LastSolve = new Solve
        {
            pack = PackId, puzzle = Index + 1, name = Current.name,
            seconds = Elapsed, totalSeconds = PlayerPrefs.GetFloat(spentKey, 0f) + unbanked,
            moves = Moves, par = Current.par, attempts = Mathf.Max(1, PlayerPrefs.GetInt(attemptsKey, 1)),
            first = !EverCleared(Index), date = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
        };
        PlayerPrefs.DeleteKey(attemptsKey);
        PlayerPrefs.DeleteKey(spentKey);
        List<Solve> list = Solves();
        list.Add(LastSolve);
        if (list.Count > MaxSolves) list.RemoveRange(0, list.Count - MaxSolves);
        try { File.WriteAllText(SolvePath, JsonUtility.ToJson(solveFile)); }
        catch (System.Exception e) { Debug.LogWarning($"Queen Blitz: couldn't save the solve log: {e.Message}"); }
    }

    /// <summary>Every logged clear, oldest first</summary>
    public static List<Solve> Solves()
    {
        if (solveFile == null)
        {
            try { solveFile = File.Exists(SolvePath) ? JsonUtility.FromJson<SolveFile>(File.ReadAllText(SolvePath)) : null; }
            catch (System.Exception e) { Debug.LogWarning($"Queen Blitz: couldn't read the solve log: {e.Message}"); }
            if (solveFile == null || solveFile.solves == null) solveFile = new SolveFile();
        }
        return solveFile.solves;
    }

    public static void ClearSolves()
    {
        Solves().Clear();
        try { if (File.Exists(SolvePath)) File.Delete(SolvePath); } catch (System.Exception) { }
    }

    /// <summary>The log as tab-separated text, newest first, for pasting into a spreadsheet</summary>
    public static string SolvesAsText()
    {
        var sb = new StringBuilder("date\tpack\tpuzzle\tname\ttime\tseconds\ttotal seconds\tmoves\tpar\tattempts\tfirst clear\n");
        List<Solve> list = Solves();
        for (int i = list.Count - 1; i >= 0; i--)
        {
            Solve s = list[i];
            sb.Append($"{s.date}\t{s.pack}\t{s.puzzle}\t{s.name}\t{Clock(s.seconds)}\t{s.seconds:0.0}\t{s.totalSeconds:0.0}\t{s.moves}\t{s.par}\t{s.attempts}\t{(s.first ? "yes" : "no")}\n");
        }
        return sb.ToString();
    }

    // ───────── Solve times for the server ─────────

    /// <summary>One puzzle's time up to its first clear; rev counts changes, sent is the rev the server has</summary>
    [System.Serializable]
    public class PuzzleStat
    {
        public string puzzle;       // "pack.index", as the wallet names it
        public string sig;          // the puzzle's layout (Sig)
        public float seconds;       // on the clock, over every attempt
        public int attempts;
        public bool cleared;
        public int moves;           // at the clear
        public bool auto;           // Auto-place put the queen down
        public int par;
        public int rev, sent;
    }

    [System.Serializable] private class StatFile { public List<PuzzleStat> stats = new List<PuzzleStat>(); }

    /// <summary>What goes to the server (no bookkeeping fields)</summary>
    [System.Serializable]
    private class StatOut { public string puzzle, sig; public float seconds; public int attempts; public bool cleared; public int moves; public bool auto; public int par; }
    [System.Serializable] private class StatOutList { public List<StatOut> stats = new List<StatOut>(); }

    private const string StatsKey = "QueenBlitz.Stats";
    private static StatFile statFile;

    private static StatFile Stats()
    {
        if (statFile == null)
        {
            try { statFile = JsonUtility.FromJson<StatFile>(PlayerPrefs.GetString(StatsKey, "")); } catch { statFile = null; }
            if (statFile == null || statFile.stats == null) statFile = new StatFile();
        }
        return statFile;
    }

    private static void SaveStats()
    {
        PlayerPrefs.SetString(StatsKey, JsonUtility.ToJson(Stats()));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// The record for the puzzle being played, made on its first start; null once it's cleared (replays
    /// don't count) or if it was cleared before records were kept
    /// </summary>
    private static PuzzleStat Tracked()
    {
        if (Puzzles.Length == 0 || string.IsNullOrEmpty(Current.sig)) return null;
        string id = PuzzleId(PackId, Index);
        foreach (PuzzleStat s in Stats().stats)
            if (s.puzzle == id && s.sig == Current.sig) return s.cleared ? null : s;
        if (EverCleared(Index)) return null;
        var stat = new PuzzleStat { puzzle = id, sig = Current.sig, par = Current.par };
        Stats().stats.Add(stat);
        return stat;
    }

    /// <summary>Records the server doesn't have the latest of yet</summary>
    public static bool StatsPending
    {
        get { foreach (PuzzleStat s in Stats().stats) if (s.rev > s.sent) return true; return false; }
    }

    /// <summary>Up to max changed records, for the wallet's next sync, and the revs being sent</summary>
    public static List<KeyValuePair<PuzzleStat, int>> StatsToSend(int max)
    {
        var list = new List<KeyValuePair<PuzzleStat, int>>();
        foreach (PuzzleStat s in Stats().stats)
        {
            if (list.Count >= max) break;
            if (s.rev > s.sent) list.Add(new KeyValuePair<PuzzleStat, int>(s, s.rev));
        }
        return list;
    }

    public static string StatsJson(List<KeyValuePair<PuzzleStat, int>> batch)
    {
        var o = new StatOutList();
        foreach (var p in batch)
        {
            PuzzleStat s = p.Key;
            o.stats.Add(new StatOut { puzzle = s.puzzle, sig = s.sig, seconds = Mathf.Round(s.seconds * 10f) / 10f, attempts = s.attempts, cleared = s.cleared, moves = s.moves, auto = s.auto, par = s.par });
        }
        string json = JsonUtility.ToJson(o);   // {"stats":[...]}
        return json.Substring(json.IndexOf('['), json.LastIndexOf(']') - json.IndexOf('[') + 1);
    }

    /// <summary>The server has these (as they were when sent; a change since then goes next time)</summary>
    public static void StatsSent(List<KeyValuePair<PuzzleStat, int>> batch)
    {
        foreach (var p in batch) if (p.Value > p.Key.sent) p.Key.sent = p.Value;
        SaveStats();
    }

    /// <summary>The account was deleted: its records go too (a new account starts counting afresh)</summary>
    public static void ForgetStats()
    {
        Stats().stats.Clear();
        SaveStats();
    }
}

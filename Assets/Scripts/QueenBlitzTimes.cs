using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Queen Blitz solve times, for gauging how hard each puzzle really is. A clock counts up from
/// the moment a puzzle starts until it is cleared (the top bar and the CLEARED panel show it when
/// ShowTimer is on). Every clear goes into a log on the device: pack and puzzle, the time of the
/// clearing attempt, the time across every attempt since the last clear, moves and attempts.
/// The Playtest page in the Queen Blitz picker lists it.
/// </summary>
public static partial class QueenBlitz
{
    /// <summary>Seconds on this attempt's clock (the director counts it while the puzzle is in play)</summary>
    public static float Elapsed { get; set; }

    /// <summary>The clock is shown to players: only with playtest on for now</summary>
    public static bool ShowTimer => Reviewer;

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

    /// <summary>A new attempt at this puzzle starts (Begin)</summary>
    private static void StartAttempt()
    {
        Elapsed = 0f;
        string key = Key("Attempts", Index);
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
    }

    /// <summary>An attempt ended without a clear (left, restarted, out of moves): its time counts towards the next clear</summary>
    public static void EndAttempt()
    {
        if (Cleared || Elapsed <= 0f) return;
        string key = Key("TimeSpent", Index);
        PlayerPrefs.SetFloat(key, PlayerPrefs.GetFloat(key, 0f) + Elapsed);
        PlayerPrefs.Save();
        Elapsed = 0f;
    }

    /// <summary>Log a clear (before RecordClear marks it cleared, so the first clear is known)</summary>
    private static void LogSolve()
    {
        string attemptsKey = Key("Attempts", Index), spentKey = Key("TimeSpent", Index);
        LastSolve = new Solve
        {
            pack = PackId, puzzle = Index + 1, name = Current.name,
            seconds = Elapsed, totalSeconds = PlayerPrefs.GetFloat(spentKey, 0f) + Elapsed,
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
}

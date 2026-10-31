using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Apple Game Center (iPhone only; Plugins/iOS/GameCenter.mm). Our server's ratings stay the real
/// ones; this copies them to Game Center leaderboards, along with Zombies! bests and achievements.
/// Signing in also links the Game Center player to their account on our server, so a new iPhone
/// gets the same name, ratings and pawns back without any buttons.
/// The leaderboard and achievement IDs below must match App Store Connect exactly.
/// </summary>
public class GameCenter : MonoBehaviour
{
    // ── IDs (App Store Connect › Game Center) ──
    public const string DuelBoard = "chess3d.duel";
    public const string MeleeBoard = "chess3d.melee";
    public static string ZombieBoard(AIDifficulty d) => "chess3d.zombies." + ZombieMode.DifficultyName(d).ToLowerInvariant();
    /// <summary>Reaching a tier in Duel or Melee: Knight, Bishop, Rook, Queen, King</summary>
    public static string TierAchievement(string tier) => "chess3d.tier." + tier.ToLowerInvariant();
    public const string ZombieRoundsAchievement = "chess3d.zombies.round50";

    /// <summary>Ratings go to Game Center once a player has this many ranked games in that mode (the server's tier threshold)</summary>
    private const int MinRatedGames = 5;
    private const int ZombieRoundsGoal = 50;
    private static readonly string[] Tiers = { "Knight", "Bishop", "Rook", "Queen", "King" }; // lowest first

    /// <summary>Raised when signing in to Game Center switched this device to the player's existing account (their name)</summary>
    public static event Action<string> Restored;

    private const string ReceiverName = "GameCenterReceiver";
    private static GameCenter receiver;

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void _GameCenterSignIn(string receiver);
    [DllImport("__Internal")] private static extern bool _GameCenterIsSignedIn();
    [DllImport("__Internal")] private static extern void _GameCenterPostScore(string leaderboardId, long score);
    [DllImport("__Internal")] private static extern void _GameCenterReportAchievement(string achievementId);
    [DllImport("__Internal")] private static extern void _GameCenterShow();
    public static bool IsAvailable => true;
    public static bool IsSignedIn => _GameCenterIsSignedIn();
#else
    public static bool IsAvailable => false;
    public static bool IsSignedIn => false;
#endif

    /// <summary>Sign in when the app opens. Apple shows its own banner or sheet; nothing happens if the player says no.</summary>
    public static void SignIn()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (receiver == null)
        {
            var go = new GameObject(ReceiverName);
            DontDestroyOnLoad(go);
            receiver = go.AddComponent<GameCenter>();
        }
        _GameCenterSignIn(ReceiverName);
#endif
    }

    /// <summary>Open Apple's Game Center leaderboards</summary>
    public static void Show()
    {
#if UNITY_IOS && !UNITY_EDITOR
        _GameCenterShow();
#endif
    }

    private static void PostScore(string board, long score)
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (IsSignedIn) _GameCenterPostScore(board, score);
#endif
    }

    /// <summary>Report an achievement once per device (Apple keeps it after that)</summary>
    private static void Achieve(string id)
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (!IsSignedIn || PlayerPrefs.GetInt("GameCenterAchieved." + id, 0) == 1) return;
        _GameCenterReportAchievement(id);
        PlayerPrefs.SetInt("GameCenterAchieved." + id, 1);
        PlayerPrefs.Save();
#endif
    }

    // ───────────────────────── What gets posted ─────────────────────────

    /// <summary>A rating the server confirmed (after a ranked game, or from My stats)</summary>
    public static void ReportRating(string mode, int rating, int games, string tier)
    {
        if (games < MinRatedGames) return;
        PostScore(mode == "melee" ? MeleeBoard : DuelBoard, rating);
        // Every tier up to the one reached, so jumping straight to Rook also earns Knight and Bishop
        int reached = Array.IndexOf(Tiers, tier);
        for (int i = 0; i <= reached; i++) Achieve(TierAchievement(Tiers[i]));
    }

    public static void ReportRating(NetRatingChange change)
    {
        if (change != null) ReportRating(change.mode, change.after, change.games, change.tier);
    }

    /// <summary>A Zombies! run ended</summary>
    public static void ReportZombieRun(AIDifficulty difficulty, int rounds)
    {
        PostScore(ZombieBoard(difficulty), ZombieMode.Best(difficulty));
        if (rounds >= ZombieRoundsGoal) Achieve(ZombieRoundsAchievement);
    }

    // ───────────────────────── Signing in ─────────────────────────

    [Serializable] private class Proof { public string teamPlayerId; }

    public void OnGameCenterIdentity(string proofJson) => StartCoroutine(Link(proofJson));

    public void OnGameCenterUnavailable(string message) => Debug.Log($"Game Center: not signed in ({message})");

    private IEnumerator Link(string proofJson)
    {
        // Never switch accounts in the middle of an online game; the next launch links instead
        if (OnlineSession.IsActive) yield break;
        string playerId = JsonUtility.FromJson<Proof>(proofJson)?.teamPlayerId ?? "";
        OnlineClient client = OnlineClient.GetOrCreate();

        // Refresh the saved account first, so the link goes to it and not to a new one
        if (client.HasToken)
        {
            bool ok = false;
            yield return client.EnsureSignedIn((success, _) => ok = success);
            if (!ok) yield break;
        }
        string linkedAs = playerId + "|" + PlayerPrefs.GetString("OnlineUserId", "");
        if (PlayerPrefs.GetString("GameCenterLinked", "") != linkedAs)
        {
            bool restored = false;
            string error = null;
            yield return client.LinkGameCenter(proofJson, (r, e) => { restored = r; error = e; });
            if (error != null) { Debug.LogWarning($"Game Center: link failed ({error})"); yield break; }
            PlayerPrefs.SetString("GameCenterLinked", playerId + "|" + PlayerPrefs.GetString("OnlineUserId", ""));
            PlayerPrefs.Save();
            if (restored) Restored?.Invoke(client.PlayerName);
        }
        yield return CatchUp(client);
    }

    /// <summary>Post everything already earned: covers games that ended while the app was closed and missed posts</summary>
    private static IEnumerator CatchUp(OnlineClient client)
    {
        PlayerStats stats = null;
        yield return client.GetStats(null, (s, _) => stats = s);
        if (stats != null)
        {
            if (stats.duel != null) ReportRating("duel", stats.duel.rating, stats.duel.games, stats.duel.tier);
            if (stats.melee != null) ReportRating("melee", stats.melee.rating, stats.melee.games, stats.melee.tier);
        }
        foreach (AIDifficulty d in new[] { AIDifficulty.Easy, AIDifficulty.Medium, AIDifficulty.Hard })
            if (ZombieMode.Best(d) > 0) PostScore(ZombieBoard(d), ZombieMode.Best(d));
    }
}

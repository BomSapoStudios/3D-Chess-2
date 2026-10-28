using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pawns and owned items live on the game server (server-cf/src/wallet.ts), with each account.
/// The phone keeps the last answer to show offline, and queues what it earns (each earning has an
/// id, so sending a queue twice pays once) until it can send it, in batches to spare the server's
/// daily request allowance. Unlocking with pawns and App Store purchases go to the server straight
/// away. The first sync from a phone that kept pawns before brings them along once.
/// PawnBank, Store and QueenBlitz read and spend through here.
/// </summary>
public class Wallet : MonoBehaviour
{
    [Serializable]
    private class State
    {
        public string userId = "";
        public int balance, lifetime, zombieLeftToday;
        public string[] items = new string[0];
        public bool migrated;
        public int utcDay;   // the day zombieLeftToday is for
    }

    [Serializable]
    private class Earning
    {
        public string id, kind, puzzle = "", run = "", brain = "";
        public int pawns, kills;
        public int expect;   // what the phone expects it to pay (shown until the server answers)
    }

    [Serializable] private class EarningList { public List<Earning> events = new List<Earning>(); }
    [Serializable] private class ItemList { public List<string> items = new List<string>(); }
    [Serializable] private class Migration { public int balance, lifetime; public string[] items, cleared; }
    [Serializable] private class SyncReply { public State wallet; }
    [Serializable] private class SpendBody { public string requestId, item; }
    [Serializable] private class PurchaseBody { public string[] transactions; }
    [Serializable] private class ErrorReply { public string error; }

    /// <summary>Pawns or owned items changed (menus redraw)</summary>
    public static event Action Changed;

    private const string StateKey = "Wallet.State", QueueKey = "Wallet.Queue", LocalKey = "Wallet.LocalItems", MigratedKey = "Wallet.Migrated";
    private const float BlitzFlushDelay = 8f;   // clears close together go in one request
    private const float StaleSeconds = 6 * 3600f;

    private static Wallet instance;
    private static State state;
    private static EarningList queue;
    private static ItemList localItems;   // bought with money, not yet confirmed by the server
    private static readonly HashSet<string> inFlight = new HashSet<string>();
    private static bool flushing, lastFlushOk;
    private static float flushAt = -1f, lastSync = -1e9f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (instance != null) return;
        Load();
        var go = new GameObject("Wallet");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<Wallet>();
    }

    private static void Load()
    {
        if (state != null) return;
        state = FromPrefs<State>(StateKey) ?? new State();
        queue = FromPrefs<EarningList>(QueueKey) ?? new EarningList();
        localItems = FromPrefs<ItemList>(LocalKey) ?? new ItemList();
    }

    private static T FromPrefs<T>(string key) where T : class
    {
        string json = PlayerPrefs.GetString(key, "");
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonUtility.FromJson<T>(json); } catch { return null; }
    }

    private static void Save()
    {
        PlayerPrefs.SetString(StateKey, JsonUtility.ToJson(state));
        PlayerPrefs.SetString(QueueKey, JsonUtility.ToJson(queue));
        PlayerPrefs.SetString(LocalKey, JsonUtility.ToJson(localItems));
        PlayerPrefs.Save();
    }

    private void Start() => SyncIfDue();

    private void OnApplicationPause(bool paused)
    {
        if (paused) { if (queue.events.Count > 0) StartCoroutine(Flush(null)); }
        else SyncIfDue();
    }

    private void Update()
    {
        if (flushAt >= 0f && Time.unscaledTime >= flushAt && !flushing)
        {
            flushAt = -1f;
            StartCoroutine(Flush(null));
        }
    }

    /// <summary>Sync on launch and on coming back, if there's something to send or the last answer is old</summary>
    private void SyncIfDue()
    {
        if (queue.events.Count > 0 || !Migrated || Time.unscaledTime - lastSync > StaleSeconds) FlushSoon(2f);
    }

    private static void FlushSoon(float seconds)
    {
        float at = Time.unscaledTime + seconds;
        if (flushAt < 0f || at < flushAt) flushAt = at;
    }

    // ───────── Reading ─────────

    private static bool Migrated => PlayerPrefs.GetInt(MigratedKey, 0) == 1;
    private static int UtcDay => (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalDays;

    private static int Expected(Func<Earning, bool> which)
    {
        Load();
        int sum = 0;
        foreach (Earning e in queue.events) if (which(e)) sum += e.expect;
        return sum;
    }

    /// <summary>Pawns to spend: the server's count plus what's still queued</summary>
    public static int Total
    {
        get
        {
            Load();
            int known = Migrated ? state.balance : PawnBank.PhoneTotal;
            return Mathf.Max(0, known + Expected(e => true));
        }
    }

    public static int LifetimeEarned
    {
        get
        {
            Load();
            int known = Migrated ? state.lifetime : PawnBank.PhoneLifetime;
            return known + Expected(e => e.expect > 0);
        }
    }

    /// <summary>What Zombies! can still pay today (the server's day is UTC)</summary>
    public static int ZombieLeftToday
    {
        get
        {
            Load();
            int left = Migrated && state.utcDay == UtcDay ? state.zombieLeftToday : PawnBank.ZombieDailyCap;
            return Mathf.Max(0, left - Expected(e => e.kind == "zombie"));
        }
    }

    /// <summary>
    /// Owns this item ("set.AbyssGlow", "blitz.feast"): on the server, bought here and not yet confirmed,
    /// or (before this phone's first sync) unlocked the old way under legacyKey
    /// </summary>
    public static bool Owns(string item, string legacyKey)
    {
        Load();
        string id = item.ToLowerInvariant();
        if (Array.IndexOf(state.items, id) >= 0 || localItems.items.Contains(id)) return true;
        return !Migrated && PlayerPrefs.GetInt(legacyKey, 0) == 1;
    }

    // ───────── Earning ─────────

    private static void Add(Earning e, float flushIn)
    {
        Load();
        e.id = Guid.NewGuid().ToString();
        queue.events.Add(e);
        Save();
        Changed?.Invoke();
        if (flushIn >= 0f) FlushSoon(flushIn);
    }

    /// <summary>A Queen Blitz puzzle cleared for the first time (the server pays it only once)</summary>
    public static void EarnBlitz(string puzzleId, int pawns) =>
        Add(new Earning { kind = "blitz", puzzle = puzzleId, pawns = pawns, expect = pawns }, BlitzFlushDelay);

    /// <summary>
    /// A Zombies! run's kills so far, paying `pay` more pawns. One queued entry per run is updated as the
    /// run goes; it's sent when the run ends or the app goes to the background.
    /// </summary>
    public static void EarnZombies(string runId, string brain, int kills, int pay)
    {
        Load();
        foreach (Earning e in queue.events)
        {
            if (e.kind != "zombie" || e.run != runId || inFlight.Contains(e.id)) continue;
            e.kills = kills;
            e.expect += pay;
            Save();
            Changed?.Invoke();
            return;
        }
        Add(new Earning { kind = "zombie", run = runId, brain = brain, kills = kills, expect = pay }, -1f);
    }

    /// <summary>A Zombies! run ended: send its pawns</summary>
    public static void RunEnded() => FlushSoon(0.5f);

    /// <summary>Queen Blitz Auto-place: a pawn, taken now and settled with the server later</summary>
    public static bool SpendAutoPlace()
    {
        if (Total < PawnBank.AutoPlaceCost) return false;
        Add(new Earning { kind = "autoplace", expect = -PawnBank.AutoPlaceCost }, BlitzFlushDelay);
        return true;
    }

    // ───────── Talking to the server ─────────

    /// <summary>Send queued earnings (and, the first time, this phone's old pawns); done(true) once the server answered</summary>
    private static IEnumerator Flush(Action<bool> done)
    {
        if (flushing)
        {
            while (flushing) yield return null;
            done?.Invoke(lastFlushOk);
            yield break;
        }
        flushing = true;
        bool ok = false;
        yield return SignIn(signedIn => ok = signedIn);
        if (ok)
        {
            ok = false;
            var batch = new EarningList();
            foreach (Earning e in queue.events) { if (batch.events.Count >= 100) break; batch.events.Add(e); inFlight.Add(e.id); }
            bool migrate = !Migrated;
            string body = JsonUtility.ToJson(batch);
            if (migrate) body = body.Substring(0, body.Length - 1) + ",\"migrate\":" + JsonUtility.ToJson(PhoneMigration()) + "}";
            long status = 0;
            string text = null;
            yield return OnlineClient.GetOrCreate().Request("POST", "/wallet/sync", body, (s, t) => { status = s; text = t; });
            if (status == 200)
            {
                foreach (Earning e in batch.events) queue.events.Remove(e);
                if (migrate) PlayerPrefs.SetInt(MigratedKey, 1);
                Apply(JsonUtility.FromJson<SyncReply>(text)?.wallet);
                ok = true;
            }
            inFlight.Clear();
        }
        lastFlushOk = ok;
        flushing = false;
        if (ok && queue.events.Count > 0) FlushSoon(1f);
        done?.Invoke(ok);
    }

    private static IEnumerator SignIn(Action<bool> done)
    {
        bool ok = false;
        yield return OnlineClient.GetOrCreate().EnsureSignedIn((signedIn, error) => ok = signedIn);
        if (ok && state.userId != OnlineClient.UserId)
        {
            // A different account on this phone: the cached wallet was someone else's
            state = new State { userId = OnlineClient.UserId };
            Save();
        }
        done(ok);
    }

    /// <summary>The server's answer becomes what the phone shows</summary>
    private static void Apply(State fresh)
    {
        if (fresh == null) return;
        fresh.userId = OnlineClient.UserId;
        fresh.utcDay = UtcDay;
        fresh.items = fresh.items ?? new string[0];
        state = fresh;
        localItems.items.RemoveAll(i => Array.IndexOf(state.items, i) >= 0);
        lastSync = Time.unscaledTime;
        Save();
        Changed?.Invoke();
    }

    /// <summary>What this phone had before pawns moved to the server</summary>
    private static Migration PhoneMigration()
    {
        var items = new List<string>();
        foreach (string id in Store.ProductIds()) if (PlayerPrefs.GetInt("Owned." + id, 0) == 1) items.Add(id.ToLowerInvariant());
        foreach (string pack in QueenBlitz.PawnPacks()) if (PlayerPrefs.GetInt("QueenBlitzOwned." + pack, 0) == 1) items.Add("blitz." + pack);
        return new Migration { balance = PawnBank.PhoneTotal, lifetime = PawnBank.PhoneLifetime, items = items.ToArray(), cleared = QueenBlitz.ClearedPuzzleIds().ToArray() };
    }

    /// <summary>Unlock an item for pawns on the server; done(null) when it's yours, else a message to show</summary>
    public static void Unlock(string item, Action<string> done)
    {
        Load();
        instance.StartCoroutine(UnlockRoutine(item.ToLowerInvariant(), done));
    }

    private static IEnumerator UnlockRoutine(string item, Action<string> done)
    {
        const string offline = "Connect to the internet to unlock with pawns.";
        bool synced = false;
        // Queued earnings count toward the price, so they go first; with nothing queued, signing in is enough
        if (queue.events.Count > 0 || !Migrated) yield return Flush(ok => synced = ok);
        else yield return SignIn(ok => synced = ok);
        if (!synced) { done(offline); yield break; }
        long status = 0;
        string text = null;
        string body = JsonUtility.ToJson(new SpendBody { requestId = Guid.NewGuid().ToString(), item = item });
        yield return OnlineClient.GetOrCreate().Request("POST", "/wallet/spend", body, (s, t) => { status = s; text = t; });
        if (status == 200) { Apply(JsonUtility.FromJson<SyncReply>(text)?.wallet); done(null); yield break; }
        if (status == 409) { yield return Refresh(); done(null); yield break; }   // already yours (an earlier try got through)
        if (status == 0) { done(offline); yield break; }
        string error = null;
        try { error = JsonUtility.FromJson<ErrorReply>(text)?.error; } catch { /* not JSON */ }
        done(string.IsNullOrEmpty(error) ? "That didn't work, please try again." : error);
    }

    private static IEnumerator Refresh()
    {
        long status = 0;
        string text = null;
        yield return OnlineClient.GetOrCreate().Request("GET", "/wallet", null, (s, t) => { status = s; text = t; });
        if (status == 200) Apply(JsonUtility.FromJson<State>(text));
    }

    /// <summary>
    /// An App Store purchase (Purchases): it's the player's straight away on this phone, and the signed
    /// transaction goes to the server. done(true) once the server has it (then the App Store is told
    /// it's finished); false leaves it to be sent again next launch.
    /// </summary>
    public static void RecordPurchase(string item, string jws, Action<bool> done)
    {
        Load();
        string id = item.ToLowerInvariant();
        if (!localItems.items.Contains(id) && Array.IndexOf(state.items, id) < 0) { localItems.items.Add(id); Save(); Changed?.Invoke(); }
        if (string.IsNullOrEmpty(jws)) { done(true); return; }   // the editor's test store has no signed transactions
        instance.StartCoroutine(SendPurchases(new[] { jws }, done));
    }

    /// <summary>Purchases the App Store has that the server doesn't show yet (after a restore, or from before the server kept them)</summary>
    public static void SendPurchases(IEnumerable<KeyValuePair<string, string>> itemsAndJws)
    {
        Load();
        var list = new List<string>();
        foreach (var p in itemsAndJws)
            if (!string.IsNullOrEmpty(p.Value) && Array.IndexOf(state.items, p.Key.ToLowerInvariant()) < 0) list.Add(p.Value);
        if (list.Count > 0) instance.StartCoroutine(SendPurchases(list.ToArray(), null));
    }

    private static IEnumerator SendPurchases(string[] jws, Action<bool> done)
    {
        bool ok = false;
        yield return SignIn(signedIn => ok = signedIn);
        if (ok)
        {
            ok = false;
            long status = 0;
            string text = null;
            yield return OnlineClient.GetOrCreate().Request("POST", "/wallet/purchase", JsonUtility.ToJson(new PurchaseBody { transactions = jws }), (s, t) => { status = s; text = t; });
            if (status == 200) { Apply(JsonUtility.FromJson<SyncReply>(text)?.wallet); ok = true; }
        }
        done?.Invoke(ok);
    }

    /// <summary>The account was deleted: its wallet goes too (the phone starts over)</summary>
    public static void Forget()
    {
        state = new State();
        queue = new EarningList();
        localItems = new ItemList();
        PlayerPrefs.SetInt(MigratedKey, 1);   // this phone's old pawns went with the account
        Save();
        Changed?.Invoke();
    }
}

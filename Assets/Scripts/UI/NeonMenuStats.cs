using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The stats screen: a player's profile (tier, ratings, record, recent games) and the
/// leaderboards. Opened from the Online screen (your own), a friend's name, or the
/// Leaderboard button on the Friends screen. Ratings come from the server (ratings.ts).
/// </summary>
public partial class NeonMenu
{
    private GameObject statsScreen;
    private TextMeshProUGUI statsTitle, statsStatus;
    private RectTransform statsLeft, statsRight, statsList;
    private GameObject boardChips;
    private Action statsBack;
    private string boardMode = "duel", boardScope = "friends";
    private int statsRequest; // ignore answers to an older request

    private void BuildStatsScreen()
    {
        statsScreen = NewRect("Stats Screen", safeRoot).gameObject;
        RectTransform root = statsScreen.GetComponent<RectTransform>();
        Stretch(root);

        RectTransform header = NewRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(16, -64);
        header.offsetMax = new Vector2(-24, -12);
        Button back = NeonButton(header, "<", new Vector2(48, 48), NeonTheme.Cyan, NeonTheme.Pink, 0f, 24, () => (statsBack ?? ShowOnline)());
        Place((RectTransform)back.transform, new Vector2(0, 0.5f), new Vector2(48, 48), new Vector2(28, 0));
        statsTitle = NewText("Title", header, "", 30, NeonTheme.Lime, TextAlignmentOptions.Left);
        NeonTheme.ApplyFont(statsTitle, display: true);
        statsTitle.enableAutoSizing = true;
        statsTitle.fontSizeMin = 18;
        statsTitle.fontSizeMax = 30;
        statsTitle.rectTransform.anchorMin = new Vector2(0, 0);
        statsTitle.rectTransform.anchorMax = new Vector2(1, 1);
        statsTitle.rectTransform.offsetMin = new Vector2(70, 0);
        statsTitle.rectTransform.offsetMax = new Vector2(GameCenter.IsAvailable ? -490 : -380, 0);

        // Leaderboard filters, in the header's right side
        RectTransform chipsBox = NewRect("Filters", header);
        chipsBox.anchorMin = new Vector2(1, 0);
        chipsBox.anchorMax = new Vector2(1, 1);
        chipsBox.pivot = new Vector2(1, 0.5f);
        chipsBox.sizeDelta = new Vector2(GameCenter.IsAvailable ? 480 : 370, 0);
        var chipsLayout = chipsBox.gameObject.AddComponent<VerticalLayoutGroup>();
        chipsLayout.childControlWidth = true;
        chipsLayout.childControlHeight = true;
        chipsLayout.childForceExpandWidth = true;
        chipsLayout.childForceExpandHeight = false;
        var filters = new System.Collections.Generic.List<(string, System.Action, System.Func<bool>, System.Func<bool>)>
        {
            ("Friends", () => { boardScope = "friends"; LoadBoard(); }, () => boardScope == "friends", () => true),
            ("Everyone", () => { boardScope = "everyone"; LoadBoard(); }, () => boardScope == "everyone", () => true),
            ("Duel", () => { boardMode = "duel"; LoadBoard(); }, () => boardMode == "duel", () => true),
            ("Melee", () => { boardMode = "melee"; LoadBoard(); }, () => boardMode == "melee", () => true),
        };
        // iPhone: Apple's Game Center leaderboards (a copy of ours, with Apple friends)
        if (GameCenter.IsAvailable) filters.Add(("Game Center", GameCenter.Show, () => false, () => GameCenter.IsSignedIn));
        ChipRow(chipsBox, filters.ToArray(), NeonTheme.Yellow, 14);
        boardChips = chipsBox.gameObject;

        statsLeft = Card(root, "Profile Card", NeonTheme.Cyan, new Vector2(0f, 0f), new Vector2(0.42f, 1f), new Vector2(16, 14), new Vector2(-8, -74));
        statsRight = Card(root, "Record Card", NeonTheme.Pink, new Vector2(0.42f, 0f), new Vector2(1f, 1f), new Vector2(8, 14), new Vector2(-24, -74));
        statsList = ScrollList(statsRight);

        statsStatus = NewText("Status", root, "", 16, NeonTheme.Yellow, TextAlignmentOptions.Center);
        statsStatus.rectTransform.anchorMin = new Vector2(0, 0.5f);
        statsStatus.rectTransform.anchorMax = new Vector2(1, 0.5f);
        statsStatus.rectTransform.sizeDelta = new Vector2(0, 40);
        statsScreen.SetActive(false);
    }

    /// <summary>A scrolling column that fills the rest of a card</summary>
    private RectTransform ScrollList(RectTransform parent)
    {
        RectTransform viewport = NewRect("Viewport", parent);
        viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // catches drags for scrolling
        RectTransform list = NewRect("List", viewport);
        list.anchorMin = new Vector2(0, 1);
        list.anchorMax = new Vector2(1, 1);
        list.pivot = new Vector2(0.5f, 1);
        list.offsetMin = list.offsetMax = Vector2.zero;
        var layout = list.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.content = list;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return list;
    }

    private void OpenStatsScreen(Action back)
    {
        statsBack = back;
        menuScreen.SetActive(false);
        setupScreen.SetActive(false);
        customizeScreen.SetActive(false);
        onlineScreen.SetActive(false);
        friendsScreen.SetActive(false);
        if (notifyScreen != null) notifyScreen.SetActive(false);
        if (accessScreen != null) accessScreen.SetActive(false);
        statsScreen.SetActive(true);
        Clear(statsLeft, keep: 0);
        Clear(statsList, keep: 0);
        statsStatus.text = "Loading…";
    }

    private static void Clear(RectTransform parent, int keep)
    {
        for (int i = parent.childCount - 1; i >= keep; i--) Destroy(parent.GetChild(i).gameObject);
    }

    // ───────────────────────── Profile ─────────────────────────

    /// <summary>A player's profile; null for your own</summary>
    private void ShowProfile(string userId, Action back)
    {
        OpenStatsScreen(back);
        boardChips.SetActive(false);
        statsTitle.text = userId == null ? "YOUR STATS" : "PLAYER";
        statsLeft.gameObject.SetActive(true);
        statsRight.offsetMin = new Vector2(8, 14);
        ((RectTransform)statsRight).anchorMin = new Vector2(0.42f, 0f);
        int request = ++statsRequest;
        StartCoroutine(LoadProfile(userId, request));
    }

    private IEnumerator LoadProfile(string userId, int request)
    {
        bool signedIn = false;
        yield return SignIn(ok => signedIn = ok);
        if (!signedIn) { statsStatus.text = "Couldn't reach the game server"; yield break; }
        PlayerStats stats = null;
        string error = null;
        yield return OnlineClient.Instance.GetStats(userId, (s, e) => { stats = s; error = e; });
        if (request != statsRequest) yield break;
        if (stats == null) { statsStatus.text = error ?? "Couldn't load stats"; yield break; }
        statsStatus.text = "";
        statsTitle.text = userId == null ? "YOUR STATS" : stats.name.ToUpperInvariant();
        FillProfile(stats, userId == null);
    }

    private void FillProfile(PlayerStats s, bool mine)
    {
        Clear(statsLeft, keep: 0);
        Clear(statsList, keep: 0);

        TextMeshProUGUI tier = NewText("Tier", statsLeft, s.tier.ToUpperInvariant(), 30, TierColor(s.tier), TextAlignmentOptions.Left);
        NeonTheme.ApplyFont(tier, display: true);
        tier.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
        Line(statsLeft, s.name, NeonTheme.Lavender, 15);
        Line(statsLeft, RatingLine("Duel", s.duel), NeonTheme.White, 17);
        Line(statsLeft, RatingLine("Melee", s.melee), NeonTheme.White, 17);
        if (s.headToHead != null && (s.headToHead.wins + s.headToHead.losses + s.headToHead.draws) > 0)
            Line(statsLeft, $"You vs them: {s.headToHead.wins} won, {s.headToHead.losses} lost, {s.headToHead.draws} drawn", NeonTheme.Yellow, 15);
        if (mine)
        {
            SectionLabel(statsLeft, "vs the computer (this phone)", NeonTheme.Cyan);
            var parts = new List<string>();
            foreach (var (level, name) in new[] { (AIDifficulty.Easy, "Pea"), (AIDifficulty.Medium, "Walnut"), (AIDifficulty.Hard, "Galaxy") })
            {
                var (w, l, d) = ComputerStats.For(level);
                parts.Add($"{name} {w}-{l}" + (d > 0 ? $"-{d}" : ""));
            }
            Line(statsLeft, string.Join("   ", parts), NeonTheme.White, 15);
        }

        // Record, endings, recent games
        if (s.games == 0)
        {
            Line(statsList, "No rated games yet. Online games against people count toward your rating and stats.", NeonTheme.Lavender, 15, 44);
            return;
        }
        Line(statsList, $"{s.games} games: {s.wins} won, {s.losses} lost, {s.draws} drawn ({s.winRate}% wins)", NeonTheme.White, 16);
        Line(statsList, $"Winning streak {s.streak}, best {s.bestStreak}", NeonTheme.White, 15);
        StatsEndings e = s.endings;
        if (e != null) Line(statsList, $"Endings: {e.checkmate} checkmate, {e.resignation} resigned, {e.timeout} out of time, {e.draw} drawn, {e.walkout} walked out", NeonTheme.Lavender, 14, 38);
        if (s.captures != null && (s.captures.taken + s.captures.lost) > 0)
            Line(statsList, $"Pieces taken {s.captures.taken}, lost {s.captures.lost}" + (string.IsNullOrEmpty(s.captures.mostTaken) ? "" : $"   Most taken: {s.captures.mostTaken}s"), NeonTheme.Lavender, 14);
        Line(statsList, $"Walk-out rate {s.walkoutRate}%" + (string.IsNullOrEmpty(s.favorite) ? "" : $"   Favorite: {s.favorite}"), NeonTheme.Lavender, 14);
        if (s.recent != null && s.recent.Length > 0)
        {
            SectionLabel(statsList, "Recent games", NeonTheme.PinkSoft);
            foreach (RecentGame g in s.recent)
            {
                string result = g.result == "win" ? "Won" : g.result == "draw" ? "Drew" : "Lost";
                string vs = g.opponents != null && g.opponents.Length > 0 ? " vs " + string.Join(", ", g.opponents) : "";
                string change = (g.change >= 0 ? "+" : "") + g.change;
                Color c = g.result == "win" ? NeonTheme.Lime : g.result == "draw" ? NeonTheme.Yellow : NeonTheme.PinkSoft;
                Line(statsList, $"{result}{vs} · {(g.mode == "melee" ? "Melee" : "Duel")} {change}", c, 14);
            }
        }
    }

    private static string RatingLine(string mode, ModeRating r) =>
        r == null || r.games == 0 ? $"{mode}: not played yet" : $"{mode}: {r.rating} (best {r.best}, {r.games} game{(r.games == 1 ? "" : "s")})";

    private TextMeshProUGUI Line(RectTransform parent, string text, Color color, float size, float height = 24)
    {
        TextMeshProUGUI t = NewText("Line", parent, text, size, color, TextAlignmentOptions.Left);
        t.fontStyle = FontStyles.Normal;
        t.enableWordWrapping = true;
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return t;
    }

    public static Color TierColor(string tier)
    {
        switch (tier)
        {
            case "King": return NeonTheme.Yellow;
            case "Queen": return NeonTheme.Pink;
            case "Rook": return NeonTheme.Cyan;
            case "Bishop": return NeonTheme.Lime;
            case "Knight": return NeonTheme.PinkSoft;
            default: return NeonTheme.Lavender;
        }
    }

    // ───────────────────────── Leaderboard ─────────────────────────

    private void ShowLeaderboard()
    {
        OpenStatsScreen(ShowFriends);
        boardChips.SetActive(true);
        statsTitle.text = "LEADERBOARD";
        statsLeft.gameObject.SetActive(false);
        ((RectTransform)statsRight).anchorMin = new Vector2(0f, 0f);
        statsRight.offsetMin = new Vector2(16, 14);
        Refresh(); // chip highlights
        LoadBoard();
    }

    private void LoadBoard()
    {
        int request = ++statsRequest;
        statsStatus.text = "Loading…";
        Clear(statsList, keep: 0);
        StartCoroutine(LoadBoardRoutine(request));
    }

    private IEnumerator LoadBoardRoutine(int request)
    {
        bool signedIn = false;
        yield return SignIn(ok => signedIn = ok);
        if (!signedIn) { statsStatus.text = "Couldn't reach the game server"; yield break; }
        LeaderboardEntry[] players = null;
        string error = null;
        yield return OnlineClient.Instance.GetLeaderboard(boardMode, boardScope, (p, e) => { players = p; error = e; });
        if (request != statsRequest) yield break;
        if (players == null) { statsStatus.text = error ?? "Couldn't load the leaderboard"; yield break; }
        statsStatus.text = "";
        Clear(statsList, keep: 0);
        if (players.Length == 0)
        {
            Line(statsList, boardScope == "friends"
                ? "Nobody here yet. Play rated games with friends to fill this up."
                : "Nobody has 5 rated games yet. Be the first!", NeonTheme.Lavender, 15, 44);
            yield break;
        }
        string me = PlayerPrefs.GetString("OnlineUserId", "");
        foreach (LeaderboardEntry p in players)
        {
            string id = p.userId;
            RectTransform row = NewRect("Row", statsList);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            TextMeshProUGUI rank = NewText("Rank", row, $"{p.rank}.", 16, NeonTheme.Lavender, TextAlignmentOptions.Right);
            rank.gameObject.AddComponent<LayoutElement>().preferredWidth = 36;
            TextMeshProUGUI name = NewText("Name", row, p.name + (id == me ? " (you)" : ""), 16, id == me ? NeonTheme.Lime : NeonTheme.White, TextAlignmentOptions.Left);
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            TextMeshProUGUI tier = NewText("Tier", row, p.tier, 15, TierColor(p.tier), TextAlignmentOptions.Left);
            tier.gameObject.AddComponent<LayoutElement>().preferredWidth = 80;
            TextMeshProUGUI rating = NewText("Rating", row, $"{p.rating}", 16, NeonTheme.Yellow, TextAlignmentOptions.Right);
            rating.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
            TextMeshProUGUI games = NewText("Games", row, $"{p.games} games", 13, NeonTheme.Lavender, TextAlignmentOptions.Right);
            games.gameObject.AddComponent<LayoutElement>().preferredWidth = 80;
            // Tap a player to see their profile
            Button open = row.gameObject.AddComponent<Button>();
            row.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.001f);
            open.onClick.AddListener(() => { Haptics.Selection(); ShowProfile(id == me ? null : id, ShowLeaderboard); });
        }
    }
}

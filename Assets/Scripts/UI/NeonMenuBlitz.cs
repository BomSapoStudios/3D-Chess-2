using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Queen Blitz from the main menu. It opens on the Daily puzzle and the packs (progress, or the
/// price of a locked one); a pack shows its puzzles ten at a time, one tab per chapter, with the
/// pawns each pays or your best. The Daily view shows a week at a time, back through the archive.
/// </summary>
public partial class NeonMenu
{
    private GameObject blitzPicker;
    private bool blitzRun;
    private int blitzChoice;
    private TextMeshProUGUI blitzTitle;
    private TextMeshProUGUI blitzPawnsLabel;
    private RectTransform blitzContent;
    private int blitzChapter;
    private enum BlitzView { Packs, Puzzles, Buy, Daily }
    private BlitzView blitzView;

    private void ShowBlitzPicker()
    {
        if (blitzPicker == null) BuildBlitzPicker();
        blitzDailyFetched.Clear();
        ShowBlitzPacks();
        // Pick up the daily puzzles in the background; the Daily card updates when they arrive
        StartCoroutine(QueenBlitz.FetchDailyWindow(() => { if (blitzPicker.activeSelf && blitzView == BlitzView.Packs) ShowBlitzPacks(); }));
        blitzPicker.transform.SetAsLastSibling();
        blitzPicker.SetActive(true);
    }

    private void BuildBlitzPicker()
    {
        // Dim the menu; tapping outside the card closes the picker
        Image dim = NewImage("Blitz Picker", transform, new Color(0f, 0f, 0f, 0.65f));
        Stretch(dim.rectTransform);
        Button closeArea = dim.gameObject.AddComponent<Button>();
        closeArea.transition = Selectable.Transition.None;
        closeArea.onClick.AddListener(() => blitzPicker.SetActive(false));
        blitzPicker = dim.gameObject;

        Image card = NewImage("Card", dim.transform, NeonTheme.Ground);
        card.sprite = NeonTheme.Rounded;
        card.type = Image.Type.Sliced;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(620, 330), Vector2.zero);
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // taps on the card don't close it
        Image border = NewImage("Border", card.transform, NeonTheme.Yellow);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        blitzTitle = NewText("Title", card.transform, "QUEEN BLITZ", 30, NeonTheme.Yellow, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(blitzTitle, display: true);
        Place(blitzTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(380, 46), new Vector2(0, -36));
        blitzPawnsLabel = NewText("Pawns", card.transform, "", 15, NeonTheme.Yellow, TextAlignmentOptions.Right);
        Place(blitzPawnsLabel.rectTransform, new Vector2(1f, 1f), new Vector2(200, 24), new Vector2(-110, -22));
        // Five quick taps on "Your pawns" switch the playtest Review pack on or off
        blitzPawnsLabel.raycastTarget = true;
        Button secret = blitzPawnsLabel.gameObject.AddComponent<Button>();
        secret.transition = Selectable.Transition.None;
        secret.onClick.AddListener(TapPawnsLabel);

        // Everything below the title is rebuilt for each view
        blitzContent = NewRect("Content", card.transform);
        Stretch(blitzContent);
        FitCardForTextSize(card.rectTransform);
        blitzPicker.SetActive(false);
    }

    private int secretTaps;
    private float secretTapTime;

    private void TapPawnsLabel()
    {
        secretTaps = Time.unscaledTime - secretTapTime < 0.6f ? secretTaps + 1 : 1;
        secretTapTime = Time.unscaledTime;
        if (secretTaps < 5) return;
        secretTaps = 0;
        QueenBlitz.Reviewer = !QueenBlitz.Reviewer;
        ShowToast(QueenBlitz.Reviewer ? "Review pack on" : "Review pack off");
        ShowBlitzPicker();
    }

    private void ClearBlitzContent()
    {
        for (int i = blitzContent.childCount - 1; i >= 0; i--) Destroy(blitzContent.GetChild(i).gameObject);
        blitzPawnsLabel.text = $"Your pawns: {PawnBank.Total}";
    }

    private TextMeshProUGUI BlitzText(string text, float size, Color color, Vector2 anchor, Vector2 box, Vector2 position)
    {
        TextMeshProUGUI t = NewText("Text", blitzContent, text, size, color, TextAlignmentOptions.Center);
        t.fontStyle = FontStyles.Normal;
        t.enableWordWrapping = true;
        Place(t.rectTransform, anchor, box, position);
        return t;
    }

    // ── Packs ──

    /// <summary>Back from a puzzle: the picker, open on that puzzle's pack and chapter</summary>
    private void ReturnToBlitzPicker(string packId, int index)
    {
        ShowBlitzPicker();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        // Screenshots (BlitzDemo 95 with DebugBlitzPack "packs" or "playtest"): the pack pages themselves
        if (packId == "packs") return;
        if (packId == "playtest") { ShowBlitzPlaytest(); return; }
#endif
        if (QueenBlitz.IsDaily(packId)) ShowBlitzDaily(0);
        else if (QueenBlitz.Owns(QueenBlitz.Info(packId))) ShowBlitzPuzzles(packId, index / QueenBlitz.ChapterSize);
    }

    private void ShowBlitzPacks()
    {
        ClearBlitzContent();
        blitzTitle.text = "QUEEN BLITZ";
        BlitzText("One queen. Clear every pawn before your moves run out. Your first clear of a puzzle keeps its pawns.",
            15, NeonTheme.Lavender, new Vector2(0.5f, 1f), new Vector2(560, 44), new Vector2(0, -84));
        blitzView = BlitzView.Packs;

        var tiles = new System.Collections.Generic.List<PackTile>();
        // The Daily puzzle comes first
        bool todayDone = QueenBlitz.DailyCleared(QueenBlitz.Today);
        int streak = QueenBlitz.DailyStreak();
        string dailyUnder = QueenBlitz.Today < QueenBlitz.DailyEpoch ? $"Starts {QueenBlitz.DailyEpoch:MMM d}"
            : todayDone ? (streak > 1 ? $"Done! {streak} days in a row" : "Done for today!")
            : streak > 0 ? $"{streak}-day streak" : $"#{QueenBlitz.DailyNumber(QueenBlitz.Today)} is ready";
        tiles.Add(new PackTile("Daily", todayDone ? NeonTheme.Lavender : NeonTheme.Yellow, dailyUnder, NeonTheme.Yellow, () => ShowBlitzDaily(0)));
        var review = new System.Collections.Generic.List<QueenBlitz.PackInfo>();
        foreach (QueenBlitz.PackInfo pack in QueenBlitz.Available())
        {
            if (pack.review) review.Add(pack);
            else tiles.Add(Tile(pack));
        }
        // Brandon's playtest packs (after the five taps) wait on a page of their own
        if (review.Count > 0)
            tiles.Add(new PackTile("Playtest", NeonTheme.Pink, $"{review.Count} test packs", NeonTheme.PinkSoft, ShowBlitzPlaytest));
        PackGrid(tiles);
    }

    /// <summary>The review and test packs, on their own page so the main one stays readable</summary>
    private void ShowBlitzPlaytest()
    {
        ClearBlitzContent();
        blitzView = BlitzView.Packs;
        blitzTitle.text = "PLAYTEST";
        BlitzBack(ShowBlitzPacks);
        BlitzText("Puzzles waiting for your playtest notes; names show tier and score.",
            15, NeonTheme.Lavender, new Vector2(0.5f, 1f), new Vector2(560, 44), new Vector2(0, -84));
        var tiles = new System.Collections.Generic.List<PackTile>();
        foreach (QueenBlitz.PackInfo pack in QueenBlitz.Available())
            if (pack.review) tiles.Add(Tile(pack));
        PackGrid(tiles);
    }

    private PackTile Tile(QueenBlitz.PackInfo pack)
    {
        int total = QueenBlitz.PuzzlesOf(pack.id).Length;
        bool owned = QueenBlitz.Owns(pack);
        Color color = pack.review ? NeonTheme.Pink : owned ? NeonTheme.Lime : NeonTheme.Muted;
        string under = owned ? $"{QueenBlitz.ClearedCount(pack.id)} / {total} cleared" : $"{total} puzzles · locked";
        return new PackTile(pack.name, color, under, owned ? NeonTheme.Yellow : NeonTheme.Lavender,
            () => { if (QueenBlitz.Owns(pack)) ShowBlitzPuzzles(pack.id, FirstOpenChapter(pack.id)); else ShowBlitzBuy(pack); });
    }

    private readonly struct PackTile
    {
        public readonly string name, under;
        public readonly Color color, underColor;
        public readonly System.Action open;
        public PackTile(string name, Color color, string under, Color underColor, System.Action open)
        { this.name = name; this.color = color; this.under = under; this.underColor = underColor; this.open = open; }
    }

    /// <summary>
    /// Packs as same-size pills, four to a row (fewer when there are fewer), the name fitted to the
    /// pill and the progress under it; more rows than fit scroll up and down
    /// </summary>
    private void PackGrid(System.Collections.Generic.List<PackTile> tiles)
    {
        const float areaW = 580f, areaH = 196f, pillH = 52f, rowH = 84f, gap = 12f;
        int cols = Mathf.Clamp(tiles.Count, 1, 4);
        int rows = (tiles.Count + cols - 1) / cols;
        float pillW = Mathf.Min(150f, (areaW - gap * (cols - 1)) / cols);
        float contentH = rows * rowH;

        RectTransform view = NewRect("Pack List", blitzContent);
        Place(view, new Vector2(0.5f, 1f), new Vector2(areaW, areaH), new Vector2(0f, -116f - areaH / 2f));
        view.gameObject.AddComponent<RectMask2D>();
        Image catcher = view.gameObject.AddComponent<Image>();
        catcher.color = new Color(0f, 0f, 0f, 0f); // takes the drags
        RectTransform content = NewRect("Packs", view);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = new Vector2(0f, Mathf.Max(contentH, areaH));
        content.anchoredPosition = Vector2.zero;
        ScrollRect scroll = view.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = view;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = contentH > areaH;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        float top = contentH < areaH ? (areaH - contentH) / 2f : 0f; // a short list sits in the middle
        for (int k = 0; k < tiles.Count; k++)
        {
            PackTile t = tiles[k];
            int row = k / cols, col = k % cols;
            int inRow = Mathf.Min(cols, tiles.Count - row * cols); // a last, shorter row is centred
            float x = (col - (inRow - 1) / 2f) * (pillW + gap);
            float y = -top - row * rowH - pillH / 2f - 4f;
            System.Action open = t.open;
            Button b = NeonButton(content, t.name, new Vector2(pillW, pillH), t.color, NeonTheme.Pink, (col - 1.5f) * 0.5f, 17, () => open());
            Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(pillW, pillH), new Vector2(x, y));
            TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
            label.enableAutoSizing = true;   // the whole name always fits its pill
            label.fontSizeMin = 11;
            label.fontSizeMax = 17;
            label.enableWordWrapping = true;
            label.margin = new Vector4(8f, 2f, 8f, 2f);
            TextMeshProUGUI under = NewText("Under", content, t.under, 12, t.underColor, TextAlignmentOptions.Center);
            under.fontStyle = FontStyles.Normal;
            under.enableAutoSizing = true;
            under.fontSizeMin = 9;
            under.fontSizeMax = 12;
            Place(under.rectTransform, new Vector2(0.5f, 1f), new Vector2(pillW + 10f, 18f), new Vector2(x, y - pillH / 2f - 12f));
        }
    }

    /// <summary>The first open chapter with a puzzle left to clear, so a returning player lands where they left off</summary>
    private static int FirstOpenChapter(string pack)
    {
        int chapters = QueenBlitz.Chapters(pack), last = 0;
        for (int c = 0; c < chapters && QueenBlitz.ChapterOpen(pack, c); c++)
        {
            last = c;
            if (QueenBlitz.ClearedCount(pack, c * QueenBlitz.ChapterSize, QueenBlitz.ChapterSize) < QueenBlitz.ChapterSize) return c;
        }
        return last;
    }

    private void ShowBlitzBuy(QueenBlitz.PackInfo pack)
    {
        ClearBlitzContent();
        blitzView = BlitzView.Buy;
        blitzTitle.text = pack.name.ToUpper();
        BlitzBack(ShowBlitzPacks);
        int total = QueenBlitz.PuzzlesOf(pack.id).Length;
        BlitzText($"{total} puzzles in {QueenBlitz.Chapters(pack.id)} chapters, from warm-ups to devious.", 17, NeonTheme.Lavender,
            new Vector2(0.5f, 0.5f), new Vector2(520, 50), new Vector2(0, 60));
        bool enough = PawnBank.Total >= pack.pawnPrice;
        Button buy = NeonButton(blitzContent, $"Unlock for {pack.pawnPrice:N0} pawns", new Vector2(280, 56), enough ? NeonTheme.Lime : NeonTheme.Muted, NeonTheme.Pink, -0.6f, 18,
            () =>
            {
                if (QueenBlitz.BuyWithPawns(pack)) { Haptics.Selection(); Sfx.Tap(); ShowBlitzPuzzles(pack.id, 0); }
                else ShowToast($"You need {pack.pawnPrice - PawnBank.Total:N0} more pawns. Clear puzzles or play Zombies! to earn them.");
            });
        Place((RectTransform)buy.transform, new Vector2(0.5f, 0.5f), new Vector2(280, 56), new Vector2(0, -10));
        // App Store purchase arrives with the store hookup (phase 5)
        BlitzText("Or buy it for $2.99 soon.", 14, NeonTheme.Lavender, new Vector2(0.5f, 0.5f), new Vector2(400, 22), new Vector2(0, -60));
    }

    private void BlitzBack(System.Action back)
    {
        Button b = NeonButton(blitzContent, "< Packs", new Vector2(96, 36), NeonTheme.Cyan, NeonTheme.Ground, -0.8f, 15, () => { Haptics.Selection(); back(); });
        Place((RectTransform)b.transform, new Vector2(0f, 1f), new Vector2(96, 36), new Vector2(66, -34));
    }

    // ── Puzzles ──

    private void ShowBlitzPuzzles(string packId, int chapter)
    {
        ClearBlitzContent();
        blitzView = BlitzView.Puzzles;
        QueenBlitz.PackId = packId;
        QueenBlitz.PackInfo pack = QueenBlitz.Info(packId);
        QueenBlitz.Puzzle[] puzzles = QueenBlitz.PuzzlesOf(packId);
        int chapters = QueenBlitz.Chapters(packId);
        blitzChapter = Mathf.Clamp(chapter, 0, chapters - 1);
        blitzTitle.text = packId == "starter" ? "QUEEN BLITZ" : pack.name.ToUpper();
        BlitzBack(ShowBlitzPacks);

        if (chapters == 1)
        {
            BlitzText("One queen. Clear every pawn before your moves run out; she drives straight through them. Your first clear of a puzzle keeps its pawns.",
                15, NeonTheme.Lavender, new Vector2(0.5f, 1f), new Vector2(560, 44), new Vector2(0, -84));
        }
        else
        {
            // One tab per chapter; a closed one says what opens it
            for (int c = 0; c < chapters; c++)
            {
                int index = c;
                bool open = QueenBlitz.ChapterOpen(packId, c);
                Color color = c == blitzChapter ? NeonTheme.Yellow : open ? NeonTheme.Cyan : NeonTheme.Muted;
                Button tab = NeonButton(blitzContent, $"{c + 1}", new Vector2(70, 34), color, NeonTheme.Ground, 0f, 16,
                    () =>
                    {
                        Haptics.Selection(); Sfx.Tap();
                        if (QueenBlitz.ChapterOpen(packId, index)) ShowBlitzPuzzles(packId, index);
                        else ShowToast($"Clear {QueenBlitz.ChapterUnlock} puzzles in chapter {index} to open chapter {index + 1}.");
                    });
                Place((RectTransform)tab.transform, new Vector2(0.5f, 1f), new Vector2(70, 34), new Vector2((c - (chapters - 1) / 2f) * 80f, -86));
            }
        }

        int first = blitzChapter * QueenBlitz.ChapterSize;
        int count = Mathf.Min(QueenBlitz.ChapterSize, puzzles.Length - first);
        for (int k = 0; k < count; k++)
        {
            int index = first + k;
            QueenBlitz.Puzzle puzzle = puzzles[index];
            int row = k / 5, col = k % 5;
            float x = (col - 2) * 112f;
            float y = row == 0 ? 168f : 64f;
            Color color = puzzle.ice ? NeonTheme.Hex("BFEFFF") : puzzle.queen.HasValue ? NeonTheme.Cyan : NeonTheme.Lime;
            Button b = NeonButton(blitzContent, $"{index + 1}", new Vector2(96, 44), color, NeonTheme.Ground, (col - 2) * 0.6f, 20, () => StartBlitz(index));
            Place((RectTransform)b.transform, new Vector2(0.5f, 0f), new Vector2(96, 44), new Vector2(x, y + 18));
            string name = puzzle.name;
            if (pack.review && puzzle.score > 0) name = $"{puzzle.tier} {puzzle.score}"; // for playtest notes
            TextMeshProUGUI label = BlitzText(name, 12, NeonTheme.Lavender, new Vector2(0.5f, 0f), new Vector2(108, 18), new Vector2(x, y - 14));
            label.enableWordWrapping = false;
            // Not yet cleared: the pawns it pays. Cleared: just that (every clear takes exactly its moves)
            bool done = QueenBlitz.EverCleared(index, packId);
            string stars = done ? "Cleared!" : $"{puzzle.pawns.Length} pawns";
            BlitzText(stars, 14, done ? NeonTheme.Lavender : NeonTheme.Yellow, new Vector2(0.5f, 0f), new Vector2(108, 20), new Vector2(x, y - 32));
        }
    }

    // ── Daily ──

    /// <summary>A week of dailies ending `weeksBack` weeks before today; the archive is free</summary>
    private void ShowBlitzDaily(int weeksBack)
    {
        ClearBlitzContent();
        blitzView = BlitzView.Daily;
        blitzTitle.text = "DAILY PUZZLE";
        BlitzBack(ShowBlitzPacks);
        System.DateTime today = QueenBlitz.Today;
        if (today < QueenBlitz.DailyEpoch)
        {
            BlitzText($"The first daily puzzle comes out on {QueenBlitz.DailyEpoch:MMMM d}.", 18, NeonTheme.Lavender,
                new Vector2(0.5f, 0.5f), new Vector2(520, 50), new Vector2(0, 20));
            return;
        }
        System.DateTime last = today.AddDays(-7 * weeksBack);
        System.DateTime first = last.AddDays(-6);
        int streak = QueenBlitz.DailyStreak();
        BlitzText(streak > 0 ? $"Streak: {streak} day{(streak == 1 ? "" : "s")} in a row. Each first clear keeps its pawns."
                : "A new puzzle every day: easy on Monday, devious on Sunday. Each first clear keeps its pawns.",
            15, NeonTheme.Lavender, new Vector2(0.5f, 1f), new Vector2(560, 44), new Vector2(0, -84));

        bool missing = false;
        for (int k = 0; k < 7; k++)
        {
            System.DateTime day = first.AddDays(k);
            float x = (k - 3) * 80f;
            if (day < QueenBlitz.DailyEpoch) continue;
            bool have = QueenBlitz.HasDaily(day);
            missing |= !have;
            bool done = QueenBlitz.DailyCleared(day);
            bool isToday = day == today;
            Color color = !have ? NeonTheme.Muted : done ? NeonTheme.Lime : isToday ? NeonTheme.Yellow : NeonTheme.Cyan;
            System.DateTime pick = day;
            Button b = NeonButton(blitzContent, $"#{QueenBlitz.DailyNumber(day)}", new Vector2(72, 54), color, NeonTheme.Ground, (k - 3) * 0.4f, 17,
                () => StartDaily(pick));
            Place((RectTransform)b.transform, new Vector2(0.5f, 0.5f), new Vector2(72, 54), new Vector2(x, 10));
            BlitzText(isToday ? "Today" : day.ToString("ddd d"), 13, isToday ? NeonTheme.Yellow : NeonTheme.Lavender,
                new Vector2(0.5f, 0.5f), new Vector2(78, 20), new Vector2(x, -30));
            if (have)
            {
                QueenBlitz.Puzzle p = QueenBlitz.PuzzlesOf(QueenBlitz.DailyId(day))[0];
                string under = done ? "Cleared!" : $"{p.pawns.Length} pawns";
                BlitzText(under, 12, done ? NeonTheme.Lavender : NeonTheme.Yellow, new Vector2(0.5f, 0.5f), new Vector2(78, 18), new Vector2(x, -48));
            }
        }

        // Older weeks are free to play too
        if (first > QueenBlitz.DailyEpoch)
        {
            Button earlier = NeonButton(blitzContent, "< Earlier", new Vector2(110, 38), NeonTheme.Cyan, NeonTheme.Ground, -0.5f, 15, () => ShowBlitzDaily(weeksBack + 1));
            Place((RectTransform)earlier.transform, new Vector2(0.5f, 0f), new Vector2(110, 38), new Vector2(-200, 40));
        }
        if (weeksBack > 0)
        {
            Button later = NeonButton(blitzContent, "Later >", new Vector2(110, 38), NeonTheme.Cyan, NeonTheme.Ground, 0.5f, 15, () => ShowBlitzDaily(weeksBack - 1));
            Place((RectTransform)later.transform, new Vector2(0.5f, 0f), new Vector2(110, 38), new Vector2(200, 40));
        }
        // Fetch this week's puzzles if any are missing, then show them
        // (only once per visit to this week, and only redrawn if something new arrived, so a
        // missing day never turns into repeated requests)
        if (missing && !blitzDailyFetched.Contains(weeksBack))
        {
            blitzDailyFetched.Add(weeksBack);
            int before = DailiesHeld(first, last);
            StartCoroutine(QueenBlitz.FetchDailies(first, last, () =>
            {
                if (DailiesHeld(first, last) > before && blitzPicker.activeSelf && blitzView == BlitzView.Daily && blitzDailyWeeks == weeksBack)
                    ShowBlitzDaily(weeksBack);
            }));
        }
        blitzDailyWeeks = weeksBack;
    }

    private int blitzDailyWeeks;
    private readonly System.Collections.Generic.HashSet<int> blitzDailyFetched = new System.Collections.Generic.HashSet<int>();

    private static int DailiesHeld(System.DateTime first, System.DateTime last)
    {
        int n = 0;
        for (System.DateTime d = first; d <= last; d = d.AddDays(1)) if (QueenBlitz.HasDaily(d)) n++;
        return n;
    }

    private void StartDaily(System.DateTime day)
    {
        if (!QueenBlitz.HasDaily(day))
        {
            ShowToast("Connect to the internet once to get this puzzle.");
            return;
        }
        QueenBlitz.PackId = QueenBlitz.DailyId(day);
        StartBlitz(0);
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>Tap a puzzle in the picker (BlitzDemo)</summary>
    public void DebugStartBlitz(int index) => StartBlitz(index);

    /// <summary>Tap Queen Blitz! on the menu (BlitzDemo)</summary>
    public void DebugBlitzFromMenu() => ShowBlitzPicker();
#endif

    private void StartBlitz(int index)
    {
        Haptics.Selection(); Sfx.Tap();
        blitzPicker.SetActive(false);
        QueenBlitz.Puzzle puzzle = QueenBlitz.Puzzles[index];
        playerCount = 2;
        boardSize = QueenBlitz.BoardFor(puzzle.size);
        playerTypes.Clear();
        playerTypes.Add(PlayerType.Human);    // you and your queen, White
        playerTypes.Add(PlayerType.Computer); // the pawns, Black (they never move)
        PlayerViewUI.StartView = PieceColor.White;
        chaosMode = false;
        timedPlay = false;
        setupOnline = false;
        quickPlay = true;   // don't overwrite the player's saved setup
        blitzRun = true;
        blitzChoice = index;
        StartFromSetup();
        blitzRun = false;
        quickPlay = false;
    }
}

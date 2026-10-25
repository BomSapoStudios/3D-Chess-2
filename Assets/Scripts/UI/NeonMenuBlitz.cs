using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Queen Blitz from the main menu. With more than one pack in the build it opens on the packs
/// (progress, or the price of a locked one); a pack shows its puzzles ten at a time, one tab per
/// chapter, with the pawns each pays or your best. Starter alone goes straight to its puzzles.
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

    private void ShowBlitzPicker()
    {
        if (blitzPicker == null) BuildBlitzPicker();
        if (QueenBlitz.Available().Count > 1) ShowBlitzPacks();
        else ShowBlitzPuzzles("starter", 0);
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

    private void ShowBlitzPacks()
    {
        ClearBlitzContent();
        blitzTitle.text = "QUEEN BLITZ";
        BlitzText("One queen. Clear every pawn in as few moves as you can. Your first clear of a puzzle keeps its pawns.",
            15, NeonTheme.Lavender, new Vector2(0.5f, 1f), new Vector2(560, 44), new Vector2(0, -84));

        var packs = QueenBlitz.Available();
        int count = packs.Count;
        float width = Mathf.Min(170f, 560f / count - 10f);
        for (int i = 0; i < count; i++)
        {
            QueenBlitz.PackInfo pack = packs[i];
            float x = (i - (count - 1) / 2f) * (width + 10f);
            int total = QueenBlitz.PuzzlesOf(pack.id).Length;
            bool owned = QueenBlitz.Owns(pack);
            Color color = pack.review ? NeonTheme.Pink : owned ? NeonTheme.Lime : NeonTheme.Muted;
            Button b = NeonButton(blitzContent, pack.name, new Vector2(width, 64), color, NeonTheme.Ground, (i - count / 2) * 0.6f, 18,
                () => { Haptics.Selection(); if (QueenBlitz.Owns(pack)) ShowBlitzPuzzles(pack.id, FirstOpenChapter(pack.id)); else ShowBlitzBuy(pack); });
            Place((RectTransform)b.transform, new Vector2(0.5f, 0.5f), new Vector2(width, 64), new Vector2(x, 10));
            string under = owned ? $"{QueenBlitz.ClearedCount(pack.id)} / {total} cleared" : $"{total} puzzles · locked";
            BlitzText(under, 13, owned ? NeonTheme.Yellow : NeonTheme.Lavender, new Vector2(0.5f, 0.5f), new Vector2(width + 8, 20), new Vector2(x, -36));
        }
    }

    /// <summary>The last chapter you've opened, so a returning player lands where they left off</summary>
    private static int FirstOpenChapter(string pack)
    {
        int chapter = 0;
        while (chapter + 1 < QueenBlitz.Chapters(pack) && QueenBlitz.ChapterOpen(pack, chapter + 1)) chapter++;
        return chapter;
    }

    private void ShowBlitzBuy(QueenBlitz.PackInfo pack)
    {
        ClearBlitzContent();
        blitzTitle.text = pack.name.ToUpper();
        BlitzBack(ShowBlitzPacks);
        int total = QueenBlitz.PuzzlesOf(pack.id).Length;
        BlitzText($"{total} puzzles in {QueenBlitz.Chapters(pack.id)} chapters, from warm-ups to devious.", 17, NeonTheme.Lavender,
            new Vector2(0.5f, 0.5f), new Vector2(520, 50), new Vector2(0, 60));
        bool enough = PawnBank.Total >= pack.pawnPrice;
        Button buy = NeonButton(blitzContent, $"Unlock for {pack.pawnPrice:N0} pawns", new Vector2(280, 56), enough ? NeonTheme.Lime : NeonTheme.Muted, NeonTheme.Pink, -0.6f, 18,
            () =>
            {
                if (QueenBlitz.BuyWithPawns(pack)) { Haptics.Selection(); ShowBlitzPuzzles(pack.id, 0); }
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
        QueenBlitz.PackId = packId;
        QueenBlitz.PackInfo pack = QueenBlitz.Info(packId);
        QueenBlitz.Puzzle[] puzzles = QueenBlitz.PuzzlesOf(packId);
        int chapters = QueenBlitz.Chapters(packId);
        blitzChapter = Mathf.Clamp(chapter, 0, chapters - 1);
        blitzTitle.text = packId == "starter" ? "QUEEN BLITZ" : pack.name.ToUpper();
        if (QueenBlitz.Available().Count > 1) BlitzBack(ShowBlitzPacks);

        if (chapters == 1)
        {
            BlitzText("One queen. Clear every pawn in as few moves as you can; she drives straight through them. Your first clear of a puzzle keeps its pawns.",
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
                        Haptics.Selection();
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
            // Not yet cleared: the pawns it pays. Cleared: your best against par
            bool done = QueenBlitz.EverCleared(index, packId);
            string stars = done ? $"Best {QueenBlitz.BestMoves(index, packId)} / par {puzzle.par}" : $"{puzzle.pawns.Length} pawns";
            BlitzText(stars, 14, done ? NeonTheme.Lavender : NeonTheme.Yellow, new Vector2(0.5f, 0f), new Vector2(108, 20), new Vector2(x, y - 32));
        }
    }

    private void StartBlitz(int index)
    {
        Haptics.Selection();
        blitzPicker.SetActive(false);
        QueenBlitz.Puzzle puzzle = QueenBlitz.Puzzles[index];
        playerCount = 2;
        boardSize = puzzle.size == 4 ? BoardSize.Small4x4x4 : puzzle.size == 6 ? BoardSize.Medium6x6x6 : BoardSize.Large8x8x8;
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

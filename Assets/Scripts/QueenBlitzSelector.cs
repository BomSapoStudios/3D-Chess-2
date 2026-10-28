using UnityEngine;

/// <summary>
/// The puzzle selector inside a Queen Blitz puzzle: tap the title bar (or its grid icon) for a card
/// with the pack's puzzles, a chapter at a time, Prev / Next, and chips to switch pack. Picking a
/// puzzle starts it the way Next puzzle does (the board resizes to its cube); leaving an unfinished
/// puzzle just abandons it (no fail, no pawns), and opening the card is not a move.
/// </summary>
public partial class QueenBlitzDirector
{
    private bool selectorOpen;
    private string selectorPack;
    private int selectorChapter;
    private Vector2 chipScroll;

    public static bool SelectorOpen => Instance != null && Instance.selectorOpen;

    private void OpenSelector()
    {
        Haptics.Selection(); Sfx.Tap();
        selectorOpen = true;
        selectorPack = QueenBlitz.PackId;
        selectorChapter = QueenBlitz.Index / QueenBlitz.ChapterSize;
    }

    private void CloseSelector() => selectorOpen = false;

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>Tap the title bar (BlitzDemo); optionally show another pack's chip</summary>
    public void DebugOpenSelector(string pack = null)
    {
        OpenSelector();
        if (!string.IsNullOrEmpty(pack)) { selectorPack = pack; selectorChapter = 0; }
    }

    /// <summary>Tap a puzzle tile (BlitzDemo)</summary>
    public void DebugJump(string pack, int index) => Jump(pack, index);
#endif

    /// <summary>Start a puzzle from the selector: the same path as Next puzzle</summary>
    private void Jump(string pack, int index)
    {
        Haptics.Selection(); Sfx.Tap();
        selectorOpen = false;
        QueenBlitz.PackId = pack;
        Restart(index);
    }

    /// <summary>The grid icon at the end of the title bar</summary>
    private static void DrawGridIcon(Rect r)
    {
        Color before = GUI.color;
        GUI.color = NeonTheme.Yellow;
        float s = r.width / 2f - 1.5f;
        for (int i = 0; i < 4; i++)
            GUI.DrawTexture(new Rect(r.x + (i % 2) * (s + 3f), r.y + (i / 2) * (s + 3f), s, s), Texture2D.whiteTexture);
        GUI.color = before;
    }

    private void DrawSelector()
    {
        // Larger Text draws the whole card bigger, like the result panel
        Vector2 ui = TouchGUI.Begin(AccessibilityPrefs.TextScale);
        float w = Mathf.Min(640f, ui.x - 24f), h = Mathf.Min(420f, ui.y - 16f);
        Rect card = new Rect((ui.x - w) / 2f, (ui.y - h) / 2f, w, h);
        TouchGUI.Block(new Rect(0, 0, ui.x, ui.y));

        // Tapping outside the card closes it
        Event e = Event.current;
        if (e.type == EventType.MouseDown && !card.Contains(e.mousePosition))
        {
            CloseSelector();
            e.Use();
            TouchGUI.End();
            return;
        }

        Color before = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(-ui.x, -ui.y, ui.x * 3f, ui.y * 3f), Texture2D.whiteTexture);
        GUI.color = before;
        NeonTheme.GUIPanel(card, NeonTheme.Cyan, 0.97f);

        float pad = 12f, x = card.x + pad, y = card.y + pad, inner = w - pad * 2f;

        // Packs you own (review packs after the five taps), and today's daily if that's where you are
        var packs = QueenBlitz.Available().FindAll(QueenBlitz.Owns);
        if (QueenBlitz.IsDaily(QueenBlitz.PackId)) packs.Insert(0, QueenBlitz.Info(QueenBlitz.PackId));
        float chipH = 34f;
        float closeW = 40f;
        Rect chipArea = new Rect(x, y, inner - closeW - 8f, chipH + 4f);
        float chipsW = 0f;
        var widths = new float[packs.Count];
        GUIStyle measure = NeonTheme.GUILabelStyle(NeonTheme.Ground, 14, true, TextAnchor.MiddleCenter);
        for (int i = 0; i < packs.Count; i++) { widths[i] = measure.CalcSize(new GUIContent(packs[i].name)).x + 24f; chipsW += widths[i] + 6f; }
        chipScroll = GUI.BeginScrollView(chipArea, chipScroll, new Rect(0, 0, Mathf.Max(chipsW, chipArea.width), chipH), GUIStyle.none, GUIStyle.none);
        float cx = 0f;
        for (int i = 0; i < packs.Count; i++)
        {
            bool here = packs[i].id == selectorPack;
            if (NeonTheme.GUIButton(new Rect(cx, 0, widths[i], chipH), packs[i].name, here ? NeonTheme.Yellow : NeonTheme.Cyan, NeonTheme.Pink, !here, 14) && !here)
            {
                Haptics.Selection(); Sfx.Tap();
                selectorPack = packs[i].id;
                selectorChapter = selectorPack == QueenBlitz.PackId ? QueenBlitz.Index / QueenBlitz.ChapterSize : 0;
            }
            cx += widths[i] + 6f;
        }
        GUI.EndScrollView();
        if (TouchGUI.NeonButton(new Rect(card.xMax - pad - closeW, y, closeW, chipH), "X", NeonTheme.Pink, NeonTheme.Cyan, fontSize: 16)) CloseSelector();
        y += chipH + 10f;

        // Prev / Next step through the pack you're playing; the name in the middle
        QueenBlitz.Puzzle[] puzzles = QueenBlitz.PuzzlesOf(selectorPack);
        bool current = selectorPack == QueenBlitz.PackId;
        float rowH = 36f, arrowW = Mathf.Min(110f, inner * 0.24f);
        int prev = -1, next = -1;
        if (current)
        {
            for (int i = QueenBlitz.Index - 1; i >= 0 && prev < 0; i--) if (QueenBlitz.PuzzleOpen(selectorPack, i)) prev = i;
            for (int i = QueenBlitz.Index + 1; i < puzzles.Length && next < 0; i++) if (QueenBlitz.PuzzleOpen(selectorPack, i)) next = i;
            GUI.enabled = prev >= 0;
            if (TouchGUI.NeonButton(new Rect(x, y, arrowW, rowH), "< Prev", NeonTheme.Lime, NeonTheme.Pink, outlined: prev < 0, fontSize: 15)) Jump(selectorPack, prev);
            GUI.enabled = next >= 0;
            if (TouchGUI.NeonButton(new Rect(card.xMax - pad - arrowW, y, arrowW, rowH), "Next >", NeonTheme.Lime, NeonTheme.Pink, outlined: next < 0, fontSize: 15)) Jump(selectorPack, next);
            GUI.enabled = true;
        }
        string heading = current ? $"Puzzle {QueenBlitz.Index + 1} of {puzzles.Length}" : $"{QueenBlitz.Info(selectorPack).name}: {QueenBlitz.ClearedCount(selectorPack)} of {puzzles.Length} cleared";
        GUI.Label(new Rect(x + arrowW + 8f, y, inner - arrowW * 2f - 16f, rowH), heading, NeonTheme.GUILabelStyle(NeonTheme.Lavender, 15, true, TextAnchor.MiddleCenter));
        y += rowH + 8f;

        // Chapter tabs, a closed one in grey (clear 7 of the 10 before it)
        int chapters = QueenBlitz.Chapters(selectorPack);
        selectorChapter = Mathf.Clamp(selectorChapter, 0, chapters - 1);
        if (chapters > 1)
        {
            float tabW = Mathf.Min(64f, (inner - (chapters - 1) * 6f) / chapters), tabH = 30f;
            float tx = card.center.x - (chapters * tabW + (chapters - 1) * 6f) / 2f;
            for (int c = 0; c < chapters; c++)
            {
                bool open = QueenBlitz.ChapterOpen(selectorPack, c);
                Color fill = c == selectorChapter ? NeonTheme.Yellow : open ? NeonTheme.Cyan : NeonTheme.Muted;
                if (TouchGUI.NeonButton(new Rect(tx + c * (tabW + 6f), y, tabW, tabH), $"{c + 1}", fill, NeonTheme.Pink, outlined: c != selectorChapter, fontSize: 14) && open)
                {
                    Haptics.Selection(); Sfx.Tap();
                    selectorChapter = c;
                }
            }
            y += tabH + 8f;
        }

        // The chapter's puzzles: two rows of five
        int first = selectorChapter * QueenBlitz.ChapterSize;
        int count = Mathf.Min(QueenBlitz.ChapterSize, puzzles.Length - first);
        float gap = 8f;
        float tileW = (inner - gap * 4f) / 5f;
        bool chapterOpen = QueenBlitz.ChapterOpen(selectorPack, selectorChapter);
        float room = card.yMax - pad - y - (chapterOpen ? 0f : 24f);
        float tileH = Mathf.Min(84f, (room - gap) / 2f);
        y += Mathf.Max(0f, (room - tileH * 2f - gap) / 2f); // centred in the space left
        GUIStyle sub = NeonTheme.GUILabelStyle(NeonTheme.Ground, tileH < 52f ? 11 : 12, true, TextAnchor.LowerCenter);
        for (int k = 0; k < count; k++)
        {
            int index = first + k;
            QueenBlitz.Puzzle p = puzzles[index];
            Rect tile = new Rect(x + (k % 5) * (tileW + gap), y + (k / 5) * (tileH + gap), tileW, tileH);
            bool here = current && index == QueenBlitz.Index;
            bool done = QueenBlitz.EverCleared(index, selectorPack);
            Color fill = !chapterOpen ? NeonTheme.Muted : here ? NeonTheme.Yellow : done ? NeonTheme.Lime : NeonTheme.Cyan;
            GUI.enabled = chapterOpen;
            if (TouchGUI.NeonButton(tile, "", fill, NeonTheme.Pink, outlined: !chapterOpen, fontSize: 20) && chapterOpen) Jump(selectorPack, index);
            GUI.enabled = true;
            Color ink = chapterOpen ? NeonTheme.Ground : NeonTheme.Lavender;
            GUIStyle num = NeonTheme.GUILabelStyle(ink, tileH < 52f ? 17 : 20, true, TextAnchor.UpperCenter, display: true);
            GUI.Label(new Rect(tile.x, tile.y + 4f, tile.width, tile.height), $"{index + 1}", num);
            sub.normal.textColor = ink;
            string state = here ? "Playing" : done ? "Cleared" : !chapterOpen ? "Locked" : $"{p.pawns.Length} pawns";
            GUI.Label(new Rect(tile.x, tile.y, tile.width, tile.height - 5f), state, sub);
        }
        if (!chapterOpen)
            GUI.Label(new Rect(x, card.yMax - pad - 20f, inner, 20f), $"Clear {QueenBlitz.ChapterUnlock} puzzles in chapter {selectorChapter} to open chapter {selectorChapter + 1}.",
                NeonTheme.GUILabelStyle(NeonTheme.PinkSoft, 13, true, TextAnchor.MiddleCenter));

        // Taps on the card don't reach the board or the buttons under it
        if (e.type == EventType.MouseDown || e.type == EventType.MouseUp) e.Use();
        TouchGUI.End();
    }
}

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Customize ("MAKE IT YOURS"): four tabs down the left (Pieces, Backdrop, Board, Effects), a
/// preview card of the current pick in the middle, and a scrolling grid of picture cards on the
/// right. Pictures come from Resources/CustomizeThumbs (rendered in the game by NeonMenuThumbs);
/// the Pieces and Effects previews play live from the menu's preview stage.
/// Every choice is saved on this phone (PieceShapes, PieceSets, Backdrops, BoardLook, PromotionStyle).
/// </summary>
public partial class NeonMenu
{
    private GameObject customizeScreen;

    private enum CustomizeTab { Pieces, Backdrop, Board, Effects }
    private CustomizeTab tab;

    private static readonly Color CardFill = NeonTheme.Hex("2A0055");
    private static readonly Color CardEdge = NeonTheme.Hex("3B1670");
    private static readonly Color RailFill = NeonTheme.Hex("22004A");
    private static readonly Color RailEdge = NeonTheme.Hex("4B2380");
    private static readonly Color PreviewFill = NeonTheme.Hex("1F003F");

    private static Color Accent(CustomizeTab t) =>
        t == CustomizeTab.Pieces ? NeonTheme.Lime : t == CustomizeTab.Backdrop ? NeonTheme.PinkSoft : t == CustomizeTab.Board ? NeonTheme.Cyan : NeonTheme.Yellow;

    // Rail
    private readonly List<(CustomizeTab tab, Image face, Image border, RawImage icon, TextMeshProUGUI label)> railTabs =
        new List<(CustomizeTab, Image, Image, RawImage, TextMeshProUGUI)>();

    // Preview card
    private Image previewBorder;
    private GameObject piecesView, backdropView, boardView, effectsView;
    private RawImage piecesPicture, backdropPicture, boardPicture, effectsPicture;
    private TextMeshProUGUI piecesName, piecesBlurb, backdropName, backdropBlurb, effectsName, effectsBlurb;
    private Image[] teamDots;
    private GameObject playPill;

    // Grid
    private TextMeshProUGUI gridTitle, gridHint;
    private RectTransform gridContent, boardRows;
    private ScrollRect gridScroll;
    private TextMeshProUGUI pawnBalance;
    private int pawnTaps;
    private float lastPawnTap;

    // A locked backdrop being looked at (PieceShapes.Showing does this for piece sets)
    private Backdrops.Kind? showingBackdrop;
    private Backdrops.Kind ShownBackdrop => showingBackdrop ?? Backdrops.Current;

    // A locked promotion animation being looked at
    private PromotionStyle.Kind? showingPromotion;
    private PromotionStyle.Kind ShownPromotion => showingPromotion ?? PromotionStyle.Current;

    // Unlock panels in the preview card (pieces and backdrop)
    private class UnlockPanel { public GameObject root; public TextMeshProUGUI pawnLabel, dollarLabel, note; public Image pawnButton; }
    private UnlockPanel piecesUnlock, backdropUnlock, effectsUnlock;
    private GameObject finishBox;

    private class PickCard
    {
        public System.Func<bool> equipped, shown;
        public Image border, check;
    }
    private readonly List<PickCard> pickCards = new List<PickCard>();
    private readonly List<(System.Func<bool> on, Image track, RectTransform knob, Image border)> boardSwitches =
        new List<(System.Func<bool>, Image, RectTransform, Image)>();

    private void BuildCustomizeScreen()
    {
        customizeScreen = NewRect("Customize Screen", safeRoot).gameObject;
        RectTransform root = customizeScreen.GetComponent<RectTransform>();
        Stretch(root);
        var hooks = customizeScreen.AddComponent<ScreenHooks>();
        hooks.disabled = () => { StopInlinePreview(); StopBackdropPreview(); PieceShapes.Showing = null; showingBackdrop = null; showingPromotion = null; };
        hooks.enabled = () => { if (previewBorder != null) { RefreshCustomize(); StartPreviewForTab(); } };

        // ── Top bar: Back, title, pawns, Done! ──
        RectTransform header = NewRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(16, -60);
        header.offsetMax = new Vector2(-24, -12);

        Button back = NeonButton(header, "<", new Vector2(46, 42), NeonTheme.Cyan, NeonTheme.Pink, -3f, 24, ShowMenu);
        Place((RectTransform)back.transform, new Vector2(0, 0.5f), new Vector2(46, 42), new Vector2(23, 0));

        TextMeshProUGUI title = NewText("Title", header, "MAKE IT YOURS", 26, NeonTheme.Lime, TextAlignmentOptions.Left);
        NeonTheme.ApplyFont(title, display: true);
        title.rectTransform.anchorMin = new Vector2(0, 0);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.offsetMin = new Vector2(62, 0);
        title.rectTransform.offsetMax = new Vector2(-380, 0);
        title.rectTransform.localEulerAngles = new Vector3(0, 0, -1.5f);

        Button done = NeonButton(header, "Done!", new Vector2(104, 42), NeonTheme.Pink, NeonTheme.Cyan, 1.5f, 19, ShowMenu);
        Place((RectTransform)done.transform, new Vector2(1, 0.5f), new Vector2(104, 42), new Vector2(-52, 0));

        // Pawn balance (pawns will buy sets in the store)
        Image chip = NewImage("Pawns", header, CardFill);
        chip.sprite = NeonTheme.Rounded;
        chip.type = Image.Type.Sliced;
        Place(chip.rectTransform, new Vector2(1, 0.5f), new Vector2(104, 34), new Vector2(-180, 0));
        Image chipEdge = NewImage("Border", chip.transform, NeonTheme.Yellow);
        chipEdge.sprite = NeonTheme.RoundedOutline;
        chipEdge.type = Image.Type.Sliced;
        Stretch(chipEdge.rectTransform);
        RawImage pawnIcon = NewRaw("Pawn", chip.transform, NeonIcons.Pawn, NeonTheme.Yellow);
        Place(pawnIcon.rectTransform, new Vector2(0, 0.5f), new Vector2(22, 22), new Vector2(20, 0));
        pawnBalance = NewText("Count", chip.transform, "0", 16, NeonTheme.Yellow, TextAlignmentOptions.Left);
        pawnBalance.rectTransform.anchorMin = new Vector2(0, 0);
        pawnBalance.rectTransform.anchorMax = new Vector2(1, 1);
        pawnBalance.rectTransform.offsetMin = new Vector2(36, 0);
        pawnBalance.rectTransform.offsetMax = new Vector2(-8, 0);
        // Testing: seven quick taps on the pawn balance unlock everything (and again to lock it back)
        Button chipButton = chip.gameObject.AddComponent<Button>();
        chipButton.transition = Selectable.Transition.None;
        chipButton.onClick.AddListener(() =>
        {
            pawnTaps = Time.unscaledTime - lastPawnTap < 0.6f ? pawnTaps + 1 : 1;
            lastPawnTap = Time.unscaledTime;
            if (pawnTaps < 7) return;
            pawnTaps = 0;
            Store.TestUnlockAll = !Store.TestUnlockAll;
            ShowToast(Store.TestUnlockAll ? "Test mode: everything unlocked" : "Test mode off: locks are back");
            BuildCards();
            Refresh();
            StartPreviewForTab();
        });

#if !UNITY_WEBGL || UNITY_EDITOR
        // Restore purchases (Apple requires one): what this Apple ID bought comes back on a new phone
        Button restore = NeonButton(header, "Restore purchases", new Vector2(124, 34), NeonTheme.Lavender, NeonTheme.Pink, 0f, 12, RestorePurchases);
        Place((RectTransform)restore.transform, new Vector2(1, 0.5f), new Vector2(124, 34), new Vector2(-312, 0));
#endif
        Purchases.PricesLoaded += RefreshCustomize;
        Purchases.Owned += OnPurchased;

        // ── Body: rail | preview card | grid ──
        RectTransform body = NewRect("Body", root);
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(16, 14);
        body.offsetMax = new Vector2(-24, -72);

        BuildRail(body);
        BuildPreviewCard(body);
        BuildGrid(body);

        SelectTab(CustomizeTab.Pieces);
    }

    // ───────── Tabs ─────────

    private void BuildRail(RectTransform body)
    {
        RectTransform rail = NewRect("Tabs", body);
        rail.anchorMin = new Vector2(0, 0);
        rail.anchorMax = new Vector2(0, 1);
        rail.pivot = new Vector2(0, 1);
        rail.offsetMin = new Vector2(0, 0);
        rail.offsetMax = new Vector2(104, 0);

        (CustomizeTab t, string name, Texture2D icon)[] tabs =
        {
            (CustomizeTab.Pieces, "Pieces", NeonIcons.Rook),
            (CustomizeTab.Backdrop, "Backdrop", NeonIcons.Picture),
            (CustomizeTab.Board, "Board", NeonIcons.Cube),
            (CustomizeTab.Effects, "Effects", NeonIcons.Star),
        };
        for (int i = 0; i < tabs.Length; i++)
        {
            var t = tabs[i];
            Image face = NewImage(t.name, rail, RailFill);
            face.sprite = NeonTheme.Rounded;
            face.type = Image.Type.Sliced;
            face.rectTransform.anchorMin = new Vector2(0, 1);
            face.rectTransform.anchorMax = new Vector2(1, 1);
            face.rectTransform.pivot = new Vector2(0.5f, 1);
            face.rectTransform.offsetMin = new Vector2(0, -66 - i * 75);
            face.rectTransform.offsetMax = new Vector2(0, -i * 75);
            Image border = NewImage("Border", face.transform, RailEdge);
            border.sprite = NeonTheme.RoundedOutline;
            border.type = Image.Type.Sliced;
            border.raycastTarget = false;
            Stretch(border.rectTransform);
            RawImage icon = NewRaw("Icon", face.transform, t.icon, NeonTheme.Lavender);
            Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(26, 26), new Vector2(0, 10));
            TextMeshProUGUI label = NewText("Label", face.transform, t.name, 14, NeonTheme.Lavender, TextAlignmentOptions.Center);
            Place(label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(100, 20), new Vector2(0, -16));
            Button b = face.gameObject.AddComponent<Button>();
            NeonTheme.NeutralTint(b);
            CustomizeTab captured = t.t;
            b.onClick.AddListener(() => { Haptics.Selection(); SelectTab(captured); });
            railTabs.Add((t.t, face, border, icon, label));
        }
    }

    private void SelectTab(CustomizeTab t)
    {
        tab = t;
        PieceShapes.Showing = null;
        showingBackdrop = null;
        showingPromotion = null;
        foreach (var r in railTabs)
        {
            bool on = r.tab == t;
            if (on)
            {
                NeonTheme.StyleFilled(r.face, Accent(r.tab), NeonTheme.Pink, -2f);
                r.border.enabled = false;
                r.icon.color = r.label.color = NeonTheme.Ground;
            }
            else
            {
                NeonTheme.StyleOutline(r.face, RailFill);    // drops the shadow and tilt
                r.face.sprite = NeonTheme.Rounded;
                r.border.enabled = true;
                r.icon.color = r.label.color = NeonTheme.Lavender;
            }
        }
        previewBorder.color = Accent(t);
        gridTitle.color = Accent(t);
        piecesView.SetActive(t == CustomizeTab.Pieces);
        backdropView.SetActive(t == CustomizeTab.Backdrop);
        boardView.SetActive(t == CustomizeTab.Board);
        effectsView.SetActive(t == CustomizeTab.Effects);
        BuildCards();
        RefreshCustomize();
        StartPreviewForTab();
    }

    // The Pieces tab plays the chosen set on a turntable; Effects waits for Play
    private void StartPreviewForTab()
    {
        if (!customizeScreen.activeInHierarchy) return;
        // (Neon Glow is a plain colour: its picture says more than circling nothing)
        if (tab == CustomizeTab.Backdrop && ShownBackdrop != Backdrops.Kind.NeonGlow) { backdropPicture.texture = StartBackdropPreview(); Cover(backdropPicture); }
        else StopBackdropPreview();
        if (tab == CustomizeTab.Pieces) { piecesPicture.texture = PlayInlineShapes(); Cover(piecesPicture); }
        else
        {
            StopInlinePreview();
            if (tab == CustomizeTab.Effects) ShowEffectsThumb();
        }
    }

    // ───────── Preview card ─────────

    private void BuildPreviewCard(RectTransform body)
    {
        Image card = NewImage("Preview", body, PreviewFill);
        card.sprite = NeonTheme.Rounded;
        card.type = Image.Type.Sliced;
        RectTransform rt = card.rectTransform;
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.offsetMin = new Vector2(118, 0);
        rt.offsetMax = new Vector2(118 + 246, 0);
        previewBorder = NewImage("Border", card.transform, NeonTheme.Lime);
        previewBorder.sprite = NeonTheme.RoundedOutline;
        previewBorder.type = Image.Type.Sliced;
        previewBorder.raycastTarget = false;
        Stretch(previewBorder.rectTransform);

        // Pieces: the set turning, its name and blurb, then the finish (for every set) and team colors
        RectTransform pv = PreviewColumn(card.transform, "Pieces");
        piecesView = pv.gameObject;
        piecesPicture = PreviewPicture(pv, 80);
        piecesName = PreviewName(pv, NeonTheme.Lime);
        piecesBlurb = PreviewBlurb(pv);
        RectTransform looks = NewRect("Finish", pv);
        finishBox = looks.gameObject;
        var looksLayout = looks.gameObject.AddComponent<VerticalLayoutGroup>();
        looksLayout.spacing = 4;
        looksLayout.childControlWidth = looksLayout.childControlHeight = true;
        looksLayout.childForceExpandWidth = true;
        looksLayout.childForceExpandHeight = false;
        TextMeshProUGUI finishLabel = NewText("Label", looks, "Finish", 12, NeonTheme.Lavender, TextAlignmentOptions.Left);
        finishLabel.fontStyle = FontStyles.Normal;
        finishLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;
        LookRow(looks, PieceSets.Kind.NeonGlow, PieceSets.Kind.Chrome);
        LookRow(looks, PieceSets.Kind.Crystal, PieceSets.Kind.Classic);
        RectTransform dots = NewRect("Team colors", looks);
        dots.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
        TextMeshProUGUI dotsLabel = NewText("Label", dots, "Glow", 12, NeonTheme.Lavender, TextAlignmentOptions.Left);
        dotsLabel.fontStyle = FontStyles.Normal;
        Place(dotsLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(40, 20), new Vector2(20, 0));
        teamDots = new Image[6];
        for (int i = 0; i < 6; i++)
        {
            teamDots[i] = NewImage(ColorNames[i], dots, Color.white);
            teamDots[i].sprite = NeonTheme.Circle;
            Place(teamDots[i].rectTransform, new Vector2(0, 0.5f), new Vector2(18, 18), new Vector2(50 + i * 26, 0));
        }

        piecesUnlock = BuildUnlockPanel(pv);

        // Backdrop: the scene's picture, name and blurb
        RectTransform bv = PreviewColumn(card.transform, "Backdrop");
        backdropView = bv.gameObject;
        backdropPicture = PreviewPicture(bv, 128);
        backdropName = PreviewName(bv, NeonTheme.PinkSoft);
        backdropBlurb = PreviewBlurb(bv);
        backdropUnlock = BuildUnlockPanel(bv);

        // Board: a little cube that follows the switches
        RectTransform cv = PreviewColumn(card.transform, "Board");
        boardView = cv.gameObject;
        boardPicture = PreviewPicture(cv, 168);
        boardPicture.color = NeonTheme.Cyan;
        TextMeshProUGUI cubeName = PreviewName(cv, NeonTheme.Cyan);
        cubeName.text = "How the cube looks";
        PreviewBlurb(cv).text = "Changes show here as you flip them.";

        // Effects: the animation's picture with a Play button that plays it right here
        RectTransform ev = PreviewColumn(card.transform, "Effects");
        effectsView = ev.gameObject;
        effectsPicture = PreviewPicture(ev, 108);
        Button play = effectsPicture.gameObject.AddComponent<Button>();
        play.transition = Selectable.Transition.None;
        effectsPicture.raycastTarget = true;
        play.onClick.AddListener(() =>
        {
            Haptics.Selection();
            effectsPicture.texture = PlayInlinePromotion(ShownPromotion);
            Cover(effectsPicture);
            playPill.SetActive(false);
        });
        Image pill = NewImage("Play", effectsPicture.transform, NeonTheme.Lime);
        pill.sprite = NeonTheme.Rounded;
        pill.type = Image.Type.Sliced;
        pill.raycastTarget = false;
        Place(pill.rectTransform, new Vector2(1, 0), new Vector2(64, 24), new Vector2(-40, 20));
        TextMeshProUGUI pillText = NewText("Text", pill.transform, "Play", 13, NeonTheme.Ground, TextAlignmentOptions.Center);
        Stretch(pillText.rectTransform);
        playPill = pill.gameObject;
        effectsName = PreviewName(ev, NeonTheme.Yellow);
        effectsBlurb = PreviewBlurb(ev);
        effectsBlurb.GetComponent<LayoutElement>().preferredHeight = 52; // these run to three lines
        effectsUnlock = BuildUnlockPanel(ev);
    }

    private void ShowEffectsThumb()
    {
        effectsPicture.texture = Thumb("Promo_" + ShownPromotion);
        Cover(effectsPicture);
        playPill.SetActive(true);
    }

    // ───────── Unlocking ─────────

    private UnlockPanel BuildUnlockPanel(RectTransform parent)
    {
        var panel = new UnlockPanel();
        RectTransform box = NewRect("Unlock", parent);
        box.gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
        panel.root = box.gameObject;

        Image pawnFace = NewImage("Pawns", box, NeonTheme.Yellow);
        pawnFace.rectTransform.anchorMin = new Vector2(0, 1);
        pawnFace.rectTransform.anchorMax = new Vector2(0.62f, 1);
        pawnFace.rectTransform.pivot = new Vector2(0, 1);
        pawnFace.rectTransform.offsetMin = new Vector2(0, -40);
        pawnFace.rectTransform.offsetMax = new Vector2(-4, 0);
        NeonTheme.StyleFilled(pawnFace, NeonTheme.Yellow, NeonTheme.Pink);
        RawImage icon = NewRaw("Pawn", pawnFace.transform, NeonIcons.Pawn, NeonTheme.Ground);
        Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(20, 20), new Vector2(18, 0));
        panel.pawnLabel = NewText("Text", pawnFace.transform, "", 15, NeonTheme.Ground, TextAlignmentOptions.Center);
        panel.pawnLabel.rectTransform.anchorMin = Vector2.zero;
        panel.pawnLabel.rectTransform.anchorMax = Vector2.one;
        panel.pawnLabel.rectTransform.offsetMin = new Vector2(28, 0);
        panel.pawnLabel.rectTransform.offsetMax = new Vector2(-4, 0);
        Button pawnBuy = pawnFace.gameObject.AddComponent<Button>();
        NeonTheme.NeutralTint(pawnBuy);
        pawnBuy.onClick.AddListener(UnlockWithPawns);
        panel.pawnButton = pawnFace;

        Image dollarFace = NewImage("Money", box, NeonTheme.Cyan);
        dollarFace.sprite = NeonTheme.RoundedOutline;
        dollarFace.type = Image.Type.Sliced;
        dollarFace.rectTransform.anchorMin = new Vector2(0.62f, 1);
        dollarFace.rectTransform.anchorMax = new Vector2(1, 1);
        dollarFace.rectTransform.pivot = new Vector2(1, 1);
        dollarFace.rectTransform.offsetMin = new Vector2(4, -40);
        dollarFace.rectTransform.offsetMax = new Vector2(0, 0);
        panel.dollarLabel = NewText("Text", dollarFace.transform, "", 15, NeonTheme.Cyan, TextAlignmentOptions.Center);
        Stretch(panel.dollarLabel.rectTransform);
        Button dollarBuy = dollarFace.gameObject.AddComponent<Button>();
        NeonTheme.NeutralTint(dollarBuy);
        dollarBuy.onClick.AddListener(UnlockWithMoney);

        panel.note = NewText("Note", box, "", 12, NeonTheme.Lavender, TextAlignmentOptions.TopLeft);
        panel.note.fontStyle = FontStyles.Normal;
        panel.note.enableWordWrapping = true;
        panel.note.rectTransform.anchorMin = new Vector2(0, 0);
        panel.note.rectTransform.anchorMax = new Vector2(1, 1);
        panel.note.rectTransform.offsetMin = Vector2.zero;
        panel.note.rectTransform.offsetMax = new Vector2(0, -46);
        panel.root.SetActive(false);
        return panel;
    }

    private void ShowUnlock(UnlockPanel panel, int pawns, string dollars)
    {
        panel.root.SetActive(true);
        panel.pawnLabel.text = $"Unlock {pawns:N0}";
        panel.dollarLabel.text = dollars;
        int have = PawnBank.Total;
        bool enough = have >= pawns;
        panel.pawnButton.color = enough ? NeonTheme.Yellow : NeonTheme.Muted;
        panel.note.text = enough ? $"You have {have:N0} pawns." : $"You have {have:N0} pawns: {pawns - have:N0} more to go.";
    }

    /// <summary>The locked item being previewed on this tab (its Store id and pawn price), or false</summary>
    private bool ShowingLocked(out string id, out int pawns)
    {
        id = null; pawns = 0;
        if (tab == CustomizeTab.Pieces && PieceShapes.Showing.HasValue) { id = Store.Id(PieceShapes.Showing.Value); pawns = Store.PieceSetPawns; }
        else if (tab == CustomizeTab.Effects && showingPromotion.HasValue) { id = Store.Id(showingPromotion.Value); pawns = Store.PromotionPawns; }
        else if (tab == CustomizeTab.Backdrop && showingBackdrop.HasValue) { id = Store.Id(showingBackdrop.Value); pawns = Store.BackdropPawns; }
        return id != null;
    }

    private void UnlockWithPawns()
    {
        if (!ShowingLocked(out string id, out int price)) return;
        if (!Store.BuyWithPawns(id, price))
        {
            ShowToast($"Not enough pawns yet: you need {price - PawnBank.Total:N0} more.");
            return;
        }
        Haptics.Selection();
        UseUnlocked();
    }

    private bool buying;

    private void UnlockWithMoney()
    {
        if (buying || !ShowingLocked(out string id, out _)) return;
        Haptics.Selection();
#if UNITY_WEBGL && !UNITY_EDITOR
        ShowToast("Buying with money works in the iPhone app. Pawns work here!");
#else
        buying = true;
        Purchases.Buy(id, outcome =>
        {
            buying = false;
            switch (outcome)
            {
                case Purchases.Outcome.Bought: break;   // OnPurchased puts it to use
                case Purchases.Outcome.Cancelled: ShowToast("No problem, nothing was charged."); break;
                case Purchases.Outcome.Pending: ShowToast("Waiting for approval. It unlocks as soon as it's approved."); break;
                case Purchases.Outcome.Unavailable: ShowToast("The App Store isn't reachable right now. Try again in a moment."); break;
                default: ShowToast("The purchase didn't go through. Nothing was charged; please try again."); break;
            }
        });
#endif
    }

    /// <summary>Bought or restored through the App Store: if it's the one on show, put it to use</summary>
    private void OnPurchased(string id)
    {
        if (customizeScreen == null || !customizeScreen.activeInHierarchy) return;
        if (ShowingLocked(out string showing, out _) && showing == id) { UseUnlocked(); return; }
        BuildCards();
        RefreshCustomize();
    }

    /// <summary>Just unlocked: use it straight away</summary>
    private void UseUnlocked()
    {
        if (tab == CustomizeTab.Pieces) { PieceShapes.Current = PieceShapes.Showing.Value; PieceShapes.Showing = null; }
        else if (tab == CustomizeTab.Effects) { PromotionStyle.Current = showingPromotion.Value; showingPromotion = null; }
        else { Backdrops.Current = showingBackdrop.Value; showingBackdrop = null; }
        ShowToast("Unlocked!");
        BuildCards();
        Refresh();
        StartPreviewForTab();
    }

    private void RestorePurchases()
    {
        Haptics.Selection();
        ShowToast("Checking the App Store for your purchases...");
        Purchases.Restore(ok =>
        {
            ShowToast(ok ? "Your purchases are restored." : "Couldn't reach the App Store. Please try again.");
            if (customizeScreen != null && customizeScreen.activeInHierarchy) { BuildCards(); Refresh(); StartPreviewForTab(); }
        });
    }

    // A row of finish chips; picking one shows the set turning in it
    private GameObject LookRow(RectTransform parent, PieceSets.Kind a, PieceSets.Kind? b)
    {
        var items = new List<(string, System.Action, System.Func<bool>, System.Func<bool>)> { FinishChip(a) };
        if (b.HasValue) items.Add(FinishChip(b.Value));
        ChipRow(parent, items.ToArray(), NeonTheme.Lime, 12);
        RectTransform row = (RectTransform)parent.GetChild(parent.childCount - 1);
        row.GetComponent<LayoutElement>().preferredHeight = 24;
        row.GetComponent<HorizontalLayoutGroup>().spacing = 5;
        return row.gameObject;
    }

    private (string, System.Action, System.Func<bool>, System.Func<bool>) FinishChip(PieceSets.Kind k)
    {
        System.Func<bool> on = () => PieceSets.Current == k;
        return (PieceSets.DisplayName(k), () => { PieceSets.Current = k; StartPreviewForTab(); }, on, () => true);
    }

    private RectTransform PreviewColumn(Transform parent, string name)
    {
        RectTransform col = NewRect(name, parent);
        Stretch(col);
        col.offsetMin = new Vector2(10, 10);
        col.offsetMax = new Vector2(-10, -10);
        var layout = col.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return col;
    }

    private RawImage PreviewPicture(RectTransform parent, float height)
    {
        // Rounded corners: the picture sits in a rounded mask
        Image frame = NewImage("Picture", parent, Color.white);
        frame.sprite = NeonTheme.Rounded;
        frame.type = Image.Type.Sliced;
        frame.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        frame.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        RawImage pic = NewRaw("Image", frame.transform, null, Color.white);
        Stretch(pic.rectTransform);
        pic.raycastTarget = false;
        pic.gameObject.AddComponent<CoverFit>();
        return pic;
    }

    private TextMeshProUGUI PreviewName(RectTransform parent, Color color)
    {
        TextMeshProUGUI t = NewText("Name", parent, "", 16, color, TextAlignmentOptions.Left);
        NeonTheme.ApplyFont(t, display: true);
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;
        return t;
    }

    private TextMeshProUGUI PreviewBlurb(RectTransform parent)
    {
        TextMeshProUGUI t = NewText("Blurb", parent, "", 13, NeonTheme.Lavender, TextAlignmentOptions.TopLeft);
        t.fontStyle = FontStyles.Normal;
        t.enableWordWrapping = true;
        t.gameObject.AddComponent<LayoutElement>().preferredHeight = 34;
        return t;
    }

    // ───────── Picture grid ─────────

    private void BuildGrid(RectTransform body)
    {
        RectTransform area = NewRect("Choices", body);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(118 + 246 + 14, 0);
        area.offsetMax = Vector2.zero;

        gridTitle = NewText("Title", area, "", 15, NeonTheme.Lime, TextAlignmentOptions.Left);
        gridTitle.rectTransform.anchorMin = new Vector2(0, 1);
        gridTitle.rectTransform.anchorMax = new Vector2(1, 1);
        gridTitle.rectTransform.pivot = new Vector2(0, 1);
        gridTitle.rectTransform.offsetMin = new Vector2(2, -22);
        gridTitle.rectTransform.offsetMax = new Vector2(0, 0);
        gridHint = NewText("Hint", area, "", 12, NeonTheme.Lavender, TextAlignmentOptions.Right);
        gridHint.fontStyle = FontStyles.Normal;
        gridHint.rectTransform.anchorMin = new Vector2(0, 1);
        gridHint.rectTransform.anchorMax = new Vector2(1, 1);
        gridHint.rectTransform.pivot = new Vector2(1, 1);
        gridHint.rectTransform.offsetMin = new Vector2(0, -22);
        gridHint.rectTransform.offsetMax = new Vector2(-4, 0);

        RectTransform viewport = NewRect("Viewport", area);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = Vector2.zero;
        viewport.offsetMax = new Vector2(0, -28);
        viewport.gameObject.AddComponent<RectMask2D>();
        viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0); // catches drags for scrolling

        gridContent = NewRect("Grid", viewport);
        gridContent.anchorMin = new Vector2(0, 1);
        gridContent.anchorMax = new Vector2(1, 1);
        gridContent.pivot = new Vector2(0.5f, 1);
        gridContent.offsetMin = gridContent.offsetMax = Vector2.zero;
        var grid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
        grid.spacing = new Vector2(10, 10);
        grid.padding = new RectOffset(4, 6, 4, 6);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        gridContent.gameObject.AddComponent<GridFit>();
        gridContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        gridScroll = viewport.gameObject.AddComponent<ScrollRect>();
        gridScroll.content = gridContent;
        gridScroll.viewport = viewport;
        gridScroll.horizontal = false;
        gridScroll.movementType = ScrollRect.MovementType.Clamped;

        // Board tab: two big switch rows instead of pictures
        boardRows = NewRect("Board Switches", area);
        boardRows.anchorMin = Vector2.zero;
        boardRows.anchorMax = Vector2.one;
        boardRows.offsetMin = new Vector2(2, 0);
        boardRows.offsetMax = new Vector2(-4, -28);
        var rows = boardRows.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.spacing = 10;
        rows.childControlWidth = rows.childControlHeight = true;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;
        BoardSwitch("Solid floors", "Each level is a solid sheet instead of an open grid.",
            () => BoardLook.SolidFloors, () => BoardLook.SolidFloors = !BoardLook.SolidFloors);
        BoardSwitch("No vertical lines", "Hide the posts between levels for a cleaner cube.",
            () => BoardLook.HideVerticalLines, () => BoardLook.HideVerticalLines = !BoardLook.HideVerticalLines);
    }

    private void BuildCards()
    {
        foreach (Transform child in gridContent) Destroy(child.gameObject);
        pickCards.Clear();
        gridScroll.verticalNormalizedPosition = 1f;
        bool board = tab == CustomizeTab.Board;
        gridScroll.gameObject.SetActive(!board);
        boardRows.gameObject.SetActive(board);
        switch (tab)
        {
            case CustomizeTab.Pieces:
                gridTitle.text = "Piece set"; gridHint.text = "Tap to use it";
                foreach (PieceShapes.Kind k in Sorted(PieceShapes.All, Store.Free, Store.Owns, PieceShapes.DisplayName))
                {
                    PieceShapes.Kind kind = k;
                    AddCard(PieceShapes.DisplayName(k), Thumb("Pieces_" + k), () => PieceShapes.Current == kind, () => PieceShapes.Active == kind,
                        () => { PieceShapes.Showing = Store.Owns(kind) ? (PieceShapes.Kind?)null : kind; if (Store.Owns(kind)) PieceShapes.Current = kind; },
                        Store.Owns(kind) ? 0 : Store.PieceSetPawns);
                }
                break;
            case CustomizeTab.Backdrop:
                gridTitle.text = "Backdrop the board floats in"; gridHint.text = $"{Backdrops.All.Length} scenes";
                foreach (Backdrops.Kind k in Sorted(Backdrops.All, Store.Free, Store.Owns, Backdrops.DisplayName))
                {
                    Backdrops.Kind kind = k;
                    AddCard(Backdrops.DisplayName(k), Thumb("Backdrop_" + k), () => Backdrops.Current == kind, () => ShownBackdrop == kind,
                        () => { showingBackdrop = Store.Owns(kind) ? (Backdrops.Kind?)null : kind; if (Store.Owns(kind)) Backdrops.Current = kind; },
                        Store.Owns(kind) ? 0 : Store.BackdropPawns);
                }
                break;
            case CustomizeTab.Board:
                gridTitle.text = "Board options"; gridHint.text = "";
                break;
            default:
                gridTitle.text = "Promotion animation"; gridHint.text = "Tap Play to watch";
                foreach (PromotionStyle.Kind k in Sorted(PromotionStyle.All, Store.Free, Store.Owns, PromotionStyle.DisplayName))
                {
                    PromotionStyle.Kind kind = k;
                    AddCard(PromotionStyle.DisplayName(k), Thumb("Promo_" + k), () => PromotionStyle.Current == kind, () => ShownPromotion == kind,
                        () => { showingPromotion = Store.Owns(kind) ? (PromotionStyle.Kind?)null : kind; if (Store.Owns(kind)) PromotionStyle.Current = kind; },
                        Store.Owns(kind) ? 0 : Store.PromotionPawns);
                }
                break;
        }
    }

    /// <summary>The free default first, then what's unlocked (A to Z), then what's locked (A to Z)</summary>
    private static IEnumerable<T> Sorted<T>(IEnumerable<T> items, System.Func<T, bool> free, System.Func<T, bool> owned, System.Func<T, string> name)
    {
        var list = new List<T>(items);
        list.Sort((a, b) =>
        {
            int Rank(T x) => free(x) ? 0 : owned(x) ? 1 : 2;
            int byRank = Rank(a).CompareTo(Rank(b));
            return byRank != 0 ? byRank : string.Compare(name(a), name(b), System.StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    /// <summary>
    /// A picture card. price above 0 means it's locked: a yellow Shop tag and the pawn price;
    /// tapping it shows it in the preview card with the unlock buttons.
    /// </summary>
    private void AddCard(string name, Texture2D picture, System.Func<bool> equipped, System.Func<bool> shown, System.Action use, int price = 0)
    {
        Image face = NewImage(name, gridContent, CardFill);
        face.sprite = NeonTheme.Rounded;
        face.type = Image.Type.Sliced;
        face.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        RawImage pic = NewRaw("Picture", face.transform, picture, Color.white);
        pic.rectTransform.anchorMin = new Vector2(0, 0);
        pic.rectTransform.anchorMax = new Vector2(1, 1);
        pic.rectTransform.offsetMin = new Vector2(0, 26);
        pic.rectTransform.offsetMax = Vector2.zero;
        pic.raycastTarget = false;
        pic.gameObject.AddComponent<CoverFit>();
        if (picture == null) pic.color = NeonTheme.Muted;

        TextMeshProUGUI label = NewText("Name", face.transform, name, 13, NeonTheme.White, TextAlignmentOptions.Left);
        label.rectTransform.anchorMin = new Vector2(0, 0);
        label.rectTransform.anchorMax = new Vector2(1, 0);
        label.rectTransform.pivot = new Vector2(0, 0);
        label.rectTransform.offsetMin = new Vector2(8, 3);
        label.rectTransform.offsetMax = new Vector2(-8, 24);
        label.enableAutoSizing = true;
        label.fontSizeMin = 10;
        label.fontSizeMax = 13;

        if (price > 0)
        {
            label.rectTransform.offsetMax = new Vector2(-56, 24);
            RawImage costIcon = NewRaw("Pawn", face.transform, NeonIcons.Pawn, NeonTheme.Yellow);
            costIcon.raycastTarget = false;
            Place(costIcon.rectTransform, new Vector2(1, 0), new Vector2(13, 13), new Vector2(-50, 13));
            TextMeshProUGUI cost = NewText("Price", face.transform, $"{price:N0}", 12, NeonTheme.Yellow, TextAlignmentOptions.Right);
            cost.rectTransform.anchorMin = new Vector2(0, 0);
            cost.rectTransform.anchorMax = new Vector2(1, 0);
            cost.rectTransform.pivot = new Vector2(1, 0);
            cost.rectTransform.offsetMin = new Vector2(8, 3);
            cost.rectTransform.offsetMax = new Vector2(-8, 24);
            Image tag = NewImage("Shop", face.transform, NeonTheme.Ground);
            tag.sprite = NeonTheme.Rounded;
            tag.type = Image.Type.Sliced;
            Place(tag.rectTransform, new Vector2(0, 1), new Vector2(54, 20), new Vector2(33, -16));
            TextMeshProUGUI tagText = NewText("Text", tag.transform, "Shop", 11, NeonTheme.Yellow, TextAlignmentOptions.Center);
            Stretch(tagText.rectTransform);
        }

        Image border = NewImage("Border", face.transform, CardEdge);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        // Lime check on the one in use
        Image check = NewImage("Check", face.transform, NeonTheme.Lime);
        check.sprite = NeonTheme.Circle;
        check.raycastTarget = false;
        Place(check.rectTransform, new Vector2(1, 1), new Vector2(22, 22), new Vector2(-17, -17));
        RawImage tick = NewRaw("Tick", check.transform, NeonIcons.Tick, NeonTheme.Ground);
        Place(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(14, 14), Vector2.zero);

        Button b = face.gameObject.AddComponent<Button>();
        NeonTheme.NeutralTint(b);
        b.onClick.AddListener(() =>
        {
            use();
            Haptics.Selection();
            Refresh();
            if (tab == CustomizeTab.Pieces || tab == CustomizeTab.Backdrop) StartPreviewForTab();    // show the new set or scene
            if (tab == CustomizeTab.Effects) b.StartCoroutine(PlayAfterPick());
        });
        pickCards.Add(new PickCard { equipped = equipped, shown = shown, border = border, check = check });
    }

    // Picking an animation plays it in the preview card
    private System.Collections.IEnumerator PlayAfterPick()
    {
        yield return null;
        effectsPicture.texture = PlayInlinePromotion(ShownPromotion);
        Cover(effectsPicture);
        playPill.SetActive(false);
    }

    private void BoardSwitch(string title, string blurb, System.Func<bool> on, System.Action flip)
    {
        Image row = NewImage(title, boardRows, CardFill);
        row.sprite = NeonTheme.Rounded;
        row.type = Image.Type.Sliced;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 82;
        Image border = NewImage("Border", row.transform, CardEdge);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        TextMeshProUGUI name = NewText("Title", row.transform, title, 15, NeonTheme.White, TextAlignmentOptions.BottomLeft);
        name.rectTransform.anchorMin = new Vector2(0, 1);
        name.rectTransform.anchorMax = new Vector2(1, 1);
        name.rectTransform.offsetMin = new Vector2(14, -34);
        name.rectTransform.offsetMax = new Vector2(-76, -10);
        TextMeshProUGUI note = NewText("Note", row.transform, blurb, 12, NeonTheme.Lavender, TextAlignmentOptions.TopLeft);
        note.fontStyle = FontStyles.Normal;
        note.enableWordWrapping = true;
        note.rectTransform.anchorMin = new Vector2(0, 0);
        note.rectTransform.anchorMax = new Vector2(1, 1);
        note.rectTransform.offsetMin = new Vector2(14, 6);
        note.rectTransform.offsetMax = new Vector2(-76, -36);

        Image track = NewImage("Switch", row.transform, RailEdge);
        track.sprite = NeonTheme.Rounded;
        track.type = Image.Type.Sliced;
        track.raycastTarget = false;
        Place(track.rectTransform, new Vector2(1, 0.5f), new Vector2(50, 28), new Vector2(-39, 0));
        Image knob = NewImage("Knob", track.transform, NeonTheme.White);
        knob.sprite = NeonTheme.Circle;
        knob.raycastTarget = false;
        knob.rectTransform.sizeDelta = new Vector2(22, 22);

        Button b = row.gameObject.AddComponent<Button>();
        NeonTheme.NeutralTint(b);
        b.onClick.AddListener(() => { flip(); Haptics.Selection(); Refresh(); });
        boardSwitches.Add((on, track, knob.rectTransform, border));
    }

    // ───────── Refresh ─────────

    private void RefreshCustomize()
    {
        if (customizeScreen == null || previewBorder == null) return;
        Color accent = Accent(tab);
        foreach (PickCard c in pickCards)
        {
            bool on = c.shown();
            c.border.color = on ? accent : CardEdge;
            c.check.gameObject.SetActive(c.equipped());
            var glow = c.border.GetComponent<Outline>();
            if (glow == null) glow = c.border.gameObject.AddComponent<Outline>();
            glow.effectColor = new Color(accent.r, accent.g, accent.b, 0.45f);
            glow.effectDistance = new Vector2(2f, -2f);
            glow.enabled = on;
        }

        pawnBalance.text = $"{PawnBank.Total:N0}";

        PieceShapes.Kind set = PieceShapes.Active;
        bool setLocked = PieceShapes.Showing.HasValue;
        finishBox.SetActive(!setLocked);
        if (setLocked) ShowUnlock(piecesUnlock, Store.PieceSetPawns, Purchases.Price(Store.Id(set), Store.PieceSetDollars)); else piecesUnlock.root.SetActive(false);
        piecesName.text = PieceShapes.DisplayName(set);
        piecesBlurb.text = PieceShapes.Description(set);
        PieceColor[] sides = { PieceColor.White, PieceColor.Black, PieceColor.Green, PieceColor.Purple, PieceColor.Yellow, PieceColor.Orange };
        for (int i = 0; i < teamDots.Length; i++) teamDots[i].color = PieceSets.Tint(PieceSets.Effective, sides[i]);

        if (backdropLoop == null) { backdropPicture.texture = Thumb("Backdrop_" + ShownBackdrop); Cover(backdropPicture); }
        backdropName.text = Backdrops.DisplayName(ShownBackdrop);
        backdropBlurb.text = Backdrops.Description(ShownBackdrop);
        if (showingBackdrop.HasValue) ShowUnlock(backdropUnlock, Store.BackdropPawns, Purchases.Price(Store.Id(ShownBackdrop), Store.BackdropDollars)); else backdropUnlock.root.SetActive(false);

        effectsName.text = PromotionStyle.DisplayName(ShownPromotion);
        effectsBlurb.text = PromotionStyle.Description(ShownPromotion);
        if (showingPromotion.HasValue) ShowUnlock(effectsUnlock, Store.PromotionPawns, Purchases.Price(Store.Id(ShownPromotion), Store.PromotionDollars)); else effectsUnlock.root.SetActive(false);

        foreach (var s in boardSwitches)
        {
            bool on = s.on();
            s.track.color = on ? NeonTheme.Cyan : RailEdge;
            s.knob.anchorMin = s.knob.anchorMax = new Vector2(on ? 1f : 0f, 0.5f);
            s.knob.anchoredPosition = new Vector2(on ? -14f : 14f, 0f);
            s.border.color = on ? NeonTheme.Cyan : CardEdge;
        }
        boardPicture.texture = NeonIcons.CubeDiagram(BoardLook.SolidFloors, !BoardLook.HideVerticalLines);
        Cover(boardPicture);
    }

    // ───────── Backdrop preview: a camera slowly circling the chosen scene ─────────

    private Camera backdropCamera;
    private RenderTexture backdropTexture;
    private Coroutine backdropLoop;
    private readonly List<Camera> pausedCameras = new List<Camera>();

    private RenderTexture StartBackdropPreview()
    {
        StopBackdropPreview();
        if (backdropTexture == null) backdropTexture = new RenderTexture(640, 400, 24) { name = "Backdrop Preview" };
        // The scenes' sun, moon and drips follow Camera.main: make the preview camera the main one
        // while it runs (the menu draws over everything, so its own camera isn't needed meanwhile)
        foreach (Camera c in Camera.allCameras)
            if (c.CompareTag("MainCamera")) { c.enabled = false; pausedCameras.Add(c); }
        var go = new GameObject("Backdrop Preview Camera") { tag = "MainCamera" };
        backdropCamera = go.AddComponent<Camera>();
        backdropCamera.fieldOfView = 55f;
        backdropCamera.nearClipPlane = 0.3f;
        backdropCamera.farClipPlane = 2000f;
        backdropCamera.targetTexture = backdropTexture;
        backdropLoop = StartCoroutine(CircleBackdrop(ShownBackdrop));
        return backdropTexture;
    }

    private System.Collections.IEnumerator CircleBackdrop(Backdrops.Kind kind)
    {
        float angle = 200f;
        bool applied = false;
        while (true)
        {
            angle += Time.unscaledDeltaTime * 9f * AccessibilityPrefs.AmbientSpeed; // a full turn in 40 seconds (nearly still with Reduce Motion)
            float a = angle * Mathf.Deg2Rad;
            backdropCamera.transform.position = new Vector3(Mathf.Cos(a) * 34f, 9f, Mathf.Sin(a) * 34f);
            backdropCamera.transform.LookAt(new Vector3(0f, -3f, 0f));
            if (!applied) applied = Backdrops.Apply(backdropCamera, kind);
            Backdrops.Drift(0.4f);
            yield return null;
        }
    }

    private void StopBackdropPreview()
    {
        if (backdropLoop != null) StopCoroutine(backdropLoop);
        backdropLoop = null;
        if (backdropCamera != null)
        {
            Scenery.Clear();
            Destroy(backdropCamera.gameObject);
        }
        backdropCamera = null;
        foreach (Camera c in pausedCameras) if (c != null) c.enabled = true;
        pausedCameras.Clear();
    }

    // ───────── Helpers ─────────

    private static readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();

    private static Texture2D Thumb(string name)
    {
        if (!thumbs.TryGetValue(name, out Texture2D tex))
        {
            tex = Resources.Load<Texture2D>("CustomizeThumbs/" + name);
            thumbs[name] = tex;
        }
        return tex;
    }

    private static RawImage NewRaw(string name, Transform parent, Texture texture, Color color)
    {
        RectTransform rt = NewRect(name, parent);
        RawImage raw = rt.gameObject.AddComponent<RawImage>();
        raw.texture = texture;
        raw.color = color;
        return raw;
    }

    private static void Cover(RawImage image)
    {
        CoverFit fit = image.GetComponent<CoverFit>();
        if (fit != null) fit.Apply();
    }
}

/// <summary>Crops a RawImage's texture to fill its rect without stretching (like CSS object-fit: cover)</summary>
public class CoverFit : MonoBehaviour
{
    private void OnRectTransformDimensionsChange() => Apply();
    private void OnEnable() => Apply();

    public void Apply()
    {
        RawImage image = GetComponent<RawImage>();
        RectTransform rt = (RectTransform)transform;
        if (image == null || image.texture == null || rt.rect.width <= 0f || rt.rect.height <= 0f) return;
        float texAspect = (float)image.texture.width / image.texture.height;
        float rectAspect = rt.rect.width / rt.rect.height;
        if (texAspect > rectAspect)
        {
            float w = rectAspect / texAspect;
            image.uvRect = new Rect((1f - w) / 2f, 0f, w, 1f);
        }
        else
        {
            float h = texAspect / rectAspect;
            image.uvRect = new Rect(0f, (1f - h) / 2f, 1f, h);
        }
    }
}

/// <summary>Keeps a two-column grid's cells as wide as the grid allows</summary>
public class GridFit : MonoBehaviour
{
    private void OnRectTransformDimensionsChange() => Fit();
    private void Start() => Fit();

    private void Fit()
    {
        var grid = GetComponent<GridLayoutGroup>();
        float width = ((RectTransform)transform).rect.width - grid.padding.left - grid.padding.right;
        if (width <= 0f) return;
        float cell = (width - grid.spacing.x * (grid.constraintCount - 1)) / grid.constraintCount;
        grid.cellSize = new Vector2(cell, 104f);
    }
}

/// <summary>Calls back when a screen is shown or hidden</summary>
public class ScreenHooks : MonoBehaviour
{
    public System.Action enabled, disabled;
    private void OnEnable() => enabled?.Invoke();
    private void OnDisable() => disabled?.Invoke();
}

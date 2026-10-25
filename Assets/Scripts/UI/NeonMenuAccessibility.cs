using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Accessibility screen: switches for Reduce Motion, Color-Blind Symbols, High Contrast,
/// Larger Text and Haptics (see AccessibilityPrefs), each with a line saying what it does.
/// </summary>
public partial class NeonMenu
{
    private GameObject accessScreen;
    private readonly List<(System.Func<bool> on, Image track, RectTransform knob, TextMeshProUGUI note, string onText, string offText)> accessSwitches =
        new List<(System.Func<bool>, Image, RectTransform, TextMeshProUGUI, string, string)>();
    private RectTransform accessLeft, accessRight;
    private static bool reopenAccessibility; // Larger Text rebuilds the menu; come back to this screen

    private void BuildAccessScreen()
    {
        accessScreen = NewRect("Accessibility Screen", safeRoot).gameObject;
        RectTransform root = accessScreen.GetComponent<RectTransform>();
        Stretch(root);

        RectTransform header = NewRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(16, -64);
        header.offsetMax = new Vector2(-24, -12);

        Button back = NeonButton(header, "<", new Vector2(48, 48), NeonTheme.Cyan, NeonTheme.Pink, 0f, 24, ShowMenu);
        Place((RectTransform)back.transform, new Vector2(0, 0.5f), new Vector2(48, 48), new Vector2(28, 0));

        TextMeshProUGUI title = NewText("Title", header, "ACCESSIBILITY", 30, NeonTheme.Lime, TextAlignmentOptions.Left);
        NeonTheme.ApplyFont(title, display: true);
        title.enableAutoSizing = true;
        title.fontSizeMin = 18;
        title.fontSizeMax = 30;
        title.rectTransform.anchorMin = new Vector2(0, 0);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.offsetMin = new Vector2(70, 0);
        title.rectTransform.offsetMax = new Vector2(-180, 0);
        title.rectTransform.localEulerAngles = new Vector3(0, 0, -1);

        Button done = NeonButton(header, "Done!", new Vector2(150, 50), NeonTheme.Pink, NeonTheme.Cyan, 2f, 20, ShowMenu);
        Place((RectTransform)done.transform, new Vector2(1, 0.5f), new Vector2(150, 50), new Vector2(-75, 0));

        accessLeft = Card(root, "Seeing Card", NeonTheme.Cyan, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(16, 14), new Vector2(-8, -74));
        accessRight = Card(root, "Feeling Card", NeonTheme.Pink, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(8, 14), new Vector2(-24, -74));

        SectionLabel(accessLeft, "Seeing", NeonTheme.Cyan);
        AccessSwitch(accessLeft, "Color-Blind Symbols", () => AccessibilityPrefs.ColorBlindSymbols, v => AccessibilityPrefs.ColorBlindSymbols = v,
            "Each player gets a shape as well as a color");
        AccessSwitch(accessLeft, "High Contrast", () => AccessibilityPrefs.HighContrast, v => AccessibilityPrefs.HighContrast = v,
            "Dark plates behind small text, bolder highlights");
        AccessSwitch(accessLeft, "Larger Text", () => AccessibilityPrefs.LargerText, v => { AccessibilityPrefs.LargerText = v; RebuildForTextSize(); },
            "Bigger text in menus, guides and results");
        ChipRow(accessLeft, new (string, System.Action, System.Func<bool>, System.Func<bool>)[]
        {
            ("1.5x", () => { if (AccessibilityPrefs.HugeText) { AccessibilityPrefs.HugeText = false; RebuildForTextSize(); } },
                () => AccessibilityPrefs.LargerText && !AccessibilityPrefs.HugeText, () => AccessibilityPrefs.LargerText),
            ("2x", () => { if (!AccessibilityPrefs.HugeText) { AccessibilityPrefs.HugeText = true; RebuildForTextSize(); } },
                () => AccessibilityPrefs.LargerText && AccessibilityPrefs.HugeText, () => AccessibilityPrefs.LargerText),
        }, NeonTheme.Cyan, 15);
        accessLeft.GetChild(accessLeft.childCount - 1).GetComponent<LayoutElement>().preferredHeight = 34;

        SectionLabel(accessRight, "Moving and feeling", NeonTheme.PinkSoft);
        AccessSwitch(accessRight, "Reduce Motion", () => AccessibilityPrefs.ReduceMotion, v => AccessibilityPrefs.ReduceMotion = v,
            "Fades in place of spins; backdrops hold still");
        AccessSwitch(accessRight, "Haptics", () => AccessibilityPrefs.Haptics, v => { AccessibilityPrefs.Haptics = v; Haptics.Medium(); },
            "Taps and buzzes as you play");

        // The six symbols, so players can learn them here
        SectionLabel(accessRight, "Player symbols", NeonTheme.PinkSoft);
        RectTransform legend = NewRect("Symbols", accessRight);
        var legendSize = legend.gameObject.AddComponent<LayoutElement>();
        legendSize.preferredHeight = 52;
        legendSize.flexibleHeight = 0;
        var legendLayout = legend.gameObject.AddComponent<HorizontalLayoutGroup>();
        legendLayout.spacing = 4;
        legendLayout.childControlWidth = true;
        legendLayout.childControlHeight = true;
        legendLayout.childForceExpandWidth = true;
        legendLayout.childForceExpandHeight = true;
        for (int i = 0; i < ColorNames.Length; i++)
        {
            PieceColor player = (PieceColor)i;
            RectTransform cell = NewRect(ColorNames[i], legend);
            Image symbol = NewImage("Symbol", cell, PlayerSymbols.Tint(player));
            symbol.sprite = PlayerSymbols.Sprite(player);
            symbol.preserveAspect = true;
            symbol.raycastTarget = false;
            symbol.rectTransform.anchorMin = new Vector2(0, 0.42f);
            symbol.rectTransform.anchorMax = new Vector2(1, 1);
            symbol.rectTransform.offsetMin = symbol.rectTransform.offsetMax = Vector2.zero;
            TextMeshProUGUI name = NewText("Name", cell, ColorNames[i], 12, NeonTheme.Lavender, TextAlignmentOptions.Center);
            name.rectTransform.anchorMin = Vector2.zero;
            name.rectTransform.anchorMax = new Vector2(1, 0.42f);
            name.rectTransform.offsetMin = name.rectTransform.offsetMax = Vector2.zero;
            accessSymbols.Add((player, symbol));
        }
        AccessibilityPrefs.Changed += RefreshAccess;
    }

    private readonly List<(PieceColor player, Image image)> accessSymbols = new List<(PieceColor, Image)>();

    private void AccessSwitch(RectTransform parent, string title, System.Func<bool> on, System.Action<bool> set, string description)
    {
        SwitchRow(parent, title, out TextMeshProUGUI note, out Image track, out RectTransform knob, () =>
        {
            set(!on());
            RefreshAccess();
        });
        note.enableAutoSizing = true;
        note.fontSizeMin = 11;
        note.fontSizeMax = 14;
        accessSwitches.Add((on, track, knob, note, description, description));
    }

    private void RefreshAccess()
    {
        if (accessScreen == null) return;
        foreach (var s in accessSwitches)
        {
            bool on = s.on();
            SetSwitch(s.track, s.knob, on);
            s.note.text = on ? s.onText : s.offText;
        }
        foreach (var s in accessSymbols) s.image.color = PlayerSymbols.Tint(s.player);
        Refresh();
    }

    private void ShowAccess()
    {
        if (statsScreen != null) statsScreen.SetActive(false);
        if (notifyScreen != null) notifyScreen.SetActive(false);
        menuScreen.SetActive(false);
        setupScreen.SetActive(false);
        customizeScreen.SetActive(false);
        onlineScreen.SetActive(false);
        friendsScreen.SetActive(false);
        accessScreen.SetActive(true);
        RefreshAccess();
    }

    /// <summary>
    /// Larger Text zooms the whole menu canvas, which every screen is laid out for when it's built:
    /// build the menu again at the new size and come straight back here
    /// </summary>
    private void RebuildForTextSize()
    {
        reopenAccessibility = true;
        AccessibilityPrefs.Changed -= RefreshAccess;
        Destroy(gameObject);
        new GameObject("Neon Menu").AddComponent<NeonMenu>();
    }

    /// <summary>With Larger Text on, put each screen in a scroll view so the zoomed menu never clips</summary>
    private void FitScreensForTextSize()
    {
        if (AccessibilityPrefs.TextScale <= 1f) return;

        // Toasts span the screen and can take two lines
        toastPanel.rectTransform.anchorMin = new Vector2(0f, 0f);
        toastPanel.rectTransform.anchorMax = new Vector2(1f, 0f);
        toastPanel.rectTransform.sizeDelta = new Vector2(-32f, 70f);
        toastPanel.rectTransform.anchoredPosition = new Vector2(0f, 48f);

        // Home: the decorative cube steps aside so the buttons get the width
        Transform cube = menuScreen.transform.Find("Cube");
        if (cube != null) cube.gameObject.SetActive(false);
        var column = (RectTransform)menuScreen.transform.Find("Column");
        if (column != null) column.offsetMin = new Vector2(24, column.offsetMin.y);
        LargerTextLayout.Pan(menuScreen, new Vector2(480, 360));

        LargerTextLayout.Stack(setupScreen, 74f, (RectTransform)setupScreen.transform.Find("Left Card"), (RectTransform)setupScreen.transform.Find("Right Card"));
        LargerTextLayout.Stack(notifyScreen, 74f, (RectTransform)notifyScreen.transform.Find("Permission Card"), (RectTransform)notifyScreen.transform.Find("Kinds Card"));
        LargerTextLayout.Stack(accessScreen, 74f, accessLeft, accessRight);
        LargerTextLayout.Pan(customizeScreen, new Vector2(844, 390));
        LargerTextLayout.Pan(onlineScreen, new Vector2(844, 390));
        LargerTextLayout.Pan(friendsScreen, new Vector2(844, 390));
        if (statsScreen != null) LargerTextLayout.Pan(statsScreen, new Vector2(844, 390));
    }
}

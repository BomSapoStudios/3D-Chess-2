using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Sound screen (from Settings): switches for sound effects, music, and music while you play,
/// each with Quiet / Medium / Loud. See SoundPrefs.
/// </summary>
public partial class NeonMenu
{
    private GameObject soundScreen;
    private readonly List<(System.Func<bool> on, Image track, RectTransform knob, TextMeshProUGUI note, string onText, string offText)> soundSwitches =
        new List<(System.Func<bool>, Image, RectTransform, TextMeshProUGUI, string, string)>();

    private void BuildSoundScreen()
    {
        soundScreen = NewRect("Sound Screen", safeRoot).gameObject;
        RectTransform root = soundScreen.GetComponent<RectTransform>();
        Stretch(root);

        RectTransform header = NewRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(16, -64);
        header.offsetMax = new Vector2(-24, -12);

        Button back = NeonButton(header, "<", new Vector2(48, 48), NeonTheme.Cyan, NeonTheme.Pink, 0f, 24, ShowSettings);
        Place((RectTransform)back.transform, new Vector2(0, 0.5f), new Vector2(48, 48), new Vector2(28, 0));

        TextMeshProUGUI title = NewText("Title", header, "SOUND", 30, NeonTheme.Lime, TextAlignmentOptions.Left);
        NeonTheme.ApplyFont(title, display: true);
        title.rectTransform.anchorMin = new Vector2(0, 0);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.offsetMin = new Vector2(70, 0);
        title.rectTransform.offsetMax = new Vector2(-180, 0);
        title.rectTransform.localEulerAngles = new Vector3(0, 0, -1);

        Button done = NeonButton(header, "Done!", new Vector2(150, 50), NeonTheme.Pink, NeonTheme.Cyan, 2f, 20, ShowSettings);
        Place((RectTransform)done.transform, new Vector2(1, 0.5f), new Vector2(150, 50), new Vector2(-75, 0));

        RectTransform left = Card(root, "Effects Card", NeonTheme.Cyan, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(16, 14), new Vector2(-8, -74));
        RectTransform right = Card(root, "Music Card", NeonTheme.Pink, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(8, 14), new Vector2(-24, -74));

        SectionLabel(left, "Sound effects", NeonTheme.Cyan);
        SoundSwitch(left, "Sound Effects", () => SoundPrefs.Effects, v => SoundPrefs.Effects = v,
            "Moves, captures, buttons and puzzles", "Off: the game is silent");
        LevelChips(left, () => SoundPrefs.EffectsLevel, v => { SoundPrefs.EffectsLevel = v; Sfx.Play("capture"); }, () => SoundPrefs.Effects, NeonTheme.Cyan);
        TextMeshProUGUI tip = NewText("Tip", left, "The ringer switch on the side of your iPhone mutes the game too.", 13, NeonTheme.Muted, TextAlignmentOptions.TopLeft);
        tip.fontStyle = FontStyles.Normal;
        tip.textWrappingMode = TextWrappingModes.Normal;
        tip.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;

        SectionLabel(right, "Music", NeonTheme.PinkSoft);
        SoundSwitch(right, "Music", () => SoundPrefs.Music, v => SoundPrefs.Music = v,
            "Plays in the menus", "Off: no music anywhere");
        SoundSwitch(right, "Music While You Play", () => SoundPrefs.MusicInGames, v => SoundPrefs.MusicInGames = v,
            "Music keeps going in games and puzzles", "Off: quiet while you think");
        LevelChips(right, () => SoundPrefs.MusicLevel, v => SoundPrefs.MusicLevel = v, () => SoundPrefs.Music, NeonTheme.PinkSoft);
    }

    private void SoundSwitch(RectTransform parent, string title, System.Func<bool> on, System.Action<bool> set, string onText, string offText)
    {
        SwitchRow(parent, title, out TextMeshProUGUI note, out Image track, out RectTransform knob, () =>
        {
            set(!on());
            Sfx.Play(on() ? "ui_toggle_on" : "ui_toggle_off");
            RefreshSound();
        });
        note.enableAutoSizing = true;
        note.fontSizeMin = 11;
        note.fontSizeMax = 14;
        soundSwitches.Add((on, track, knob, note, onText, offText));
    }

    private void LevelChips(RectTransform parent, System.Func<int> level, System.Action<int> set, System.Func<bool> enabled, Color color)
    {
        ChipRow(parent, new (string, System.Action, System.Func<bool>, System.Func<bool>)[]
        {
            ("Quiet", () => set(0), () => level() == 0, enabled),
            ("Medium", () => set(1), () => level() == 1, enabled),
            ("Loud", () => set(2), () => level() == 2, enabled),
        }, color, 15);
        parent.GetChild(parent.childCount - 1).GetComponent<LayoutElement>().preferredHeight = 36;
    }

    private void RefreshSound()
    {
        if (soundScreen == null) return;
        foreach (var s in soundSwitches)
        {
            bool on = s.on();
            SetSwitch(s.track, s.knob, on);
            s.note.text = on ? s.onText : s.offText;
        }
        Refresh();
    }

    private void ShowSound()
    {
        if (statsScreen != null) statsScreen.SetActive(false);
        if (notifyScreen != null) notifyScreen.SetActive(false);
        if (settingsScreen != null) settingsScreen.SetActive(false);
        if (accessScreen != null) accessScreen.SetActive(false);
        menuScreen.SetActive(false);
        setupScreen.SetActive(false);
        customizeScreen.SetActive(false);
        onlineScreen.SetActive(false);
        friendsScreen.SetActive(false);
        soundScreen.SetActive(true);
        RefreshSound();
    }
}

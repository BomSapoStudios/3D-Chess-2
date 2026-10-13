using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Zombie Survival from the main menu: pick a difficulty, see your best score, start a run</summary>
public partial class NeonMenu
{
    private GameObject zombiePicker;
    private bool zombieRun;
    private TextMeshProUGUI[] zombieBestLabels;

    private void ShowZombiePicker()
    {
        if (zombiePicker == null) BuildZombiePicker();
        AIDifficulty[] levels = { AIDifficulty.Easy, AIDifficulty.Medium, AIDifficulty.Hard };
        for (int i = 0; i < levels.Length; i++)
        {
            int best = ZombieMode.Best(levels[i]);
            zombieBestLabels[i].text = best > 0 ? $"Best {best}" : "No runs yet";
        }
        zombiePicker.transform.SetAsLastSibling();
        zombiePicker.SetActive(true);
    }

    private void BuildZombiePicker()
    {
        // Dim the menu; tapping outside the card closes the picker
        Image dim = NewImage("Zombie Picker", transform, new Color(0f, 0f, 0f, 0.65f));
        Stretch(dim.rectTransform);
        Button closeArea = dim.gameObject.AddComponent<Button>();
        closeArea.transition = Selectable.Transition.None;
        closeArea.onClick.AddListener(() => zombiePicker.SetActive(false));
        zombiePicker = dim.gameObject;

        Image card = NewImage("Card", dim.transform, NeonTheme.Ground);
        card.sprite = NeonTheme.Rounded;
        card.type = Image.Type.Sliced;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(600, 290), Vector2.zero);
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // taps on the card don't close it
        Image border = NewImage("Border", card.transform, NeonTheme.Lime);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        TextMeshProUGUI title = NewText("Title", card.transform, "ZOMBIE SURVIVAL", 30, NeonTheme.Lime, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(title, display: true);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(560, 50), new Vector2(0, -40));
        TextMeshProUGUI hint = NewText("Hint", card.transform,
            "Zombie pawns rise every round and march at you. Last as long as you can. No check: lose every piece and it's over.",
            15, NeonTheme.Lavender, TextAlignmentOptions.Center);
        hint.fontStyle = FontStyles.Normal;
        hint.enableWordWrapping = true;
        Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(540, 48), new Vector2(0, -92));

        (AIDifficulty level, string name, string note, Color color)[] levels =
        {
            (AIDifficulty.Easy, "Pea", "2 a round from 25\nturn into Knights", NeonTheme.Cyan),
            (AIDifficulty.Medium, "Walnut", "2 a round from 15\nturn into Queens", NeonTheme.Yellow),
            (AIDifficulty.Hard, "Galaxy", "2 a round from 10\nbites spread", NeonTheme.Pink),
        };
        zombieBestLabels = new TextMeshProUGUI[levels.Length];
        for (int i = 0; i < levels.Length; i++)
        {
            var l = levels[i];
            float x = (i - 1) * 180f;
            Button b = NeonButton(card.transform, l.name, new Vector2(160, 56), l.color, NeonTheme.Ground, (i - 1) * 1f, 20, () => StartZombieRun(l.level));
            Place((RectTransform)b.transform, new Vector2(0.5f, 0f), new Vector2(160, 56), new Vector2(x, 104));
            TextMeshProUGUI note = NewText("Note", card.transform, l.note, 13, NeonTheme.Lavender, TextAlignmentOptions.Center);
            note.fontStyle = FontStyles.Normal;
            Place(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(170, 36), new Vector2(x, 56));
            zombieBestLabels[i] = NewText("Best", card.transform, "", 14, l.color, TextAlignmentOptions.Center);
            Place(zombieBestLabels[i].rectTransform, new Vector2(0.5f, 0f), new Vector2(170, 22), new Vector2(x, 26));
        }
        zombiePicker.SetActive(false);
    }

    private void StartZombieRun(AIDifficulty level)
    {
        Haptics.Selection();
        zombiePicker.SetActive(false);
        playerCount = 2;
        boardSize = BoardSize.Large8x8x8;
        difficulty = level;
        playerTypes.Clear();
        playerTypes.Add(PlayerType.Human);    // you, White
        playerTypes.Add(PlayerType.Computer); // the zombies, Black
        PlayerViewUI.StartView = PieceColor.White;
        chaosMode = false;
        timedPlay = false;
        setupOnline = false;
        quickPlay = true;   // don't overwrite the player's saved setup
        zombieRun = true;
        StartFromSetup();
        zombieRun = false;
        quickPlay = false;
    }
}

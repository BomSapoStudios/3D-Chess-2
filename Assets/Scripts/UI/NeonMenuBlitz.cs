using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Queen Blitz from the main menu: pick a puzzle (with your best stars on each) and play it</summary>
public partial class NeonMenu
{
    private GameObject blitzPicker;
    private bool blitzRun;
    private int blitzChoice;
    private TextMeshProUGUI[] blitzStarLabels;

    private void ShowBlitzPicker()
    {
        if (blitzPicker == null) BuildBlitzPicker();
        for (int i = 0; i < QueenBlitz.Puzzles.Length; i++)
        {
            int stars = QueenBlitz.BestStars(i);
            blitzStarLabels[i].text = stars > 0 ? QueenBlitz.StarText(stars) : $"Par {QueenBlitz.Puzzles[i].par}";
            blitzStarLabels[i].color = stars > 0 ? NeonTheme.Yellow : NeonTheme.Muted;
        }
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

        TextMeshProUGUI title = NewText("Title", card.transform, "QUEEN BLITZ", 30, NeonTheme.Yellow, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(title, display: true);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(580, 46), new Vector2(0, -36));
        TextMeshProUGUI hint = NewText("Hint", card.transform,
            "One queen. Clear every pawn in as few moves as you can. She drives straight through pawns; blocks stop her.",
            15, NeonTheme.Lavender, TextAlignmentOptions.Center);
        hint.fontStyle = FontStyles.Normal;
        hint.enableWordWrapping = true;
        Place(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(560, 44), new Vector2(0, -84));

        int count = QueenBlitz.Puzzles.Length;
        blitzStarLabels = new TextMeshProUGUI[count];
        for (int i = 0; i < count; i++)
        {
            int index = i;
            int row = i / 5, col = i % 5;
            float x = (col - 2) * 112f;
            float y = row == 0 ? 168f : 64f;
            Color color = QueenBlitz.Puzzles[i].ice ? NeonTheme.Hex("BFEFFF") : QueenBlitz.Puzzles[i].queen.HasValue ? NeonTheme.Cyan : NeonTheme.Lime;
            Button b = NeonButton(card.transform, $"{i + 1}", new Vector2(96, 44), color, NeonTheme.Ground, (col - 2) * 0.6f, 20, () => StartBlitz(index));
            Place((RectTransform)b.transform, new Vector2(0.5f, 0f), new Vector2(96, 44), new Vector2(x, y + 18));
            TextMeshProUGUI name = NewText("Name", card.transform, QueenBlitz.Puzzles[i].name, 12, NeonTheme.Lavender, TextAlignmentOptions.Center);
            name.fontStyle = FontStyles.Normal;
            Place(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(108, 18), new Vector2(x, y - 14));
            blitzStarLabels[i] = NewText("Stars", card.transform, "", 14, NeonTheme.Yellow, TextAlignmentOptions.Center);
            Place(blitzStarLabels[i].rectTransform, new Vector2(0.5f, 0f), new Vector2(108, 20), new Vector2(x, y - 32));
        }
        blitzPicker.SetActive(false);
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

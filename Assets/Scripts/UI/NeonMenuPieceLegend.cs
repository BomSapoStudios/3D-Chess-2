using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Customize > Pieces: a "Who's who" pill on the turning set opens a legend over the preview card,
/// each of the set's six pieces with its name and the plain chess piece it stands for, so an
/// alien or an elephant reads as a Knight or a Rook. Tap anywhere on it to close.
/// </summary>
public partial class NeonMenu
{
    private static readonly ChessPieceType[] LegendOrder =
        { ChessPieceType.King, ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Pawn };

    private GameObject legendRoot;
    private TextMeshProUGUI legendTitle;
    private RawImage[] legendPieces, legendClassic;

    private void BuildPieceLegend(Transform card, RawImage picture)
    {
        // The pill on the turning set
        Image pill = NewImage("Who's who", picture.transform.parent, NeonTheme.Lime);
        pill.sprite = NeonTheme.Rounded;
        pill.type = Image.Type.Sliced;
        Place(pill.rectTransform, new Vector2(1, 0), new Vector2(84, 22), new Vector2(-48, 15));
        TextMeshProUGUI pillText = NewText("Text", pill.transform, "Who's who", 12, NeonTheme.Ground, TextAlignmentOptions.Center);
        Stretch(pillText.rectTransform);
        Button open = pill.gameObject.AddComponent<Button>();
        open.transition = Selectable.Transition.None;
        open.onClick.AddListener(() => { Haptics.Selection(); Sfx.Tap(); ShowPieceLegend(true); });

        // The legend, over the whole card
        Image panel = NewImage("Legend", card, PreviewFill);
        panel.sprite = NeonTheme.Rounded;
        panel.type = Image.Type.Sliced;
        Stretch(panel.rectTransform);
        Button close = panel.gameObject.AddComponent<Button>();
        close.transition = Selectable.Transition.None;
        close.onClick.AddListener(() => { Haptics.Selection(); Sfx.Tap(); ShowPieceLegend(false); });
        Image edge = NewImage("Border", panel.transform, NeonTheme.Lime);
        edge.sprite = NeonTheme.RoundedOutline;
        edge.type = Image.Type.Sliced;
        edge.raycastTarget = false;
        Stretch(edge.rectTransform);

        legendTitle = NewText("Title", panel.transform, "", 14, NeonTheme.Lime, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(legendTitle, display: true);
        legendTitle.rectTransform.anchorMin = new Vector2(0, 1);
        legendTitle.rectTransform.anchorMax = new Vector2(1, 1);
        legendTitle.rectTransform.pivot = new Vector2(0.5f, 1);
        legendTitle.rectTransform.sizeDelta = new Vector2(-16, 22);
        legendTitle.rectTransform.anchoredPosition = new Vector2(0, -10);

        TextMeshProUGUI hint = NewText("Hint", panel.transform, "Tap to close", 11, NeonTheme.Lavender, TextAlignmentOptions.Center);
        hint.fontStyle = FontStyles.Normal;
        Place(hint.rectTransform, new Vector2(0.5f, 0), new Vector2(200, 16), new Vector2(0, 14));

        // Two columns of three: the set's piece, its name, and the plain piece beside the name
        RectTransform grid = NewRect("Pieces", panel.transform);
        Stretch(grid);
        grid.offsetMin = new Vector2(10, 26);
        grid.offsetMax = new Vector2(-10, -36);
        legendPieces = new RawImage[LegendOrder.Length];
        legendClassic = new RawImage[LegendOrder.Length];
        for (int i = 0; i < LegendOrder.Length; i++)
        {
            int col = i % 2, row = i / 2;
            RectTransform cell = NewRect(LegendOrder[i].ToString(), grid);
            cell.anchorMin = new Vector2(col * 0.5f, 1f - (row + 1) / 3f);
            cell.anchorMax = new Vector2((col + 1) * 0.5f, 1f - row / 3f);
            cell.offsetMin = cell.offsetMax = Vector2.zero;

            legendPieces[i] = NewRaw("Piece", cell, null, Color.white);
            legendPieces[i].raycastTarget = false;
            legendPieces[i].rectTransform.anchorMin = legendPieces[i].rectTransform.anchorMax = new Vector2(0, 0.5f);
            legendPieces[i].rectTransform.pivot = new Vector2(0, 0.5f);
            legendPieces[i].rectTransform.sizeDelta = new Vector2(52, 52);
            legendPieces[i].rectTransform.anchoredPosition = new Vector2(2, 0);

            TextMeshProUGUI label = NewText("Name", cell, LegendOrder[i].ToString(), 13, NeonTheme.White, TextAlignmentOptions.Left);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0, 0.5f);
            label.rectTransform.pivot = new Vector2(0, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(60, 18);
            label.rectTransform.anchoredPosition = new Vector2(56, 10);

            legendClassic[i] = NewRaw("Classic", cell, null, NeonTheme.Lavender);
            legendClassic[i].raycastTarget = false;
            legendClassic[i].rectTransform.anchorMin = legendClassic[i].rectTransform.anchorMax = new Vector2(0, 0.5f);
            legendClassic[i].rectTransform.pivot = new Vector2(0, 0.5f);
            legendClassic[i].rectTransform.sizeDelta = new Vector2(24, 24);
            legendClassic[i].rectTransform.anchoredPosition = new Vector2(56, -11);
        }

        legendRoot = panel.gameObject;
        legendRoot.SetActive(false);
    }

    private void ShowPieceLegend(bool show)
    {
        if (legendRoot == null) return;
        legendRoot.SetActive(show);
        if (show) RefreshPieceLegend();
    }

    private void RefreshPieceLegend()
    {
        if (legendRoot == null || !legendRoot.activeSelf) return;
        PieceShapes.Kind set = PieceShapes.Active;
        legendTitle.text = PieceShapes.DisplayName(set);
        bool themed = set != PieceShapes.Kind.Cyberpunk; // Cyberpunk's pieces are the plain shapes already
        for (int i = 0; i < LegendOrder.Length; i++)
        {
            legendPieces[i].texture = LegendIcon(set, LegendOrder[i]);
            legendClassic[i].texture = LegendIcon(PieceShapes.Kind.Cyberpunk, LegendOrder[i]);
            legendClassic[i].gameObject.SetActive(themed && legendClassic[i].texture != null);
        }
    }

    // The same pictures as the setup bench (Resources/PieceIcons/<set folder>/Light_<Piece>)
    private static Texture2D LegendIcon(PieceShapes.Kind set, ChessPieceType type) =>
        Resources.Load<Texture2D>("PieceIcons/" + PieceShapes.IconFolder(set) + "Light_" + type);
}

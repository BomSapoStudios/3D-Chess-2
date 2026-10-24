using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Customize: tapping a promotion animation shows it playing, over and over, on a pawn that
/// becomes a queen. The pieces stand on a little stage far from the menu with their own camera,
/// drawn into a picture on a pop-up card; tap anywhere to close it.
/// </summary>
public partial class NeonMenu
{
    private GameObject previewPopup;
    private RawImage previewImage;
    private TextMeshProUGUI previewTitle, previewNote;
    private GameObject previewStage;
    private Camera previewCamera;
    private RenderTexture previewTexture;
    private Coroutine previewLoop;

    private static readonly Vector3 StagePosition = new Vector3(5000f, 0f, 0f);

    private void ShowPromotionPreview(PromotionStyle.Kind kind)
    {
        if (previewPopup == null) BuildPromotionPreview();
        previewTitle.text = PromotionStyle.DisplayName(kind).ToUpperInvariant();
        previewNote.text = PromotionStyle.Description(kind);
        previewPopup.transform.SetAsLastSibling();
        previewPopup.SetActive(true);
        if (previewLoop != null) StopCoroutine(previewLoop);
        previewLoop = StartCoroutine(PreviewLoop(kind));
    }

    private void BuildPromotionPreview()
    {
        Image dim = NewImage("Promotion Preview", transform, new Color(0f, 0f, 0f, 0.7f));
        Stretch(dim.rectTransform);
        Button close = dim.gameObject.AddComponent<Button>();
        close.transition = Selectable.Transition.None;
        close.onClick.AddListener(ClosePromotionPreview);
        previewPopup = dim.gameObject;

        Image card = NewImage("Card", dim.transform, NeonTheme.Ground);
        card.sprite = NeonTheme.Rounded;
        card.type = Image.Type.Sliced;
        card.raycastTarget = false;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(460, 330), Vector2.zero);
        Image border = NewImage("Border", card.transform, NeonTheme.Lime);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        previewTitle = NewText("Title", card.transform, "", 24, NeonTheme.Lime, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(previewTitle, display: true);
        Place(previewTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(420, 36), new Vector2(0, -28));

        EnsurePreviewTexture();
        previewImage = new GameObject("Picture", typeof(RectTransform)).AddComponent<RawImage>();
        previewImage.transform.SetParent(card.transform, false);
        previewImage.texture = previewTexture;
        previewImage.raycastTarget = false;
        Place(previewImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(400, 220), new Vector2(0, 4));

        previewNote = NewText("Note", card.transform, "", 14, NeonTheme.Lavender, TextAlignmentOptions.Center);
        previewNote.fontStyle = FontStyles.Normal;
        previewNote.enableWordWrapping = true;
        Place(previewNote.rectTransform, new Vector2(0.5f, 0f), new Vector2(420, 36), new Vector2(0, 34));
        TextMeshProUGUI hint = NewText("Hint", card.transform, "Tap anywhere to close", 12, NeonTheme.Muted, TextAlignmentOptions.Center);
        Place(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(420, 18), new Vector2(0, 12));
        previewPopup.SetActive(false);
    }

    private void EnsurePreviewTexture()
    {
        if (previewTexture == null) previewTexture = new RenderTexture(640, 400, 16) { name = "Piece Preview" };
    }

    // ───────── Live previews inside the Customize screen's preview card ─────────

    /// <summary>Play a set's turntable, or a promotion animation, into the preview texture (no pop-up)</summary>
    private RenderTexture PlayInlineShapes()
    {
        EnsurePreviewTexture();
        StopInlinePreview();
        previewLoop = StartCoroutine(ShapeLoop());
        return previewTexture;
    }

    private RenderTexture PlayInlinePromotion(PromotionStyle.Kind kind)
    {
        EnsurePreviewTexture();
        StopInlinePreview();
        previewLoop = StartCoroutine(PreviewLoop(kind));
        return previewTexture;
    }

    private void StopInlinePreview()
    {
        if (previewLoop != null) StopCoroutine(previewLoop);
        previewLoop = null;
        PieceEffects.ClearAround(StagePosition, 60f, null);
        if (previewStage != null) Destroy(previewStage);
        previewStage = null;
        PieceEffects.PreviewCamera = null;
    }

    private void ClosePromotionPreview()
    {
        if (previewLoop != null) StopCoroutine(previewLoop);
        previewLoop = null;
        PieceEffects.ClearAround(StagePosition, 60f, null); // cubes, rings and copies from an animation mid-play
        if (previewStage != null) Destroy(previewStage);
        previewStage = null;
        PieceEffects.PreviewCamera = null;
        if (previewPopup != null) previewPopup.SetActive(false);
    }

    /// <summary>A pawn becomes a queen, again and again, until the card is closed</summary>
    private IEnumerator PreviewLoop(PromotionStyle.Kind kind)
    {
        if (previewStage != null) Destroy(previewStage);
        PieceEffects.ClearAround(StagePosition, 60f, null); // anything the last animation left behind
        previewStage = new GameObject("Promotion Stage");
        previewStage.transform.position = StagePosition;

        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(previewStage.transform, false);
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        light.intensity = 1.1f;

        previewCamera = new GameObject("Preview Camera").AddComponent<Camera>();
        previewCamera.transform.SetParent(previewStage.transform, false);
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = NeonTheme.Ground;
        previewCamera.targetTexture = previewTexture;
        previewCamera.fieldOfView = 40f;
        PieceEffects.PreviewCamera = previewCamera;

        ChessPiece queen = null;
        while (true)
        {
            if (queen != null) Destroy(queen.gameObject);
            ChessPiece pawn = StagePiece<Pawn>();
            queen = StagePiece<Queen>();
            // Frame the pieces (and room for the effects) the first time round
            Bounds b = PieceBounds(pawn);
            b.Encapsulate(PieceBounds(queen));
            PieceEffects.PreviewCell = Mathf.Max(b.size.x, b.size.z) * 1.6f;
            float height = b.size.y;
            Vector3 look = StagePosition + Vector3.up * height * 0.55f;
            previewCamera.transform.position = look + new Vector3(0f, height * 0.45f, -height * 2.5f);
            previewCamera.transform.LookAt(look);
            yield return null;
            PieceEffects.Promote(pawn, queen, kind);
            Destroy(pawn.gameObject);
            yield return new WaitForSeconds(2.6f);
        }
    }

    // ───────── Piece set preview ─────────

    /// <summary>Customize > Piece set: the whole set, both sides, turning on a little turntable</summary>
    private void ShowShapePreview(PieceShapes.Kind kind)
    {
        if (previewPopup == null) BuildPromotionPreview();
        previewTitle.text = PieceShapes.DisplayName(kind).ToUpperInvariant();
        previewNote.text = PieceShapes.Description(kind);
        previewPopup.transform.SetAsLastSibling();
        previewPopup.SetActive(true);
        if (previewLoop != null) StopCoroutine(previewLoop);
        previewLoop = StartCoroutine(ShapeLoop());
    }

    private IEnumerator ShapeLoop()
    {
        if (previewStage != null) Destroy(previewStage);
        previewStage = new GameObject("Piece Set Stage");
        previewStage.transform.position = StagePosition;

        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(previewStage.transform, false);
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
        light.intensity = 1.1f;

        previewCamera = new GameObject("Preview Camera").AddComponent<Camera>();
        previewCamera.transform.SetParent(previewStage.transform, false);
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = NeonTheme.Ground;
        previewCamera.targetTexture = previewTexture;
        previewCamera.fieldOfView = 34f;

        // White's row in front, Black's behind, on a slowly turning turntable
        var turntable = new GameObject("Turntable").transform;
        turntable.SetParent(previewStage.transform, false);
        System.Type[] order = { typeof(Rook), typeof(Knight), typeof(Bishop), typeof(Queen), typeof(King), typeof(Pawn) };
        const float gap = 1.9f;
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < order.Length; i++)
            {
                ChessPiece piece = StagePiece(order[i], row == 0 ? PieceColor.White : PieceColor.Black);
                piece.transform.SetParent(turntable, false);
                piece.transform.localPosition = new Vector3((i - (order.Length - 1) / 2f) * gap, 0f, row == 0 ? -1.4f : 1.4f);
            }
        Vector3 look = StagePosition + Vector3.up * 1.1f;
        previewCamera.transform.position = look + new Vector3(0f, 3.6f, -14.5f);
        previewCamera.transform.LookAt(look);
        while (true)
        {
            turntable.localRotation = Quaternion.Euler(0f, freezeTurntable ? 16f : Mathf.Sin(Time.time * 0.5f) * 35f, 0f);
            yield return null;
        }
    }

    private bool freezeTurntable; // a still pose, for thumbnails

    private ChessPiece StagePiece<T>() where T : ChessPiece => StagePiece(typeof(T), PieceColor.White);

    private ChessPiece StagePiece(System.Type type, PieceColor color)
    {
        var go = new GameObject($"Preview {type.Name}");
        go.SetActive(false);
        go.transform.SetParent(previewStage.transform, false);
        ChessPiece piece = (ChessPiece)go.AddComponent(type);
        piece.pieceColor = color;
        piece.pieceType = PieceModels.TypeOf(piece);
        go.SetActive(true);
        piece.ApplyMaterial();
        go.transform.position = StagePosition;
        return piece;
    }

    private static Bounds PieceBounds(ChessPiece piece)
    {
        Renderer[] rs = piece.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(piece.transform.position + Vector3.up, Vector3.one * 2f);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}

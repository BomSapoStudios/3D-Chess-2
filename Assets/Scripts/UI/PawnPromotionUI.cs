using UnityEngine;

/// <summary>
/// Pawn promotion picker: a big neon panel with a picture of each piece the pawn can become.
/// Piece pictures are rendered from the cyberpunk models (Art/Pieces/render_icons.py) into
/// Resources/PieceIcons/<Light|Dark>_<Piece>.png.
/// </summary>
public class PawnPromotionUI : MonoBehaviour
{
    public bool showPromotionUI = false;

    private static readonly ChessPieceType[] Choices = { ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Knight };

    private System.Action<ChessPieceType> onPieceSelected;
    private PieceColor promotingPlayerColor;
    private readonly Texture2D[] icons = new Texture2D[4];

#if DEVELOPMENT_BUILD
    // Test hook: `defaults write BomSapo.Chess3D DebugShowPromotion -int 1` opens the picker on game start
    private void Start()
    {
        if (PlayerPrefs.GetInt("DebugShowPromotion", 0) == 1)
            ShowPromotionSelection(PlayerPrefs.GetInt("DebugPromotionDark", 0) == 1 ? PieceColor.Black : PieceColor.White, t => Debug.Log($"Debug promotion picked {t}"));
    }
#endif

    /// <summary>
    /// Show the promotion selection UI
    /// </summary>
    public void ShowPromotionSelection(PieceColor playerColor, System.Action<ChessPieceType> onSelection)
    {
        showPromotionUI = true;
        promotingPlayerColor = playerColor;
        onPieceSelected = onSelection;

        string team = playerColor == PieceColor.White ? "Light" : "Dark";
        string folder = PieceShapes.Current == PieceShapes.Kind.AbyssGlow ? "Abyss/" : ""; // icons match the chosen piece set
        for (int i = 0; i < Choices.Length; i++)
        {
            icons[i] = Resources.Load<Texture2D>($"PieceIcons/{folder}{team}_{Choices[i]}");
        }
    }

    /// <summary>
    /// Hide the promotion selection UI
    /// </summary>
    public void HidePromotionSelection()
    {
        showPromotionUI = false;
        onPieceSelected = null;
    }

    private void OnGUI()
    {
        if (!showPromotionUI) return;
        GUI.depth = -20;
        Vector2 ui = TouchGUI.Begin();

        // Dim the board and swallow taps on it while choosing
        Rect full = new Rect(-200f, -200f, ui.x + 400f, ui.y + 400f);
        GUI.DrawTexture(full, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.04f, 0f, 0.1f, 0.65f), 0, 0);
        TouchGUI.Block(full);

        Color accent = promotingPlayerColor == PieceColor.White ? NeonTheme.Cyan : NeonTheme.Pink;

        // Four big cards, sized to fill most of the screen
        const float gap = 16f;
        const float pad = 26f;
        float cardW = Mathf.Clamp((ui.x - 80f - pad * 2f - gap * 3f) / 4f, 110f, 170f);
        float iconSize = cardW - 16f;
        float cardH = Mathf.Min(iconSize + 46f, ui.y - 150f);
        iconSize = Mathf.Min(iconSize, cardH - 46f);
        float panelW = cardW * 4f + gap * 3f + pad * 2f;
        float panelH = cardH + 112f;
        Rect panel = new Rect((ui.x - panelW) / 2f, (ui.y - panelH) / 2f, panelW, panelH);
        NeonTheme.GUIPanel(panel, accent, 0.96f);

        GUI.Label(new Rect(panel.x, panel.y + 14f, panel.width, 40f), "PROMOTE YOUR PAWN!",
            NeonTheme.GUILabelStyle(NeonTheme.Lime, 28, true, TextAnchor.MiddleCenter, display: true));
        GUI.Label(new Rect(panel.x, panel.y + 54f, panel.width, 26f), "Tap the piece it becomes",
            NeonTheme.GUILabelStyle(NeonTheme.Lavender, 17, true, TextAnchor.MiddleCenter));

        for (int i = 0; i < Choices.Length; i++)
        {
            Rect card = new Rect(panel.x + pad + i * (cardW + gap), panel.y + 92f, cardW, cardH);
            bool tapped = TouchGUI.NeonButton(card, "", i == 0 ? NeonTheme.Lime : accent, NeonTheme.Ground, outlined: true);

            Rect iconRect = new Rect(card.x + (cardW - iconSize) / 2f, card.y + 4f, iconSize, iconSize);
            if (icons[i] != null) GUI.DrawTexture(iconRect, icons[i], ScaleMode.ScaleToFit, true);

            GUI.Label(new Rect(card.x, card.yMax - 40f, card.width, 34f), Choices[i].ToString().ToUpperInvariant(),
                NeonTheme.GUILabelStyle(i == 0 ? NeonTheme.Lime : accent, 17, true, TextAnchor.MiddleCenter, display: true));

            if (tapped)
            {
                TouchGUI.End();
                OnPieceButtonClicked(Choices[i]);
                return;
            }
        }
        TouchGUI.End();
    }

    private void OnPieceButtonClicked(ChessPieceType pieceType)
    {
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: ENTRY - Player selected {pieceType}");
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: showPromotionUI={showPromotionUI}");
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: promotingPlayerColor={promotingPlayerColor}");
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: onPieceSelected callback is {(onPieceSelected != null ? "NOT NULL" : "NULL")}");
        
        // Validate promotion is still active
        if (PawnPromotionManager.Instance != null && !PawnPromotionManager.Instance.IsPromotionInProgress())
        {
            Debug.LogWarning($"PawnPromotionUI.OnPieceButtonClicked: Promotion no longer in progress - ignoring click");
            HidePromotionSelection(); // Hide UI since promotion is no longer active
            return;
        }
        
        if (onPieceSelected == null)
        {
            Debug.LogError($"PawnPromotionUI.OnPieceButtonClicked: ERROR - onPieceSelected callback is NULL! Cannot proceed with promotion.");
            return;
        }
        
        // CRITICAL FIX: Store the callback locally BEFORE hiding the UI
        // This prevents the race condition where HidePromotionSelection() nulls the callback
        System.Action<ChessPieceType> localCallback = onPieceSelected;
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: Stored callback locally to prevent race condition");
        
        // Hide the UI 
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: About to hide promotion selection");
        HidePromotionSelection();
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: UI hidden, showPromotionUI={showPromotionUI}");
        
        // Notify the callback using the locally stored reference
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: About to invoke callback with {pieceType}");
        try
        {
            Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: Invoking locally stored callback...");
            localCallback.Invoke(pieceType);
            Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: Callback invocation completed successfully");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"PawnPromotionUI.OnPieceButtonClicked: Exception during callback invocation: {ex.Message}");
            Debug.LogError($"PawnPromotionUI.OnPieceButtonClicked: Exception stack trace: {ex.StackTrace}");
            
            // Failsafe: Try to find PawnPromotionManager and call it directly
            Debug.LogError($"PawnPromotionUI.OnPieceButtonClicked: Attempting direct call to PawnPromotionManager");
            PawnPromotionManager manager = FindFirstObjectByType<PawnPromotionManager>();
            if (manager != null)
            {
                Debug.LogWarning($"PawnPromotionUI.OnPieceButtonClicked: Found PawnPromotionManager, trying direct call");
                // Use reflection to call the private OnPieceSelected method
                var method = typeof(PawnPromotionManager).GetMethod("OnPieceSelected", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    method.Invoke(manager, new object[] { pieceType });
                    Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: Direct method call successful");
                }
                else
                {
                    Debug.LogError($"PawnPromotionUI.OnPieceButtonClicked: OnPieceSelected method not found via reflection");
                }
            }
            else
            {
                Debug.LogError($"PawnPromotionUI.OnPieceButtonClicked: PawnPromotionManager not found for emergency call");
            }
        }
        
        Debug.Log($"PawnPromotionUI.OnPieceButtonClicked: EXIT");
    }
}

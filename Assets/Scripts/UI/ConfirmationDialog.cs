using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple confirmation dialog for important game actions like forfeit.
/// Uses immediate mode GUI for simplicity and reliability.
/// </summary>
public class ConfirmationDialog : MonoBehaviour
{
    [Header("Dialog Settings")]
    public bool enableConfirmationDialogs = true;
    public float dialogTimeout = 30f; // Auto-cancel after this time
    
    public static ConfirmationDialog Instance { get; private set; }
    
    // Dialog state
    private bool isDialogActive = false;
    private string dialogTitle = "";
    private string dialogMessage = "";
    private System.Action onConfirm;
    private System.Action onCancel;
    private float dialogStartTime;
    
    // GUI styling
    private GUIStyle dialogBoxStyle;
    private GUIStyle titleStyle;
    private GUIStyle messageStyle;
    private GUIStyle buttonStyle;
    private GUIStyle overlayStyle;
    
    // Dialog dimensions
    private Rect dialogRect;
    private Rect overlayRect;
    private bool stylesInitialized = false;
    
    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            Debug.Log("ConfirmationDialog: Instance created");
        }
        else
        {
            Debug.LogWarning("ConfirmationDialog: Multiple instances detected, destroying duplicate");
            Destroy(gameObject);
        }
    }
    
    private void Start()
    {
        InitializeStyles();
    }
    
    private void Update()
    {
        if (isDialogActive)
        {
            // Handle timeout
            if (Time.time - dialogStartTime > dialogTimeout)
            {
                Debug.Log("ConfirmationDialog: Dialog timed out, auto-canceling");
                OnCancelClicked();
            }
            
            // Handle keyboard shortcuts
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnConfirmClicked();
            }
            else if (Input.GetKeyDown(KeyCode.Escape))
            {
                OnCancelClicked();
            }
        }
    }
    
    /// <summary>
    /// Initialize GUI styles for the dialog
    /// </summary>
    private void InitializeStyles()
    {
        // Dialog box background
        dialogBoxStyle = new GUIStyle();
        dialogBoxStyle.normal.background = CreateColorTexture(new Color(0.2f, 0.2f, 0.2f, 0.95f));
        dialogBoxStyle.border = new RectOffset(10, 10, 10, 10);
        dialogBoxStyle.padding = new RectOffset(20, 20, 20, 20);
        
        // Overlay background
        overlayStyle = new GUIStyle();
        overlayStyle.normal.background = CreateColorTexture(new Color(0f, 0f, 0f, 0.5f));
        
        // Title style
        titleStyle = new GUIStyle();
        titleStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.025f);
        titleStyle.fontStyle = FontStyle.Bold;
        titleStyle.alignment = TextAnchor.MiddleCenter;
        titleStyle.normal.textColor = Color.white;
        titleStyle.wordWrap = true;
        
        // Message style
        messageStyle = new GUIStyle();
        messageStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.02f);
        messageStyle.alignment = TextAnchor.MiddleLeft;
        messageStyle.normal.textColor = Color.white;
        messageStyle.wordWrap = true;
        
        // Button style
        buttonStyle = new GUIStyle();
        buttonStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.02f);
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
        buttonStyle.normal.textColor = Color.white;
        buttonStyle.normal.background = CreateColorTexture(new Color(0.4f, 0.4f, 0.4f, 1f));
        buttonStyle.hover.background = CreateColorTexture(new Color(0.6f, 0.6f, 0.6f, 1f));
        buttonStyle.active.background = CreateColorTexture(new Color(0.3f, 0.3f, 0.3f, 1f));
        buttonStyle.border = new RectOffset(5, 5, 5, 5);
        buttonStyle.padding = new RectOffset(10, 10, 10, 10);
        
        // Calculate dialog rect
        float dialogWidth = Screen.width * 0.4f;
        float dialogHeight = Screen.height * 0.3f;
        float dialogX = (Screen.width - dialogWidth) / 2f;
        float dialogY = (Screen.height - dialogHeight) / 2f;
        
        dialogRect = new Rect(dialogX, dialogY, dialogWidth, dialogHeight);
        overlayRect = new Rect(0, 0, Screen.width, Screen.height);
        
        stylesInitialized = true;
    }
    
    /// <summary>
    /// Create a texture with a solid color
    /// </summary>
    private Texture2D CreateColorTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
    
    /// <summary>
    /// Show a confirmation dialog
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="message">Dialog message</param>
    /// <param name="onConfirm">Action to execute on confirm</param>
    /// <param name="onCancel">Action to execute on cancel (optional)</param>
    public void ShowDialog(string title, string message, System.Action onConfirm, System.Action onCancel = null)
    {
        if (!enableConfirmationDialogs)
        {
            Debug.Log("ConfirmationDialog: Dialogs disabled, auto-confirming");
            onConfirm?.Invoke();
            return;
        }
        
        if (isDialogActive)
        {
            Debug.LogWarning("ConfirmationDialog: Dialog already active, ignoring new request");
            return;
        }
        
        dialogTitle = title;
        dialogMessage = message;
        this.onConfirm = onConfirm;
        this.onCancel = onCancel;
        isDialogActive = true;
        dialogStartTime = Time.time;
        
        Debug.Log($"ConfirmationDialog: Showing dialog - {title}: {message}");
    }
    
    /// <summary>
    /// Show forfeit confirmation dialog
    /// </summary>
    /// <param name="playerColor">Player requesting forfeit</param>
    /// <param name="onConfirm">Action to execute on confirm</param>
    public void ShowForfeitDialog(PieceColor playerColor, System.Action onConfirm)
    {
        string title = "Confirm Forfeit";
        string message = $"Forfeit as {playerColor}? The game ends now and you lose.";
        
        ShowDialog(title, message, onConfirm);
    }
    
    /// <summary>
    /// Show draw offer confirmation dialog
    /// </summary>
    /// <param name="playerColor">Player offering draw</param>
    /// <param name="onConfirm">Action to execute on confirm</param>
    public void ShowDrawOfferDialog(PieceColor playerColor, System.Action onConfirm)
    {
        string title = "Offer Draw";
        string message = $"Offer a draw as {playerColor}? Your opponent can accept or decline.";
        
        ShowDialog(title, message, onConfirm);
    }
    
    /// <summary>
    /// Hide the dialog without any action
    /// </summary>
    public void HideDialog()
    {
        if (!isDialogActive) return;
        
        isDialogActive = false;
        dialogTitle = "";
        dialogMessage = "";
        onConfirm = null;
        onCancel = null;
        
        Debug.Log("ConfirmationDialog: Dialog hidden");
    }
    
    /// <summary>
    /// Handle confirm button click
    /// </summary>
    private void OnConfirmClicked()
    {
        if (!isDialogActive) return;
        
        Debug.Log("ConfirmationDialog: Confirm clicked");
        
        System.Action confirmAction = onConfirm;
        HideDialog();
        confirmAction?.Invoke();
    }
    
    /// <summary>
    /// Handle cancel button click
    /// </summary>
    private void OnCancelClicked()
    {
        if (!isDialogActive) return;
        
        Debug.Log("ConfirmationDialog: Cancel clicked");
        
        System.Action cancelAction = onCancel;
        HideDialog();
        cancelAction?.Invoke();
    }
    
    /// <summary>
    /// Check if dialog is currently active
    /// </summary>
    public bool IsDialogActive()
    {
        return isDialogActive;
    }
    
    private void OnGUI()
    {
        if (!isDialogActive) return;

        // Neon panel like the Game Menu, scaled for the screen and kept in the safe area
        GUI.depth = -30;
        Vector2 ui = TouchGUI.Begin();
        TouchGUI.Block(new Rect(0, 0, ui.x, ui.y));
        Color dim = new Color(0f, 0f, 0f, 0.6f);
        Color old = GUI.color;
        GUI.color = dim;
        GUI.DrawTexture(new Rect(-ui.x, -ui.y, ui.x * 3f, ui.y * 3f), Texture2D.whiteTexture);
        GUI.color = old;

        float w = Mathf.Min(420f, ui.x - 40f), h = 210f;
        Rect panel = new Rect((ui.x - w) / 2f, (ui.y - h) / 2f, w, h);
        NeonTheme.GUIPanel(panel, NeonTheme.Cyan, 0.97f);

        GUI.Label(new Rect(panel.x + 20, panel.y + 16, w - 40, 40), dialogTitle.ToUpperInvariant(),
            NeonTheme.GUILabelStyle(NeonTheme.Lime, 26, true, TextAnchor.MiddleCenter, display: true));
        GUIStyle message = NeonTheme.GUILabelStyle(NeonTheme.Lavender, 17, false, TextAnchor.MiddleCenter);
        message.wordWrap = true;
        GUI.Label(new Rect(panel.x + 24, panel.y + 60, w - 48, 70), dialogMessage.Replace("\\n", " "), message);

        float bw = (w - 60f) / 2f;
        if (TouchGUI.NeonButton(new Rect(panel.x + 20, panel.yMax - 64, bw, 46), "Confirm", NeonTheme.Pink, NeonTheme.Cyan, fontSize: 18)) OnConfirmClicked();
        else if (TouchGUI.NeonButton(new Rect(panel.xMax - 20 - bw, panel.yMax - 64, bw, 46), "Cancel", NeonTheme.Cyan, NeonTheme.Pink, fontSize: 18)) OnCancelClicked();
        TouchGUI.End();
    }
}
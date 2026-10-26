using UnityEngine;

/// <summary>
/// A "?" button on the game screen that opens a one-screen guide to playing.
/// Zombie Survival and Queen Blitz have their own guides with only their rules. Each guide
/// opens by itself the first time its kind of game is started.
/// </summary>
public class HowToPlayUI : MonoBehaviour
{
    private const string SeenKey = "HowToPlaySeen";
    private bool showing;
    private EmergencyChessBoard board;
    private bool autoShownThisGame;

    // Two columns so the whole guide fits on a phone screen without scrolling
    private const string GuideLeft =
        "SETUP\n" +
        "Tap a piece in your tray, then a dot on the board. \"Auto-place\" places the rest for you. " +
        "When everyone is done, tap your \"Ready to Play\" button.\n\n" +
        "MOVING\n" +
        "Tap one of your pieces, then a dot. Pieces move left-right, front-back, and up-down between floors.\n\n" +
        "SEEING THE BOARD\n" +
        "Drag to turn. Pinch to zoom. The floor buttons on the right show one floor at a time.";

    private const string GuideRight =
        "PIECES\n" +
        "King: 1 step any direction, including up or down.\n" +
        "Queen: any distance, straight or diagonal.\n" +
        "Rook: any distance along one axis.\n" +
        "Bishop: any distance diagonally, across floors too.\n" +
        "Knight: an L (2 then 1) in any plane; jumps.\n" +
        "Pawn: forward to the far side, then promotes.\n\n" +
        "WINNING\n" +
        "Checkmate the other King. Lose your King and you're out.";

    // Zombie Survival
    private const string ZombieLeft =
        "SETUP\n" +
        "You have a King, Queen, 2 Rooks, 2 Bishops and 2 Knights, and no pawns. Place them anywhere on " +
        "your first wall of the cube, or tap \"Auto-place\", then \"Ready to Play\".\n\n" +
        "THE HORDE\n" +
        "Zombie pawns rise on the far side and march toward you one step a round. A zombie bites any of " +
        "your pieces on the squares diagonally in front of it; a blocked zombie waits. More rise every " +
        "round, and two a round later on.\n\n" +
        "PROMOTED ZOMBIES\n" +
        "A zombie that reaches your side becomes a Knight (Pea) or a Queen (Walnut, Galaxy) and hunts you.";

    private const string ZombieRight =
        "HOW IT ENDS\n" +
        "There is no check: your King is just another piece. The run ends when your last piece falls.\n\n" +
        "SCORE\n" +
        "10 a round survived, 5 per zombie pawn destroyed, 20 per promoted zombie. Best score is kept for each brain size.\n\n" +
        "PAWNS\n" +
        "Each zombie you destroy earns pawns: 1 on Pea, 1.5 on Walnut, 2 on Galaxy (a run rounds down), up to 100 a day.\n\n" +
        "BRAIN SIZE\n" +
        "Pea: two a round from round 25. Walnut: from 15. Galaxy: from 10, and bitten pieces rise as zombies.\n\n" +
        "HELP\n" +
        "Turn Planner (Zombie Hunt) shows the fastest way to destroy each zombie. Undo takes back your last round.";

    // Queen Blitz
    private const string BlitzLeft =
        "THE GOAL\n" +
        "Clear every pawn with your lone Queen within par, the fewest moves possible. The top bar counts the moves you have left; " +
        "run out with pawns still standing and the puzzle is lost, so Retry. The first time you clear a puzzle, you keep all its pawns.\n\n" +
        "SWEEPING\n" +
        "Tap the Queen, then a dot. She slides any distance along any straight line or diagonal (26 directions) " +
        "and knocks out every pawn she passes through, not just the one she stops on.\n\n" +
        "PLACING THE QUEEN\n" +
        "In early puzzles she's placed for you. Later you tap her on the bench and pick any free square; " +
        "placing her isn't a move. \"Auto-place\" picks a best square for 1 pawn.";

    private const string BlitzRight =
        "BLOCKS\n" +
        "Glowing blocks can't be entered or passed. They stop the Queen on the square before them.\n\n" +
        "ICE\n" +
        "On an ice floor the Queen can't stop partway: every move slides until the edge of the board " +
        "or the square before a block.\n\n" +
        "HELP\n" +
        "Undo takes back a move. Restart starts the puzzle again. Tap the title bar to jump to another puzzle or pack. The floor buttons on the right show one " +
        "floor at a time, and the View buttons look from either side.";

    /// <summary>The guide for the game being played: its title, seen-key, and two columns</summary>
    private static (string title, string key, string left, string right) Guide()
    {
        if (QueenBlitz.Active) return ("QUEEN BLITZ", "HowToPlaySeen.Blitz", BlitzLeft, BlitzRight);
        if (ZombieMode.Active) return ("ZOMBIE SURVIVAL", "HowToPlaySeen.Zombies", ZombieLeft, ZombieRight);
        return ("HOW TO PLAY", SeenKey, GuideLeft, GuideRight);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        GameObject go = new GameObject("How To Play UI");
        go.AddComponent<HowToPlayUI>();
        DontDestroyOnLoad(go);
    }

    private bool InGame()
    {
        if (board == null) board = FindFirstObjectByType<EmergencyChessBoard>();
        return board != null;
    }

    private void Update()
    {
        if (!InGame())
        {
            showing = false;
            autoShownThisGame = false;
            return;
        }
        if (!autoShownThisGame)
        {
            autoShownThisGame = true;
            string key = Guide().key; // each mode's guide opens by itself the first time
            if (PlayerPrefs.GetInt(key, 0) == 0)
            {
                showing = true;
                PlayerPrefs.SetInt(key, 1);
                PlayerPrefs.Save();
            }
        }
    }

    private void OnGUI()
    {
        if (!InGame()) return;

        Vector2 ui = TouchGUI.Begin();
        GUI.depth = -10; // draw above the other game UI

        if (!showing)
        {
            // Sits just left of the level buttons
            // Top right, above the level buttons
            if (TouchGUI.NeonButton(new Rect(ui.x - 82f, 16f, 72f, 44f), "?", NeonTheme.Yellow, NeonTheme.Pink, fontSize: 22))
            {
                showing = true;
            }
            TouchGUI.End();
            return;
        }

        float zoom = AccessibilityPrefs.TextScale;
        if (zoom > 1f)
        {
            // Larger Text: the guide is drawn bigger in one column that scrolls
            TouchGUI.End();
            ui = TouchGUI.Begin(zoom);
            GUI.depth = -10;
        }

        Rect full = new Rect(0, 0, ui.x, ui.y);
        TouchGUI.Block(full);

        float margin = zoom > 1f ? 12f : 30f;
        Rect panel = new Rect(margin, zoom > 1f ? 8f : 15f, ui.x - margin * 2, ui.y - (zoom > 1f ? 16f : 30f));
        NeonTheme.GUIPanel(panel, NeonTheme.Cyan, 0.96f);

        var guide = Guide();
        GUI.Label(new Rect(panel.x, panel.y + 6f, panel.width, 30f), guide.title, NeonTheme.GUILabelStyle(NeonTheme.Lime, 22, true, TextAnchor.MiddleCenter, display: true));

        GUIStyle body = NeonTheme.GUILabelStyle(NeonTheme.White, 12, false, TextAnchor.UpperLeft);
        float top = panel.y + 40f;
        if (zoom > 1f)
        {
            Rect view = new Rect(panel.x + 16f, top, panel.width - 32f, panel.yMax - 58f - top);
            ScrollingText(view, guide.left + "\n\n" + guide.right, body);
        }
        else
        {
            float columnWidth = (panel.width - 48f) / 2f;
            float columnHeight = panel.height - 100f;
            GUI.Label(new Rect(panel.x + 16f, top, columnWidth, columnHeight), guide.left, body);
            GUI.Label(new Rect(panel.x + 32f + columnWidth, top, columnWidth, columnHeight), guide.right, body);
        }

        if (NeonTheme.GUIButton(new Rect(panel.x + panel.width / 2f - 70f, panel.yMax - 52f, 140f, 42f), "Got it!", NeonTheme.Pink, NeonTheme.Cyan, false, 17))
        {
            showing = false;
            scroll = 0f;
        }

        TouchGUI.End();
    }

    private float scroll;

    /// <summary>Text taller than its box: drag (or use the wheel) to scroll it, with a thin bar showing where you are</summary>
    private void ScrollingText(Rect view, string text, GUIStyle style)
    {
        float height = style.CalcHeight(new GUIContent(text), view.width - 10f);
        float most = Mathf.Max(0f, height - view.height);
        Event e = Event.current;
        if (view.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag) { scroll -= e.delta.y; e.Use(); }
            else if (e.type == EventType.ScrollWheel) { scroll += e.delta.y * 12f; e.Use(); }
        }
        scroll = Mathf.Clamp(scroll, 0f, most);

        GUI.BeginGroup(view);
        GUI.Label(new Rect(0f, -scroll, view.width - 10f, height), text, style);
        GUI.EndGroup();

        if (most > 0f && e.type == EventType.Repaint)
        {
            float barHeight = Mathf.Max(24f, view.height * view.height / height);
            float barY = view.y + (view.height - barHeight) * (scroll / most);
            GUI.DrawTexture(new Rect(view.xMax - 4f, barY, 4f, barHeight), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, NeonTheme.Cyan, 0, 2);
        }
    }
}

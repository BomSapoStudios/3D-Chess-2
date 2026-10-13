using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The result panel shown when a game ends, with Rematch and Home.
/// Online: everyone still here has 15 seconds to tap Rematch. If they all do, the server opens
/// a new room with the same settings (seats passed along, so someone else goes first) and
/// everyone moves into it; otherwise everyone goes back to the menu when time runs out.
/// Local: Rematch starts the same game again.
/// </summary>
public class RematchUI : MonoBehaviour
{
    /// <summary>True while the panel is up (the old New Game button stays hidden)</summary>
    public static bool Showing { get; private set; }
    /// <summary>A rematch room the menu should join as soon as it opens</summary>
    public static string PendingRoom;
    /// <summary>A note for the menu to show when it opens ("Rematch didn't start")</summary>
    public static string PendingNote;

    private const float RematchSeconds = 15f;
    private const float ShowDelay = 1.2f; // let the last move or capture play out first

    private float gameOverAt = -1f;
    private float localDeadline;   // fallback countdown until the server's arrives
    private bool voted, leaving;
    private Texture2D ring;
    private float ringFraction = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        SceneManager.sceneLoaded += (scene, mode) => TryCreate();
        TryCreate();
    }

    private static void TryCreate()
    {
        Showing = false;
        if (FindFirstObjectByType<RematchUI>() != null) return;
        if (FindFirstObjectByType<MainMenuController>() != null) return; // the menu, not a game
        new GameObject("Rematch UI").AddComponent<RematchUI>();
    }

    private static bool Online => OnlineSession.IsActive;
    private static OnlineGameBridge Bridge => OnlineGameBridge.Instance;

    private bool armed; // seen this game being set up or played (the game state outlives scenes)

    private bool GameIsOver()
    {
        // A rematch loads straight from a finished game, so the old Game Over lingers for a moment
        // until the new game starts; only a game seen in progress here can end
        var state = GameStateManager.Instance != null ? GameStateManager.Instance.currentState : GameState.WaitingForConfiguration;
        if (state == GameState.PiecePlacement || state == GameState.Playing) armed = true;
        if (!armed) return false;
        if (state == GameState.GameOver) return true;
        return Online && Bridge != null && Bridge.ServerGameOver && !OnlineSession.Replaying;
    }

    private void Update()
    {
        Reason(); // start listening for the end-of-game message once the game exists
        if (leaving) return;
        if (gameOverAt < 0f)
        {
            if (!GameIsOver()) return;
            gameOverAt = Time.unscaledTime;
            localDeadline = Time.unscaledTime + RematchSeconds;
            Haptics.Selection();
            if (!Online && !ZombieMode.Active) RecordVsComputer();
        }
        Showing = Time.unscaledTime - gameOverAt >= ShowDelay;
        if (!Online) return;

        NetRematch r = Bridge != null ? Bridge.Rematch : null;
        if (r != null && !string.IsNullOrEmpty(r.code))
        {
            // Everyone said yes: off to the new room
            PendingRoom = r.code;
            GoHome(null);
        }
        else if (Showing && SecondsLeft(r) <= 0f && CanRematch(r))
        {
            GoHome(voted ? "Rematch didn't start: not everyone said yes" : null);
        }
        else if (Showing && r != null && r.failed && voted)
        {
            GoHome("Rematch didn't start: " + WhyNot(r));
        }
    }

    // ───────────────────────── Online state ─────────────────────────

    private float SecondsLeft(NetRematch r)
    {
        if (r != null && r.deadline > 0)
        {
            double left = (r.deadline - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0;
            return Mathf.Clamp((float)left, 0f, RematchSeconds);
        }
        return Mathf.Max(0f, localDeadline - Time.unscaledTime);
    }

    /// <summary>Could a rematch still happen: at least two players here who haven't said no</summary>
    private static bool CanRematch(NetRematch r)
    {
        if (r == null) return true; // not heard yet; assume so
        if (r.failed) return false;
        int willing = (r.present ?? new string[0]).Count(c => !(r.declined ?? new string[0]).Contains(c));
        return willing >= 2;
    }

    private static string WhyNot(NetRematch r)
    {
        string me = OnlineSession.LocalColor.ToString();
        string[] declined = (r.declined ?? new string[0]).Where(c => c != me).ToArray();
        if (declined.Length > 0) return $"{string.Join(" and ", declined)} said no";
        if ((r.present ?? new string[0]).Length < 2) return "your opponent left";
        return "time ran out";
    }

    private string OnlineStatus(NetRematch r)
    {
        if (r == null) return voted ? "Waiting for the others…" : "Play again? Everyone has to say yes.";
        if (!CanRematch(r)) return "No rematch: " + WhyNot(r);
        string me = OnlineSession.LocalColor.ToString();
        string[] yes = (r.accepted ?? new string[0]).Where(c => c != me).ToArray();
        string[] waiting = (r.present ?? new string[0]).Where(c => c != me && !(r.accepted ?? new string[0]).Contains(c)).ToArray();
        if (voted) return waiting.Length > 0 ? $"Waiting for {string.Join(", ", waiting)}…" : "Starting the rematch…";
        if (yes.Length > 0) return $"{string.Join(" and ", yes)} want{(yes.Length == 1 ? "s" : "")} a rematch!";
        return "Play again? Everyone has to say yes.";
    }

    // ───────────────────────── Actions ─────────────────────────

    private void Rematch()
    {
        Haptics.Selection();
        if (Online)
        {
            if (voted || OnlineClient.Instance == null) return;
            voted = true;
            OnlineClient.Instance.SendRematch(true);
            return;
        }
        // Local: the same game again
        GameConfiguration config = SceneController.Instance != null ? SceneController.Instance.GetCurrentGameConfiguration() : null;
        if (config == null) { GoHome(null); return; }
        leaving = true;
        Showing = false;
        SceneController.Instance.LoadGameScene(config);
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>Tap Rematch (RematchDemo)</summary>
    public void DebugRematch() => Rematch();
#endif

    private void GoHome(string note)
    {
        if (leaving) return;
        leaving = true;
        Showing = false;
        if (Online && PendingRoom == null && OnlineClient.Instance != null && !voted) OnlineClient.Instance.SendRematch(false);
        PendingNote = note;
        if (SceneController.Instance != null) SceneController.Instance.LoadMainMenu();
    }

    // ───────────────────────── Drawing ─────────────────────────

    private void OnGUI()
    {
        if (!Showing || leaving) return;
        Vector2 ui = TouchGUI.Begin();
        GUI.depth = -15;

        float w = Mathf.Min(500f, ui.x - 32f), h = 158f;
        Rect panel = new Rect((ui.x - w) / 2f, ui.y - h - 12f, w, h);
        TouchGUI.Block(panel);
        NeonTheme.GUIPanel(panel, NeonTheme.Pink, 0.94f);

        var (title, color) = Headline();
        GUI.Label(new Rect(panel.x + 18, panel.y + 8, w - 36, 34), title, NeonTheme.GUILabelStyle(color, 24, true, TextAnchor.MiddleLeft, display: true));
        // Online: the rating change once the server has recorded the game
        NetRatingChange rating = Online && Bridge != null ? Bridge.MyRating : null;
        if (rating != null)
        {
            int change = rating.after - rating.before;
            string mode = rating.mode == "melee" ? "Melee" : "Duel";
            string line = $"{mode} {rating.after} ({(change >= 0 ? "+" : "")}{change})";
            if (rating.tier != rating.tierBefore) line += $"  Now {rating.tier}!";
            GUI.Label(new Rect(panel.x + 18, panel.y + 8, w - 36, 34), line, NeonTheme.GUILabelStyle(change >= 0 ? NeonTheme.Lime : NeonTheme.PinkSoft, 16, true, TextAnchor.MiddleRight));
        }
        string reason = Reason();
        if (!string.IsNullOrEmpty(reason))
            GUI.Label(new Rect(panel.x + 18, panel.y + 40, w - 36, 20), reason, NeonTheme.GUILabelStyle(NeonTheme.Lavender, 13, false));

        float rowY = panel.y + 66f;
        NetRematch r = Online && Bridge != null ? Bridge.Rematch : null;
        bool possible = !Online || CanRematch(r);
        float x = panel.x + 18f;

        if (Online && possible)
        {
            // Countdown ring with the seconds left in the middle
            float left = SecondsLeft(r);
            Rect ringRect = new Rect(x, rowY - 2, 54, 54);
            DrawRing(ringRect, left / RematchSeconds);
            GUI.Label(ringRect, Mathf.CeilToInt(left).ToString(), NeonTheme.GUILabelStyle(NeonTheme.Yellow, 20, true, TextAnchor.MiddleCenter, display: true));
            x += 66f;
        }

        GUI.enabled = possible && !voted;
        string label = voted ? "Waiting…" : "Rematch!";
        if (TouchGUI.NeonButton(new Rect(x, rowY, 170, 50), label, NeonTheme.Lime, NeonTheme.Pink, fontSize: 19)) Rematch();
        GUI.enabled = true;
        if (TouchGUI.NeonButton(new Rect(x + 184, rowY, 120, 50), "Home", NeonTheme.Cyan, NeonTheme.Pink, outlined: true, fontSize: 17)) GoHome(null);

        string status = Online ? OnlineStatus(r) : ZombieMode.Active ? $"Another run on {ZombieMode.DifficultyName(ZombieMode.Difficulty)}?" : "Same players, same settings.";
        GUI.Label(new Rect(panel.x + 18, panel.y + h - 32, w - 36, 22), status, NeonTheme.GUILabelStyle(possible ? NeonTheme.Yellow : NeonTheme.PinkSoft, 14, true));
        TouchGUI.End();
    }

    private (string, Color) Headline()
    {
        if (ZombieMode.Active) return ("OVERRUN!", NeonTheme.Pink);
        PieceColor? winner = GameEndDetectionManager.Instance != null ? GameEndDetectionManager.Instance.Winner : null;
        string reason = Reason() ?? "";
        bool draw = winner == null || reason.IndexOf("draw", StringComparison.OrdinalIgnoreCase) >= 0;
        if (Online && Bridge != null && Bridge.ServerGameOver && GameStateManager.Instance != null && GameStateManager.Instance.currentState != GameState.GameOver)
            return ("GAME OVER", NeonTheme.Yellow); // ended on the server (someone walked out)
        if (draw) return ("IT'S A DRAW", NeonTheme.Yellow);

        // Whose win is it from this device's point of view
        PieceColor[] mine = Online
            ? new[] { OnlineSession.LocalColor }
            : (PlayerManager.Instance != null ? PlayerManager.Instance.GetAllPlayers().ToArray() : new PieceColor[0])
                .Where(c => TurnManager.Instance == null || !TurnManager.Instance.IsPlayerAI(c)).ToArray();
        if (mine.Length == 1) return mine[0] == winner.Value ? ("YOU WIN!", NeonTheme.Lime) : ("YOU LOST", NeonTheme.Pink);
        return ($"{winner.Value.ToString().ToUpperInvariant()} WINS!", NeonTheme.Lime);
    }

    /// <summary>Quick Play and local games against robots: tally wins, losses and draws by difficulty on this phone</summary>
    private void RecordVsComputer()
    {
        if (TurnManager.Instance == null || PlayerManager.Instance == null) return;
        var players = PlayerManager.Instance.GetAllPlayers();
        PieceColor[] humans = players.Where(c => !TurnManager.Instance.IsPlayerAI(c)).ToArray();
        if (humans.Length != 1 || humans.Length == players.Count) return; // only one person against the computer
        GameConfiguration config = SceneController.Instance != null ? SceneController.Instance.GetCurrentGameConfiguration() : null;
        if (config == null) return;
        PieceColor? winner = GameEndDetectionManager.Instance != null ? GameEndDetectionManager.Instance.Winner : null;
        ComputerStats.Record(config.aiDifficulty, winner == null ? 0 : winner.Value == humans[0] ? 1 : -1);
    }

    private string lastReason;
    private bool subscribed;

    private string Reason()
    {
        if (!subscribed && GameEndDetectionManager.Instance != null)
        {
            subscribed = true;
            GameEndDetectionManager.Instance.OnGameEnd += r => lastReason = r;
        }
        return lastReason;
    }

    /// <summary>A ring that empties clockwise from the top as time runs out</summary>
    private void DrawRing(Rect rect, float fraction)
    {
        const int size = 96;
        if (ring == null) ring = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        if (Mathf.Abs(fraction - ringFraction) > 0.004f)
        {
            ringFraction = fraction;
            var pixels = new Color32[size * size];
            float c = (size - 1) / 2f, outer = size / 2f - 1f, inner = outer - 9f;
            Color32 on = NeonTheme.Yellow, off = new Color(NeonTheme.Muted.r, NeonTheme.Muted.g, NeonTheme.Muted.b, 0.7f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - c, dy = y - c, d = Mathf.Sqrt(dx * dx + dy * dy);
                if (d > outer + 0.5f || d < inner - 0.5f) continue;
                float edge = Mathf.Clamp01(Mathf.Min(outer + 0.5f - d, d - inner + 0.5f));
                // Angle measured clockwise from 12 o'clock (texture rows run bottom to top)
                float angle = Mathf.Atan2(dx, dy) / (2f * Mathf.PI);
                if (angle < 0f) angle += 1f;
                Color32 col = angle <= fraction ? on : off;
                col.a = (byte)(col.a * edge);
                pixels[y * size + x] = col;
            }
            ring.SetPixels32(pixels);
            ring.Apply();
        }
        GUI.DrawTexture(rect, ring);
    }

    private void OnDestroy()
    {
        Showing = false;
        if (ring != null) Destroy(ring);
    }
}

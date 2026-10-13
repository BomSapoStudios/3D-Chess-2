using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turn Planner: a "Turn Planner" button during play opens a panel that steps through each
/// of the current player's pieces and each of its moves (best first), with the squares named
/// in A1a notation. For the chosen move it shows what it captures, what could take the piece
/// on its new square, which of your pieces it leaves open to attack, and what it threatens.
/// While the panel is open the board is stripped down to just the pieces involved, with
/// colored markers and lines, so the consequences of the move stand out.
/// </summary>
public class TurnPlannerUI : MonoBehaviour
{
    public static TurnPlannerUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.open;

    private bool open;
    private bool focus = true;
    private PieceColor side;
    private List<ChessPiece> pieces = new List<ChessPiece>();
    private int pieceIndex;
    private List<TurnPlannerAnalysis.MoveOutcome> moves = new List<TurnPlannerAnalysis.MoveOutcome>();
    private int moveIndex;
    private Vector2 listScroll;
    private string note;

    // Zombie Survival: the planner finds the fastest way to destroy each zombie instead
    private List<ZombieHuntPlanner.Hunt> hunts = new List<ZombieHuntPlanner.Hunt>();
    private int huntIndex;
    private ZombieHuntPlanner.Hunt CurrentHunt => huntIndex < hunts.Count ? hunts[huntIndex] : null;

    private readonly HashSet<Renderer> hidden = new HashSet<Renderer>();
    private readonly List<GameObject> markers = new List<GameObject>();
    private readonly List<(Vector3 world, string text, Color color)> tags = new List<(Vector3, string, Color)>();

    private static readonly Color Good = NeonTheme.Lime, Danger = NeonTheme.Pink, Warn = NeonTheme.Yellow, Info = NeonTheme.Cyan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("Turn Planner UI");
        Instance = go.AddComponent<TurnPlannerUI>();
        DontDestroyOnLoad(go);
    }

    private static bool Playing =>
        ChessBoard.Instance != null && GameStateManager.Instance != null && GameStateManager.Instance.currentState == GameState.Playing;

    private static PieceColor CurrentPlayer => TurnManager.Instance != null ? TurnManager.Instance.GetCurrentPlayer() : PieceColor.White;

    /// <summary>Can this device make a move for the player on turn?</summary>
    private static bool CanMoveNow()
    {
        if (!Playing || TurnManager.Instance == null) return false;
        PieceColor p = CurrentPlayer;
        if (TurnManager.Instance.IsPlayerAI(p)) return false;
        if (OnlineSession.IsActive && !OnlineSession.IsLocal(p)) return false;
        return InputManager.Instance == null || !InputManager.Instance.IsBusy;
    }

    // ───────────────────────── State ─────────────────────────

    private Rect savedCameraRect;
    private Camera shrunkCamera;

    // Draw the board in the space to the right of the panel, so the panel never covers it
    private void ShrinkCamera(bool on)
    {
        Camera cam = Camera.main;
        if (on && cam != null && shrunkCamera == null)
        {
            shrunkCamera = cam;
            savedCameraRect = cam.rect;
            float panelFraction = Mathf.Clamp01((PanelWidth() + 16f) * TouchGUI.Scale / Screen.width + Screen.safeArea.x / Screen.width);
            cam.rect = new Rect(panelFraction, 0f, 1f - panelFraction, 1f);
        }
        else if (!on && shrunkCamera != null)
        {
            shrunkCamera.rect = savedCameraRect;
            shrunkCamera = null;
        }
    }

    private static float PanelWidth()
    {
        float uiWidth = Screen.safeArea.width / TouchGUI.Scale;
        return Mathf.Min(360f, uiWidth * 0.4f);
    }

    private void Open()
    {
        open = true;
        ShrinkCamera(true);
        note = null;
        side = CurrentPlayer;
        Recompute(keepSelection: false);
    }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    public void DebugOpen() => Open();
    public void DebugClose() => Close();
#endif

    private void Close()
    {
        open = false;
        ShrinkCamera(false);
        ClearFocus();
    }

    /// <summary>Rebuild the piece and move lists (the board changed or the turn passed)</summary>
    private void Recompute(bool keepSelection)
    {
        side = CurrentPlayer;
        if (ZombieMode.Active)
        {
            ChessPiece keepZombie = keepSelection ? CurrentHunt?.zombie : null;
            hunts = side == ZombieMode.PlayerColor ? ZombieHuntPlanner.Plan() : new List<ZombieHuntPlanner.Hunt>();
            int keep = keepZombie != null ? hunts.FindIndex(h => h.zombie == keepZombie) : -1;
            huntIndex = Mathf.Max(0, keep);
            listScroll = Vector2.zero;
            ApplyFocus();
            return;
        }
        ChessPiece keepPiece = keepSelection && pieceIndex < pieces.Count ? pieces[pieceIndex] : null;
        pieces = TurnPlannerAnalysis.MovablePieces(side);
        pieceIndex = keepPiece != null ? Mathf.Max(0, pieces.IndexOf(keepPiece)) : 0;
        LoadMoves(0);
    }

    private void LoadMoves(int index)
    {
        moves = pieceIndex < pieces.Count ? TurnPlannerAnalysis.Analyze(pieces[pieceIndex]) : new List<TurnPlannerAnalysis.MoveOutcome>();
        moveIndex = Mathf.Clamp(index, 0, Mathf.Max(0, moves.Count - 1));
        listScroll = Vector2.zero;
        ApplyFocus();
    }

    private void StepPiece(int delta)
    {
        if (pieces.Count == 0) return;
        pieceIndex = (pieceIndex + delta + pieces.Count) % pieces.Count;
        LoadMoves(0);
    }

    private void StepMove(int delta)
    {
        if (moves.Count == 0) return;
        moveIndex = (moveIndex + delta + moves.Count) % moves.Count;
        ApplyFocus();
    }

    /// <summary>A tap on the board while planning: plan that piece (if it's one of yours and visible)</summary>
    public void OnBoardTap(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in hits)
        {
            ChessPiece piece = h.collider.GetComponentInParent<ChessPiece>();
            if (piece == null || piece.IsInTray()) continue;
            if (hidden.Count > 0 && !Involved().Contains(piece)) continue; // hidden while focused: tap goes through
            if (ZombieMode.Active)
            {
                int hunt = hunts.FindIndex(x => x.zombie == piece || x.hunter == piece);
                if (hunt < 0) { note = "No hunt for that piece"; return; }
                note = null;
                huntIndex = hunt;
                ApplyFocus();
                return;
            }
            int index = pieces.IndexOf(piece);
            if (index < 0)
            {
                note = piece.pieceColor == side ? $"Your {piece.pieceType} has no moves" : $"That's {piece.pieceColor}'s {piece.pieceType}";
                return;
            }
            note = null;
            pieceIndex = index;
            LoadMoves(0);
            return;
        }
    }

    private TurnPlannerAnalysis.MoveOutcome Current => moveIndex < moves.Count ? moves[moveIndex] : null;

    private bool dirty;
    private TurnManager subscribedTurns;
    private ChaosRotationManager subscribedChaos;

    // Refresh after a move or a Chaos Mode spin (not every frame: that would lose your place)
    private void MarkDirty(PieceColor a, PieceColor b) => dirty = true;
    private void MarkDirtyChaos(ChaosRotationType t) => dirty = true;

    private void Update()
    {
        if (TurnManager.Instance != null && subscribedTurns != TurnManager.Instance)
        {
            subscribedTurns = TurnManager.Instance;
            subscribedTurns.OnTurnChanged += MarkDirty;
        }
        if (ChaosRotationManager.Instance != null && subscribedChaos != ChaosRotationManager.Instance)
        {
            subscribedChaos = ChaosRotationManager.Instance;
            subscribedChaos.OnChaosRotationCompleted += MarkDirtyChaos;
        }
        if (!open) return;
        if (!Playing) { Close(); return; }
        if (dirty || CurrentPlayer != side)
        {
            dirty = false;
            Recompute(keepSelection: CurrentPlayer == side);
        }
    }

    private void LateUpdate()
    {
        if (!open || !focus) return;
        // Keep other pieces hidden (other systems may re-enable renderers)
        var keep = Involved();
        foreach (ChessPiece p in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
        {
            bool show = keep.Contains(p) || p.IsInTray();
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                if (!show && r.enabled) { r.enabled = false; hidden.Add(r); }
                else if (show && hidden.Remove(r)) r.enabled = true;
            }
        }
    }

    // ───────────────────────── Board focus ─────────────────────────

    private HashSet<ChessPiece> Involved()
    {
        var set = new HashSet<ChessPiece>();
        if (ZombieMode.Active)
        {
            var hunt = CurrentHunt;
            if (hunt != null) { set.Add(hunt.zombie); if (hunt.hunter != null) set.Add(hunt.hunter); }
            return set;
        }
        var o = Current;
        if (o == null) { if (pieceIndex < pieces.Count) set.Add(pieces[pieceIndex]); return set; }
        set.Add(o.piece);
        if (o.captures != null) set.Add(o.captures);
        foreach (var a in o.attackers) set.Add(a);
        foreach (var d in o.defenders) set.Add(d);
        foreach (var t in o.threatens) set.Add(t);
        foreach (var e in o.exposed) { set.Add(e.piece); foreach (var b in e.by) set.Add(b); }
        return set;
    }

    private void ApplyFocus()
    {
        ClearMarkers();
        if (ZombieMode.Active) { HuntFocus(); return; }
        var o = Current;
        if (o == null || ChessBoard.Instance == null) return;

        Vector3 dest = SquareCenter(o.to);
        Vector3 start = PieceCenter(o.piece);
        Marker(dest, o.captures != null ? Good : Info, 1.15f);
        Line(start, dest, Info, 0.12f);
        tags.Add((start, TurnPlannerAnalysis.Label(o.from), Info));
        tags.Add((dest, TurnPlannerAnalysis.Label(o.to) + (o.captures != null ? " takes" : ""), o.captures != null ? Good : Info));

        foreach (var a in o.attackers)
        {
            Line(PieceCenter(a), dest, Danger, 0.07f);
            Marker(PieceCenter(a), Danger, 0.7f);
            tags.Add((PieceCenter(a), TurnPlannerAnalysis.Label(a.CurrentPosition), Danger));
        }
        foreach (var d in o.defenders) Marker(PieceCenter(d), Good, 0.5f);
        foreach (var e in o.exposed)
        {
            Marker(PieceCenter(e.piece), Warn, 0.9f);
            tags.Add((PieceCenter(e.piece), TurnPlannerAnalysis.Label(e.piece.CurrentPosition), Warn));
            foreach (var b in e.by) Line(PieceCenter(b), PieceCenter(e.piece), Warn, 0.05f);
        }
        foreach (var t in o.threatens)
        {
            if (t == o.captures) continue;
            Line(dest, PieceCenter(t), Info, 0.04f);
            tags.Add((PieceCenter(t), TurnPlannerAnalysis.Label(t.CurrentPosition), Info));
        }
    }

    /// <summary>The hunter's route in cyan, the zombie's predicted path in pink, the kill square in lime</summary>
    private void HuntFocus()
    {
        var hunt = CurrentHunt;
        if (hunt == null || ChessBoard.Instance == null) return;
        if (hunt.zombiePath != null)
        {
            for (int i = 0; i < hunt.zombiePath.Count; i++)
            {
                Marker(SquareCenter(hunt.zombiePath[i]), Danger, i == 0 ? 0.6f : 0.4f);
                if (i > 0) Line(SquareCenter(hunt.zombiePath[i - 1]), SquareCenter(hunt.zombiePath[i]), Danger, 0.06f);
            }
        }
        tags.Add((PieceCenter(hunt.zombie), TurnPlannerAnalysis.Label(hunt.zombie.CurrentPosition), Danger));
        if (!hunt.Reachable) return;
        for (int i = 1; i < hunt.route.Count; i++)
        {
            Vector3 a = i == 1 ? PieceCenter(hunt.hunter) : SquareCenter(hunt.route[i - 1]);
            Vector3 b = SquareCenter(hunt.route[i]);
            Line(a, b, Info, 0.1f);
            bool kill = i == hunt.route.Count - 1;
            Marker(b, kill ? Good : Info, kill ? 1.15f : 0.8f);
            tags.Add((b, (kill ? "x " : $"{i}. ") + TurnPlannerAnalysis.Label(hunt.route[i]), kill ? Good : Info));
        }
        tags.Add((PieceCenter(hunt.hunter), TurnPlannerAnalysis.Label(hunt.route[0]), Info));
    }

    private void ClearFocus()
    {
        foreach (Renderer r in hidden) if (r != null) r.enabled = true;
        hidden.Clear();
        ClearMarkers();
    }

    private void ClearMarkers()
    {
        foreach (GameObject m in markers) if (m != null) Destroy(m);
        markers.Clear();
        tags.Clear();
    }

    private static Transform Container()
    {
        GameObject c = GameObject.Find("Pieces Container");
        return c != null ? c.transform : null;
    }

    private static Vector3 SquareCenter(BoardPosition p)
    {
        Vector3 local = ChessBoard.Instance.BoardToLocalPosition(p) + Vector3.up * 1.05f;
        Transform c = Container();
        return c != null ? c.TransformPoint(local) : local;
    }

    private static Vector3 PieceCenter(ChessPiece p) => p.transform.position + p.transform.up * 1.05f;

    private void Marker(Vector3 at, Color color, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());
        go.name = "Planner Marker";
        go.transform.position = at;
        go.transform.localScale = Vector3.one * size;
        Material template = Resources.Load<Material>("PieceSets/Crystal");
        var m = template != null ? new Material(template) : new Material(Shader.Find("Unlit/Color"));
        m.color = new Color(color.r, color.g, color.b, 0.45f);
        if (template != null) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", color * 0.6f); }
        go.GetComponent<Renderer>().material = m;
        markers.Add(go);
    }

    private void Line(Vector3 a, Vector3 b, Color color, float width)
    {
        var go = new GameObject("Planner Line");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.widthMultiplier = width;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = lr.endColor = new Color(color.r, color.g, color.b, 0.9f);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        markers.Add(go);
    }

    // ───────────────────────── UI ─────────────────────────

    private void OnGUI()
    {
        if (!Playing) return;
        Vector2 ui = TouchGUI.Begin();

        if (!open)
        {
            if (TouchGUI.NeonButton(new Rect(16, 76, 150, 44), "Turn Planner", NeonTheme.Lime, NeonTheme.Pink, fontSize: 16)) Open();
            TouchGUI.End();
            return;
        }

        DrawTags();

        float w = PanelWidth();
        // Fill the strip the board was moved out of (the camera no longer draws there)
        GUI.DrawTexture(new Rect(-Screen.safeArea.x / TouchGUI.Scale - 4, -60, w + 16 + Screen.safeArea.x / TouchGUI.Scale + 4, ui.y + 120),
            Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, NeonTheme.Ground, 0, 0);
        Rect panel = new Rect(8, 8, w, ui.y - 16);
        NeonTheme.GUIPanel(panel, NeonTheme.Lime, 0.94f);
        TouchGUI.Block(panel);
        float x = panel.x + 12, y = panel.y + 10, inner = w - 24;

        GUI.Label(new Rect(x, y, inner - 50, 30), ZombieMode.Active ? "ZOMBIE HUNT" : "TURN PLANNER", NeonTheme.GUILabelStyle(NeonTheme.Lime, 20, true, TextAnchor.MiddleLeft, display: true));
        if (TouchGUI.NeonButton(new Rect(panel.xMax - 50, y, 40, 32), "X", NeonTheme.Pink, NeonTheme.Ground, fontSize: 16)) { Close(); TouchGUI.End(); return; }
        y += 34;
        GUI.Label(new Rect(x, y, inner, 20), $"{side} to move" + (CanMoveNow() ? "" : " (planning only)") + (focus ? "" : " · tap a piece to plan it"), NeonTheme.GUILabelStyle(NeonTheme.Lavender, 13));
        y += 22;

        if (ZombieMode.Active)
        {
            DrawHunts(panel, x, y, inner);
            TouchGUI.End();
            return;
        }

        if (pieces.Count == 0)
        {
            GUI.Label(new Rect(x, y, inner, 40), "No pieces can move right now.", NeonTheme.GUILabelStyle(Warn, 15));
            TouchGUI.End();
            return;
        }

        // Piece stepper
        ChessPiece piece = pieces[pieceIndex];
        if (TouchGUI.NeonButton(new Rect(x, y, 40, 40), "<", Info, NeonTheme.Pink, fontSize: 20)) StepPiece(-1);
        GUI.Label(new Rect(x + 46, y, inner - 92, 40), $"{piece.pieceType} at {TurnPlannerAnalysis.Label(piece.CurrentPosition)}\n<size=11>piece {pieceIndex + 1} of {pieces.Count}</size>",
            Rich(NeonTheme.GUILabelStyle(NeonTheme.White, 16, true, TextAnchor.MiddleCenter)));
        if (TouchGUI.NeonButton(new Rect(x + inner - 40, y, 40, 40), ">", Info, NeonTheme.Pink, fontSize: 20)) StepPiece(1);
        y += 46;

        // Move stepper
        var o = Current;
        if (o != null)
        {
            if (TouchGUI.NeonButton(new Rect(x, y, 40, 40), "<", NeonTheme.Yellow, NeonTheme.Pink, fontSize: 20)) StepMove(-1);
            string arrow = o.captures != null ? " x " : " → ";
            GUI.Label(new Rect(x + 46, y, inner - 92, 40), $"{TurnPlannerAnalysis.Label(o.from)}{arrow}{TurnPlannerAnalysis.Label(o.to)}\n<size=11>move {moveIndex + 1} of {moves.Count}, best first</size>",
                Rich(NeonTheme.GUILabelStyle(NeonTheme.Yellow, 17, true, TextAnchor.MiddleCenter)));
            if (TouchGUI.NeonButton(new Rect(x + inner - 40, y, 40, 40), ">", NeonTheme.Yellow, NeonTheme.Pink, fontSize: 20)) StepMove(1);
            y += 48;

            // Consequences
            foreach (var (text, color) in Describe(o))
            {
                var style = NeonTheme.GUILabelStyle(color, 13, true, TextAnchor.UpperLeft);
                float h = style.CalcHeight(new GUIContent(text), inner);
                GUI.Label(new Rect(x, y, inner, h), text, style);
                y += h + 3;
            }
            y += 4;
        }

        // Bottom buttons
        float bottom = panel.yMax - 50;
        if (TouchGUI.NeonButton(new Rect(x, bottom, 120, 40), focus ? "Show all" : "Focus", Info, NeonTheme.Pink, outlined: true, fontSize: 14))
        {
            focus = !focus;
            if (!focus) { foreach (Renderer r in hidden) if (r != null) r.enabled = true; hidden.Clear(); }
            else ApplyFocus(); // focus on whichever piece was picked while showing all
        }
        if (o != null && CanMoveNow())
        {
            if (TouchGUI.NeonButton(new Rect(x + inner - 150, bottom, 150, 40), "Make this move", NeonTheme.Lime, NeonTheme.Pink, fontSize: 15))
            {
                var move = o;
                // (logged so stray moves can be traced)
                Debug.Log($"TurnPlanner: make move {move.piece.pieceType} {TurnPlannerAnalysis.Label(move.from)}->{TurnPlannerAnalysis.Label(move.to)} (piece index {pieceIndex})");
                Close();
                if (InputManager.Instance == null || !InputManager.Instance.PlannerMove(move.piece, move.to)) note = "Couldn't make that move";
            }
        }

        // All moves for this piece, tap to pick
        float listTop = y, listHeight = bottom - 8 - listTop;
        if (listHeight > 60 && moves.Count > 1)
        {
            Rect view = new Rect(x, listTop, inner, listHeight);
            Rect content = new Rect(0, 0, inner - 16, moves.Count * 30);
            listScroll = GUI.BeginScrollView(view, listScroll, content);
            for (int i = 0; i < moves.Count; i++)
            {
                var m = moves[i];
                string tag = m.captures != null ? $"takes {m.captures.pieceType}" : m.IsSafe ? "safe" : "can be taken";
                if (m.givesCheck) tag += ", check!";
                if (m.exposed.Count > 0) tag += $", exposes {m.exposed.Count}";
                Color c = i == moveIndex ? NeonTheme.Yellow : m.captures != null ? Good : m.IsSafe ? Info : Danger;
                if (GUI.Button(new Rect(0, i * 30, inner - 16, 28), GUIContent.none, GUIStyle.none)) { moveIndex = i; ApplyFocus(); }
                GUI.Label(new Rect(4, i * 30, inner - 24, 28), $"{TurnPlannerAnalysis.Label(m.to)}  ·  {tag}", NeonTheme.GUILabelStyle(c, 13));
            }
            GUI.EndScrollView();
        }

        if (!string.IsNullOrEmpty(note)) GUI.Label(new Rect(x, bottom - 22, inner, 20), note, NeonTheme.GUILabelStyle(Danger, 12));
        TouchGUI.End();
    }

    /// <summary>Zombie Survival: step through the zombies and the fastest way to destroy each</summary>
    private void DrawHunts(Rect panel, float x, float y, float inner)
    {
        if (hunts.Count == 0)
        {
            GUI.Label(new Rect(x, y, inner, 40), side == ZombieMode.PlayerColor ? "No zombies on the board." : "The zombies are moving…", NeonTheme.GUILabelStyle(Warn, 15));
            return;
        }
        var hunt = CurrentHunt;
        if (TouchGUI.NeonButton(new Rect(x, y, 40, 40), "<", Danger, NeonTheme.Ground, fontSize: 20)) { huntIndex = (huntIndex - 1 + hunts.Count) % hunts.Count; ApplyFocus(); }
        GUI.Label(new Rect(x + 46, y, inner - 92, 40), $"Zombie {(hunt.zombie.pieceType == ChessPieceType.Pawn ? "" : hunt.zombie.pieceType + " ")}on {TurnPlannerAnalysis.Label(hunt.zombie.CurrentPosition)}\n<size=11>zombie {huntIndex + 1} of {hunts.Count}, quickest kill first</size>",
            Rich(NeonTheme.GUILabelStyle(NeonTheme.White, 16, true, TextAnchor.MiddleCenter)));
        if (TouchGUI.NeonButton(new Rect(x + inner - 40, y, 40, 40), ">", Danger, NeonTheme.Ground, fontSize: 20)) { huntIndex = (huntIndex + 1) % hunts.Count; ApplyFocus(); }
        y += 48;

        var lines = new List<(string, Color)>();
        if (hunt.Reachable)
        {
            lines.Add(($"Your {hunt.hunter.pieceType} on {TurnPlannerAnalysis.Label(hunt.route[0])} gets it in {hunt.turns} move{(hunt.turns == 1 ? "" : "s")}", Good));
            var steps = new List<string>();
            for (int i = 1; i < hunt.route.Count; i++) steps.Add((i == hunt.route.Count - 1 ? "x " : "") + TurnPlannerAnalysis.Label(hunt.route[i]));
            lines.Add(($"Route: {TurnPlannerAnalysis.Label(hunt.route[0])} → {string.Join(" → ", steps)}", Info));
            if (hunt.zombiePath.Count > 1)
                lines.Add(($"The zombie should go {string.Join(" → ", hunt.zombiePath.ConvertAll(p => TurnPlannerAnalysis.Label(p)))}", Danger));
            lines.Add(hunt.safeRoute ? ("No zombie can bite it on the way", Info) : ("A zombie could bite it on the way", Warn));
            lines.Add(hunt.safeAfter ? ("Safe after the kill", Info) : ("Can be bitten right after the kill", Warn));
        }
        else lines.Add(($"None of your pieces can reach it within {ZombieHuntPlanner.MaxTurns} moves", Warn));
        lines.Add(("Guesses assume your other pieces stay put; new zombies rise at random.", NeonTheme.Lavender));
        foreach (var (text, color) in lines)
        {
            var style = NeonTheme.GUILabelStyle(color, 13, true, TextAnchor.UpperLeft);
            float h = style.CalcHeight(new GUIContent(text), inner);
            GUI.Label(new Rect(x, y, inner, h), text, style);
            y += h + 3;
        }
        y += 4;

        float bottom = panel.yMax - 50;
        if (TouchGUI.NeonButton(new Rect(x, bottom, 120, 40), focus ? "Show all" : "Focus", Info, NeonTheme.Pink, outlined: true, fontSize: 14))
        {
            focus = !focus;
            if (!focus) { foreach (Renderer r in hidden) if (r != null) r.enabled = true; hidden.Clear(); }
            else ApplyFocus();
        }
        if (hunt.Reachable && CanMoveNow())
        {
            if (TouchGUI.NeonButton(new Rect(x + inner - 150, bottom, 150, 40), hunt.turns == 1 ? "Take it" : "Make 1st move", NeonTheme.Lime, NeonTheme.Pink, fontSize: 15))
            {
                var h = hunt;
                Close();
                if (InputManager.Instance == null || !InputManager.Instance.PlannerMove(h.hunter, h.FirstStep)) note = "Couldn't make that move";
            }
        }

        // Every zombie, tap to pick
        float listHeight = bottom - 8 - y;
        if (listHeight > 60 && hunts.Count > 1)
        {
            Rect view = new Rect(x, y, inner, listHeight);
            Rect content = new Rect(0, 0, inner - 16, hunts.Count * 30);
            listScroll = GUI.BeginScrollView(view, listScroll, content);
            for (int i = 0; i < hunts.Count; i++)
            {
                var h = hunts[i];
                string tag = h.Reachable ? $"{h.turns} move{(h.turns == 1 ? "" : "s")} · {h.hunter.pieceType}{(h.safeRoute ? "" : ", risky")}" : "out of reach";
                Color c = i == huntIndex ? NeonTheme.Yellow : !h.Reachable ? NeonTheme.Lavender : h.safeRoute ? Good : Warn;
                if (GUI.Button(new Rect(0, i * 30, inner - 16, 28), GUIContent.none, GUIStyle.none)) { huntIndex = i; ApplyFocus(); }
                GUI.Label(new Rect(4, i * 30, inner - 24, 28), $"{TurnPlannerAnalysis.Label(h.zombie.CurrentPosition)}  ·  {tag}", NeonTheme.GUILabelStyle(c, 13));
            }
            GUI.EndScrollView();
        }
        if (!string.IsNullOrEmpty(note)) GUI.Label(new Rect(x, bottom - 22, inner, 20), note, NeonTheme.GUILabelStyle(Danger, 12));
    }

    private static IEnumerable<(string, Color)> Describe(TurnPlannerAnalysis.MoveOutcome o)
    {
        if (o.captures != null) yield return ($"Takes {TurnPlannerAnalysis.Name(o.captures)} on {TurnPlannerAnalysis.Label(o.to)}", Good);
        else yield return ($"Moves to empty square {TurnPlannerAnalysis.Label(o.to)}", NeonTheme.Lavender);

        if (o.attackers.Count == 0) yield return ("Safe there: nothing can take it", Info);
        else
        {
            yield return ($"Can be taken by {List(o.attackers)}", Danger);
            if (o.defenders.Count > 0) yield return ($"Guarded by {o.defenders.Count} of your pieces", Good);
        }

        foreach (var (piece, by) in o.exposed)
            yield return ($"Leaves your {piece.pieceType} on {TurnPlannerAnalysis.Label(piece.CurrentPosition)} open to {List(by)}", Warn);

        if (o.givesCheck) yield return ("Puts a king in check!", Good);
        var threats = o.threatens.FindAll(t => t != o.captures && t.pieceType != ChessPieceType.King);
        if (threats.Count > 0) yield return ($"Then threatens {List(threats)}", Info);
    }

    private static string List(List<ChessPiece> pieces)
    {
        var parts = new List<string>();
        foreach (var p in pieces) parts.Add($"{p.pieceColor} {p.pieceType} ({TurnPlannerAnalysis.Label(p.CurrentPosition)})");
        return string.Join(", ", parts);
    }

    private static GUIStyle Rich(GUIStyle s)
    {
        s.richText = true;
        return s;
    }

    /// <summary>Square names floating next to the pieces involved</summary>
    private void DrawTags()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        float scale = TouchGUI.Scale;
        Rect safe = Screen.safeArea;
        foreach (var (world, text, color) in tags)
        {
            Vector3 s = cam.WorldToScreenPoint(world);
            if (s.z <= 0) continue;
            float gx = (s.x - safe.x) / scale;
            float gy = (Screen.height - s.y - (Screen.height - safe.yMax)) / scale;
            Rect r = new Rect(gx + 10, gy - 12, 70, 22);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, new Color(0.05f, 0f, 0.12f, 0.75f), 0, 6);
            GUI.Label(r, text, NeonTheme.GUILabelStyle(color, 13, true, TextAnchor.MiddleCenter));
        }
    }
}

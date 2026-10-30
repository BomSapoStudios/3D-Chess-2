using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Saved games on this phone, one for each mode: Quick Play, Zombies! and Local Game. Queen Blitz
/// puzzles aren't saved: leaving one (or closing the app) means it starts fresh next time.
/// At the start of every turn a person plays, the position is saved (the pieces Undo keeps, plus
/// the clocks, the count to the next Chaos spin, players knocked out and the game's settings), so
/// the player can go back to the menu, change a setting, and carry on. Tapping the mode again
/// offers Resume or Start new. A finished game, or a new one in the same mode, replaces the save.
/// </summary>
public class SavedGames : MonoBehaviour
{
    public const string QuickPlay = "QuickPlay", Zombies = "Zombies", Blitz = "Blitz", Local = "Local";

    [System.Serializable]
    public class SavedPiece { public int type, color, x, y, z; }

    [System.Serializable]
    public class Save
    {
        public GameConfiguration config;
        public int view;               // the side the player looks from
        public string blitzPack;
        public List<SavedPiece> pieces = new List<SavedPiece>();
        public int turn;
        public int moves;              // moves played, for the Resume card
        public int zombieRounds, zombiePawns, zombiePromoted;
        public int blitzMoves;
        public bool blitzHasMirror;    // the Mirror Queen has left her start, and is on blitzMirror
        public int[] blitzMirror;
        public List<int> blitzBombs = new List<int>(); // bombs still on the board, x,y,z each
        public int chaosCount;
        public List<int> knockedOut = new List<int>();
        public List<int> clockColors = new List<int>();
        public List<float> clockSeconds = new List<float>();
    }

    public static SavedGames Instance { get; private set; }

    private static string Key(string slot) => "SavedGame." + slot;
    public static bool Has(string slot) => Load(slot) != null;
    public static void Delete(string slot) { PlayerPrefs.DeleteKey(Key(slot)); PlayerPrefs.Save(); }

    public static Save Load(string slot)
    {
        string json = PlayerPrefs.GetString(Key(slot), "");
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            Save save = JsonUtility.FromJson<Save>(json);
            return save != null && save.config != null && save.pieces.Count > 0 ? save : null;
        }
        catch { return null; }
    }

    /// <summary>The save the next game scene picks up instead of starting fresh</summary>
    private static Save pending;

    /// <summary>Open the saved game for this mode</summary>
    public static bool Resume(string slot)
    {
        Save save = Load(slot);
        if (save == null || SceneController.Instance == null) return false;
        pending = save;
        save.config.saveSlot = slot;
        PlayerViewUI.StartView = (PieceColor)save.view;
        if (save.config.queenBlitz) QueenBlitz.PackId = save.blitzPack;
        SceneController.Instance.LoadGameScene(save.config);
        return true;
    }

    /// <summary>One line about the saved game for the Resume card</summary>
    public static string Describe(Save save)
    {
        GameConfiguration c = save.config;
        string moves = save.moves == 1 ? "1 move in" : $"{save.moves} moves in";
        if (c.zombieMode)
        {
            int score = save.zombieRounds * 10 + save.zombiePawns * 5 + save.zombiePromoted * 20;
            return $"{ZombieMode.DifficultyName(c.aiDifficulty)} run, round {save.zombieRounds + 1}, score {score}";
        }
        if (c.queenBlitz)
        {
            string name = QueenBlitz.IsDaily(save.blitzPack) ? "Daily puzzle" : $"Puzzle {c.blitzPuzzle + 1}";
            return $"{name}, {save.blitzMoves} move{(save.blitzMoves == 1 ? "" : "s")} so far";
        }
        string size = c.boardSize == BoardSize.Large8x8x8 ? "Feast 8³" : c.boardSize == BoardSize.Medium6x6x6 ? "Meal 6³" : c.boardSize == BoardSize.Small4x4x4 ? "Snack 4³" : "Snack 5³";
        if (c.saveSlot == QuickPlay) return $"Playing {(PieceColor)save.view} on {size}, {moves}";
        return $"{c.playerCount} players on {size}, {moves}";
    }

    // ───────────────────────── In the game ─────────────────────────

    private string slot;
    private Save resume;
    private bool finished;
    private bool setUp; // this scene's game has reached setup (the state left over from the last game doesn't count)
    private bool restored; // this game was put back from a save
    private int turnsSeen, savedAtTurn = -1, movesBefore;
    private float nextClockSave;
    private TurnManager subscribedTurns;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        Delete(Blitz); // from builds that saved puzzles part way through
        SceneManager.sceneLoaded += (scene, mode) => TryCreate();
        TryCreate();
    }

    private static void TryCreate()
    {
        if (Instance != null || MenuScene.IsActive) { pending = null; return; }
        GameConfiguration config = SceneController.Instance != null ? SceneController.Instance.GetCurrentGameConfiguration() : null;
        if (config == null || config.isOnlineGame || string.IsNullOrEmpty(config.saveSlot)) { pending = null; return; }
        Instance = new GameObject("Saved Games").AddComponent<SavedGames>();
        Instance.slot = config.saveSlot;
        Instance.resume = pending;
        pending = null;
        if (Instance.resume == null) Delete(Instance.slot); // a new game in this mode replaces the old one
        else Instance.movesBefore = Instance.resume.moves;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>This game is being saved as the player goes (shown on the in-game menu)</summary>
    public static bool Saving => Instance != null && !Instance.finished && Instance.resume == null && Instance.savedAtTurn >= 0;

    /// <summary>A saved game is being put back (setup is skipped with stand-in pieces)</summary>
    public static bool Resuming => Instance != null && Instance.resume != null;

    /// <summary>This game was put back from a save: it's past its opening, so the saved player keeps the move</summary>
    public static bool Restored => Instance != null && Instance.restored;

    private int TurnKey => QueenBlitz.Active ? QueenBlitz.Moves : turnsSeen;

    private void Update()
    {
        if (TurnManager.Instance != null && subscribedTurns != TurnManager.Instance)
        {
            subscribedTurns = TurnManager.Instance;
            subscribedTurns.OnTurnChanged += (a, b) => { if (a != b) turnsSeen++; };
        }
        if (GameStateManager.Instance == null) return;
        GameState state = GameStateManager.Instance.currentState;
        if (state == GameState.PiecePlacement && (!setUp || finished))
        {
            // This game's setup (or a rematch's, in the same scene)
            if (finished) { finished = false; restored = false; savedAtTurn = -1; turnsSeen = 0; movesBefore = 0; }
            setUp = true;
        }
        if (!setUp || finished) return;

        // Over: nothing to come back to
        if (state == GameState.GameOver || (QueenBlitz.Active && (QueenBlitz.Cleared || QueenBlitz.OutOfMoves)))
        {
            finished = true;
            Delete(slot);
            return;
        }

        if (resume != null)
        {
            if (state == GameState.PiecePlacement) FillTrays();
            return;
        }

        // Save once at the start of each turn a person plays, after the last move has settled
        if (state == GameState.Playing && savedAtTurn != TurnKey && PersonToMove() && GameStateManager.Instance.CanMovePieces() && ZombiesOut()
            && (InputManager.Instance == null || !InputManager.Instance.IsBusy))
        {
            savedAtTurn = TurnKey;
            Write();
        }
        // Keep the clocks current through the turn, so leaving mid-turn doesn't give time back
        else if (state == GameState.Playing && savedAtTurn == TurnKey && Time.time >= nextClockSave
                 && TimerManager.Instance != null && TimerManager.Instance.IsTimedPlayActive())
        {
            nextClockSave = Time.time + 2f;
            Save save = Load(slot);
            if (save == null) return;
            for (int i = 0; i < save.clockColors.Count && i < save.clockSeconds.Count; i++)
                save.clockSeconds[i] = TimerManager.Instance.GetRemainingTime((PieceColor)save.clockColors[i]);
            PlayerPrefs.SetString(Key(slot), JsonUtility.ToJson(save));
        }
    }

    private void LateUpdate()
    {
        // Put the saved position on the board as play starts, once the zombies (if any) are out,
        // before a robot moving first gets going
        if (resume == null || !setUp || GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.Playing) return;
        if (!ZombiesOut()) return;
        Restore(resume);
        resume = null;
        restored = true;
        turnsSeen = 0;
        savedAtTurn = TurnKey; // this position is the save
    }

    /// <summary>Zombies! has its starting horde on the board (it comes out as play starts)</summary>
    private static bool ZombiesOut() => !ZombieMode.Active || (ZombieDirector.Instance != null && ZombieDirector.Instance.Started);

    private static bool PersonToMove() =>
        TurnManager.Instance != null && !TurnManager.Instance.IsPlayerAI(TurnManager.Instance.GetCurrentPlayer());

    /// <summary>Resuming: skip setup by putting every piece still in a tray somewhere legal, then everyone is ready</summary>
    private void FillTrays()
    {
        PlacementManager pm = PlacementManager.Instance;
        if (pm == null || !pm.PiecesReady || PlayerManager.Instance == null) return;
        var engine = new AIPlacementEngine(AIDifficulty.Easy);
        foreach (PieceColor color in PlayerManager.Instance.GetAllPlayers())
        {
            if (pm.IsPlayerReady(color)) continue;
            if (QueenBlitz.Active)
            {
                // The puzzle lays out the pawns (and a given queen); the queen and bombs go anywhere for now
                if (color == QueenBlitz.PlayerColor) QueenBlitz.FillBombs();
                if (color != QueenBlitz.PlayerColor || QueenBlitz.Current.queen.HasValue) continue;
                PieceTray tray = PieceTray.GetTrayForColor(color);
                if (tray == null) continue;
                foreach (ChessPiece p in tray.GetComponentsInChildren<ChessPiece>(true))
                    if (p.pieceType == ChessPieceType.Queen) { pm.ExecuteAIPlacement(p, QueenBlitz.Current.best); break; }
                continue;
            }
            if (ZombieMode.Active && color == ZombieMode.ZombieColor) continue; // the director readies the zombies
            for (int i = 0; i < 64 && !pm.IsPlayerPlacementCompleted(color); i++)
            {
                AIPlacement placement = engine.FindBestPlacement(color);
                if (placement == null || placement.piece == null) break;
                pm.ExecuteAIPlacement(placement.piece, placement.position);
            }
            pm.SetPlayerReady(color, true);
        }
    }

    private void Write()
    {
        GameConfiguration config = SceneController.Instance != null ? SceneController.Instance.GetCurrentGameConfiguration() : null;
        if (config == null || ChessBoard.Instance == null) return;
        config.saveSlot = slot;
        var save = new Save
        {
            config = config,
            view = (int)(PlayerViewUI.LastViewed ?? FirstHuman(config)),
            blitzPack = QueenBlitz.PackId,
            turn = (int)TurnManager.Instance.GetCurrentPlayer(),
            moves = movesBefore + turnsSeen,
            zombieRounds = ZombieMode.Rounds, zombiePawns = ZombieMode.PawnsDestroyed, zombiePromoted = ZombieMode.PromotedDestroyed,
            blitzMoves = QueenBlitz.Moves,
            blitzHasMirror = QueenBlitz.MirrorAt.HasValue,
            blitzMirror = QueenBlitz.MirrorAt is BoardPosition mirror ? new[] { mirror.x, mirror.y, mirror.z } : new int[0],
            chaosCount = ChaosRotationManager.Instance != null ? ChaosRotationManager.Instance.MovesSinceLastChaos : 0,
        };
        if (QueenBlitz.Active) foreach (BoardPosition b in QueenBlitz.BombsSnapshot()) save.blitzBombs.AddRange(new[] { b.x, b.y, b.z });
        foreach (ChessPiece p in UndoManager.BoardPieces())
        {
            BoardPosition at = p.CurrentPosition;
            save.pieces.Add(new SavedPiece { type = (int)p.pieceType, color = (int)p.pieceColor, x = at.x, y = at.y, z = at.z });
        }
        foreach (PieceColor c in PlayerManager.Instance != null ? PlayerManager.Instance.GetAllPlayers() : new List<PieceColor>())
        {
            if (PlayerManager.Instance.IsPlayerEliminated(c)) save.knockedOut.Add((int)c);
            if (TimerManager.Instance != null && TimerManager.Instance.IsTimedPlayActive())
            {
                save.clockColors.Add((int)c);
                save.clockSeconds.Add(TimerManager.Instance.GetRemainingTime(c));
            }
        }
        PlayerPrefs.SetString(Key(slot), JsonUtility.ToJson(save));
        PlayerPrefs.Save();
        Debug.Log($"SavedGames: saved {slot}, {save.pieces.Count} pieces, {(PieceColor)save.turn} to move");
    }

    private static PieceColor FirstHuman(GameConfiguration config)
    {
        int i = config.playerTypes.IndexOf(PlayerType.Human);
        return i >= 0 ? (PieceColor)i : PieceColor.White;
    }

    private static void Restore(Save save)
    {
        if (InputManager.Instance != null) InputManager.Instance.ClearSelection();
        var pieces = new List<(ChessPieceType, PieceColor, BoardPosition)>();
        foreach (SavedPiece s in save.pieces) pieces.Add(((ChessPieceType)s.type, (PieceColor)s.color, new BoardPosition(s.x, s.y, s.z)));
        UndoManager.SetBoard(pieces);

        ZombieMode.Rounds = save.zombieRounds;
        ZombieMode.PawnsDestroyed = save.zombiePawns;
        ZombieMode.PromotedDestroyed = save.zombiePromoted;
        if (ZombieDirector.Instance != null) ZombieDirector.Instance.Recount();
        if (QueenBlitz.Active)
        {
            QueenBlitz.Moves = save.blitzMoves;
            QueenBlitz.MirrorAt = save.blitzHasMirror && save.blitzMirror != null && save.blitzMirror.Length == 3
                ? new BoardPosition(save.blitzMirror[0], save.blitzMirror[1], save.blitzMirror[2]) : (BoardPosition?)null;
            int pawns = save.pieces.FindAll(s => (PieceColor)s.color == QueenBlitz.PawnColor).Count;
            QueenBlitz.Swept = QueenBlitz.Current.pawns.Length - pawns; // swept before it was saved
            var bombs = new List<BoardPosition>();
            for (int i = 0; i + 2 < save.blitzBombs.Count; i += 3) bombs.Add(new BoardPosition(save.blitzBombs[i], save.blitzBombs[i + 1], save.blitzBombs[i + 2]));
            if (QueenBlitz.HasBombs) QueenBlitz.RestoreBombs(bombs);
        }
        if (ChaosRotationManager.Instance != null) ChaosRotationManager.Instance.MovesSinceLastChaos = save.chaosCount;
        if (PlayerManager.Instance != null) PlayerManager.Instance.SetEliminated(save.knockedOut.ConvertAll(c => (PieceColor)c));
        if (GameEndDetectionManager.Instance != null) GameEndDetectionManager.Instance.enableGameEndDetection = !ZombieMode.Active && !QueenBlitz.Active;
        if (TimerManager.Instance != null)
            for (int i = 0; i < save.clockColors.Count && i < save.clockSeconds.Count; i++)
                TimerManager.Instance.SetRemainingTime((PieceColor)save.clockColors[i], save.clockSeconds[i]);

        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.InvalidateCache();
        TurnManager.Instance.SetCurrentPlayer((PieceColor)save.turn);
        if (CheckDetectionManager.Instance != null) CheckDetectionManager.Instance.SafeUpdateVisualFeedback();
        Debug.Log($"SavedGames: resumed with {pieces.Count} pieces, {(PieceColor)save.turn} to move");
    }

    // While setup is skipped, cover the board so the stand-in setup doesn't flash by
    private void OnGUI()
    {
        if (resume == null) return;
        GUI.depth = -30;
        GUI.color = NeonTheme.Ground;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        Vector2 ui = TouchGUI.Begin();
        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22 };
        style.normal.textColor = NeonTheme.Lime;
        if (NeonTheme.DisplayFont != null) style.font = NeonTheme.DisplayFont;
        GUI.Label(new Rect(0, 0, ui.x, ui.y), "Picking up where you left off…", style);
        TouchGUI.End();
    }
}

#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

/// <summary>
/// Development-only: plays a Zombie Survival run by itself and logs each round.
/// `defaults write BomSapo.Chess3D DebugZombieDemo -int 1` (Pea), 2 (Walnut) or 3 (Galaxy) on the
/// simulator, then launch. White is played by the robot brain; logs "ZOMBIE DEMO".
/// </summary>
public class ZombieDemo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        int level = PlayerPrefs.GetInt("DebugZombieDemo", 0);
        if (level == 0 || FindFirstObjectByType<ZombieDemo>() != null) return;
        var go = new GameObject("Zombie Demo");
        DontDestroyOnLoad(go);
        go.AddComponent<ZombieDemo>().StartCoroutine(Run((AIDifficulty)level)); // 1 Easy, 2 Medium, 3 Hard
    }

    private static IEnumerator Run(AIDifficulty level)
    {
        yield return new WaitForSeconds(2f);
        var config = new GameConfiguration();
        config.playerCount = 2;
        config.boardSize = BoardSize.Large8x8x8;
        config.aiDifficulty = level;
        config.playerTypes = new System.Collections.Generic.List<PlayerType> { PlayerType.Human, PlayerType.Computer };
        config.aiPlayerCount = 1;
        config.zombieMode = true;
        config.ApplyToLegacyFields();
        SceneController.Instance.LoadGameScene(config);

        while (PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) yield return null;
        yield return new WaitForSeconds(1f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSeconds(4f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        float wait = 0f;
        while (GameStateManager.Instance.currentState != GameState.Playing && wait < 30f) { wait += Time.deltaTime; yield return null; }
        Debug.Log($"ZOMBIE DEMO: started on {ZombieMode.DifficultyName(level)}, active {ZombieMode.Active}, state {GameStateManager.Instance.currentState}");

        int lastRound = -1;
        bool planned = false;
        float started = Time.time;
        while (GameStateManager.Instance.currentState == GameState.Playing && Time.time - started < 240f)
        {
            if (TurnManager.Instance.GetCurrentPlayer() == PieceColor.White && GameStateManager.Instance.CanMovePieces())
            {
                if (ZombieMode.Rounds != lastRound)
                {
                    lastRound = ZombieMode.Rounds;
                    int white = Count(PieceColor.White), pawns = Count(PieceColor.Black, true), promoted = Count(PieceColor.Black) - pawns;
                    Debug.Log($"ZOMBIE DEMO: round {ZombieMode.Rounds}: White {white} pieces, zombies {pawns} pawns + {promoted} promoted, score {ZombieMode.Score}");
                }
                if (ZombieMode.Rounds == 3 && !planned)
                {
                    planned = true;
                    var hunts = ZombieHuntPlanner.Plan();
                    foreach (var h in hunts)
                    {
                        string route = h.Reachable ? string.Join(" > ", h.route.ConvertAll(p => p.ToString())) : "out of reach";
                        Debug.Log($"ZOMBIE DEMO: hunt zombie {h.zombie.CurrentPosition}: {(h.Reachable ? $"{h.hunter.pieceType} in {h.turns}, safe route {h.safeRoute}, safe after {h.safeAfter}" : "")} route {route}; zombie path {string.Join(" > ", h.zombiePath.ConvertAll(p => p.ToString()))}");
                        if (h.Reachable && h.turns == 1 && !h.hunter.GetValidMoves().Contains(h.zombie.CurrentPosition)) Debug.Log("ZOMBIE DEMO: FAIL one-move hunt isn't a real capture");
                        if (h.Reachable && !h.hunter.GetValidMoves().Contains(h.FirstStep)) Debug.Log("ZOMBIE DEMO: FAIL first step isn't a legal move");
                    }
                    TurnPlannerUI.Instance.DebugOpen();
                    Debug.Log("ZOMBIE DEMO: planner open");
                    yield return new WaitForSeconds(6f);
                    TurnPlannerUI.Instance.DebugClose();
                }
                var brain = new RobotBrain();
                AIMove move = brain.FindMove(PieceColor.White, RobotBrain.Settings.For(AIDifficulty.Easy));
                if (move == null) { Debug.Log("ZOMBIE DEMO: White has no move"); break; }
                ChessBoard.Instance.MovePiece(move.fromPosition, move.toPosition);
                yield return new WaitForSeconds(0.3f);
            }
            yield return null;
        }
        Debug.Log($"ZOMBIE DEMO: finished in state {GameStateManager.Instance.currentState} after {ZombieMode.Rounds} rounds, score {ZombieMode.Score}, pawns destroyed {ZombieMode.PawnsDestroyed}, promoted destroyed {ZombieMode.PromotedDestroyed}");
    }

    private static int Count(PieceColor color, bool pawnsOnly = false)
    {
        Vector3Int d = BoardDimensionsManager.Instance.GetDimensions();
        int n = 0;
        for (int x = 0; x < d.x; x++)
        for (int y = 0; y < d.y; y++)
        for (int z = 0; z < d.z; z++)
        {
            ChessPiece p = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
            if (p != null && p.pieceColor == color && (!pawnsOnly || p.pieceType == ChessPieceType.Pawn)) n++;
        }
        return n;
    }
}
#endif

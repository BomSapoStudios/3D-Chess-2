using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Manages players for scalable 3D chess.
/// Handles player colors, types, turn order, and elimination for 2-6 players.
/// </summary>
public class PlayerManager : MonoBehaviour
{
    public static PlayerManager Instance { get; private set; }

    [Header("Player Configuration")]
    // Active players in turn order
    private List<PieceColor> activePlayers = new List<PieceColor>();

    // Player types for each color (Human or Computer)
    private Dictionary<PieceColor, PlayerType> playerTypes = new Dictionary<PieceColor, PlayerType>();

    // Eliminated players (for multi-player games)
    private HashSet<PieceColor> eliminatedPlayers = new HashSet<PieceColor>();

    // Standard player color order for turn rotation
    private readonly PieceColor[] colorOrder = {
        PieceColor.White,
        PieceColor.Black,
        PieceColor.Green,
        PieceColor.Purple,
        PieceColor.Yellow,
        PieceColor.Orange
    };

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("PlayerManager: Instance created");
        }
        else
        {
            Debug.LogWarning("PlayerManager: Duplicate instance detected, destroying");
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Initialize players based on game configuration
    /// </summary>
    public void InitializePlayers(GameConfiguration config)
    {
        activePlayers.Clear();
        playerTypes.Clear();
        eliminatedPlayers.Clear();

        Debug.Log($"PlayerManager: Initializing {config.playerCount} players");
        Debug.Log($"🔍 DEBUG: config.playerTypes.Count = {config.playerTypes.Count}");
        for (int i = 0; i < config.playerTypes.Count; i++)
        {
            Debug.Log($"🔍 DEBUG: config.playerTypes[{i}] = {config.playerTypes[i]}");
        }

        // Add players based on configuration
        for (int i = 0; i < config.playerCount && i < colorOrder.Length; i++)
        {
            PieceColor color = colorOrder[i];
            activePlayers.Add(color);

            // Set player type from configuration
            PlayerType type;
            Debug.Log($"🔍 PlayerManager: Processing player {i} ({color})");
            Debug.Log($"🔍   Condition check: i < config.playerTypes.Count = {i < config.playerTypes.Count}");

            if (i < config.playerTypes.Count)
            {
                Debug.Log($"🔍   config.playerTypes[{i}] = {config.playerTypes[i]}");
            }

            if (i < config.playerTypes.Count)
            {
                // Always use playerTypes list when available (for scalable multi-player support)
                type = config.playerTypes[i];
                Debug.Log($"🔍   Using playerTypes list: type = {type}");
            }
            else
            {
                // Fallback to legacy fields only if list is too short (2-player backward compatibility)
                type = i == 0 ? config.whitePlayerType : config.blackPlayerType;
                Debug.Log($"🔍   Using legacy fallback: type = {type} (i={i}, whitePlayerType={config.whitePlayerType}, blackPlayerType={config.blackPlayerType})");
            }

            playerTypes[color] = type;

            Debug.Log($"PlayerManager: Added {color} player as {type}");
        }

        Debug.Log($"PlayerManager: Initialization complete - {activePlayers.Count} players, {playerTypes.Count(p => p.Value == PlayerType.Computer)} AI");
        Debug.Log($"🎯 PlayerManager: Final state of playerTypes dictionary:");
        foreach (var kvp in playerTypes)
        {
            Debug.Log($"🎯   {kvp.Key} => {kvp.Value}");
        }
    }

    /// <summary>
    /// Get the next player in turn order
    /// </summary>
    public PieceColor GetNextPlayer(PieceColor current)
    {
        // Find current player index
        int index = activePlayers.IndexOf(current);
        if (index == -1)
        {
            Debug.LogWarning($"PlayerManager: {current} not in active players, returning first player");
            return activePlayers.Count > 0 ? activePlayers[0] : PieceColor.White;
        }

        // Cycle through active players, skipping eliminated ones
        int nextIndex = (index + 1) % activePlayers.Count;
        int attempts = 0;

        while (eliminatedPlayers.Contains(activePlayers[nextIndex]) && attempts < activePlayers.Count)
        {
            nextIndex = (nextIndex + 1) % activePlayers.Count;
            attempts++;
        }

        if (attempts >= activePlayers.Count)
        {
            Debug.LogError("PlayerManager: All players eliminated - should not happen");
            return activePlayers[0];
        }

        return activePlayers[nextIndex];
    }

    /// <summary>
    /// Get all opponents of a specific player (excluding eliminated players)
    /// </summary>
    public List<PieceColor> GetOpponents(PieceColor player)
    {
        return activePlayers
            .Where(p => p != player && !eliminatedPlayers.Contains(p))
            .ToList();
    }

    /// <summary>
    /// Get all active (non-eliminated) players
    /// </summary>
    public List<PieceColor> GetActivePlayers()
    {
        return activePlayers
            .Where(p => !eliminatedPlayers.Contains(p))
            .ToList();
    }

    /// <summary>
    /// Get all registered players (including eliminated)
    /// </summary>
    public List<PieceColor> GetAllPlayers()
    {
        return new List<PieceColor>(activePlayers);
    }

    /// <summary>
    /// Get the player type for a specific color
    /// </summary>
    public PlayerType GetPlayerType(PieceColor color)
    {
        Debug.Log($"🎯 PlayerManager.GetPlayerType: Called for {color}");
        Debug.Log($"🎯   playerTypes dictionary has {playerTypes.Count} entries");
        Debug.Log($"🎯   ContainsKey({color}): {playerTypes.ContainsKey(color)}");

        if (playerTypes.ContainsKey(color))
        {
            PlayerType result = playerTypes[color];
            Debug.Log($"🎯   Returning {result} for {color}");
            return result;
        }

        Debug.LogWarning($"PlayerManager: No player type set for {color}, defaulting to Human");
        Debug.LogWarning($"🎯 PlayerManager: Dictionary contents:");
        foreach (var kvp in playerTypes)
        {
            Debug.LogWarning($"🎯   {kvp.Key} => {kvp.Value}");
        }
        return PlayerType.Human;
    }

    /// <summary>
    /// Set the player type for a specific color
    /// </summary>
    public void SetPlayerType(PieceColor color, PlayerType type)
    {
        // Diagnostic logging to catch runtime modifications
        string previousValue = playerTypes.ContainsKey(color) ? playerTypes[color].ToString() : "NONE";
        Debug.LogWarning($"⚠️ PlayerManager.SetPlayerType: Changing {color} from {previousValue} to {type}");
        Debug.LogWarning($"⚠️ STACK TRACE:\n{System.Environment.StackTrace}");

        playerTypes[color] = type;
        Debug.Log($"PlayerManager: {color} player type set to {type}");
    }

    /// <summary>
    /// Check if a player is AI-controlled
    /// </summary>
    public bool IsPlayerAI(PieceColor color)
    {
        return GetPlayerType(color) == PlayerType.Computer;
    }

    /// <summary>
    /// Eliminate a player from the game (multi-player only)
    /// </summary>
    public void EliminatePlayer(PieceColor color)
    {
        if (!eliminatedPlayers.Contains(color))
        {
            eliminatedPlayers.Add(color);
            Debug.Log($"PlayerManager: {color} player eliminated");
        }
    }

    /// <summary>
    /// Check if a player is eliminated
    /// </summary>
    public bool IsPlayerEliminated(PieceColor color)
    {
        return eliminatedPlayers.Contains(color);
    }

    /// <summary>
    /// Get the number of active (non-eliminated) players
    /// </summary>
    public int GetActivePlayerCount()
    {
        return activePlayers.Count - eliminatedPlayers.Count;
    }

    /// <summary>
    /// Get the total number of registered players
    /// </summary>
    public int GetTotalPlayerCount()
    {
        return activePlayers.Count;
    }

    /// <summary>
    /// Check if a color is a registered player
    /// </summary>
    public bool IsRegisteredPlayer(PieceColor color)
    {
        return activePlayers.Contains(color);
    }

    /// <summary>
    /// Get game mode description (for UI)
    /// </summary>
    public string GetGameModeDescription()
    {
        int aiCount = playerTypes.Count(p => p.Value == PlayerType.Computer);
        int humanCount = playerTypes.Count - aiCount;

        if (aiCount == 0)
        {
            return $"{humanCount} Human Players";
        }
        else if (humanCount == 0)
        {
            return $"{aiCount} AI Players";
        }
        else
        {
            return $"{humanCount} Human vs {aiCount} AI";
        }
    }

    /// <summary>
    /// Reset all player data (for new game)
    /// </summary>
    public void ResetPlayers()
    {
        activePlayers.Clear();
        playerTypes.Clear();
        eliminatedPlayers.Clear();
        Debug.Log("PlayerManager: All player data reset");
    }

    // ===== CONQUEST SYSTEM FOR MULTI-PLAYER GAMES =====

    /// <summary>
    /// Transfer all pieces from one player to another (conquest system)
    /// Used when a player is checkmated in multi-player games with more than 2 players remaining
    /// </summary>
    /// <param name="fromColor">Color of the defeated player</param>
    /// <param name="toColor">Color of the conquering player</param>
    /// <returns>Number of pieces transferred</returns>
    public int TransferPiecesToPlayer(PieceColor fromColor, PieceColor toColor)
    {
        if (ChessBoard.Instance == null)
        {
            Debug.LogError("PlayerManager.TransferPiecesToPlayer: ChessBoard.Instance is null");
            return 0;
        }

        Debug.Log($"🎨 CONQUEST: Transferring all {fromColor} pieces to {toColor}");

        // Get dynamic board dimensions
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4);

        int transferCount = 0;

        // STEP 1: Find and remove the checkmated king from the board
        ChessPiece kingToRemove = null;
        BoardPosition kingPosition = new BoardPosition(-1, -1, -1);

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);

                    if (piece != null && piece.pieceColor == fromColor && piece.pieceType == ChessPieceType.King)
                    {
                        kingToRemove = piece;
                        kingPosition = pos;
                        break;
                    }
                }
                if (kingToRemove != null) break;
            }
            if (kingToRemove != null) break;
        }

        // The king dissolves into orbs that turn its pieces, one by one
        PieceEffects.ConquestEffect effect = PieceEffects.BeginConquest(kingToRemove, fromColor, toColor);

        // Remove the king from the board
        if (kingToRemove != null)
        {
            Debug.Log($"👑 CONQUEST: Removing checkmated {fromColor} King at {kingPosition}");
            ChessBoard.Instance.RemovePieceFromBoard(kingToRemove, kingPosition);
        }
        else
        {
            Debug.LogWarning($"⚠️ CONQUEST: Could not find {fromColor} King to remove during conquest!");
        }

        // STEP 2: Find all remaining pieces belonging to the defeated player and convert them
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);

                    if (piece != null && piece.pieceColor == fromColor)
                    {
                        // Skip king pieces (should already be removed, but safety check)
                        if (piece.pieceType == ChessPieceType.King)
                        {
                            Debug.LogWarning($"⚠️ CONQUEST: Found {fromColor} King still on board at {pos} - this shouldn't happen!");
                            continue;
                        }

                        // Convert piece to new color
                        piece.ConvertToColor(toColor);
                        effect.Add(piece);
                        transferCount++;

                        Debug.Log($"  Converted {piece.pieceType} at {pos} from {fromColor} to {toColor}");
                    }
                }
            }
        }

        Debug.Log($"🎨 CONQUEST: Transferred {transferCount} pieces from {fromColor} to {toColor}");
        effect.Play();

        return transferCount;
    }

    /// <summary>
    /// Get the player who delivered the checkmate (for determining who gets conquered pieces)
    /// Uses the last move to determine which player delivered the final blow
    /// </summary>
    /// <param name="checkmatedPlayer">Color of the checkmated player</param>
    /// <param name="lastMover">Optional: The player who just moved. If provided, will verify they are attacking. If null, will determine from turn system.</param>
    /// <returns>Color of the player who delivered checkmate</returns>
    public PieceColor GetCheckmateTriggeringPlayer(PieceColor checkmatedPlayer, PieceColor? lastMover = null)
    {
        if (CheckDetectionManager.Instance == null)
        {
            Debug.LogError("PlayerManager.GetCheckmateTriggeringPlayer: CheckDetectionManager.Instance is null");
            // If lastMover provided, use it
            if (lastMover.HasValue)
            {
                Debug.Log($"🎯 CONQUEST: Using provided lastMover {lastMover.Value} (CheckDetectionManager unavailable)");
                return lastMover.Value;
            }
            // Fallback: return first opponent
            List<PieceColor> opponents = GetOpponents(checkmatedPlayer);
            return opponents.Count > 0 ? opponents[0] : PieceColor.White;
        }

        // Get all players attacking the checkmated king
        List<PieceColor> attackingPlayers = CheckDetectionManager.Instance.GetAttackingPlayers(checkmatedPlayer);

        if (attackingPlayers.Count == 0)
        {
            Debug.LogError($"PlayerManager.GetCheckmateTriggeringPlayer: No attacking players found for {checkmatedPlayer} king!");
            // If lastMover provided, use it (trust the turn system)
            if (lastMover.HasValue)
            {
                Debug.Log($"🎯 CONQUEST: Using provided lastMover {lastMover.Value} (no attackers detected)");
                return lastMover.Value;
            }
            // Fallback: return first opponent
            List<PieceColor> opponents = GetOpponents(checkmatedPlayer);
            return opponents.Count > 0 ? opponents[0] : PieceColor.White;
        }

        // If lastMover is provided and is one of the attackers, use it (most reliable)
        if (lastMover.HasValue && attackingPlayers.Contains(lastMover.Value))
        {
            Debug.Log($"🎯 CONQUEST: {lastMover.Value} delivered checkmate to {checkmatedPlayer} (confirmed from turn system)");
            return lastMover.Value;
        }

        if (attackingPlayers.Count == 1)
        {
            // Simple case: only one player attacking
            Debug.Log($"🎯 CONQUEST: {attackingPlayers[0]} delivered checkmate to {checkmatedPlayer}");
            return attackingPlayers[0];
        }

        // Multiple players attacking - use last mover if available
        if (lastMover.HasValue)
        {
            Debug.LogWarning($"PlayerManager.GetCheckmateTriggeringPlayer: lastMover {lastMover.Value} provided but not in attacking list {string.Join(", ", attackingPlayers)}");
            Debug.LogWarning($"  Using lastMover anyway as it's most reliable");
            return lastMover.Value;
        }

        // Fallback: try to determine from turn system
        if (TurnManager.Instance != null)
        {
            // Get previous player (the one who just moved)
            PieceColor currentPlayer = TurnManager.Instance.GetCurrentPlayer();
            PieceColor previousPlayer = GetPreviousPlayer(currentPlayer);

            // Check if previous player is one of the attackers
            if (attackingPlayers.Contains(previousPlayer))
            {
                Debug.Log($"🎯 CONQUEST: {previousPlayer} delivered checkmate to {checkmatedPlayer} (determined from turn system)");
                return previousPlayer;
            }
        }

        // Final fallback: return first attacking player
        Debug.LogWarning($"PlayerManager.GetCheckmateTriggeringPlayer: Multiple attackers, using first: {attackingPlayers[0]}");
        return attackingPlayers[0];
    }

    /// <summary>
    /// Get the previous player in turn order (helper for determining who just moved)
    /// </summary>
    private PieceColor GetPreviousPlayer(PieceColor currentPlayer)
    {
        int currentIndex = activePlayers.IndexOf(currentPlayer);
        if (currentIndex == -1)
        {
            return activePlayers.Count > 0 ? activePlayers[0] : PieceColor.White;
        }

        // Go backwards, skipping eliminated players
        int prevIndex = (currentIndex - 1 + activePlayers.Count) % activePlayers.Count;
        int attempts = 0;

        while (eliminatedPlayers.Contains(activePlayers[prevIndex]) && attempts < activePlayers.Count)
        {
            prevIndex = (prevIndex - 1 + activePlayers.Count) % activePlayers.Count;
            attempts++;
        }

        return activePlayers[prevIndex];
    }
}

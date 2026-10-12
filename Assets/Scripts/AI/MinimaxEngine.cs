using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimax algorithm with alpha-beta pruning for 3D chess AI.
/// This engine evaluates board positions and finds the best moves for the AI player.
/// </summary>
public class MinimaxEngine
{
    private const float POSITIVE_INFINITY = 99999f;
    private const float NEGATIVE_INFINITY = -99999f;
    private const float CHECKMATE_SCORE = 10000f;

    #if UNITY_WEBGL && !UNITY_EDITOR
    private const int MAX_CACHE_ENTRIES = 1000;       // WebGL: Limited cache to conserve browser memory
    private const float DEFAULT_TIME_LIMIT = 2.0f;   // WebGL: Shorter time limit to prevent browser freezing
    private const int MAX_DEPTH_WEBGL = 3;           // WebGL: Limit search depth for performance
    #else
    private const int MAX_CACHE_ENTRIES = 5000;      // Standalone: Larger cache for better performance
    private const float DEFAULT_TIME_LIMIT = 5.0f;   // Standalone: Standard time limit
    #endif

    private int nodesEvaluated = 0;
    private bool enableDebugLogging = false;

    // PERFORMANCE CACHE: Cache moves to avoid expensive recalculation
    private Dictionary<PieceColor, List<AIMove>> movesCache = new Dictionary<PieceColor, List<AIMove>>();
    private int lastBoardHash = 0;
    
    // EVALUATION CACHE: Cache pieces and attack squares per evaluation to eliminate redundant calculations
    private Dictionary<PieceColor, List<ChessPiece>> evalPieceCache;
    private Dictionary<ChessPiece, List<BoardPosition>> evalAttackCache;
    
    // PERFORMANCE TRACKING: Monitor cache effectiveness (debug builds only)
    private int piecesCacheHits = 0;
    private int attackCacheHits = 0;
    
    public MinimaxEngine(bool debug = false)
    {
        enableDebugLogging = debug;
    }
    
    /// <summary>
    /// Find the best move for the given player using minimax with alpha-beta pruning
    /// </summary>
    public AIMove FindBestMove(PieceColor player, int depth, float timeLimit = DEFAULT_TIME_LIMIT)
    {
        // The search now lives in RobotSearch (right point of view, real turn order, king safety)
        var settings = new RobotSearch.Settings { maxDepth = Mathf.Max(1, depth), seconds = timeLimit, quiescence = true };
        return new RobotSearch().FindMove(player, settings);
    }

    /// <summary>The old search, kept for reference; no longer used</summary>
    private AIMove FindBestMoveLegacy(PieceColor player, int depth, float timeLimit = DEFAULT_TIME_LIMIT)
    {
        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Enforce depth limit to prevent browser freezing
        depth = Mathf.Min(depth, MAX_DEPTH_WEBGL);

        if (enableDebugLogging)
            Debug.Log($"MinimaxEngine (WebGL): Depth limited to {depth}, time limit: {timeLimit}s");
        #endif

        nodesEvaluated = 0;
        float startTime = Time.time;
        
        // PERFORMANCE: Enable simulation mode to skip expensive validations
        if (ChessBoard.Instance != null)
        {
            ChessBoard.Instance.EnableSimulationMode();
        }
        
        // PERFORMANCE: Enable simulation mode in CheckDetectionManager too
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.EnableSimulationMode();
        }
        
        if (enableDebugLogging)
            Debug.Log($"MinimaxEngine: Starting search for {player} at depth {depth} with simulation mode enabled");
        
        List<AIMove> possibleMoves = GetAllPossibleMoves(player);
        
        if (possibleMoves.Count == 0)
        {
            Debug.LogWarning($"MinimaxEngine: No moves available for {player}");
            
            // PERFORMANCE: Disable simulation mode before returning
            if (ChessBoard.Instance != null)
            {
                ChessBoard.Instance.DisableSimulationMode();
            }
            
            // PERFORMANCE: Disable simulation mode in CheckDetectionManager too
            if (CheckDetectionManager.Instance != null)
            {
                CheckDetectionManager.Instance.DisableSimulationMode();
            }
            
            return null;
        }
        
        AIMove bestMove = null;
        float bestScore = NEGATIVE_INFINITY;
        
        // Evaluate each possible move
        foreach (AIMove move in possibleMoves)
        {
            // Check time limit
            if (Time.time - startTime > timeLimit)
            {
                Debug.LogWarning($"MinimaxEngine: Time limit exceeded ({timeLimit}s), stopping search");
                break;
            }
            
            // Make the move temporarily
            ChessPiece capturedPiece = MakeMove(move);
            
            // Evaluate this position
            float score = Minimax(depth - 1, NEGATIVE_INFINITY, POSITIVE_INFINITY, false, GetOppositeColor(player));
            
            // Undo the move
            UndoMove(move, capturedPiece);
            
            // Update best move if this is better
            if (score > bestScore)
            {
                bestScore = score;
                bestMove = move;
                bestMove.evaluationScore = score;
            }
            
            if (enableDebugLogging)
                Debug.Log($"MinimaxEngine: Move {move.piece.pieceType} {move.fromPosition}→{move.toPosition} scored {score:F2}");
        }
        
        float searchTime = Time.time - startTime;
        
        // PERFORMANCE: Disable simulation mode before returning
        if (ChessBoard.Instance != null)
        {
            ChessBoard.Instance.DisableSimulationMode();
        }
        
        // PERFORMANCE: Disable simulation mode in CheckDetectionManager too
        if (CheckDetectionManager.Instance != null)
        {
            CheckDetectionManager.Instance.DisableSimulationMode();
        }
        
        // CRITICAL FIX: Validate the best move is actually legal before returning it
        // This prevents the AI from choosing moves that will be blocked by ChessBoard.MovePiece()
        if (bestMove != null)
        {
            Debug.Log($"🔍 VALIDATION: Checking if best move {bestMove.piece.pieceType} {bestMove.fromPosition}→{bestMove.toPosition} is actually legal...");
            
            // Use GetLegalMoves() to get properly filtered moves (no check-leaving moves)
            List<BoardPosition> legalMoves = bestMove.piece.GetLegalMoves();
            bool isMoveActuallyLegal = legalMoves.Contains(bestMove.toPosition);
            
            Debug.Log($"🔍 VALIDATION: Piece has {legalMoves.Count} legal moves, target position {bestMove.toPosition} is legal: {isMoveActuallyLegal}");
            
            if (!isMoveActuallyLegal)
            {
                Debug.LogError($"🚨 CRITICAL: AI selected ILLEGAL move {bestMove.piece.pieceType} {bestMove.fromPosition}→{bestMove.toPosition}!");
                Debug.LogError($"🚨 This move would be blocked by ChessBoard.MovePiece() causing game to freeze!");
                
                // ENHANCED RECOVERY: Search all legal moves from all pieces
                bestMove = FindBestLegalMove(player);
                
                if (bestMove != null)
                {
                    Debug.Log($"✅ RECOVERY SUCCESS: Found legal alternative {bestMove.piece.pieceType} {bestMove.fromPosition}→{bestMove.toPosition}");
                }
                else
                {
                    Debug.LogError($"💀 RECOVERY FAILED: No legal moves found for {player}! This may indicate stalemate/checkmate.");
                }
            }
            else
            {
                Debug.Log($"✅ VALIDATION SUCCESS: Move is legal and safe to execute");
            }
        }
        
        if (enableDebugLogging)
        {
            Debug.Log($"MinimaxEngine: Search completed in {searchTime:F2}s");
            Debug.Log($"MinimaxEngine: Evaluated {nodesEvaluated} nodes");
            Debug.Log($"MinimaxEngine: Best move: {bestMove} (score: {bestScore:F2})");
        }
        
        return bestMove;
    }
    
    /// <summary>
    /// Minimax algorithm with alpha-beta pruning
    /// </summary>
    private float Minimax(int depth, float alpha, float beta, bool isMaximizing, PieceColor currentPlayer)
    {
        nodesEvaluated++;
        
        // Base case: depth reached or game over
        if (depth == 0)
        {
            return EvaluatePosition(currentPlayer);
        }
        
        // Check for checkmate/stalemate
        List<AIMove> moves = GetAllPossibleMoves(currentPlayer);
        if (moves.Count == 0)
        {
            // No legal moves - could be checkmate or stalemate
            if (IsPlayerInCheck(currentPlayer))
            {
                // Checkmate - return a score based on depth to prefer faster checkmates
                return isMaximizing ? -CHECKMATE_SCORE + depth : CHECKMATE_SCORE - depth;
            }
            else
            {
                // Stalemate
                return 0;
            }
        }
        
        if (isMaximizing)
        {
            float maxEval = NEGATIVE_INFINITY;
            
            foreach (AIMove move in moves)
            {
                ChessPiece capturedPiece = MakeMove(move);
                float eval = Minimax(depth - 1, alpha, beta, false, GetOppositeColor(currentPlayer));
                UndoMove(move, capturedPiece);
                
                maxEval = Mathf.Max(maxEval, eval);
                alpha = Mathf.Max(alpha, eval);
                
                // Alpha-beta pruning
                if (beta <= alpha)
                    break;
            }
            
            return maxEval;
        }
        else
        {
            float minEval = POSITIVE_INFINITY;
            
            foreach (AIMove move in moves)
            {
                ChessPiece capturedPiece = MakeMove(move);
                float eval = Minimax(depth - 1, alpha, beta, true, GetOppositeColor(currentPlayer));
                UndoMove(move, capturedPiece);
                
                minEval = Mathf.Min(minEval, eval);
                beta = Mathf.Min(beta, eval);
                
                // Alpha-beta pruning
                if (beta <= alpha)
                    break;
            }
            
            return minEval;
        }
    }
    
    /// <summary>
    /// Get all possible legal moves for a player (with caching for performance)
    /// </summary>
    private List<AIMove> GetAllPossibleMoves(PieceColor player)
    {
        if (ChessBoard.Instance == null)
            return new List<AIMove>();

        // PERFORMANCE OPTIMIZATION: Check cache first
        int currentBoardHash = GetBoardHash();
        if (currentBoardHash == lastBoardHash && movesCache.ContainsKey(player))
        {
            if (enableDebugLogging)
                Debug.Log($"MinimaxEngine: Using cached moves for {player} ({movesCache[player].Count} moves)");
            return new List<AIMove>(movesCache[player]); // Return copy to prevent modification
        }

        // Cache miss - calculate moves
        List<AIMove> moves = new List<AIMove>();

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // DIAGNOSTIC: Log board scanning details for debugging

        int piecesFound = 0;

        // Scan all board positions for pieces of the given color
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition position = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(position);

                    if (piece != null && piece.pieceColor == player)
                    {
                        piecesFound++;

                        // PERFORMANCE: Use GetValidMoves instead of GetLegalMoves to skip expensive check validation
                        // Check validation will be done once at the top level, not for every minimax node
                        List<BoardPosition> validMoves = piece.GetValidMoves();


                        foreach (BoardPosition movePos in validMoves)
                        {
                            // PERFORMANCE: Skip moves that would capture own pieces (basic validation only)
                            ChessPiece targetPiece = ChessBoard.Instance.GetPieceAt(movePos);
                            if (targetPiece != null && targetPiece.pieceColor == player)
                                continue; // Can't capture own piece

                            moves.Add(new AIMove
                            {
                                piece = piece,
                                fromPosition = position,
                                toPosition = movePos,
                                evaluationScore = 0
                            });
                        }
                    }
                }
            }
        }

        // DIAGNOSTIC: Summary logging

        // Update cache with memory limit
        #if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: Limit cache size to prevent memory issues
        if (movesCache.Count >= MAX_CACHE_ENTRIES / 2) // Clear when half-full to maintain headroom
        {
            movesCache.Clear();
            if (enableDebugLogging)
                Debug.Log("MinimaxEngine (WebGL): Cache cleared to conserve memory");
        }
        #endif

        movesCache[player] = new List<AIMove>(moves);
        lastBoardHash = currentBoardHash;

        if (enableDebugLogging)
            Debug.Log($"MinimaxEngine: Calculated {moves.Count} moves for {player} (cached for reuse)");

        return moves;
    }
    
    /// <summary>
    /// Generate a simple hash of the current board state for caching
    /// </summary>
    private int GetBoardHash()
    {
        if (ChessBoard.Instance == null)
            return 0;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        int hash = 0;
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null)
                    {
                        // Scalable hash calculation adjusted for dynamic board size
                        int positionMultiplier = x * boardDimensions.y * boardDimensions.z + y * boardDimensions.z + z + 1;
                        hash ^= ((int)piece.pieceType + 1) * positionMultiplier * ((int)piece.pieceColor + 1);
                    }
                }
            }
        }
        return hash;
    }
    
    /// <summary>
    /// Make a move temporarily for evaluation (returns captured piece if any)
    /// </summary>
    private ChessPiece MakeMove(AIMove move)
    {
        if (ChessBoard.Instance == null)
            return null;
        
        ChessPiece capturedPiece = ChessBoard.Instance.GetPieceAt(move.toPosition);
        
        // Remove piece from original position
        ChessBoard.Instance.SetPieceAt(move.fromPosition, null);
        
        // Place piece at new position
        ChessBoard.Instance.SetPieceAt(move.toPosition, move.piece);
        
        // Update piece's internal position
        move.piece.SetCurrentPosition(move.toPosition);
        
        // PERFORMANCE: Invalidate move cache since board state changed
        ClearCache();
        
        return capturedPiece;
    }
    
    /// <summary>
    /// Undo a move (restore previous board state)
    /// </summary>
    private void UndoMove(AIMove move, ChessPiece capturedPiece)
    {
        if (ChessBoard.Instance == null)
            return;
        
        // Remove piece from new position
        ChessBoard.Instance.SetPieceAt(move.toPosition, capturedPiece);
        
        // Restore piece to original position
        ChessBoard.Instance.SetPieceAt(move.fromPosition, move.piece);
        
        // Update piece's internal position
        move.piece.SetCurrentPosition(move.fromPosition);
        
        // PERFORMANCE: Invalidate move cache since board state changed
        ClearCache();
    }
    
    /// <summary>
    /// Evaluate the current board position for the given player
    /// Positive scores favor the player, negative scores favor the opponent
    /// ENHANCED: Added aggression bonuses to break defensive loops
    /// PERFORMANCE FIX: Added evaluation-level caching to eliminate redundant calculations
    /// </summary>
    private float EvaluatePosition(PieceColor player)
    {
        if (ChessBoard.Instance == null)
            return 0;
        
        // PERFORMANCE FIX: Initialize evaluation cache to eliminate redundant calculations
        InitializeEvaluationCache();
        
        float score = 0;
        
        // Material evaluation (piece values)
        score += CalculateMaterialScore(player);
        score -= CalculateMaterialScore(GetOppositeColor(player));
        
        // Positional evaluation
        score += CalculatePositionalScore(player);
        score -= CalculatePositionalScore(GetOppositeColor(player));
        
        // King safety evaluation
        score += CalculateKingSafety(player);
        score -= CalculateKingSafety(GetOppositeColor(player));
        
        // Mobility evaluation (number of legal moves)
        score += CalculateMobility(player) * 0.1f;
        score -= CalculateMobility(GetOppositeColor(player)) * 0.1f;
        
        // AGGRESSIVE: Bonus for attacking moves and threats
        score += CalculateAggressionBonus(player);
        score -= CalculateAggressionBonus(GetOppositeColor(player));
        
        // AGGRESSIVE: King pressure bonus
        score += CalculateKingPressure(player);
        score -= CalculateKingPressure(GetOppositeColor(player));
        
        // ANTI-REPETITION: Penalty for repeated positions
        score += CalculateRepetitionPenalty(player);
        
        // PERFORMANCE FIX: Clear evaluation cache after calculations complete
        ClearEvaluationCache();
        
        return score;
    }
    
    /// <summary>
    /// Calculate material score based on piece values
    /// </summary>
    private float CalculateMaterialScore(PieceColor player)
    {
        float materialScore = 0;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));

                    if (piece != null && piece.pieceColor == player)
                    {
                        materialScore += GetPieceValue(piece.pieceType);
                    }
                }
            }
        }

        return materialScore;
    }
    
    /// <summary>
    /// Get the material value of a piece type
    /// </summary>
    private float GetPieceValue(ChessPieceType pieceType)
    {
        switch (pieceType)
        {
            case ChessPieceType.Pawn:   return 1.0f;
            case ChessPieceType.Knight: return 3.0f;
            case ChessPieceType.Bishop: return 3.0f;
            case ChessPieceType.Rook:   return 5.0f;
            case ChessPieceType.Queen:  return 9.0f;
            case ChessPieceType.King:   return 0.0f; // King safety handled separately
            default: return 0.0f;
        }
    }
    
    /// <summary>
    /// Calculate positional score (piece placement quality)
    /// </summary>
    private float CalculatePositionalScore(PieceColor player)
    {
        float positionalScore = 0;
        
        // 3D Center control - pieces closer to center of 4x4x4 cube are more valuable
        positionalScore += CalculateCenterControl(player);
        
        // Piece coordination - pieces that support each other
        positionalScore += CalculatePieceCoordination(player);
        
        // Vertical layer control - controlling different Y levels
        positionalScore += CalculateLayerControl(player);
        
        return positionalScore;
    }
    
    /// <summary>
    /// Calculate how well pieces control the center of the 3D board
    /// Center squares are more valuable regardless of board size
    /// </summary>
    private float CalculateCenterControl(PieceColor player)
    {
        float centerScore = 0;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Calculate center point dynamically (e.g., 1.5 for size 4, 2.5 for size 6, 3.5 for size 8)
        float centerX = (boardDimensions.x - 1) / 2.0f;
        float centerY = (boardDimensions.y - 1) / 2.0f;
        float centerZ = (boardDimensions.z - 1) / 2.0f;

        // Calculate max possible distance from center for normalization
        float maxDistance = Mathf.Sqrt(
            Mathf.Pow(centerX, 2) +
            Mathf.Pow(centerY, 2) +
            Mathf.Pow(centerZ, 2)
        );

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);

                    if (piece != null && piece.pieceColor == player)
                    {
                        // Calculate distance from center
                        float distanceFromCenter = Mathf.Sqrt(
                            Mathf.Pow(x - centerX, 2) +
                            Mathf.Pow(y - centerY, 2) +
                            Mathf.Pow(z - centerZ, 2)
                        );

                        // Closer to center = higher score (inverse relationship, normalized by board size)
                        float centerValue = (maxDistance - distanceFromCenter) * 0.1f;
                        centerScore += centerValue;
                    }
                }
            }
        }

        return centerScore;
    }
    
    /// <summary>
    /// Calculate how well pieces coordinate and support each other
    /// </summary>
    private float CalculatePieceCoordination(PieceColor player)
    {
        float coordinationScore = 0;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Count how many friendly pieces each piece can "see" (attack squares)
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);

                    if (piece != null && piece.pieceColor == player)
                    {
                        List<BoardPosition> attackSquares = piece.GetAttackSquares();

                        foreach (BoardPosition attackPos in attackSquares)
                        {
                            ChessPiece targetPiece = ChessBoard.Instance.GetPieceAt(attackPos);

                            // Bonus for defending friendly pieces
                            if (targetPiece != null && targetPiece.pieceColor == player)
                            {
                                coordinationScore += 0.1f;
                            }
                        }
                    }
                }
            }
        }

        return coordinationScore;
    }
    
    /// <summary>
    /// Calculate control over different vertical layers (Y levels)
    /// Having pieces spread across different layers provides strategic advantage
    /// </summary>
    private float CalculateLayerControl(PieceColor player)
    {
        float layerScore = 0;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        int[] piecesPerLayer = new int[boardDimensions.y]; // Count pieces on each Y level (dynamically sized)

        // Count pieces on each layer
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));

                    if (piece != null && piece.pieceColor == player)
                    {
                        piecesPerLayer[y]++;
                    }
                }
            }
        }

        // Bonus for having pieces on multiple layers (diversity)
        int layersOccupied = 0;
        for (int i = 0; i < boardDimensions.y; i++)
        {
            if (piecesPerLayer[i] > 0) layersOccupied++;
        }

        layerScore += layersOccupied * 0.2f; // Bonus for layer diversity

        return layerScore;
    }
    
    /// <summary>
    /// Calculate king safety score
    /// </summary>
    private float CalculateKingSafety(PieceColor player)
    {
        float kingSafetyScore = 0;
        
        // Find the king
        ChessPiece king = FindKing(player);
        if (king == null) return -1000; // King missing = game over
        
        BoardPosition kingPos = king.CurrentPosition;
        
        // Count escape squares (empty squares king can move to)
        int escapeSquares = 0;
        List<BoardPosition> kingMoves = king.GetValidMoves();
        
        foreach (BoardPosition move in kingMoves)
        {
            ChessPiece pieceAtMove = ChessBoard.Instance.GetPieceAt(move);
            if (pieceAtMove == null || pieceAtMove.pieceColor != player)
            {
                escapeSquares++;
            }
        }
        
        kingSafetyScore += escapeSquares * 0.1f; // More escape squares = safer
        
        // Penalty for being near edges (easier to checkmate in corners)
        float distanceFromEdge = Mathf.Min(
            Mathf.Min(kingPos.x, 3 - kingPos.x),
            Mathf.Min(kingPos.y, 3 - kingPos.y),
            Mathf.Min(kingPos.z, 3 - kingPos.z)
        );
        
        kingSafetyScore += distanceFromEdge * 0.2f; // Penalty for being near edges
        
        // Count friendly pieces near king (protection)
        int protectors = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue; // Skip king's own square
                    
                    BoardPosition nearPos = new BoardPosition(
                        kingPos.x + dx, kingPos.y + dy, kingPos.z + dz
                    );
                    
                    if (nearPos.IsValid())
                    {
                        ChessPiece nearPiece = ChessBoard.Instance.GetPieceAt(nearPos);
                        if (nearPiece != null && nearPiece.pieceColor == player)
                        {
                            protectors++;
                        }
                    }
                }
            }
        }
        
        kingSafetyScore += protectors * 0.15f; // Bonus for friendly pieces nearby
        
        return kingSafetyScore;
    }
    
    /// <summary>
    /// Find the king of the specified color
    /// </summary>
    private ChessPiece FindKing(PieceColor color)
    {
        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));

                    if (piece != null && piece.pieceColor == color && piece.pieceType == ChessPieceType.King)
                    {
                        return piece;
                    }
                }
            }
        }

        return null; // King not found (should not happen in normal game)
    }
    
    /// <summary>
    /// Calculate mobility score (number of legal moves)
    /// </summary>
    private float CalculateMobility(PieceColor player)
    {
        return GetAllPossibleMoves(player).Count;
    }
    
    /// <summary>
    /// Check if a player is currently in check
    /// </summary>
    private bool IsPlayerInCheck(PieceColor player)
    {
        if (CheckDetectionManager.Instance == null)
            return false;
        
        return CheckDetectionManager.Instance.IsKingInCheck(player);
    }
    
    /// <summary>
    /// Get the opposite color
    /// </summary>
    private PieceColor GetOppositeColor(PieceColor color)
    {
        return color == PieceColor.White ? PieceColor.Black : PieceColor.White;
    }
    
    /// <summary>
    /// Get statistics about the last search
    /// </summary>
    public int GetNodesEvaluated()
    {
        return nodesEvaluated;
    }
    
    /// <summary>
    /// Clear the move cache (call when board state changes externally)
    /// </summary>
    public void ClearCache()
    {
        movesCache.Clear();
        lastBoardHash = 0;
    }
    
    /// <summary>
    /// Calculate penalty for positions that would create repetitions
    /// </summary>
    private float CalculateRepetitionPenalty(PieceColor player)
    {
        if (TurnManager.Instance == null) return 0f;
        
        // Generate hash for current position with this player to move
        string currentPositionHash = GeneratePositionHashForPlayer(player);
        
        // Check how many times this position has occurred
        int repetitionCount = TurnManager.Instance.GetRepetitionCount(currentPositionHash);
        
        // Apply penalties based on repetition count
        float penalty = 0f;
        if (repetitionCount >= 2)
        {
            penalty = -10.0f; // Heavy penalty for 3rd repetition (would trigger draw)
        }
        else if (repetitionCount >= 1)
        {
            penalty = -2.0f; // Moderate penalty for 2nd repetition (warns of potential loop)
        }
        
        if (penalty < 0f && enableDebugLogging)
        {
            Debug.Log($"MinimaxEngine: Repetition penalty {penalty} applied for {player} (repetition count: {repetitionCount})");
        }
        
        return penalty;
    }
    
    /// <summary>
    /// Generate position hash for evaluation (similar to TurnManager but for specific player)
    /// </summary>
    private string GeneratePositionHashForPlayer(PieceColor player)
    {
        if (ChessBoard.Instance == null) return "empty";

        System.Text.StringBuilder hashBuilder = new System.Text.StringBuilder();

        // Include player to move in hash
        hashBuilder.Append(player == PieceColor.White ? "W:" : "B:");

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Generate hash based on piece positions (same as TurnManager)
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(pos);

                    if (piece != null)
                    {
                        // Format: position|color|type
                        hashBuilder.Append($"{x}{y}{z}|{piece.pieceColor}|{piece.pieceType}|");
                    }
                }
            }
        }

        return hashBuilder.ToString();
    }
    
    /// <summary>
    /// Enhanced recovery method: Find the best legal move from any piece
    /// This is used when the primary minimax search selects an illegal move
    /// </summary>
    private AIMove FindBestLegalMove(PieceColor player)
    {
        Debug.Log($"🔧 FindBestLegalMove: Searching all pieces for {player} to find any legal move...");

        if (ChessBoard.Instance == null) return null;

        List<AIMove> allLegalMoves = new List<AIMove>();

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Scan all positions for pieces of the given color
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition position = new BoardPosition(x, y, z);
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(position);

                    if (piece != null && piece.pieceColor == player)
                    {
                        // Use GetLegalMoves() to ensure moves are truly legal
                        List<BoardPosition> legalMoves = piece.GetLegalMoves();

                        Debug.Log($"🔧 FindBestLegalMove: {piece.pieceType} at {position} has {legalMoves.Count} legal moves");

                        foreach (BoardPosition movePos in legalMoves)
                        {
                            // Create AIMove for each legal move
                            AIMove legalMove = new AIMove
                            {
                                piece = piece,
                                fromPosition = position,
                                toPosition = movePos,
                                evaluationScore = EvaluateMoveQuickly(piece, position, movePos, player)
                            };

                            allLegalMoves.Add(legalMove);
                        }
                    }
                }
            }
        }

        Debug.Log($"🔧 FindBestLegalMove: Found {allLegalMoves.Count} total legal moves for {player}");

        if (allLegalMoves.Count == 0)
        {
            Debug.LogError($"🔧 FindBestLegalMove: NO LEGAL MOVES FOUND for {player} - this indicates stalemate or checkmate");
            return null;
        }

        // Find the best move from all legal moves
        AIMove bestLegalMove = allLegalMoves[0];
        foreach (AIMove move in allLegalMoves)
        {
            if (move.evaluationScore > bestLegalMove.evaluationScore)
            {
                bestLegalMove = move;
            }
        }

        Debug.Log($"🔧 FindBestLegalMove: Best legal move is {bestLegalMove.piece.pieceType} {bestLegalMove.fromPosition}→{bestLegalMove.toPosition} (score: {bestLegalMove.evaluationScore:F2})");

        return bestLegalMove;
    }
    
    /// <summary>
    /// Quick evaluation for a specific move (used in recovery situations)
    /// ENHANCED: Made more aggressive to prefer attacking moves
    /// </summary>
    private float EvaluateMoveQuickly(ChessPiece piece, BoardPosition from, BoardPosition to, PieceColor player)
    {
        float score = 0f;
        
        // AGGRESSIVE: Big bonus for captures
        ChessPiece targetPiece = ChessBoard.Instance.GetPieceAt(to);
        if (targetPiece != null && targetPiece.pieceColor != player)
        {
            score += GetPieceValue(targetPiece.pieceType) * 1.5f; // 50% bonus for captures
            
            // AGGRESSIVE: Extra bonus for capturing valuable pieces
            if (targetPiece.pieceType == ChessPieceType.Queen)
            {
                score += 3.0f; // Extra queen capture bonus
            }
            else if (targetPiece.pieceType == ChessPieceType.Rook)
            {
                score += 2.0f; // Extra rook capture bonus
            }
        }
        
        // AGGRESSIVE: Bonus for advancing toward enemy
        if (IsInOpponentTerritory(to, player))
        {
            score += 1.0f; // Reward aggressive positioning
        }
        
        // AGGRESSIVE: Bonus for attacking enemy pieces (even if not capturing)
        List<BoardPosition> attackSquares = GetCachedAttackSquares(piece);
        foreach (BoardPosition attackPos in attackSquares)
        {
            ChessPiece enemyPiece = ChessBoard.Instance.GetPieceAt(attackPos);
            if (enemyPiece != null && enemyPiece.pieceColor != player)
            {
                score += GetPieceValue(enemyPiece.pieceType) * 0.3f; // Bonus for threats
            }
        }
        
        // AGGRESSIVE: King pressure bonus (OPTIMIZED: using Manhattan distance)
        ChessPiece enemyKing = FindKing(GetOppositeColor(player));
        if (enemyKing != null)
        {
            int distanceToKing = Mathf.Abs(to.x - enemyKing.CurrentPosition.x) +
                                Mathf.Abs(to.y - enemyKing.CurrentPosition.y) +
                                Mathf.Abs(to.z - enemyKing.CurrentPosition.z);
            
            // Bonus for getting closer to enemy king  
            if (distanceToKing <= 4)
            {
                score += (4.0f - distanceToKing) * 0.4f; // Slightly reduced multiplier
            }
        }
        
        // Prefer central positions (but lower priority than aggression)
        float centerDistance = Mathf.Abs(to.y - 1.5f) + Mathf.Abs(to.z - 1.5f);
        score += (3f - centerDistance) * 0.05f; // Reduced from 0.1f to prioritize aggression
        
        // Add small random factor to break ties
        score += Random.Range(-0.05f, 0.05f);
        
        return score;
    }
    
    /// <summary>
    /// PERFORMANCE FIX: Initialize evaluation cache to prevent redundant calculations
    /// </summary>
    private void InitializeEvaluationCache()
    {
        evalPieceCache = new Dictionary<PieceColor, List<ChessPiece>>();
        evalAttackCache = new Dictionary<ChessPiece, List<BoardPosition>>();
        piecesCacheHits = 0;
        attackCacheHits = 0;
    }
    
    /// <summary>
    /// PERFORMANCE FIX: Clear evaluation cache after calculations complete
    /// </summary>
    private void ClearEvaluationCache()
    {
        // PERFORMANCE TRACKING: Log cache effectiveness if debug enabled
        if (enableDebugLogging && (piecesCacheHits > 0 || attackCacheHits > 0))
        {
            Debug.Log($"MinimaxEngine: Cache performance - Pieces cache hits: {piecesCacheHits}, Attack cache hits: {attackCacheHits}");
        }
        
        evalPieceCache?.Clear();
        evalAttackCache?.Clear();
        evalPieceCache = null;
        evalAttackCache = null;
    }
    
    /// <summary>
    /// Get all pieces for the specified player (PERFORMANCE OPTIMIZATION)
    /// This replaces expensive board scanning with direct piece collection
    /// PERFORMANCE FIX: Added caching to prevent redundant calls per evaluation
    /// </summary>
    private List<ChessPiece> GetPlayerPieces(PieceColor player)
    {
        // PERFORMANCE FIX: Return cached result if already calculated in this evaluation
        if (evalPieceCache != null && evalPieceCache.ContainsKey(player))
        {
            piecesCacheHits++;
            return evalPieceCache[player];
        }

        List<ChessPiece> pieces = new List<ChessPiece>(16); // Pre-allocate for typical piece count

        // OPTIMIZED: Only scan occupied positions instead of all squares
        // Early exit if ChessBoard is null
        if (ChessBoard.Instance == null) return pieces;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null && piece.pieceColor == player)
                    {
                        pieces.Add(piece);
                    }
                }
            }
        }

        // PERFORMANCE FIX: Cache result for reuse within same evaluation
        if (evalPieceCache != null)
        {
            evalPieceCache[player] = pieces;
        }

        return pieces;
    }
    
    /// <summary>
    /// Get attack squares for a piece with caching to prevent redundant calculations
    /// PERFORMANCE FIX: Caches GetAttackSquares() results within same evaluation
    /// </summary>
    private List<BoardPosition> GetCachedAttackSquares(ChessPiece piece)
    {
        // PERFORMANCE FIX: Return cached result if already calculated
        if (evalAttackCache != null && evalAttackCache.ContainsKey(piece))
        {
            attackCacheHits++;
            return evalAttackCache[piece];
        }
        
        // Calculate attack squares (this is the expensive operation)
        List<BoardPosition> attackSquares = piece.GetAttackSquares();
        
        // PERFORMANCE FIX: Cache result for reuse within same evaluation
        if (evalAttackCache != null)
        {
            evalAttackCache[piece] = attackSquares;
        }
        
        return attackSquares;
    }
    
    /// <summary>
    /// Calculate aggression bonus for attacking moves and threats
    /// This encourages the AI to be more aggressive and break defensive loops
    /// OPTIMIZED: Uses piece-based scanning instead of board-based scanning
    /// </summary>
    private float CalculateAggressionBonus(PieceColor player)
    {
        float aggressionScore = 0f;
        PieceColor opponent = GetOppositeColor(player);
        
        // PERFORMANCE OPTIMIZED: Scan only existing pieces instead of all 64 board positions
        List<ChessPiece> playerPieces = GetPlayerPieces(player);
        
        foreach (ChessPiece piece in playerPieces)
        {
            BoardPosition pos = piece.CurrentPosition;
            List<BoardPosition> attackSquares = GetCachedAttackSquares(piece);
            
            foreach (BoardPosition attackPos in attackSquares)
            {
                ChessPiece targetPiece = ChessBoard.Instance.GetPieceAt(attackPos);
                
                if (targetPiece != null && targetPiece.pieceColor == opponent)
                {
                    // AGGRESSIVE: Big bonus for attacking valuable pieces
                    float targetValue = GetPieceValue(targetPiece.pieceType);
                    aggressionScore += targetValue * 0.8f; // 80% of piece value as attack bonus
                    
                    // AGGRESSIVE: Extra bonus for attacking the king
                    if (targetPiece.pieceType == ChessPieceType.King)
                    {
                        aggressionScore += 5.0f; // Big king threat bonus
                    }
                    
                    // AGGRESSIVE: Bonus for attacking defended pieces  
                    if (IsPieceDefended(targetPiece))
                    {
                        aggressionScore += 0.5f; // Pressure even defended pieces
                    }
                }
            }
            
            // AGGRESSIVE: Bonus for pieces in opponent territory
            if (IsInOpponentTerritory(pos, player))
            {
                aggressionScore += 0.3f * GetPieceValue(piece.pieceType);
            }
        }
        
        return aggressionScore;
    }
    
    /// <summary>
    /// Calculate bonus for putting pressure on the enemy king
    /// OPTIMIZED: Uses piece-based scanning and Manhattan distance for better performance
    /// </summary>
    private float CalculateKingPressure(PieceColor player)
    {
        float kingPressureScore = 0f;
        ChessPiece enemyKing = FindKing(GetOppositeColor(player));
        
        if (enemyKing == null) return 0f;
        
        BoardPosition kingPos = enemyKing.CurrentPosition;
        
        // PERFORMANCE OPTIMIZED: Scan only existing pieces and use Manhattan distance
        List<ChessPiece> playerPieces = GetPlayerPieces(player);
        
        foreach (ChessPiece piece in playerPieces)
        {
            BoardPosition pos = piece.CurrentPosition;
            
            // OPTIMIZED: Use Manhattan distance instead of expensive Euclidean distance
            int manhattanDistance = Mathf.Abs(pos.x - kingPos.x) + 
                                    Mathf.Abs(pos.y - kingPos.y) + 
                                    Mathf.Abs(pos.z - kingPos.z);
            
            // AGGRESSIVE: Bonus inversely proportional to distance from king
            if (manhattanDistance <= 4) // Only reward reasonably close pieces
            {
                float proximityBonus = (4.0f - manhattanDistance) * 0.3f; // Reduced weight for performance
                kingPressureScore += proximityBonus * GetPieceValue(piece.pieceType) * 0.15f; // Slightly reduced multiplier
            }
            
            // AGGRESSIVE: Bonus for controlling squares around the king (only for close pieces)
            if (manhattanDistance <= 3) // Only calculate for pieces close enough to matter
            {
                List<BoardPosition> controlledSquares = GetCachedAttackSquares(piece);
                foreach (BoardPosition controlledPos in controlledSquares)
                {
                    // OPTIMIZED: Use Manhattan distance for control squares too
                    int controlDistance = Mathf.Abs(controlledPos.x - kingPos.x) +
                                         Mathf.Abs(controlledPos.y - kingPos.y) +
                                         Mathf.Abs(controlledPos.z - kingPos.z);
                    
                    if (controlDistance <= 2) // Squares very close to king
                    {
                        kingPressureScore += 0.6f; // Reduced from 0.8f for better balance
                    }
                }
            }
        }
        
        return kingPressureScore;
    }
    
    /// <summary>
    /// Check if a piece is defended by friendly pieces
    /// </summary>
    private bool IsPieceDefended(ChessPiece piece)
    {
        if (piece == null) return false;

        BoardPosition piecePos = piece.CurrentPosition;
        PieceColor pieceColor = piece.pieceColor;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Check if any friendly piece can attack this position
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    BoardPosition pos = new BoardPosition(x, y, z);
                    ChessPiece defender = ChessBoard.Instance.GetPieceAt(pos);

                    if (defender != null && defender.pieceColor == pieceColor && defender != piece)
                    {
                        List<BoardPosition> attackSquares = defender.GetAttackSquares();
                        if (attackSquares.Contains(piecePos))
                        {
                            return true; // Piece is defended
                        }
                    }
                }
            }
        }

        return false;
    }
    
    /// <summary>
    /// Check if a position is in opponent territory (aggressive positioning)
    /// </summary>
    private bool IsInOpponentTerritory(BoardPosition pos, PieceColor player)
    {
        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        // Consider opponent territory based on Y levels (upper half vs lower half of board)
        int midpoint = boardDimensions.y / 2;

        if (player == PieceColor.White)
        {
            // White advancing to upper levels (Y >= midpoint) is aggressive
            return pos.y >= midpoint;
        }
        else
        {
            // Black advancing to lower levels (Y < midpoint) is aggressive
            return pos.y < midpoint;
        }
    }
    
    // === DRAW EVALUATION METHODS ===
    
    /// <summary>
    /// Evaluate if the current position warrants offering a draw
    /// Returns true if AI should offer a draw based on position assessment
    /// </summary>
    public bool ShouldOfferDraw(PieceColor aiPlayer, AIDifficulty difficulty)
    {
        if (ChessBoard.Instance == null) return false;
        
        float positionScore = EvaluatePosition(aiPlayer);
        float materialBalance = CalculateMaterialBalance(aiPlayer);
        GamePhase gamePhase = CalculateGamePhase();
        bool hasRepetitionRisk = CheckRepetitionRisk();
        int mobility = GetAllPossibleMoves(aiPlayer).Count;
        
        // Draw thresholds based on difficulty
        float drawThreshold = GetDrawOfferThreshold(difficulty);
        
        if (enableDebugLogging)
        {
            Debug.Log($"MinimaxEngine: Draw evaluation for {aiPlayer} (difficulty: {difficulty})");
            Debug.Log($"  Position score: {positionScore:F2}, Material balance: {materialBalance:F2}");
            Debug.Log($"  Game phase: {gamePhase}, Repetition risk: {hasRepetitionRisk}, Mobility: {mobility}");
            Debug.Log($"  Draw threshold: {drawThreshold:F2}");
        }
        
        // Offer draw if position is roughly equal or slightly disadvantageous
        if (positionScore >= -drawThreshold && positionScore <= drawThreshold)
        {
            // Additional conditions for draw offer
            if (gamePhase == GamePhase.Endgame || hasRepetitionRisk || 
                materialBalance >= -1.0f || mobility < 5)
            {
                if (enableDebugLogging)
                    Debug.Log($"MinimaxEngine: {aiPlayer} should offer draw - position roughly equal");
                return true;
            }
        }
        
        // Offer draw if insufficient material for checkmate
        if (HasInsufficientMaterial(aiPlayer) && HasInsufficientMaterial(GetOppositeColor(aiPlayer)))
        {
            if (enableDebugLogging)
                Debug.Log($"MinimaxEngine: {aiPlayer} should offer draw - insufficient material");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Evaluate if AI should accept an opponent's draw offer
    /// Returns true if accepting the draw is beneficial or neutral
    /// </summary>
    public bool ShouldAcceptDraw(PieceColor aiPlayer, AIDifficulty difficulty)
    {
        if (ChessBoard.Instance == null) return false;
        
        float positionScore = EvaluatePosition(aiPlayer);
        float materialBalance = CalculateMaterialBalance(aiPlayer);
        GamePhase gamePhase = CalculateGamePhase();
        bool kingSafetyRisk = HasKingSafetyRisk(aiPlayer);
        int mobility = GetAllPossibleMoves(aiPlayer).Count;
        
        // Acceptance thresholds based on difficulty
        float acceptanceThreshold = GetDrawAcceptanceThreshold(difficulty);
        
        if (enableDebugLogging)
        {
            Debug.Log($"MinimaxEngine: Draw acceptance evaluation for {aiPlayer} (difficulty: {difficulty})");
            Debug.Log($"  Position score: {positionScore:F2}, Material balance: {materialBalance:F2}");
            Debug.Log($"  Game phase: {gamePhase}, King safety risk: {kingSafetyRisk}, Mobility: {mobility}");
            Debug.Log($"  Acceptance threshold: {acceptanceThreshold:F2}");
        }
        
        // Accept if position is equal or worse
        if (positionScore <= acceptanceThreshold)
        {
            if (enableDebugLogging)
                Debug.Log($"MinimaxEngine: {aiPlayer} should accept draw - position not advantageous");
            return true;
        }
        
        // Accept if material disadvantage or king safety concerns
        if (materialBalance <= -2.0f || kingSafetyRisk)
        {
            if (enableDebugLogging)
                Debug.Log($"MinimaxEngine: {aiPlayer} should accept draw - material/safety concerns");
            return true;
        }
        
        // Accept if limited mobility (potential stalemate risk)
        if (mobility < 3)
        {
            if (enableDebugLogging)
                Debug.Log($"MinimaxEngine: {aiPlayer} should accept draw - limited mobility");
            return true;
        }
        
        // Accept if insufficient material for both sides
        if (HasInsufficientMaterial(aiPlayer) && HasInsufficientMaterial(GetOppositeColor(aiPlayer)))
        {
            if (enableDebugLogging)
                Debug.Log($"MinimaxEngine: {aiPlayer} should accept draw - insufficient material");
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Calculate material balance (positive = AI advantage, negative = disadvantage)
    /// </summary>
    private float CalculateMaterialBalance(PieceColor aiPlayer)
    {
        float aiMaterial = CalculateMaterialScore(aiPlayer);
        float opponentMaterial = CalculateMaterialScore(GetOppositeColor(aiPlayer));
        return aiMaterial - opponentMaterial;
    }
    
    /// <summary>
    /// Determine the current game phase based on material and piece count
    /// </summary>
    private GamePhase CalculateGamePhase()
    {
        int totalPieces = 0;
        float totalMaterial = 0;

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null)
                    {
                        totalPieces++;
                        totalMaterial += GetPieceValue(piece.pieceType);
                    }
                }
            }
        }

        // Phase determination for 3D chess (scales with board size)
        // 4x4x4 starts with ~32 pieces, 6x6x6 with ~72, 8x8x8 with ~128
        int totalSquares = boardDimensions.x * boardDimensions.y * boardDimensions.z;
        int estimatedStartingPieces = totalSquares / 2; // Rough estimate

        // Endgame: Less than 25% of starting pieces
        // Middlegame: 25-50% of starting pieces
        // Opening: More than 50% of starting pieces
        if (totalPieces <= estimatedStartingPieces / 4 || totalMaterial <= 20)
            return GamePhase.Endgame;
        else if (totalPieces <= estimatedStartingPieces / 2 || totalMaterial <= 40)
            return GamePhase.Middlegame;
        else
            return GamePhase.Opening;
    }
    
    /// <summary>
    /// Check if there's a risk of repetitive moves leading to draw
    /// </summary>
    private bool CheckRepetitionRisk()
    {
        if (TurnManager.Instance == null) return false;
        
        // Check recent move history for patterns
        // This is a simplified check - could be enhanced with actual move history analysis
        string currentPositionHash = GeneratePositionHashForPlayer(PieceColor.White);
        int repetitionCount = TurnManager.Instance.GetRepetitionCount(currentPositionHash);
        
        return repetitionCount >= 1; // Risk if position has occurred before
    }
    
    /// <summary>
    /// Check if AI's king has safety concerns
    /// </summary>
    private bool HasKingSafetyRisk(PieceColor aiPlayer)
    {
        float kingSafety = CalculateKingSafety(aiPlayer);
        return kingSafety < -1.0f; // Threshold indicating king is in danger
    }
    
    /// <summary>
    /// Check if a player has insufficient material to achieve checkmate
    /// </summary>
    private bool HasInsufficientMaterial(PieceColor player)
    {
        List<ChessPieceType> pieces = new List<ChessPieceType>();

        // Get dynamic board dimensions from BoardDimensionsManager
        Vector3Int boardDimensions = BoardDimensionsManager.Instance != null
            ? BoardDimensionsManager.Instance.GetDimensions()
            : new Vector3Int(4, 4, 4); // Fallback to 4x4x4 for 2-player games

        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int y = 0; y < boardDimensions.y; y++)
            {
                for (int z = 0; z < boardDimensions.z; z++)
                {
                    ChessPiece piece = ChessBoard.Instance.GetPieceAt(new BoardPosition(x, y, z));
                    if (piece != null && piece.pieceColor == player && piece.pieceType != ChessPieceType.King)
                    {
                        pieces.Add(piece.pieceType);
                    }
                }
            }
        }

        // Insufficient material cases for 3D chess
        if (pieces.Count == 0) return true; // King only
        if (pieces.Count == 1)
        {
            // King + single minor piece usually insufficient in 3D
            return pieces[0] == ChessPieceType.Knight || pieces[0] == ChessPieceType.Bishop;
        }
        if (pieces.Count == 2)
        {
            // Two knights or two bishops may be insufficient depending on position
            return pieces.TrueForAll(p => p == ChessPieceType.Knight || p == ChessPieceType.Bishop);
        }

        return false; // Assume sufficient material otherwise
    }
    
    /// <summary>
    /// Get draw offer threshold based on AI difficulty
    /// </summary>
    private float GetDrawOfferThreshold(AIDifficulty difficulty)
    {
        return difficulty switch
        {
            AIDifficulty.Easy => 2.0f,    // Liberal - offers draws in slightly losing positions
            AIDifficulty.Medium => 1.0f,  // Balanced - offers draws in roughly equal positions
            AIDifficulty.Hard => 0.5f,    // Conservative - only offers draws in very equal positions
            _ => 1.0f
        };
    }
    
    /// <summary>
    /// Get draw acceptance threshold based on AI difficulty
    /// </summary>
    private float GetDrawAcceptanceThreshold(AIDifficulty difficulty)
    {
        return difficulty switch
        {
            AIDifficulty.Easy => 0.5f,    // Liberal - accepts draws even in slightly winning positions
            AIDifficulty.Medium => -0.5f, // Balanced - accepts draws in equal or slightly losing positions
            AIDifficulty.Hard => -1.5f,   // Aggressive - only accepts draws in losing positions
            _ => -0.5f
        };
    }
}

/// <summary>
/// Game phase enumeration for draw evaluation
/// </summary>
public enum GamePhase
{
    Opening,
    Middlegame,
    Endgame
}
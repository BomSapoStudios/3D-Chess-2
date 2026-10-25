using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Board size options for 3D chess
/// </summary>
public enum BoardSize
{
    Small4x4x4,    // 4x4x4 board (default for 2 players)
    Medium6x6x6,   // 6x6x6 board (for 2 players, more strategic)
    Large8x8x8     // 8x8x8 board (for 2 players, maximum complexity)
}

/// <summary>
/// Data structure for storing game configuration settings that persist between scenes.
/// This includes player types, AI difficulty, and other game setup preferences.
/// </summary>
[System.Serializable]
public class GameConfiguration
{
    [Header("Game Setup")]
    public int playerCount = 2;                    // Number of players (2, 4, 6)
    public int aiPlayerCount = 0;                  // Number of AI players (0 to playerCount)
    public BoardSize boardSize = BoardSize.Small4x4x4;  // Board size selection
    
    [Header("Player Setup")]
    public PlayerType whitePlayerType = PlayerType.Human;
    public PlayerType blackPlayerType = PlayerType.Human;
    
    [Header("Scalable Player Setup")]
    public List<PlayerType> playerTypes = new List<PlayerType>(); // Scalable for 2/4/6 players
    public bool useRandomAssignment = false;
    public int humanPlayerStartPosition = 0; // Which position human starts from
    
    [Header("AI Settings")]
    public AIDifficulty aiDifficulty = AIDifficulty.Medium;
    
    [Header("Game Preferences")]
    public bool enableTurnValidation = true;
    public bool showMoveHints = true;
    public bool enableSoundEffects = true;
    
    [Header("Chaos Mode Settings")]
    public bool enableChaosMode = false;      // Enable simplified chaos mode 
    public int chaosTurnInterval = 9;         // Turns between slice rotations (default 9, range 3-25)
    
    [Header("Timed Play Settings")]
    public bool enableTimedPlay = false;      // Enable timed play mode
    public int timePerPlayerMinutes = 10;     // Minutes per player (default 10, range 3-30)

    [Header("Zombie Survival")]
    public bool zombieMode = false;           // single player vs endless zombie pawns (ZombieDirector)

    [Header("Queen Blitz")]
    public bool queenBlitz = false;           // puzzle: clear the pawns with a lone queen (QueenBlitzDirector)
    public int blitzPuzzle = 0;               // which puzzle (QueenBlitz.Puzzles)
    public string saveSlot = "";             // SavedGames slot (Quick Play, Zombies, Blitz, Local); empty = not saved

    [Header("Online Multiplayer Settings")]
    public bool isOnlineGame = false;         // Is this an online multiplayer game?
    public bool isPublicGame = false;         // Is this game visible in public lobby?
    public string roomCode = "";              // Room code for online game
    
    /// <summary>
    /// Default constructor with Human vs Human setup
    /// </summary>
    public GameConfiguration()
    {
        playerCount = 2;
        aiPlayerCount = 0;
        boardSize = BoardSize.Small4x4x4;
        whitePlayerType = PlayerType.Human;
        blackPlayerType = PlayerType.Human;
        aiDifficulty = AIDifficulty.Medium;
        enableTurnValidation = true;
        showMoveHints = true;
        enableSoundEffects = true;
        enableChaosMode = false;
        chaosTurnInterval = 9;
        enableTimedPlay = false;
        timePerPlayerMinutes = 10;
    }
    
    /// <summary>
    /// Constructor for specific game mode setup
    /// </summary>
    public GameConfiguration(PlayerType whiteType, PlayerType blackType, AIDifficulty difficulty = AIDifficulty.Medium)
    {
        playerCount = 2;
        aiPlayerCount = (whiteType == PlayerType.Computer ? 1 : 0) + (blackType == PlayerType.Computer ? 1 : 0);
        boardSize = BoardSize.Small4x4x4;
        whitePlayerType = whiteType;
        blackPlayerType = blackType;
        aiDifficulty = difficulty;
        enableTurnValidation = true;
        showMoveHints = true;
        enableSoundEffects = true;
        enableChaosMode = false;
        chaosTurnInterval = 9;
        enableTimedPlay = false;
        timePerPlayerMinutes = 10;
    }
    
    /// <summary>
    /// Constructor for enhanced menu flow
    /// </summary>
    public GameConfiguration(int players, int aiPlayers, BoardSize size, AIDifficulty difficulty = AIDifficulty.Medium)
    {
        playerCount = players;
        aiPlayerCount = aiPlayers;
        boardSize = size;
        aiDifficulty = difficulty;
        
        // Set player types based on AI count (for 2-player games)
        if (playerCount == 2)
        {
            if (aiPlayerCount == 0)
            {
                whitePlayerType = PlayerType.Human;
                blackPlayerType = PlayerType.Human;
            }
            else if (aiPlayerCount == 1)
            {
                whitePlayerType = PlayerType.Human;
                blackPlayerType = PlayerType.Computer;
            }
            else
            {
                whitePlayerType = PlayerType.Computer;
                blackPlayerType = PlayerType.Computer;
            }
        }
        
        enableTurnValidation = true;
        showMoveHints = true;
        enableSoundEffects = true;
        enableChaosMode = false;
        chaosTurnInterval = 9;
        enableTimedPlay = false;
        timePerPlayerMinutes = 10;
    }
    
    /// <summary>
    /// Check if this is a Human vs Human game
    /// </summary>
    public bool IsHumanVsHuman()
    {
        return whitePlayerType == PlayerType.Human && blackPlayerType == PlayerType.Human;
    }
    
    /// <summary>
    /// Check if this is a Human vs AI game
    /// </summary>
    public bool IsHumanVsAI()
    {
        return (whitePlayerType == PlayerType.Human && blackPlayerType == PlayerType.Computer) ||
               (whitePlayerType == PlayerType.Computer && blackPlayerType == PlayerType.Human);
    }
    
    /// <summary>
    /// Check if this is an AI vs AI game
    /// </summary>
    public bool IsAIVsAI()
    {
        return whitePlayerType == PlayerType.Computer && blackPlayerType == PlayerType.Computer;
    }
    
    /// <summary>
    /// Get which color the human player is (for Human vs AI games)
    /// </summary>
    public PieceColor GetHumanPlayerColor()
    {
        if (!IsHumanVsAI())
        {
            return PieceColor.White; // Default fallback
        }
        
        return whitePlayerType == PlayerType.Human ? PieceColor.White : PieceColor.Black;
    }
    
    /// <summary>
    /// Get which color the AI player is (for Human vs AI games)
    /// </summary>
    public PieceColor GetAIPlayerColor()
    {
        if (!IsHumanVsAI())
        {
            return PieceColor.Black; // Default fallback
        }
        
        return whitePlayerType == PlayerType.Computer ? PieceColor.White : PieceColor.Black;
    }
    
    /// <summary>
    /// Get a user-friendly description of the game mode
    /// </summary>
    public string GetGameModeDescription()
    {
        if (IsHumanVsHuman())
        {
            return "Human vs Human";
        }
        else if (IsAIVsAI())
        {
            return $"AI vs AI ({aiDifficulty})";
        }
        else
        {
            PieceColor humanColor = GetHumanPlayerColor();
            return $"Human ({humanColor}) vs AI ({aiDifficulty})";
        }
    }
    
    /// <summary>
    /// Create a copy of this configuration
    /// </summary>
    public GameConfiguration Clone()
    {
        GameConfiguration copy = new GameConfiguration
        {
            playerCount = this.playerCount,
            aiPlayerCount = this.aiPlayerCount,
            boardSize = this.boardSize,
            whitePlayerType = this.whitePlayerType,
            blackPlayerType = this.blackPlayerType,
            aiDifficulty = this.aiDifficulty,
            enableTurnValidation = this.enableTurnValidation,
            showMoveHints = this.showMoveHints,
            enableSoundEffects = this.enableSoundEffects,
            
            // Chaos Mode Settings
            enableChaosMode = this.enableChaosMode,
            chaosTurnInterval = this.chaosTurnInterval,
            
            // Timed Play Settings
            enableTimedPlay = this.enableTimedPlay,
            timePerPlayerMinutes = this.timePerPlayerMinutes,
            zombieMode = this.zombieMode,
            queenBlitz = this.queenBlitz,
            blitzPuzzle = this.blitzPuzzle,
            saveSlot = this.saveSlot
        };

        // Copy playerTypes list (critical for multi-player games)
        Debug.Log($"🔄 GameConfiguration.Clone(): Copying playerTypes list (source count: {this.playerTypes.Count})");
        for (int i = 0; i < this.playerTypes.Count; i++)
        {
            Debug.Log($"🔄   Source playerTypes[{i}] = {this.playerTypes[i]}");
        }

        copy.playerTypes = new List<PlayerType>(this.playerTypes);

        Debug.Log($"🔄 GameConfiguration.Clone(): After copy (dest count: {copy.playerTypes.Count})");
        for (int i = 0; i < copy.playerTypes.Count; i++)
        {
            Debug.Log($"🔄   Dest playerTypes[{i}] = {copy.playerTypes[i]}");
        }

        return copy;
    }
    
    /// <summary>
    /// Quick setup for Human vs AI with human as White
    /// </summary>
    public static GameConfiguration HumanWhiteVsAI(AIDifficulty difficulty = AIDifficulty.Medium)
    {
        return new GameConfiguration(PlayerType.Human, PlayerType.Computer, difficulty);
    }
    
    /// <summary>
    /// Quick setup for Human vs AI with human as Black
    /// </summary>
    public static GameConfiguration HumanBlackVsAI(AIDifficulty difficulty = AIDifficulty.Medium)
    {
        return new GameConfiguration(PlayerType.Computer, PlayerType.Human, difficulty);
    }
    
    /// <summary>
    /// Quick setup for AI vs AI
    /// </summary>
    public static GameConfiguration AIVsAI(AIDifficulty difficulty = AIDifficulty.Medium)
    {
        return new GameConfiguration(PlayerType.Computer, PlayerType.Computer, difficulty);
    }
    
    /// <summary>
    /// Get board size description
    /// </summary>
    public string GetBoardSizeDescription()
    {
        return boardSize switch
        {
            BoardSize.Small4x4x4 => "4x4x4 (Compact)",
            BoardSize.Medium6x6x6 => "6x6x6 (Standard)",
            BoardSize.Large8x8x8 => "8x8x8 (Large)",
            _ => "Unknown"
        };
    }
    
    /// <summary>
    /// Get player setup description
    /// </summary>
    public string GetPlayerSetupDescription()
    {
        int humanCount = playerCount - aiPlayerCount;
        if (aiPlayerCount == 0)
        {
            return $"{playerCount} Human Players";
        }
        else if (humanCount == 0)
        {
            return $"{playerCount} AI Players";
        }
        else
        {
            return $"{humanCount} Human, {aiPlayerCount} AI";
        }
    }
    
    /// <summary>
    /// Get comprehensive configuration description
    /// </summary>
    public string GetFullDescription()
    {
        return $"{GetPlayerSetupDescription()} on {GetBoardSizeDescription()} board";
    }
    
    /// <summary>
    /// Debug string representation
    /// </summary>
    public override string ToString()
    {
        return $"GameConfig: {GetFullDescription()}, AI: {aiDifficulty}, Validation: {enableTurnValidation}, Hints: {showMoveHints}, Sound: {enableSoundEffects}";
    }
    
    // === SCALABLE SIDE SELECTION HELPER METHODS ===
    
    /// <summary>
    /// Initialize playerTypes list with appropriate size and default values
    /// </summary>
    public void InitializePlayerTypes()
    {
        playerTypes.Clear();
        for (int i = 0; i < playerCount; i++)
        {
            playerTypes.Add(PlayerType.Human); // Default all to human
        }
    }
    
    /// <summary>
    /// Check if side selection is needed (when 0 < AI count < total players)
    /// </summary>
    public bool NeedsSideSelection()
    {
        return aiPlayerCount > 0 && aiPlayerCount < playerCount;
    }
    
    /// <summary>
    /// Set player type at specific position (0-based index)
    /// </summary>
    public void SetPlayerTypeAtPosition(int position, PlayerType type)
    {
        if (position >= 0 && position < playerTypes.Count)
        {
            playerTypes[position] = type;
        }
    }
    
    /// <summary>
    /// Randomly assign player types based on AI count
    /// </summary>
    public void RandomlyAssignPlayers()
    {
        if (playerTypes.Count != playerCount)
        {
            InitializePlayerTypes();
        }
        
        // Randomly select positions for AI players
        List<int> availablePositions = new List<int>();
        for (int i = 0; i < playerCount; i++)
        {
            availablePositions.Add(i);
        }
        
        // Set all to human first
        for (int i = 0; i < playerCount; i++)
        {
            playerTypes[i] = PlayerType.Human;
        }
        
        // Randomly assign AI players
        for (int i = 0; i < aiPlayerCount; i++)
        {
            int randomIndex = Random.Range(0, availablePositions.Count);
            int position = availablePositions[randomIndex];
            playerTypes[position] = PlayerType.Computer;
            availablePositions.RemoveAt(randomIndex);
        }
    }
    
    /// <summary>
    /// Validate that assignment matches configuration (AI count matches)
    /// </summary>
    public bool ValidateAssignment()
    {
        if (playerTypes.Count != playerCount) return false;
        
        int actualAICount = 0;
        foreach (var type in playerTypes)
        {
            if (type == PlayerType.Computer) actualAICount++;
        }
        
        return actualAICount == aiPlayerCount;
    }
    
    /// <summary>
    /// Apply the scalable configuration to legacy 2-player fields for compatibility
    /// </summary>
    public void ApplyToLegacyFields()
    {
        if (playerCount == 2 && playerTypes.Count == 2)
        {
            whitePlayerType = playerTypes[0];
            blackPlayerType = playerTypes[1];
        }
    }
}
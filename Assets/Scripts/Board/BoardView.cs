using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// How the cube is drawn on this device, separate from the rules.
///
/// Yellow and Orange (players 5 and 6) advance along the board's y axis, which is normally up and
/// down the screen. In an online game played as one of them, the cube is drawn tipped a quarter turn
/// about the x axis, so their pawns march away from them like everyone else's. Pieces still stand
/// upright and floors still lie flat, because only where each square is drawn changes: board y is
/// drawn as depth (view z) and board z as height (view y). Green and Purple then advance up and down
/// for them instead.
///
/// Rules, moves, the server and other devices only ever see board coordinates. Anything that turns
/// a board square or a board direction into a place in the Pieces Container goes through here.
/// </summary>
public static class BoardView
{
    private static bool? tilted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        tilted = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => tilted = null;

    /// <summary>True when this device draws the cube tipped (online, playing Yellow or Orange)</summary>
    public static bool Tilted
    {
        get
        {
            if (!tilted.HasValue) tilted = Decide();
            return tilted.Value;
        }
    }

    /// <summary>
    /// Editor only (Tools > Tipped Cube Test): tip the cube in local games too, so the tipped look
    /// can be checked without a 6-player online game. Never set in a device build.
    /// </summary>
    public static bool ForceForTesting;

    private static bool Decide()
    {
        if (Application.isEditor && ForceForTesting && InGameScene()) return true;
        if (!OnlineSession.IsActive) return false;
        if (OnlineSession.LocalColor != PieceColor.Yellow && OnlineSession.LocalColor != PieceColor.Orange) return false;
        if (!InGameScene()) return false; // menu thumbnails etc.
        SceneController sc = SceneController.Instance;
        GameConfiguration config = sc != null ? sc.GetCurrentGameConfiguration() : null;
        return config == null || config.isOnlineGame;
    }

    private static bool InGameScene()
    {
        SceneController sc = SceneController.Instance;
        string gameScene = sc != null ? sc.gameSceneName : "SampleScene";
        return SceneManager.GetActiveScene().name == gameScene;
    }

    private static Vector3Int Dims => BoardDimensionsManager.Instance != null
        ? BoardDimensionsManager.Instance.GetDimensions()
        : new Vector3Int(4, 4, 4);

    /// <summary>The cube's size as drawn (y and z swap places when tipped)</summary>
    public static Vector3Int ViewDims
    {
        get
        {
            Vector3Int d = Dims;
            return Tilted ? new Vector3Int(d.x, d.z, d.y) : d;
        }
    }

    /// <summary>Where a board square is drawn, in cells (x across, y up, z into the screen)</summary>
    public static Vector3Int ToView(BoardPosition p)
    {
        if (!Tilted) return new Vector3Int(p.x, p.y, p.z);
        return new Vector3Int(p.x, Dims.z - 1 - p.z, p.y);
    }

    /// <summary>The board square drawn at a cell</summary>
    public static BoardPosition FromView(Vector3Int v)
    {
        if (!Tilted) return new BoardPosition(v.x, v.y, v.z);
        return new BoardPosition(v.x, v.z, Dims.z - 1 - v.y);
    }

    /// <summary>A board direction (e.g. a pawn's forward) as drawn, in the Pieces Container</summary>
    public static Vector3 Dir(Vector3 board) => Tilted ? new Vector3(board.x, -board.z, board.y) : board;

    /// <summary>Which drawn floor (counted from the bottom) a square is on, for Floors and sounds</summary>
    public static int Level(BoardPosition p) => ToView(p).y;

    /// <summary>How many drawn floors there are</summary>
    public static int Levels => ViewDims.y;

    /// <summary>The board direction from the cube's centre toward a player's starting face</summary>
    public static Vector3 HomeSide(PieceColor color)
    {
        switch (color)
        {
            case PieceColor.White: return Vector3.left;     // starts at x = 0, advances +x
            case PieceColor.Black: return Vector3.right;    // starts at x = max
            case PieceColor.Green: return Vector3.back;     // starts at z = 0
            case PieceColor.Purple: return Vector3.forward; // starts at z = max
            case PieceColor.Yellow: return Vector3.down;    // starts at y = 0
            default: return Vector3.up;                     // Orange starts at y = max
        }
    }

    /// <summary>A player's starting face as drawn, in the Pieces Container (a unit axis)</summary>
    public static Vector3 HomeSideDrawn(PieceColor color) => Dir(HomeSide(color));
}

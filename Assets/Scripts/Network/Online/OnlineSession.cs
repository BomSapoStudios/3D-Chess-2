using System.Collections.Generic;

/// <summary>
/// Whether the current game is an online game, which side this device plays, and
/// whether a change being applied came from another player (so it isn't sent back).
/// Game code checks these to block input for remote sides and to report local actions.
/// The host's device also plays the game's robot seats and reports their moves.
/// </summary>
public static class OnlineSession
{
    public static bool IsActive { get; private set; }
    public static PieceColor LocalColor { get; private set; }
    public static bool IsHost { get; private set; }

    private static readonly HashSet<PieceColor> robots = new HashSet<PieceColor>();

    /// <summary>True while applying a move/placement received from the server</summary>
    public static bool ApplyingRemote { get; set; }

    /// <summary>
    /// True while a resumed game is being rebuilt from the server's history. Every side is
    /// treated as remote (nothing is sent, nobody can touch pieces, robots wait), moves are
    /// instant and effects are skipped.
    /// </summary>
    public static bool Replaying { get; set; }

    public static void Begin(PieceColor localColor, bool isHost = false, IEnumerable<PieceColor> robotColors = null)
    {
        IsActive = true;
        LocalColor = localColor;
        IsHost = isHost;
        robots.Clear();
        if (robotColors != null) robots.UnionWith(robotColors);
        ApplyingRemote = false;
    }

    public static void End()
    {
        IsActive = false;
        ApplyingRemote = false;
        Replaying = false;
        robots.Clear();
    }

    public static bool IsRobot(PieceColor color) => robots.Contains(color);

    /// <summary>Played on this device: our own side, plus robot seats when we're the host</summary>
    public static bool IsLocal(PieceColor color) => !Replaying && (color == LocalColor || (IsHost && robots.Contains(color)));

    /// <summary>Online: only sides played on this device may change locally</summary>
    public static bool CanControl(PieceColor color) => !IsActive || ApplyingRemote || IsLocal(color);

    /// <summary>Report this local action to the server?</summary>
    public static bool ShouldSend(PieceColor color) =>
        IsActive && !ApplyingRemote && IsLocal(color) && OnlineClient.Instance != null;
}

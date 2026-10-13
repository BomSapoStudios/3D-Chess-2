using UnityEngine;

/// <summary>Wins, losses and draws against the computer at each difficulty, kept on this phone</summary>
public static class ComputerStats
{
    /// <param name="outcome">1 win, 0 draw, -1 loss</param>
    public static void Record(AIDifficulty difficulty, int outcome)
    {
        string key = $"VsComputer.{difficulty}.{(outcome > 0 ? "Wins" : outcome < 0 ? "Losses" : "Draws")}";
        PlayerPrefs.SetInt(key, PlayerPrefs.GetInt(key, 0) + 1);
        PlayerPrefs.Save();
    }

    public static (int wins, int losses, int draws) For(AIDifficulty difficulty) =>
        (PlayerPrefs.GetInt($"VsComputer.{difficulty}.Wins", 0), PlayerPrefs.GetInt($"VsComputer.{difficulty}.Losses", 0), PlayerPrefs.GetInt($"VsComputer.{difficulty}.Draws", 0));
}

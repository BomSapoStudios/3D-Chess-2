using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The home screen's daily bonus: 50 pawns once per UTC day (the server decides, Wallet.ClaimDaily). A badge
/// beside the tagline is lit while it's waiting and counts down to the next one otherwise; it only
/// talks to the server when tapped.
/// </summary>
public partial class NeonMenu
{
    private Button dailyButton;
    private Image dailyFace;
    private TextMeshProUGUI dailyLabel;
    private float nextDailyRefresh;
    private bool claiming;

    /// <summary>The badge sits beside the tagline, between the title and the mode buttons, wherever the column lays them out</summary>
    private void BuildDailyClaim(RectTransform tagline)
    {
        dailyButton = NeonButton(tagline, "Claim 50 pawns", new Vector2(176, 40), NeonTheme.Yellow, NeonTheme.Pink, 1.5f, 15, ClaimDaily);
        Place((RectTransform)dailyButton.transform, new Vector2(0, 0.5f), new Vector2(176, 40), new Vector2(240 + 88, 0));
        dailyFace = dailyButton.GetComponent<Image>();
        dailyLabel = dailyButton.GetComponentInChildren<TextMeshProUGUI>();
        Wallet.Changed += RefreshDailyClaim;
        RefreshDailyClaim();
    }

    /// <summary>Once a second while the menu shows: the countdown (worked out here, no requests)</summary>
    private void UpdateDailyClaim()
    {
        if (dailyButton == null || !menuScreen.activeInHierarchy || Time.unscaledTime < nextDailyRefresh) return;
        nextDailyRefresh = Time.unscaledTime + 1f;
        RefreshDailyClaim();
    }

    private void RefreshDailyClaim()
    {
        if (dailyButton == null) return;
        bool available = Wallet.DailyAvailable;
        dailyButton.interactable = available && !claiming;
        dailyFace.color = available ? NeonTheme.Yellow : NeonTheme.Muted;
        if (available) dailyLabel.text = "Claim 50 pawns";
        else
        {
            System.TimeSpan left = Wallet.UntilDaily;
            dailyLabel.text = left.TotalHours >= 1 ? $"Next bonus in {(int)left.TotalHours}h {left.Minutes}m" : $"Next bonus in {left.Minutes}m {left.Seconds}s";
        }
    }

    private void ClaimDaily()
    {
        if (claiming || !Wallet.DailyAvailable) return;
        Haptics.Selection(); Sfx.Tap();
        claiming = true;
        RefreshDailyClaim();
        Wallet.ClaimDaily((pawns, message) =>
        {
            claiming = false;
            RefreshDailyClaim();
            if (pawns > 0)
            {
                Sfx.Play("purchase");
                ShowToast($"+{pawns} pawns!");
                StartCoroutine(PopDaily());
            }
            else if (message != null) ShowToast(message);
        });
    }

    /// <summary>A little pop on the badge when the pawns land</summary>
    private IEnumerator PopDaily()
    {
        Transform t = dailyButton.transform;
        for (float s = 0f; s < 1f; s += Time.unscaledDeltaTime / 0.35f)
        {
            t.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(s * Mathf.PI));
            yield return null;
        }
        t.localScale = Vector3.one;
    }
}

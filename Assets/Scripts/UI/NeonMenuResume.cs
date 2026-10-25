using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Resume or Start new: shown when a mode with a saved game (SavedGames) is tapped.
/// </summary>
public partial class NeonMenu
{
    private GameObject resumePicker;
    private TextMeshProUGUI resumeDetail;
    private string resumeSlot;
    private Action resumeStartNew;

    /// <summary>Tapping a mode: offer the saved game first, if there is one</summary>
    private Action OrResume(string slot, Action startNew) => () =>
    {
        SavedGames.Save save = SavedGames.Load(slot);
        if (save == null) { startNew(); return; }
        if (resumePicker == null) BuildResumePicker();
        resumeSlot = slot;
        resumeStartNew = startNew;
        resumeDetail.text = SavedGames.Describe(save);
        resumePicker.transform.SetAsLastSibling();
        resumePicker.SetActive(true);
    };

#if DEVELOPMENT_BUILD || UNITY_EDITOR
    /// <summary>ResumeDemo: show the card as if the mode had been tapped</summary>
    public void DebugOfferResume(string slot) => OrResume(slot, () => Debug.Log("ResumeDemo: Start new"))();
#endif

    private void BuildResumePicker()
    {
        // Dim the menu; tapping outside the card closes it
        Image dim = NewImage("Resume Picker", transform, new Color(0f, 0f, 0f, 0.65f));
        Stretch(dim.rectTransform);
        Button closeArea = dim.gameObject.AddComponent<Button>();
        closeArea.transition = Selectable.Transition.None;
        closeArea.onClick.AddListener(() => resumePicker.SetActive(false));
        resumePicker = dim.gameObject;

        Image card = NewImage("Card", dim.transform, NeonTheme.Ground);
        card.sprite = NeonTheme.Rounded;
        card.type = Image.Type.Sliced;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520, 230), Vector2.zero);
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // taps on the card don't close it
        Image border = NewImage("Border", card.transform, NeonTheme.Lime);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        TextMeshProUGUI title = NewText("Title", card.transform, "GAME IN PROGRESS", 30, NeonTheme.Lime, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(title, display: true);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(480, 50), new Vector2(0, -42));
        resumeDetail = NewText("Detail", card.transform, "", 15, NeonTheme.Lavender, TextAlignmentOptions.Center);
        resumeDetail.fontStyle = FontStyles.Normal;
        Place(resumeDetail.rectTransform, new Vector2(0.5f, 1f), new Vector2(480, 24), new Vector2(0, -78));

        Button resume = NeonButton(card.transform, "Resume", new Vector2(190, 64), NeonTheme.Lime, NeonTheme.Pink, -1f, 22, () =>
        {
            Haptics.Selection();
            resumePicker.SetActive(false);
            if (!SavedGames.Resume(resumeSlot)) ShowToast("Couldn't open the saved game");
        });
        Place((RectTransform)resume.transform, new Vector2(0.5f, 0f), new Vector2(190, 64), new Vector2(-105, 62));
        Button fresh = NeonButton(card.transform, "Start new", new Vector2(190, 64), NeonTheme.Pink, NeonTheme.Cyan, 1f, 22, () =>
        {
            Haptics.Selection();
            resumePicker.SetActive(false);
            resumeStartNew?.Invoke(); // the save goes once the new game starts
        });
        Place((RectTransform)fresh.transform, new Vector2(0.5f, 0f), new Vector2(190, 64), new Vector2(105, 62));

        FitCardForTextSize(card.rectTransform);
        resumePicker.SetActive(false);
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Time to update!": shown when the server says this build is too old to play online (see
/// OnlineClient.ClientVersion), and again on Online Game until the app is updated. Everything
/// that plays on the phone keeps working.
/// </summary>
public partial class NeonMenu
{
    // The App Store's id for the app (App Store Connect › App Information › Apple ID). While it's
    // empty the app is only on TestFlight, so Update opens TestFlight.
    private const string AppStoreId = "";

    private GameObject updatePicker;

    private void ShowUpdatePicker()
    {
        if (this == null) return; // the menu went away (a game is loading)
        // Nothing online works on this build, so don't leave the player on an online screen behind the card
        if (onlineScreen.activeSelf || friendsScreen.activeSelf || (statsScreen != null && statsScreen.activeSelf)) ShowMenu();
        if (updatePicker == null) BuildUpdatePicker();
        updatePicker.transform.SetAsLastSibling();
        updatePicker.SetActive(true);
    }

    private void BuildUpdatePicker()
    {
        // Dim the menu; tapping outside the card closes it
        Image dim = NewImage("Update Picker", transform, new Color(0f, 0f, 0f, 0.65f));
        Stretch(dim.rectTransform);
        Button closeArea = dim.gameObject.AddComponent<Button>();
        closeArea.transition = Selectable.Transition.None;
        closeArea.onClick.AddListener(() => updatePicker.SetActive(false));
        updatePicker = dim.gameObject;

        Image card = NewImage("Card", dim.transform, NeonTheme.Ground);
        card.sprite = NeonTheme.Rounded;
        card.type = Image.Type.Sliced;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(560, 240), Vector2.zero);
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // taps on the card don't close it
        Image border = NewImage("Border", card.transform, NeonTheme.Lime);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        TextMeshProUGUI title = NewText("Title", card.transform, "TIME TO UPDATE!", 28, NeonTheme.Lime, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(title, display: true);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(520, 44), new Vector2(0, -36));

        TextMeshProUGUI body = NewText("Body", card.transform,
            "A new version of 3D Chess Ultra is out. Update to keep playing online.\nEverything else keeps working on this version.",
            16, NeonTheme.Lavender, TextAlignmentOptions.Top);
        body.fontStyle = FontStyles.Normal;
        body.enableWordWrapping = true;
        body.enableAutoSizing = true;
        body.fontSizeMin = 11;
        body.fontSizeMax = 16;
        Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(510, 70), new Vector2(0, -102));

        Button later = NeonButton(card.transform, "Later", new Vector2(180, 56), NeonTheme.Cyan, NeonTheme.Pink, -1f, 20, () => updatePicker.SetActive(false));
        Place((RectTransform)later.transform, new Vector2(0.5f, 0f), new Vector2(180, 56), new Vector2(-105, 46));
        Button update = NeonButton(card.transform, "Update", new Vector2(180, 56), NeonTheme.Lime, NeonTheme.Pink, 1f, 20, OpenStoreForUpdate);
        Place((RectTransform)update.transform, new Vector2(0.5f, 0f), new Vector2(180, 56), new Vector2(105, 46));

        FitCardForTextSize(card.rectTransform);
        updatePicker.SetActive(false);
    }

    private static void OpenStoreForUpdate()
    {
        Haptics.Selection();
        Application.OpenURL(string.IsNullOrEmpty(AppStoreId) ? "itms-beta://" : $"itms-apps://apps.apple.com/app/id{AppStoreId}");
    }
}

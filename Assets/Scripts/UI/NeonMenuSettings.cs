using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Settings screen: your online account (Sign in with Apple, Restore purchases, Delete account)
/// on the left; Notifications, Accessibility, help and the privacy policy on the right, with the
/// app's version underneath.
/// </summary>
public partial class NeonMenu
{
    private const string PrivacyUrl = "https://apps.bomsapostudios.com/3dchess/privacy.html";
    private const string SupportUrl = "https://apps.bomsapostudios.com/3dchess/support.html";
    private const string SupportEmail = "support@bomsapostudios.com";

    private GameObject settingsScreen;
    private RectTransform settingsLeft, settingsRight;
    private TextMeshProUGUI settingsAccountNote, settingsAppleLabel;
    private Button settingsAppleButton;
    private GameObject deletePicker;
    private TextMeshProUGUI deleteBody;
    private bool deleting;

    private void BuildSettingsScreen()
    {
        settingsScreen = NewRect("Settings Screen", safeRoot).gameObject;
        RectTransform root = settingsScreen.GetComponent<RectTransform>();
        Stretch(root);

        RectTransform header = NewRect("Header", root);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.offsetMin = new Vector2(16, -64);
        header.offsetMax = new Vector2(-24, -12);

        Button back = NeonButton(header, "<", new Vector2(48, 48), NeonTheme.Cyan, NeonTheme.Pink, 0f, 24, ShowMenu);
        Place((RectTransform)back.transform, new Vector2(0, 0.5f), new Vector2(48, 48), new Vector2(28, 0));

        TextMeshProUGUI title = NewText("Title", header, "SETTINGS", 30, NeonTheme.Lime, TextAlignmentOptions.Left);
        NeonTheme.ApplyFont(title, display: true);
        title.rectTransform.anchorMin = new Vector2(0, 0);
        title.rectTransform.anchorMax = new Vector2(1, 1);
        title.rectTransform.offsetMin = new Vector2(70, 0);
        title.rectTransform.offsetMax = new Vector2(-180, 0);
        title.rectTransform.localEulerAngles = new Vector3(0, 0, -1);

        Button done = NeonButton(header, "Done!", new Vector2(150, 50), NeonTheme.Pink, NeonTheme.Cyan, 2f, 20, ShowMenu);
        Place((RectTransform)done.transform, new Vector2(1, 0.5f), new Vector2(150, 50), new Vector2(-75, 0));

        settingsLeft = Card(root, "Account Card", NeonTheme.Cyan, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(16, 14), new Vector2(-8, -74));
        settingsRight = Card(root, "Options Card", NeonTheme.Pink, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(8, 14), new Vector2(-24, -74));

        // Left: your account
        SectionLabel(settingsLeft, "Your account", NeonTheme.Cyan);
        settingsAccountNote = NewText("Account", settingsLeft, "", 14, NeonTheme.Lavender, TextAlignmentOptions.TopLeft);
        settingsAccountNote.fontStyle = FontStyles.Normal;
        settingsAccountNote.enableWordWrapping = true;
        settingsAccountNote.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
        settingsAppleButton = NeonButton(Row(settingsLeft), "Sign in with Apple", new Vector2(0, 44), NeonTheme.White, NeonTheme.Cyan, 0f, 16, SettingsSignInWithApple);
        settingsAppleLabel = settingsAppleButton.GetComponentInChildren<TextMeshProUGUI>();
        NeonButton(Row(settingsLeft), "Restore purchases", new Vector2(0, 44), NeonTheme.Lavender, NeonTheme.Pink, 0f, 16, RestorePurchases);
        NeonButton(Row(settingsLeft), "Delete account", new Vector2(0, 44), NeonTheme.Pink, NeonTheme.Cyan, 0f, 16, ShowDeletePicker);

        // Right: the other settings screens, then help
        SectionLabel(settingsRight, "Settings", NeonTheme.PinkSoft);
        RectTransform screens = Row(settingsRight);
        if (PushNotifications.Supported) NeonButton(screens, "Notifications", new Vector2(0, 44), NeonTheme.Lavender, NeonTheme.Pink, 0f, 15, ShowNotify);
        NeonButton(screens, "Accessibility", new Vector2(0, 44), NeonTheme.PinkSoft, NeonTheme.Cyan, 0f, 15, ShowAccess);

        SectionLabel(settingsRight, "Help", NeonTheme.PinkSoft);
        RectTransform help = Row(settingsRight);
        NeonButton(help, "Support", new Vector2(0, 44), NeonTheme.Yellow, NeonTheme.Pink, 0f, 15, () => Application.OpenURL(SupportUrl));
        NeonButton(help, "Email us", new Vector2(0, 44), NeonTheme.Yellow, NeonTheme.Pink, 0f, 15, EmailSupport);
        NeonButton(Row(settingsRight), "Privacy Policy", new Vector2(0, 44), NeonTheme.Cyan, NeonTheme.Pink, 0f, 15, () => Application.OpenURL(PrivacyUrl));

        TextMeshProUGUI version = NewText("Version", settingsRight, $"3D Chess Ultra {Application.version} · {SupportEmail}", 13, NeonTheme.Muted, TextAlignmentOptions.Left);
        version.fontStyle = FontStyles.Normal;
        version.enableAutoSizing = true;
        version.fontSizeMin = 10;
        version.fontSizeMax = 13;
        version.gameObject.AddComponent<LayoutElement>().preferredHeight = 22;
    }

    private void ShowSettings()
    {
        if (statsScreen != null) statsScreen.SetActive(false);
        if (notifyScreen != null) notifyScreen.SetActive(false);
        if (accessScreen != null) accessScreen.SetActive(false);
        menuScreen.SetActive(false);
        setupScreen.SetActive(false);
        customizeScreen.SetActive(false);
        onlineScreen.SetActive(false);
        friendsScreen.SetActive(false);
        settingsScreen.SetActive(true);
        RefreshSettings();
    }

    private void RefreshSettings()
    {
        if (settingsAccountNote == null) return;
        var client = OnlineClient.Instance;
        bool hasAccount = client != null ? client.HasToken : !string.IsNullOrEmpty(PlayerPrefs.GetString("OnlineToken", ""));
        bool apple = client != null && client.SignedInWithApple;
        settingsAccountNote.text = !hasAccount
            ? "You get an online name the first time you play online. Nothing is saved about you until then."
            : apple
                ? $"You're {CurrentName()}. Your name, friends and ratings are saved to your Apple ID."
                : $"You're {CurrentName()}. Sign in with Apple to keep your name, friends and ratings on a new iPhone.";
        settingsAppleLabel.text = apple ? "Signed in with Apple" : "Sign in with Apple";
        settingsAppleButton.interactable = !apple && AppleSignIn.IsAvailable;
    }

    private void SettingsSignInWithApple()
    {
        AppleSignIn.Start((token, error) =>
        {
            if (token == null)
            {
                if (error != "cancelled") ShowToast(error);
                return;
            }
            StartCoroutine(SettingsLinkApple(token));
        });
    }

    private IEnumerator SettingsLinkApple(string identityToken)
    {
        var client = OnlineClient.GetOrCreate();
        string signInError = null;
        yield return client.EnsureSignedIn((ok, error) => signInError = ok ? null : error);
        if (signInError != null) { ShowToast(signInError); yield break; }
        yield return client.LinkApple(identityToken, (restored, error) =>
        {
            if (error != null) ShowToast(error);
            else ShowToast(restored ? $"Welcome back, {CurrentName()}!" : "Signed in with Apple. Your name and friends are saved.");
        });
        ResetFriendsCache();
        RefreshSettings();
    }

    private void EmailSupport()
    {
        string subject = UnityEngine.Networking.UnityWebRequest.EscapeURL($"3D Chess Ultra {Application.version}").Replace("+", "%20");
        Application.OpenURL($"mailto:{SupportEmail}?subject={subject}");
    }

    // ── Delete account ──

    private void ShowDeletePicker()
    {
        Haptics.Selection();
        if (deletePicker == null) BuildDeletePicker();
        bool hasAccount = OnlineClient.GetOrCreate().HasToken;
        if (!hasAccount)
        {
            ShowToast("There's no online account on this iPhone, so there's nothing to delete.");
            return;
        }
        deleteBody.text = $"This deletes {CurrentName()} for good: your name, friends, ratings, stats and online games. " +
                          "Online games you're still in count as a loss.\n\n" +
                          "Your settings, puzzles, pawns and purchases on this iPhone stay. If you play online again, you'll start fresh with a new name.";
        deletePicker.transform.SetAsLastSibling();
        deletePicker.SetActive(true);
    }

    private void BuildDeletePicker()
    {
        // Dim the menu; tapping outside the card closes it
        Image dim = NewImage("Delete Picker", transform, new Color(0f, 0f, 0f, 0.65f));
        Stretch(dim.rectTransform);
        Button closeArea = dim.gameObject.AddComponent<Button>();
        closeArea.transition = Selectable.Transition.None;
        closeArea.onClick.AddListener(() => { if (!deleting) deletePicker.SetActive(false); });
        deletePicker = dim.gameObject;

        Image card = NewImage("Card", dim.transform, NeonTheme.Ground);
        card.sprite = NeonTheme.Rounded;
        card.type = Image.Type.Sliced;
        Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(580, 290), Vector2.zero);
        card.gameObject.AddComponent<Button>().transition = Selectable.Transition.None; // taps on the card don't close it
        Image border = NewImage("Border", card.transform, NeonTheme.Pink);
        border.sprite = NeonTheme.RoundedOutline;
        border.type = Image.Type.Sliced;
        border.raycastTarget = false;
        Stretch(border.rectTransform);

        TextMeshProUGUI title = NewText("Title", card.transform, "DELETE ACCOUNT?", 28, NeonTheme.Pink, TextAlignmentOptions.Center);
        NeonTheme.ApplyFont(title, display: true);
        Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(540, 44), new Vector2(0, -36));

        deleteBody = NewText("Body", card.transform, "", 15, NeonTheme.Lavender, TextAlignmentOptions.Top);
        deleteBody.fontStyle = FontStyles.Normal;
        deleteBody.enableWordWrapping = true;
        deleteBody.enableAutoSizing = true;
        deleteBody.fontSizeMin = 11;
        deleteBody.fontSizeMax = 15;
        Place(deleteBody.rectTransform, new Vector2(0.5f, 1f), new Vector2(530, 136), new Vector2(0, -132));

        Button keep = NeonButton(card.transform, "Keep it", new Vector2(200, 56), NeonTheme.Cyan, NeonTheme.Pink, -1f, 20, () => deletePicker.SetActive(false));
        Place((RectTransform)keep.transform, new Vector2(0.5f, 0f), new Vector2(200, 56), new Vector2(-115, 48));
        Button delete = NeonButton(card.transform, "Delete forever", new Vector2(200, 56), NeonTheme.Pink, NeonTheme.Cyan, 1f, 20, ConfirmDelete);
        Place((RectTransform)delete.transform, new Vector2(0.5f, 0f), new Vector2(200, 56), new Vector2(115, 48));

        FitCardForTextSize(card.rectTransform);
        deletePicker.SetActive(false);
    }

    private void ConfirmDelete()
    {
        if (deleting) return;
        Haptics.Selection();
        var client = OnlineClient.GetOrCreate();
        if (client.SignedInWithApple && AppleSignIn.IsAvailable)
        {
            // Apple asks apps to cut the Sign in with Apple link too: one more sign-in gives the server a code to do that
            AppleSignIn.Start((token, error) =>
            {
                if (token == null && error == "cancelled") { ShowToast("Your account wasn't deleted."); return; }
                StartCoroutine(DeleteRoutine(token != null ? AppleSignIn.LastAuthorizationCode : null));
            });
        }
        else StartCoroutine(DeleteRoutine(null));
    }

    private IEnumerator DeleteRoutine(string appleCode)
    {
        deleting = true;
        ShowToast("Deleting your account...");
        string error = null;
        yield return OnlineClient.GetOrCreate().DeleteAccount(appleCode, e => error = e);
        deleting = false;
        if (error != null) { ShowToast("Couldn't delete your account: " + error); yield break; }
        deletePicker.SetActive(false);
        myGames = new GameInfo[0];
        if (onlineMenuLabel != null) onlineMenuLabel.text = "Online Game";
        RebuildGamesList();
        ResetFriendsCache();
        RefreshSettings();
        ShowToast("Your account is deleted.");
    }
}

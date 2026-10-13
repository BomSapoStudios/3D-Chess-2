using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Push notifications on iPhone (Plugins/iOS/Push.mm): asks for permission, hands this
/// device's token to the server once the player is signed in, and reports which
/// notification was tapped so the menu can open that game. Does nothing elsewhere.
/// </summary>
public class PushNotifications : MonoBehaviour
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void _PushRequest();
    [DllImport("__Internal")] private static extern void _PushRefreshStatus();
    [DllImport("__Internal")] private static extern string _PushStatus();
    [DllImport("__Internal")] private static extern string _PushToken();
    [DllImport("__Internal")] private static extern bool _PushIsSandbox();
    [DllImport("__Internal")] private static extern string _PushTakeTappedRoom();
    [DllImport("__Internal")] private static extern void _PushOpenSettings();
    public static bool Supported => true;
#else
    private static void _PushRequest() { }
    private static void _PushRefreshStatus() { }
    private static string _PushStatus() => "unknown";
    private static string _PushToken() => "";
    private static bool _PushIsSandbox() => false;
    private static string _PushTakeTappedRoom() => null;
    private static void _PushOpenSettings() { }
    public static bool Supported => false;
#endif

    /// <summary>authorized | denied | notDetermined | unknown (not checked yet, or not an iPhone)</summary>
    public static string Status => _PushStatus();

    /// <summary>Show iOS's permission prompt if the player hasn't answered it yet</summary>
    public static void RequestPermission()
    {
        if (Supported && Status != "denied") _PushRequest();
    }

    /// <summary>Check again whether notifications are allowed (they may have changed it in Settings)</summary>
    public static void RefreshStatus() => _PushRefreshStatus();

    /// <summary>This app's notification settings in the Settings app</summary>
    public static void OpenSettings() => _PushOpenSettings();

    /// <summary>The last tapped notification as (kind, roomCode), or null. Taking it clears it.</summary>
    public static (string kind, string room)? TakeTap()
    {
        string tap = _PushTakeTappedRoom();
        if (tap == null) return null;
        int bar = tap.IndexOf('|');
        return bar < 0 ? (tap, "") : (tap.Substring(0, bar), tap.Substring(bar + 1));
    }

    private string registered; // token + account last sent to the server this launch
    private bool registering;
    private float nextCheck;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (!Supported) return;
        var go = new GameObject("Push Notifications");
        go.AddComponent<PushNotifications>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck || registering) return;
        nextCheck = Time.unscaledTime + 1f;

        // Once iOS has given out a token and the player has an account, tell the server where to reach them
        string token = _PushToken();
        OnlineClient client = OnlineClient.Instance;
        if (string.IsNullOrEmpty(token) || client == null || !client.HasToken) return;
        string key = token + "|" + PlayerPrefs.GetString("OnlineUserId", "") + "|" + client.ServerUrl;
        if (key == registered) return;
        registering = true;
        StartCoroutine(client.RegisterPush(token, _PushIsSandbox(), error =>
        {
            registering = false;
            if (error == null) registered = key;
            else nextCheck = Time.unscaledTime + 30f; // try again in a while
        }));
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) RefreshStatus();
    }
}

/// <summary>Which notifications this player wants (kept on the server, so they follow the account)</summary>
[Serializable]
public class PushPrefs
{
    public bool all = true;
    public bool turn = true;
    public bool joined = true;
    public bool started = true;
    public bool challenge = true;
    public bool friend = true;
}

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bom Sapo Studios card shown once when the app opens: the frog logo on black, the studio name and a
/// copyright line. It waits for Unity's own splash to finish, fades in, holds, then fades away to the
/// menu underneath. A tap can cut the hold short, but only after the card has been fully up for MinHold.
/// </summary>
public class StudioSplash : MonoBehaviour
{
    private const float FadeIn = 0.8f;
    private const float Hold = 2f;
    private const float MinHold = 1.2f;
    private const float FadeOut = 0.8f;

    private Canvas canvas;
    private CanvasGroup group;
    private bool skipped;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        GameObject go = new GameObject("Studio Splash");
        go.AddComponent<StudioSplash>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000; // above every menu and game canvas
        CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(844, 390);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        gameObject.AddComponent<GraphicRaycaster>();
        group = gameObject.AddComponent<CanvasGroup>();

        // Solid black behind everything; it also catches taps so they don't reach the menu
        Image bg = NewRect("Background", transform).gameObject.AddComponent<Image>();
        bg.color = Color.black;
        Stretch(bg.rectTransform);

        // Content sits in a group that fades in on its own, so the black stays up from the first frame
        RectTransform content = NewRect("Content", transform);
        Stretch(content);
        CanvasGroup contentGroup = content.gameObject.AddComponent<CanvasGroup>();
        contentGroup.alpha = 0f;

        Texture2D logoTex = Resources.Load<Texture2D>("Splash/BomSapoLogo");
        if (logoTex != null)
        {
            RawImage logo = NewRect("Logo", content).gameObject.AddComponent<RawImage>();
            logo.texture = logoTex;
            logo.raycastTarget = false;
            Place(logo.rectTransform, new Vector2(0, 52), new Vector2(190, 190));
        }

        TextMeshProUGUI name = NewText("Name", content, "BOM SAPO", 30, Color.white, true);
        name.characterSpacing = 18;
        Place(name.rectTransform, new Vector2(0, -68), new Vector2(600, 40));

        TextMeshProUGUI studios = NewText("Studios", content, "STUDIOS", 13, NeonTheme.Hex("7CD992"), false);
        studios.characterSpacing = 40;
        Place(studios.rectTransform, new Vector2(0, -96), new Vector2(600, 22));

        TextMeshProUGUI copyright = NewText("Copyright", content, "© 2026 Bom Sapo Studios. All rights reserved.", 11, new Color(1, 1, 1, 0.55f), false);
        RectTransform cr = copyright.rectTransform;
        cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 0f);
        cr.pivot = new Vector2(0.5f, 0f);
        cr.sizeDelta = new Vector2(600, 18);
        cr.anchoredPosition = new Vector2(0, 14);

        StartCoroutine(Run(contentGroup));
    }

    private IEnumerator Run(CanvasGroup contentGroup)
    {
        // Scripts start while Unity's "Made with Unity" splash is still drawing over everything, so the
        // card would otherwise play out unseen underneath it
        while (!UnityEngine.Rendering.SplashScreen.isFinished) yield return null;
        yield return null;
        yield return null;

        // Fade the logo and text in, then hold (a tap only shortens the hold once MinHold has passed)
        for (float t = 0; t < FadeIn; t += Step)
        {
            contentGroup.alpha = t / FadeIn;
            yield return null;
        }
        contentGroup.alpha = 1f;
        skipped = false;
        for (float t = 0; t < Hold && !(skipped && t >= MinHold); t += Step) yield return null;

        // The whole card fades away to the menu; taps stay blocked until it's gone
        for (float t = 0; t < FadeOut; t += Step)
        {
            group.alpha = 1f - t / FadeOut;
            yield return null;
        }
        Destroy(gameObject);
        Resources.UnloadUnusedAssets();
    }

    // The menu builds itself over the first frames after launch, and a single one of those can take
    // longer than the whole card; capping the step means every fade is actually drawn, frame by frame
    private static float Step => Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);

    private void Update()
    {
        // NeonMenu turns off every other root canvas when it builds (HideOldMenu); keep this one showing
        if (!canvas.enabled) canvas.enabled = true;
        ReturnOldMenu();
        if (Input.GetMouseButtonDown(0) || (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)) skipped = true;
    }

    // MainMenuController builds the old menu under FindFirstObjectByType<Canvas>(), which can be this one.
    // Move it to the scene's own "Canvas" (which NeonMenu hides), or it would show through the splash
    private void ReturnOldMenu()
    {
        if (transform.childCount <= 2) return;
        GameObject sceneCanvas = GameObject.Find("Canvas");
        if (sceneCanvas == null || sceneCanvas.GetComponent<Canvas>() == null)
        {
            sceneCanvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            sceneCanvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            sceneCanvas.GetComponent<Canvas>().enabled = false;
        }
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != "Background" && child.name != "Content") child.SetParent(sceneCanvas.transform, false);
        }
    }

    private static RectTransform NewRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, bool display)
    {
        TextMeshProUGUI tmp = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        NeonTheme.ApplyFont(tmp, display);
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        return tmp;
    }
}

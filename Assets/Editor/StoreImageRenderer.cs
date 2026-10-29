using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tools > Store Images > Render All: makes the App Store Connect in-app purchase pictures for
/// every locked Customize item (piece sets, backdrops, promotions) into ~/Desktop/store-images.
/// For each item: a 1024x1024 promotional image (no text) and a 2796x1290 review screenshot of
/// the Customize screen previewing it. Files are named by product ID
/// ("BomSapo.Chess3D." + Store.Id(item), lowercased). Runs the game in Play mode from MainMenu,
/// drives the menu through reflection, then starts a Quick Play game for the board shots.
/// New items are picked up from the enums, so re-run it whenever items are added.
/// Command line (editor closed): Unity -projectPath . -executeMethod StoreImageRenderer.RunFromCommandLine
/// </summary>
public static class StoreImageRenderer
{
    private const string RunningKey = "StoreImageRenderer.Running";
    private const string QuitKey = "StoreImageRenderer.Quit";
    private const string PrefsKey = "StoreImageRenderer.SavedPrefs";
    private const string CandidatesKey = "StoreImageRenderer.Candidates";
    private const string NewOnlyKey = "StoreImageRenderer.NewOnly";
    private static bool timingCandidates;
    private const int PromoSize = 1024;
    private const int ReviewWidth = 2796, ReviewHeight = 1290;

    // The PlayerPrefs a run changes, put back afterwards
    private static readonly string[] TouchedPrefs = { "PieceShapes", "Backdrop", "PromotionStyle", "PieceSet", "DebugOpenStats", "DebugCapture", "DebugRematchDemo", "DebugAISelfTest", "DebugEffectsDemo" };

    /// <summary>Seconds into each promotion animation where it looks most like itself</summary>
    private static float MidPoint(PromotionStyle.Kind kind)
    {
        switch (kind)
        {
            case PromotionStyle.Kind.GlitchRebuild: return 0.6f;   // the cloud of cubes
            case PromotionStyle.Kind.SpinMorph: return 0.7f;       // column of light and its orb
            case PromotionStyle.Kind.Coronation: return 1.1f;      // the shockwave
            case PromotionStyle.Kind.BlackHole: return 1.3f;       // the black ball becoming the queen
            case PromotionStyle.Kind.Splat: return 1.7f;           // the paint splash
            case PromotionStyle.Kind.GraveRise: return 1.75f;      // the piece climbing out of the mound
            default: return 0.75f;
        }
    }

    public static string OutputFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Desktop", "store-images");

    public static string ProductId(string storeId) => Store.ProductId(storeId);

    /// <summary>Tools > Store Images > New Items Only: only items without images yet (new backdrops, sets, promotions)</summary>
    [MenuItem("Tools/Store Images/New Items Only")]
    public static void RenderNew()
    {
        SessionState.SetBool(NewOnlyKey, true);
        RenderAll(keepNewOnly: true);
    }

    /// <summary>Command line: Unity -projectPath . -executeMethod StoreImageRenderer.RunNewFromCommandLine</summary>
    public static void RunNewFromCommandLine()
    {
        SessionState.SetBool(QuitKey, true);
        RenderNew();
    }

    [MenuItem("Tools/Store Images/Render All")]
    public static void RenderAllMenu() => RenderAll();

    public static void RenderAll(bool keepNewOnly = false)
    {
        if (!keepNewOnly) SessionState.SetBool(NewOnlyKey, false);
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) { Debug.LogWarning("STORE IMAGES: stop Play mode (and wait for compiling) first"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // Save the prefs a run touches; locked items show as locked (with their unlock buttons)
        var saved = new List<string>();
        foreach (string key in TouchedPrefs)
            if (PlayerPrefs.HasKey(key)) saved.Add(key + "=" + PlayerPrefs.GetInt(key));
        SessionState.SetString(PrefsKey, string.Join(";", saved));
        foreach (string key in TouchedPrefs) PlayerPrefs.DeleteKey(key);
        PlayerPrefs.Save();

        Directory.CreateDirectory(OutputFolder);
        SetGameViewSize(ReviewWidth, ReviewHeight);
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
        SessionState.SetBool(RunningKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>
    /// Tools > Store Images > Promotion Timing Candidates: only the promotion pictures, each at
    /// several moments (named _t0.3 and so on), to pick MidPoint from when animations change
    /// </summary>
    [MenuItem("Tools/Store Images/Promotion Timing Candidates")]
    public static void RenderPromotionCandidates()
    {
        SessionState.SetBool(CandidatesKey, true);
        RenderAll();
    }

    public static void RunCandidatesFromCommandLine()
    {
        SessionState.SetBool(QuitKey, true);
        RenderPromotionCandidates();
    }

    public static void RunFromCommandLine()
    {
        SessionState.SetBool(QuitKey, true);
        RenderAll();
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.playModeStateChanged -= OnPlayMode;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }

    private static void OnPlayMode(PlayModeStateChange change)
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            var host = new GameObject("Store Image Renderer");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<ThumbCapture>().StartCoroutine(Run());
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            SessionState.EraseBool(RunningKey);
            foreach (string key in TouchedPrefs) PlayerPrefs.DeleteKey(key);
            foreach (string pair in SessionState.GetString(PrefsKey, "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = pair.Split('=');
                PlayerPrefs.SetInt(kv[0], int.Parse(kv[1]));
            }
            PlayerPrefs.Save();
            SessionState.EraseString(PrefsKey);
            Debug.Log("STORE IMAGES: prefs restored");
            if (SessionState.GetBool(QuitKey, false)) { SessionState.EraseBool(QuitKey); EditorApplication.Exit(0); }
        }
    }

    // ───────── The run ─────────

    private static readonly List<string> failures = new List<string>();

    private static IEnumerator Run()
    {
        failures.Clear();
        Application.runInBackground = true;   // keep going when the editor isn't in front
        Debug.Log("STORE IMAGES: started");
        NeonMenu menu = null;
        float giveUp = Time.realtimeSinceStartup + 30f;
        while ((menu = UnityEngine.Object.FindFirstObjectByType<NeonMenu>()) == null && Time.realtimeSinceStartup < giveUp) yield return null;
        if (menu == null) { Debug.LogError("STORE IMAGES: no menu"); EditorApplication.ExitPlaymode(); yield break; }
        yield return new WaitForSecondsRealtime(3f);

        timingCandidates = SessionState.GetBool(CandidatesKey, false);
        SessionState.EraseBool(CandidatesKey);
        if (!timingCandidates)
        {
            yield return Step(PieceSetPromos());
            yield return Step(ReviewShots(menu));
        }
        yield return Step(GamePromos());
        if (!timingCandidates) yield return Step(EverythingImages(menu));

        PieceShapes.Showing = null;
        Debug.Log("STORE IMAGES: done" + (failures.Count > 0 ? " — failed: " + string.Join(", ", failures) : ""));
        EditorApplication.ExitPlaymode();
    }

    /// <summary>Runs a part, carrying on with the next if it throws</summary>
    private static IEnumerator Step(IEnumerator part)
    {
        while (true)
        {
            object current;
            try
            {
                if (!part.MoveNext()) yield break;
                current = part.Current;
            }
            catch (Exception e)
            {
                Debug.LogError("STORE IMAGES: " + e);
                failures.Add(e.Message);
                yield break;
            }
            if (current is IEnumerator nested) yield return Step(nested);
            else yield return current;
        }
    }

    private static IEnumerable<PieceShapes.Kind> LockedSets => PieceShapes.All.Where(k => !Store.Free(k) && Wanted(Store.Id(k)));
    private static IEnumerable<Backdrops.Kind> LockedBackdrops => Backdrops.All.Where(k => !Store.Free(k) && Wanted(Store.Id(k)));
    private static IEnumerable<PromotionStyle.Kind> LockedPromotions => PromotionStyle.All.Where(k => !Store.Free(k) && Wanted(Store.Id(k)));

    // "New Items Only": just the items missing an image in the output folder (the promo or the
    // review shot: the promos are rendered first, so checking only the promo skipped the reviews)
    private static bool Wanted(string storeId) =>
        !SessionState.GetBool(NewOnlyKey, false) || !File.Exists(Path.Combine(OutputFolder, ProductId(storeId) + ".png"))
        || !File.Exists(Path.Combine(OutputFolder, ProductId(storeId) + "_review.png"));

    // ───────── Piece sets: the six pieces in a row on the menu's violet ─────────

    private static readonly Vector3 Stage = new Vector3(-6000f, 0f, 0f);

    private static IEnumerator PieceSetPromos()
    {
        PieceSets.Kind finish = PieceSets.Current;
        PieceSets.Current = PieceSets.Kind.NeonGlow;   // the default finish
        foreach (PieceShapes.Kind set in LockedSets)
        {
            PieceShapes.Showing = set;
            var stage = new GameObject("Store Stage");
            stage.transform.position = Stage;
            Camera cam = StageCamera(stage.transform);
            BuildBackground(stage.transform, cam);

            // One side (the pink one, which glows on the violet), the tall pieces at the back:
            // R Q K B behind, N P in front
            (System.Type type, float x, float z)[] lineup =
            {
                (typeof(Rook), -2.85f, 1.1f), (typeof(Queen), -0.95f, 1.5f), (typeof(King), 0.95f, 1.5f), (typeof(Bishop), 2.85f, 1.1f),
                (typeof(Knight), -1.1f, -1.0f), (typeof(Pawn), 1.1f, -1.0f),
            };
            var pieces = new List<ChessPiece>();
            foreach (var spot in lineup)
            {
                ChessPiece p = MakePiece(spot.type, PieceColor.Black, stage.transform);
                p.transform.position = Stage + new Vector3(spot.x, 0f, spot.z);
                p.transform.rotation = Quaternion.Euler(0f, 180f - spot.x * 6f, 0f);
                pieces.Add(p);
            }
            yield return null;
            yield return null;

            // Frame the group: centred, filling most of the width
            Bounds b = BoundsOf(pieces);
            foreach (Renderer r in stage.GetComponentsInChildren<Renderer>())
                if (r.GetComponentInParent<ChessPiece>() != null && r.bounds.size.magnitude > 6f) Debug.Log($"STORE IMAGES: big renderer {r.name} {r.GetType().Name} {r.bounds.size}");
            Vector3 look = b.center + Vector3.down * b.size.y * 0.06f;
            Vector3 back = new Vector3(0f, 0.32f, -1f).normalized;
            float dist = 20f;
            for (int pass = 0; pass < 4; pass++)
            {
                cam.transform.position = look + back * dist;
                cam.transform.LookAt(look);
                cam.aspect = 1f;
                dist *= ViewportSpread(cam, pieces) / 0.9f;
            }
            cam.transform.position = look + back * dist;
            yield return new WaitForSecondsRealtime(0.5f);

            SavePng(RenderCamera(cam, PromoSize, PromoSize), ProductId(Store.Id(set)));
            UnityEngine.Object.Destroy(stage);
            yield return null;
        }
        PieceShapes.Showing = null;
        PieceSets.Current = finish;
    }

    private static Camera StageCamera(Transform parent)
    {
        var light = new GameObject("Light").AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(35f, -25f, 0f);
        light.intensity = 1.15f;
        var cam = new GameObject("Store Camera").AddComponent<Camera>();
        cam.transform.SetParent(parent, false);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = NeonTheme.Ground;
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 400f;
        cam.enabled = false;
        return cam;
    }

    /// <summary>A soft violet glow behind the pieces and a neon floor line, like the menu's look</summary>
    private static void BuildBackground(Transform parent, Camera cam)
    {
        Shader unlit = Shader.Find("Unlit/Texture");
        var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        UnityEngine.Object.Destroy(glow.GetComponent<Collider>());
        glow.transform.SetParent(parent, false);
        glow.transform.localPosition = new Vector3(0f, 1.6f, 22f);
        glow.transform.localScale = new Vector3(70f, 70f, 1f);
        glow.GetComponent<Renderer>().material = new Material(unlit) { mainTexture = RadialTexture(NeonTheme.Hex("4A1A8C"), NeonTheme.Ground) };

        var floor = GameObject.CreatePrimitive(PrimitiveType.Quad);
        UnityEngine.Object.Destroy(floor.GetComponent<Collider>());
        floor.transform.SetParent(parent, false);
        floor.transform.localPosition = new Vector3(0f, -0.02f, 0.4f);
        floor.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        floor.transform.localScale = new Vector3(16f, 6f, 1f);
        floor.GetComponent<Renderer>().material = new Material(Shader.Find("Unlit/Transparent")) { mainTexture = RadialTexture(new Color(NeonTheme.Pink.r, NeonTheme.Pink.g, NeonTheme.Pink.b, 0.55f), new Color(NeonTheme.Pink.r, NeonTheme.Pink.g, NeonTheme.Pink.b, 0f)) };
    }

    private static Texture2D RadialTexture(Color centre, Color edge)
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                float t = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 2f);
                px[y * n + x] = Color.Lerp(centre, edge, Mathf.SmoothStep(0f, 1f, t));
            }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>How much of the frame the pieces' meshes cover, edge to edge (1 = all of it)</summary>
    private static float ViewportSpread(Camera cam, IEnumerable<ChessPiece> pieces)
    {
        float spread = 0.01f;
        foreach (ChessPiece p in pieces)
            foreach (MeshFilter mf in p.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null || !mf.GetComponent<Renderer>().enabled) continue;
                Bounds lb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 v = cam.WorldToViewportPoint(mf.transform.TransformPoint(corner));
                    spread = Mathf.Max(spread, Mathf.Abs(v.x - 0.5f) * 2f, Mathf.Abs(v.y - 0.5f) * 2f);
                }
            }
        return spread;
    }

    private static ChessPiece MakePiece(System.Type type, PieceColor color, Transform parent)
    {
        var go = new GameObject("Store " + type.Name);
        go.SetActive(false);
        go.transform.SetParent(parent, false);
        var piece = (ChessPiece)go.AddComponent(type);
        piece.pieceColor = color;
        piece.pieceType = PieceModels.TypeOf(piece);
        go.SetActive(true);
        piece.ApplyMaterial();
        return piece;
    }

    private static Bounds BoundsOf(IEnumerable<Component> things)
    {
        Bounds? b = null;
        foreach (Component c in things)
            foreach (Renderer r in c.GetComponentsInChildren<Renderer>())
            {
                if (b == null) b = r.bounds;
                else { Bounds e = b.Value; e.Encapsulate(r.bounds); b = e; }
            }
        return b ?? new Bounds(Vector3.zero, Vector3.one);
    }

    // ───────── Unlock Everything: a collage of what it opens, and the Customize card ─────────

    private static IEnumerator EverythingImages(NeonMenu menu)
    {
        if (!Wanted(Store.EverythingId)) yield break;
        // The review shot: Customize with the Unlock Everything card over the choices
        if (menu == null) menu = UnityEngine.Object.FindFirstObjectByType<NeonMenu>();
        if (menu == null)
        {
            // Back to the menu after the game shots
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            float until = Time.realtimeSinceStartup + 30f;
            while ((menu = UnityEngine.Object.FindFirstObjectByType<NeonMenu>()) == null && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(3f);
        }
        if (menu != null)
        {
            Call(menu, "ShowCustomize");
            yield return new WaitForSecondsRealtime(1f);
            SelectTab(menu, "Pieces");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return CaptureScreen(ProductId(Store.EverythingId) + "_review");
        }

        // The promo image: three piece sets, three backdrops and three promotions, from their own pictures
        var tiles = new List<string>();
        tiles.AddRange(PieceShapes.All.Where(k => !Store.Free(k)).Take(3).Select(k => ProductId(Store.Id(k))));
        tiles.AddRange(Backdrops.All.Where(k => !Store.Free(k)).Take(3).Select(k => ProductId(Store.Id(k))));
        tiles.AddRange(PromotionStyle.All.Where(k => !Store.Free(k)).Take(3).Select(k => ProductId(Store.Id(k))));
        // Mixed so no row is all one kind
        int[] order = { 0, 3, 6, 4, 7, 1, 8, 2, 5 };
        var tex = new Texture2D(PromoSize, PromoSize, TextureFormat.RGB24, false);
        var px = new Color[PromoSize * PromoSize];
        for (int i = 0; i < px.Length; i++) px[i] = NeonTheme.Ground;
        const int gap = 14, edge = 4;
        int cell = (PromoSize - gap * 4) / 3;
        Color[] edges = { NeonTheme.Pink, NeonTheme.Cyan, NeonTheme.Lime };
        for (int t = 0; t < 9; t++)
        {
            string path = Path.Combine(OutputFolder, tiles[order[t]] + ".png");
            if (!File.Exists(path)) { failures.Add("Unlock Everything needs " + tiles[order[t]]); continue; }
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(path));
            int x0 = gap + (t % 3) * (cell + gap), y0 = gap + (2 - t / 3) * (cell + gap);
            Color border = edges[(t + t / 3) % 3];
            for (int y = 0; y < cell; y++)
                for (int x = 0; x < cell; x++)
                {
                    bool onEdge = x < edge || y < edge || x >= cell - edge || y >= cell - edge;
                    px[(y0 + y) * PromoSize + x0 + x] = onEdge ? border : src.GetPixelBilinear((x + 0.5f) / cell, (y + 0.5f) / cell);
                }
            UnityEngine.Object.Destroy(src);
        }
        tex.SetPixels(px);
        tex.Apply();
        SavePng(tex, ProductId(Store.EverythingId));
    }

    // ───────── Review screenshots: the Customize screen previewing each locked item ─────────

    private static IEnumerator ReviewShots(NeonMenu menu)
    {
        Call(menu, "ShowCustomize");
        yield return new WaitForSecondsRealtime(1f);

        SelectTab(menu, "Pieces");
        foreach (PieceShapes.Kind set in LockedSets)
        {
            yield return Pick(menu, PieceShapes.DisplayName(set));
            yield return new WaitForSecondsRealtime(2.5f);
            yield return CaptureScreen(ProductId(Store.Id(set)) + "_review");
        }

        SelectTab(menu, "Backdrop");
        foreach (Backdrops.Kind backdrop in LockedBackdrops)
        {
            yield return Pick(menu, Backdrops.DisplayName(backdrop));
            yield return new WaitForSecondsRealtime(3.5f);
            yield return CaptureScreen(ProductId(Store.Id(backdrop)) + "_review");
        }

        SelectTab(menu, "Effects");
        foreach (PromotionStyle.Kind promotion in LockedPromotions)
        {
            yield return Pick(menu, PromotionStyle.DisplayName(promotion));
            // Picking plays it in the preview card: catch it mid-animation
            yield return new WaitForSecondsRealtime(MidPoint(promotion) + 0.05f);
            yield return CaptureScreen(ProductId(Store.Id(promotion)) + "_review");
            yield return new WaitForSecondsRealtime(2f);
        }

        SelectTab(menu, "Pieces");
    }

    private static void SelectTab(NeonMenu menu, string tab)
    {
        System.Type tabType = typeof(NeonMenu).GetNestedType("CustomizeTab", BindingFlags.NonPublic);
        Call(menu, "SelectTab", Enum.Parse(tabType, tab));
    }

    /// <summary>Taps the item's card in the grid, scrolled into view</summary>
    private static IEnumerator Pick(NeonMenu menu, string displayName)
    {
        var content = (RectTransform)Field(menu, "gridContent");
        var scroll = (ScrollRect)Field(menu, "gridScroll");
        Transform card = content.Find(displayName);
        if (card == null) throw new Exception("no card for " + displayName);
        card.GetComponent<Button>().onClick.Invoke();
        yield return null;
        card = content.Find(displayName);   // tapping can rebuild the cards
        if (card == null) yield break;

        // Scroll so the card sits in the middle of the viewport where it can
        Canvas.ForceUpdateCanvases();
        float contentH = content.rect.height, viewH = scroll.viewport.rect.height;
        if (contentH > viewH)
        {
            float cardCentre = -((RectTransform)card).anchoredPosition.y;
            float top = Mathf.Clamp(cardCentre - viewH / 2f, 0f, contentH - viewH);
            scroll.verticalNormalizedPosition = 1f - top / (contentH - viewH);
        }
        scroll.velocity = Vector2.zero;
    }

    private static IEnumerator CaptureScreen(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        if (shot.width != ReviewWidth || shot.height != ReviewHeight)
            Debug.LogWarning($"STORE IMAGES: {name} came out {shot.width}x{shot.height} (Game view not at {ReviewWidth}x{ReviewHeight}?)");
        SavePng(shot, name);
    }

    // ───────── Board shots in a Quick Play game: backdrops and promotions ─────────

    private static IEnumerator GamePromos()
    {
        if (SceneController.Instance == null) throw new Exception("no SceneController to start a game");
        SceneController.Instance.QuickStartHumanVsAI(PieceColor.White, AIDifficulty.Easy);
        float giveUp = Time.realtimeSinceStartup + 40f;
        while ((PlacementManager.Instance == null || !PlacementManager.Instance.PiecesReady) && Time.realtimeSinceStartup < giveUp) yield return null;
        if (PlacementManager.Instance == null) throw new Exception("game never started");
        yield return new WaitForSecondsRealtime(1f);
        PlacementManager.Instance.AutoPlaceRemaining(PieceColor.White);
        yield return new WaitForSecondsRealtime(3f);
        PlacementManager.Instance.SetPlayerReady(PieceColor.White, true);
        giveUp = Time.realtimeSinceStartup + 30f;
        while (GameStateManager.Instance != null && GameStateManager.Instance.CanPlacePieces() && Time.realtimeSinceStartup < giveUp) yield return null;
        yield return new WaitForSecondsRealtime(4f);

        Vector3 centre = BoardCentre();
        float cell = ChessBoard.Instance != null ? ChessBoard.Instance.cellSize : 2.8f;
        Camera main = Camera.main;
        if (main == null) throw new Exception("no game camera");
        Debug.Log($"STORE IMAGES: board centre {centre}, camera at {main.transform.position} fov {main.fieldOfView}");

        // Backdrops: the game's own view of the board, a little closer since the square is narrower
        foreach (Backdrops.Kind backdrop in timingCandidates ? Enumerable.Empty<Backdrops.Kind>() : LockedBackdrops)
        {
            main = Camera.main;
            float until = Time.realtimeSinceStartup + 10f;
            while (main != null && !Backdrops.Apply(main, backdrop) && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSecondsRealtime(3f);
            Camera cam = CopyCamera(main);
            cam.transform.position = centre + (main.transform.position - centre) * 0.88f;
            cam.transform.LookAt(centre + Vector3.up * cell * 0.15f);
            SavePng(RenderCamera(cam, PromoSize, PromoSize), ProductId(Store.Id(backdrop)));
            UnityEngine.Object.Destroy(cam.gameObject);
            yield return null;
        }
        if (Camera.main != null) main = Camera.main;
        Backdrops.Apply(main, Backdrops.Kind.NeonGlow);
        yield return new WaitForSecondsRealtime(2f);

        // Promotions: a white pawn on the camera's side becomes a queen, caught mid-animation,
        // seen from just outside the cube with the board behind
        foreach (PromotionStyle.Kind promotion in LockedPromotions)
        {
            if (Camera.main != null) main = Camera.main;
            if (main == null) throw new Exception("lost the game camera");
            Vector3 eye = main.transform.position;
            Pawn real = UnityEngine.Object.FindObjectsByType<Pawn>(FindObjectsSortMode.None)
                .Where(p => p.pieceColor == PieceColor.White && p.gameObject.activeInHierarchy && p.CurrentPosition.IsValid())
                .OrderBy(p => Mathf.Round(Flat(eye - p.transform.position).magnitude / cell)).ThenByDescending(p => p.transform.position.y)
                .FirstOrDefault();
            if (real == null)
            {
                foreach (Pawn p in UnityEngine.Object.FindObjectsByType<Pawn>(FindObjectsSortMode.None))
                    Debug.Log($"STORE IMAGES: pawn {p.pieceColor} at {p.transform.position} board {p.CurrentPosition.x},{p.CurrentPosition.y},{p.CurrentPosition.z} active {p.gameObject.activeInHierarchy}");
                throw new Exception("no pawn on the board for " + promotion);
            }
            Vector3 at = real.transform.position;
            Bounds body = BoundsOf(new Component[] { real });
            Vector3 toEye = Flat(eye - at).normalized;
            Camera cam = CopyCamera(main);
            cam.fieldOfView = 48f;
            Vector3 look = new Vector3(at.x, body.min.y, at.z) + Vector3.up * cell * 0.75f;
            cam.transform.position = look + toEye * cell * 2.8f + Vector3.up * cell * 0.85f;
            cam.transform.LookAt(look);
            Debug.Log($"STORE IMAGES: {promotion} on the pawn at {at}");

            // Pieces standing between the camera and the pawn step out of the shot
            var hidden = new List<GameObject> { real.gameObject };
            Vector3 sight = Flat(at - cam.transform.position);
            foreach (ChessPiece other in UnityEngine.Object.FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
            {
                if (other == real || !other.gameObject.activeInHierarchy || !other.CurrentPosition.IsValid()) continue;
                Vector3 o = other.transform.position;
                float t = Vector3.Dot(Flat(o - cam.transform.position), sight) / sight.sqrMagnitude;
                Vector3 nearest = cam.transform.position + sight * t;
                bool inFront = t > 0f && t < 0.95f && Flat(o - nearest).magnitude < cell * 0.9f;
                if (inFront && o.y > at.y - cell * 1.5f) hidden.Add(other.gameObject);
            }
            foreach (GameObject g in hidden) g.SetActive(false);

            float[] times = timingCandidates ? new[] { 0.3f, 0.5f, 0.7f, 0.9f, 1.1f, 1.3f, 1.6f, 1.9f } : new[] { MidPoint(promotion) };
            foreach (float time in times)
            {
                ChessPiece pawn = MakePiece(typeof(Pawn), PieceColor.White, real.transform.parent);
                ChessPiece queen = MakePiece(typeof(Queen), PieceColor.White, real.transform.parent);
                pawn.transform.SetPositionAndRotation(at, real.transform.rotation);
                queen.transform.SetPositionAndRotation(at, real.transform.rotation);
                pawn.transform.localScale = queen.transform.localScale = real.transform.localScale;

                PieceEffects.PreviewCamera = cam;
                yield return null;
                PieceEffects.Promote(pawn, queen, promotion);
                UnityEngine.Object.Destroy(pawn.gameObject);
                yield return new WaitForSecondsRealtime(time);
                string name = ProductId(Store.Id(promotion)) + (timingCandidates ? $"_t{time:0.0}" : "");
                if (timingCandidates) name = Path.Combine("candidates", name);
                SavePng(RenderCamera(cam, PromoSize, PromoSize), name);

                yield return new WaitForSecondsRealtime(Mathf.Max(0.5f, 2.6f - time));
                PieceEffects.PreviewCamera = null;
                if (queen != null) UnityEngine.Object.Destroy(queen.gameObject);
                yield return new WaitForSecondsRealtime(0.3f);
            }
            UnityEngine.Object.Destroy(cam.gameObject);
            foreach (GameObject g in hidden) if (g != null) g.SetActive(true);
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>The middle of the cube, from the board's corner cells</summary>
    private static Vector3 BoardCentre()
    {
        if (ChessBoard.Instance == null) return Vector3.zero;
        Vector3Int dims = BoardDimensionsManager.Instance != null ? BoardDimensionsManager.Instance.GetDimensions() : new Vector3Int(4, 4, 4);
        Vector3 a = ChessBoard.Instance.BoardToWorld(new BoardPosition(0, 0, 0));
        Vector3 b = ChessBoard.Instance.BoardToWorld(new BoardPosition(dims.x - 1, dims.y - 1, dims.z - 1));
        return (a + b) / 2f + Vector3.up * ChessBoard.Instance.cellSize * 0.5f;
    }

    /// <summary>A still copy of a camera (same sky, clear and culling), rendered by hand</summary>
    private static Camera CopyCamera(Camera like)
    {
        var go = new GameObject("Store Camera");
        Camera cam = go.AddComponent<Camera>();
        cam.CopyFrom(like);
        cam.tag = "Untagged";
        cam.enabled = false;
        cam.targetTexture = null;
        var sky = like.GetComponent<Skybox>();
        if (sky != null) go.AddComponent<Skybox>().material = sky.material;
        return cam;
    }

    // ───────── Rendering and files ─────────

    private static Texture2D RenderCamera(Camera cam, int width, int height)
    {
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        RenderTexture before = cam.targetTexture;
        float aspect = cam.aspect;
        cam.targetTexture = rt;
        cam.aspect = (float)width / height;
        cam.Render();
        cam.targetTexture = before;
        cam.ResetAspect();
        RenderTexture active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        RenderTexture.active = active;
        rt.Release();
        return tex;
    }

    /// <summary>Writes an opaque sRGB PNG (alpha dropped)</summary>
    private static void SavePng(Texture2D tex, string name)
    {
        Texture2D opaque = tex;
        if (tex.format != TextureFormat.RGB24)
        {
            opaque = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
            opaque.SetPixels32(tex.GetPixels32());
            opaque.Apply();
        }
        string path = Path.Combine(OutputFolder, name + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, opaque.EncodeToPNG());
        if (opaque != tex) UnityEngine.Object.Destroy(opaque);
        UnityEngine.Object.Destroy(tex);
        Debug.Log("STORE IMAGES: saved " + path);
    }

    // ───────── Reflection into the menu (its parts are private) ─────────

    private static object Call(object target, string method, params object[] args)
    {
        MethodInfo m = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(x => x.Name == method && x.GetParameters().Length == args.Length);
        return m.Invoke(target, args);
    }

    private static object Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);

    // ───────── Game view resolution ─────────

    internal static void SetGameViewSize(int width, int height)
    {
        try
        {
            Assembly ed = typeof(Editor).Assembly;
            System.Type sizesType = ed.GetType("UnityEditor.GameViewSizes");
            System.Type single = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            object sizes = single.GetProperty("instance").GetValue(null);
            object groupType = sizesType.GetProperty("currentGroupType").GetValue(sizes);
            object group = sizesType.GetMethod("GetGroup").Invoke(sizes, new[] { groupType });
            System.Type groupT = group.GetType();
            int total = (int)groupT.GetMethod("GetTotalCount").Invoke(group, null);
            int index = -1;
            for (int i = 0; i < total; i++)
            {
                object s = groupT.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                System.Type st = s.GetType();
                if ((int)st.GetProperty("width").GetValue(s) == width && (int)st.GetProperty("height").GetValue(s) == height
                    && st.GetProperty("sizeType").GetValue(s).ToString() == "FixedResolution") { index = i; break; }
            }
            if (index < 0)
            {
                System.Type sizeT = ed.GetType("UnityEditor.GameViewSize");
                System.Type kindT = ed.GetType("UnityEditor.GameViewSizeType");
                object size = Activator.CreateInstance(sizeT, Enum.Parse(kindT, "FixedResolution"), width, height, "App Store review");
                groupT.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                index = (int)groupT.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            System.Type gameViewT = ed.GetType("UnityEditor.GameView");
            EditorWindow view = EditorWindow.GetWindow(gameViewT);
            PropertyInfo selected = gameViewT.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (selected != null) selected.SetValue(view, index);
            else gameViewT.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(view, new object[] { index, null });
        }
        catch (Exception e)
        {
            Debug.LogWarning("STORE IMAGES: couldn't set the Game view to " + width + "x" + height + ": " + e.Message);
        }
    }
}

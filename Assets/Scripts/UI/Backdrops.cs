using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// The scenery the board floats in. Each backdrop is a 360° sky around the camera.
/// Add a new one by adding a value to Kind, a name in DisplayName and a case in Build.
/// </summary>
public static class Backdrops
{
    public enum Kind
    {
        NeonGlow = 0,
        DeepSpace = 1,
        NeonDesert = 2,
        CyberCity = 3,
        NeonJungle = 4,
        MeltingClocks = 5,
        EarthOrbit = 6,
        DeepSea = 7,
        Graveyard = 8,
    }

    public static readonly Kind[] All = { Kind.DeepSpace, Kind.NeonDesert, Kind.CyberCity, Kind.NeonJungle, Kind.MeltingClocks, Kind.EarthOrbit, Kind.DeepSea, Kind.Graveyard, Kind.NeonGlow };

    private const string PrefKey = "Backdrop";

    /// <summary>The backdrop in use: the one picked, if it's unlocked (Store), else Neon Glow</summary>
    public static Kind Current
    {
        get
        {
            var picked = (Kind)PlayerPrefs.GetInt(PrefKey, (int)Kind.NeonGlow);
            return Store.Owns(picked) ? picked : Kind.NeonGlow;
        }
        set
        {
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    public static string DisplayName(Kind kind)
    {
        switch (kind)
        {
            case Kind.DeepSpace: return "Deep Space";
            case Kind.NeonDesert: return "Neon Desert";
            case Kind.CyberCity: return "Cyber City";
            case Kind.NeonJungle: return "Neon Jungle";
            case Kind.MeltingClocks: return "Melting Clocks";
            case Kind.EarthOrbit: return "Earth Orbit";
            case Kind.DeepSea: return "Deep Sea";
            case Kind.Graveyard: return "Graveyard";
            default: return "Neon Glow";
        }
    }

    public static string Description(Kind kind)
    {
        switch (kind)
        {
            case Kind.DeepSpace: return "Stars, galaxies and glowing nebulae all around the board";
            case Kind.NeonDesert: return "Synthwave sunset over shifting sand dunes, with drifting dust";
            case Kind.CyberCity: return "High above a rainy neon megacity at night, with flying traffic";
            case Kind.NeonJungle: return "A glowing night jungle above a frog pond, with robot critters";
            case Kind.MeltingClocks: return "A dreamy golden dusk where soft clocks droop from branches and drift past the board";
            case Kind.EarthOrbit: return "Low orbit above the turning Earth, with the planets, passing comets and satellites whizzing by";
            case Kind.DeepSea: return "Dark open water full of fish that sometimes eat each other. Watch out for the tentacle";
            case Kind.Graveyard: return "A moonlit cemetery with glowing tombstones, creeping mist, will-o'-wisps and bats";
            default: return "Clean violet glow, easy on the eyes";
        }
    }

    // ───────── Skies (generated once on a worker thread, then cached) ─────────

    private const int SkyWidth = 2048, SkyHeight = 1024;
    private static readonly System.Collections.Generic.Dictionary<Kind, Material> skies = new System.Collections.Generic.Dictionary<Kind, Material>();
    private static readonly System.Collections.Generic.Dictionary<Kind, Task<Color32[]>> skyTasks = new System.Collections.Generic.Dictionary<Kind, Task<Color32[]>>();

    private static Func<Color32[]> Generator(Kind kind)
    {
        switch (kind)
        {
            case Kind.NeonDesert: return () => DesertSky.Generate(SkyWidth, SkyHeight, 1984);
            case Kind.CyberCity: return () => CitySky.Generate(SkyWidth, SkyHeight, 2077);
            case Kind.NeonJungle: return () => JungleSky.Generate(SkyWidth, SkyHeight, 1066);
            case Kind.MeltingClocks: return () => DreamSky.Generate(SkyWidth, SkyHeight, 1931);
            case Kind.EarthOrbit: return () => SpaceSky.Generate(SkyWidth, SkyHeight, 1957, galaxies: 1);
            case Kind.DeepSea: return () => DeepSeaSky.Generate(SkyWidth, SkyHeight, 4040);
            case Kind.Graveyard: return () => GraveyardSky.Generate(SkyWidth, SkyHeight, 1031);
            default: return () => SpaceSky.Generate(SkyWidth, SkyHeight, 20261006);
        }
    }

    // Color shown while a sky is still being generated
    private static Color LoadingColor(Kind kind) =>
        kind == Kind.NeonDesert ? new Color32(30, 4, 50, 255) : kind == Kind.CyberCity ? new Color32(20, 3, 40, 255) : kind == Kind.NeonJungle ? new Color32(6, 24, 28, 255) : kind == Kind.MeltingClocks ? new Color32(120, 90, 110, 255) : kind == Kind.DeepSea ? new Color32(5, 23, 38, 255) : kind == Kind.Graveyard ? new Color32(20, 12, 34, 255) : new Color32(4, 2, 14, 255);

    /// <summary>
    /// Apply the chosen backdrop to a camera. Returns false while a sky is still being
    /// generated; call again next frame (a plain color shows meanwhile).
    /// </summary>
    public static bool Apply(Camera cam, Kind kind)
    {
        if (kind == Kind.NeonGlow)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = NeonTheme.Ground;
            RenderSettings.skybox = null;
            Scenery.Clear();
            if (probe != null)
            {
                probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.SolidColor;
                probe.backgroundColor = NeonTheme.Ground;
                probe.RenderProbe();
                UseProbeAsEnvironment();
            }
            return true;
        }

        if (!skies.TryGetValue(kind, out Material sky) || sky == null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = LoadingColor(kind);

            if (!skyTasks.TryGetValue(kind, out Task<Color32[]> task))
            {
                task = Task.Run(Generator(kind));
                skyTasks[kind] = task;
            }
            if (!task.IsCompleted) return false;
            skyTasks.Remove(kind);
            if (task.IsFaulted)
            {
                Debug.LogError($"Backdrops: {kind} sky failed: {task.Exception}");
                return true;
            }

            var tex = new Texture2D(SkyWidth, SkyHeight, TextureFormat.RGBA32, true);
            tex.wrapModeU = TextureWrapMode.Repeat;
            tex.wrapModeV = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Trilinear;
            tex.SetPixels32(task.Result);
            tex.Apply(true, true);

            Shader shader = Shader.Find("Skybox/Panoramic");
            if (shader == null)
            {
                Debug.LogError("Backdrops: Skybox/Panoramic shader missing from build");
                return true;
            }
            sky = new Material(shader);
            sky.SetTexture("_MainTex", tex);
            sky.SetFloat("_Mapping", 1f);   // latitude-longitude layout
            sky.SetFloat("_ImageType", 0f); // 360°
            sky.SetFloat("_Exposure", 1f);
            skies[kind] = sky;
        }

        RenderSettings.skybox = sky;
        cam.clearFlags = CameraClearFlags.Skybox;

        if (kind == Kind.NeonDesert) Scenery.BuildDesert(cam);
        else if (kind == Kind.CyberCity) Scenery.BuildCity(cam);
        else if (kind == Kind.NeonJungle) Scenery.BuildJungle(cam);
        else if (kind == Kind.MeltingClocks) Scenery.BuildDream(cam);
        else if (kind == Kind.EarthOrbit) Scenery.BuildOrbit(cam);
        else if (kind == Kind.DeepSea) Scenery.BuildDeepSea(cam);
        else if (kind == Kind.Graveyard) Scenery.BuildGraveyard(cam);
        else Scenery.Clear();

        RefreshReflections();
        return true;
    }

    private static ReflectionProbe probe;

    /// <summary>
    /// Capture the sky into a reflection probe so shiny piece sets (Chrome, Crystal)
    /// reflect the current backdrop.
    /// </summary>
    private static void RefreshReflections()
    {
        // Mobile quality levels often switch realtime probes off
        QualitySettings.realtimeReflectionProbes = true;
        if (probe == null)
        {
            var go = new GameObject("Backdrop Reflections");
            probe = go.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.cullingMask = 0; // sky only
            probe.size = new Vector3(500, 500, 500);
            probe.resolution = 128;
        }
        probe.clearFlags = UnityEngine.Rendering.ReflectionProbeClearFlags.Skybox;
        probe.RenderProbe();
        UseProbeAsEnvironment();
    }

    // Also make the captured sky the scene's default reflection, so every renderer
    // picks it up even outside the probe's blending
    private static void UseProbeAsEnvironment()
    {
        if (probe != null && probe.realtimeTexture != null)
        {
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = probe.realtimeTexture;
        }
    }

    /// <summary>Slowly turn the space sky so it feels alive (the desert stays put)</summary>
    public static void Drift(float degreesPerSecond)
    {
        if (skies.TryGetValue(Kind.DeepSpace, out Material space) && space != null && RenderSettings.skybox == space)
        {
            space.SetFloat("_Rotation", (Time.time * degreesPerSecond) % 360f);
        }
    }
}

/// <summary>
/// 3D extras that go with a backdrop: ground, particles, fog and lighting.
/// Everything is undone by Clear() so other backdrops look as before.
/// </summary>
public static class Scenery
{
    private static GameObject root;
    private static bool fogWasOn;
    private static readonly System.Collections.Generic.Dictionary<Light, (Color color, float intensity, Quaternion rotation)> savedLights =
        new System.Collections.Generic.Dictionary<Light, (Color, float, Quaternion)>();
    private static Color savedAmbient;
    private static bool changed;

    public static void Clear()
    {
        if (root != null) UnityEngine.Object.Destroy(root);
        root = null;
        if (!changed) return;
        RenderSettings.fog = fogWasOn;
        RenderSettings.ambientLight = savedAmbient;
        foreach (var entry in savedLights)
        {
            if (entry.Key == null) continue;
            entry.Key.color = entry.Value.color;
            entry.Key.intensity = entry.Value.intensity;
            entry.Key.transform.rotation = entry.Value.rotation;
        }
        savedLights.Clear();
        changed = false;
    }

    public static void BuildDesert(Camera cam)
    {
        Clear();
        fogWasOn = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        changed = true;

        root = new GameObject("Neon Desert Scenery");

        // Rolling sand dunes far below the board; DesertDunes slowly shifts them
        GameObject ground = new GameObject("Sand Dunes");
        ground.transform.SetParent(root.transform, false);
        ground.transform.position = new Vector3(0f, -26f, 0f);
        ground.AddComponent<MeshFilter>();
        var groundRenderer = ground.AddComponent<MeshRenderer>();
        var sandMat = new Material(Shader.Find("Standard"));
        sandMat.mainTexture = SandTexture();
        sandMat.mainTextureScale = new Vector2(16f, 16f);
        sandMat.color = new Color(0.86f, 0.6f, 0.66f);
        sandMat.SetFloat("_Metallic", 0f);
        sandMat.SetFloat("_Glossiness", 0.12f);
        groundRenderer.sharedMaterial = sandMat;
        groundRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ground.AddComponent<DesertDunes>();

        // Haze: distant dunes melt into the horizon color
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.55f, 0.16f, 0.45f);
        RenderSettings.fogStartDistance = 70f;
        RenderSettings.fogEndDistance = 330f;

        // Warm sunset light from the sun's side, so chrome glints gold and pink
        RenderSettings.ambientLight = new Color(0.42f, 0.24f, 0.42f);
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            savedLights[light] = (light.color, light.intensity, light.transform.rotation);
            light.color = new Color(1f, 0.68f, 0.45f);
            light.intensity = Mathf.Max(light.intensity, 1.15f);
            light.transform.rotation = Quaternion.Euler(18f, 180f, 0f); // low sun, shining toward the board
        }

        // Drifting sand dust around the board
        root.AddComponent<DesertDust>();
    }

    public static void BuildCity(Camera cam)
    {
        Clear();
        fogWasOn = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        changed = true;

        root = new GameObject("Cyber City Scenery");
        CityScenery.Build(root.transform);

        // Just a light, dark night haze for depth; the pink comes from drifting wisps (CityWisps)
        RenderSettings.fog = true;
        // Exactly the sky's horizon haze color, reaching full strength at the outer ring of towers,
        // so the farthest towers melt into the haze instead of making a hard line against it
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = CitySky.HazeColor;
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 175f;

        // Cool night light from above, with magenta city glow as ambient
        RenderSettings.ambientLight = new Color(0.34f, 0.22f, 0.46f);
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            savedLights[light] = (light.color, light.intensity, light.transform.rotation);
            light.color = new Color(0.7f, 0.8f, 1f);
            light.intensity = Mathf.Min(light.intensity, 0.9f);
            light.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        }
    }

    public static void BuildJungle(Camera cam)
    {
        Clear();
        fogWasOn = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        changed = true;

        root = new GameObject("Neon Jungle Scenery");
        JungleScenery.Build(root.transform);

        // Damp night air: the far trees fade into the sky's horizon mist
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = JungleSky.HazeColor;
        RenderSettings.fogStartDistance = 45f;
        RenderSettings.fogEndDistance = 170f;

        // Cool moonlight from the moon's side, teal glow from the jungle as ambient
        RenderSettings.ambientLight = new Color(0.2f, 0.34f, 0.36f);
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            savedLights[light] = (light.color, light.intensity, light.transform.rotation);
            light.color = new Color(0.65f, 0.9f, 1f);
            light.intensity = Mathf.Min(light.intensity, 0.95f);
            light.transform.rotation = Quaternion.Euler(40f, 45f, 0f);
        }
    }

    public static void BuildDream(Camera cam)
    {
        Clear();
        fogWasOn = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        changed = true;

        root = new GameObject("Melting Clocks Scenery");
        DreamScenery.Build(root.transform);

        // Warm haze: the far plain fades into the glowing horizon
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = DreamSky.HazeColor;
        RenderSettings.fogStartDistance = 90f;
        RenderSettings.fogEndDistance = 420f;

        // Low golden sun from the sky's sun (ahead of White, +x), teal sky light as ambient
        RenderSettings.ambientLight = new Color(0.36f, 0.4f, 0.48f);
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            savedLights[light] = (light.color, light.intensity, light.transform.rotation);
            light.color = new Color(1f, 0.82f, 0.6f);
            light.intensity = Mathf.Max(light.intensity, 1.1f);
            light.transform.rotation = Quaternion.Euler(16f, -90f, 0f);
        }
    }

    public static void BuildOrbit(Camera cam)
    {
        Clear();
        fogWasOn = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        changed = true;

        root = new GameObject("Earth Orbit Scenery");
        // Lights are saved before OrbitSky points them at the sun
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            savedLights[light] = (light.color, light.intensity, light.transform.rotation);
            light.intensity = Mathf.Max(light.intensity, 1.2f);
        }
        OrbitScenery.Build(root.transform);

        // No air up here: no haze, and a cool dim fill from the Earth below
        RenderSettings.fog = false;
        RenderSettings.ambientLight = new Color(0.22f, 0.26f, 0.36f);
    }

    public static void BuildDeepSea(Camera cam)
    {
        Clear();
        fogWasOn = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        changed = true;

        root = new GameObject("Deep Sea Scenery");
        DeepSeaScenery.Build(root.transform);

        // Murky water: everything fades into the deep blue a little way out
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = DeepSeaSky.HazeColor;
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 165f;

        // Faint blue-green light filtering down from far above
        RenderSettings.ambientLight = new Color(0.16f, 0.3f, 0.38f);
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            savedLights[light] = (light.color, light.intensity, light.transform.rotation);
            light.color = new Color(0.55f, 0.85f, 1f);
            light.intensity = Mathf.Min(light.intensity, 0.9f);
            light.transform.rotation = Quaternion.Euler(75f, 20f, 0f);
        }
    }

    public static void BuildGraveyard(Camera cam)
    {
        Clear();
        fogWasOn = RenderSettings.fog;
        savedAmbient = RenderSettings.ambientLight;
        changed = true;

        root = new GameObject("Graveyard Scenery");
        GraveyardScenery.Build(root.transform);

        // Night mist: the far graves fade into the sky's purple haze
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = GraveyardSky.HazeColor;
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 150f;

        // Pale green moonlight from the moon's side, cool violet ambient
        RenderSettings.ambientLight = new Color(0.22f, 0.2f, 0.32f);
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            savedLights[light] = (light.color, light.intensity, light.transform.rotation);
            light.color = new Color(0.75f, 1f, 0.8f);
            light.intensity = Mathf.Min(light.intensity, 0.9f);
            light.transform.rotation = Quaternion.Euler(25f, -90f, 0f);
        }
    }

    // Wind ripples in sand: soft wavy bands with fine grain, tiles seamlessly
    private static Texture2D SandTexture()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Trilinear;
        tex.anisoLevel = 8;
        var rng = new System.Random(7);
        Color light = new Color(0.98f, 0.72f, 0.62f);  // sunlit peach
        Color dark = new Color(0.62f, 0.34f, 0.48f);   // rose shadow in the troughs
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (float)x / size, v = (float)y / size;
            // Ripples run across the wind, with a gentle wobble (whole periods so it tiles)
            float wobble = 0.03f * Mathf.Sin(u * Mathf.PI * 2f) + 0.012f * Mathf.Sin(u * Mathf.PI * 2f * 3f + 1.7f);
            float phase = (v + wobble) * 6f;
            float ripple = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f);
            ripple = Mathf.Pow(ripple, 1.8f); // sharp crests, wide troughs
            float grain = (float)rng.NextDouble() * 0.08f - 0.04f;
            Color c = Color.Lerp(dark, light, 0.62f + 0.3f * ripple) + new Color(grain, grain, grain); // subtle ripples
            c.a = 1f;
            tex.SetPixel(x, y, c);
        }
        tex.Apply(true);
        return tex;
    }
}

/// <summary>
/// A wide field of dunes whose shapes drift slowly with the wind, plus sand ripples
/// that creep along the surface.
/// </summary>
public class DesertDunes : MonoBehaviour
{
    private const int Res = 90;          // vertices per side
    private const float Size = 700f;     // world units per side
    private Mesh mesh;
    private Vector3[] vertices;
    private Material material;
    private float nextUpdate;

    private void Start()
    {
        mesh = new Mesh { name = "Dunes" };
        mesh.MarkDynamic();
        vertices = new Vector3[Res * Res];
        var uvs = new Vector2[Res * Res];
        var triangles = new int[(Res - 1) * (Res - 1) * 6];
        for (int z = 0; z < Res; z++)
        for (int x = 0; x < Res; x++)
        {
            int i = z * Res + x;
            float fx = (float)x / (Res - 1), fz = (float)z / (Res - 1);
            vertices[i] = new Vector3((fx - 0.5f) * Size, 0f, (fz - 0.5f) * Size);
            uvs[i] = new Vector2(fx, fz);
        }
        int t = 0;
        for (int z = 0; z < Res - 1; z++)
        for (int x = 0; x < Res - 1; x++)
        {
            int i = z * Res + x;
            triangles[t++] = i; triangles[t++] = i + Res; triangles[t++] = i + 1;
            triangles[t++] = i + 1; triangles[t++] = i + Res; triangles[t++] = i + Res + 1;
        }
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        GetComponent<MeshFilter>().sharedMesh = mesh;
        material = GetComponent<MeshRenderer>().sharedMaterial;
        Shape(0f);
    }

    private void Update()
    {
        // Ripples creep with the wind every frame (cheap)
        if (material != null) material.mainTextureOffset = new Vector2(0f, Time.time * 0.012f);

        // Dune shapes shift a little several times a second
        if (Time.time >= nextUpdate)
        {
            nextUpdate = Time.time + 0.1f;
            Shape(Time.time);
        }
    }

    // A handful of lone dunes on an otherwise calm plain: (x, z, length, width, height, angle)
    private (float x, float z, float length, float width, float height, float angle)[] dunes;

    private void PlaceDunes()
    {
        var rng = new System.Random(42);
        dunes = new (float, float, float, float, float, float)[7];
        for (int i = 0; i < dunes.Length; i++)
        {
            // Spread around the board, never close to it
            float angle = (float)(i * Math.PI * 2 / dunes.Length + rng.NextDouble() * 0.6);
            float dist = 95f + (float)rng.NextDouble() * 150f;
            dunes[i] = (Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist,
                        28f + (float)rng.NextDouble() * 30f,   // along the wind
                        45f + (float)rng.NextDouble() * 45f,   // across the wind (crescent width)
                        12f + (float)rng.NextDouble() * 12f,
                        (float)(rng.NextDouble() * 0.5 - 0.25));
        }
    }

    /// <summary>Sand height at a point (local to the dune field)</summary>
    public float HeightAt(float x, float z, float time)
    {
        // Calm plain with a very gentle swell
        float h = 0.8f * Mathf.Sin(x * 0.013f + 0.7f) * Mathf.Sin(z * 0.011f + 1.9f);

        float drift = time * 0.35f; // dunes creep slowly downwind (+x)
        foreach (var d in dunes)
        {
            float dx = x - (d.x + drift), dz = z - d.z;
            float cos = Mathf.Cos(d.angle), sin = Mathf.Sin(d.angle);
            float along = dx * cos + dz * sin;
            float across = -dx * sin + dz * cos;
            // Gentle windward slope, steep lee face, horns curving downwind at the edges
            along -= 0.25f * across * across / d.width;
            float a = along < 0f ? d.length : d.length * 0.45f;
            float e = (along * along) / (a * a) + (across * across) / (d.width * d.width);
            if (e < 9f) h += d.height * Mathf.Exp(-e * 1.4f);
        }

        // Keep it flat right under the board
        float r = Mathf.Sqrt(x * x + z * z);
        return h * Mathf.Clamp01((r - 30f) / 40f);
    }

    private void Shape(float time)
    {
        if (dunes == null) PlaceDunes();
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i].y = HeightAt(vertices[i].x, vertices[i].z, time);
        }
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        cacti?.Settle(this, time);
    }

    private DesertCacti cacti;

    private void Awake()
    {
        cacti = gameObject.AddComponent<DesertCacti>();
    }

    // Sharp-crested, smooth-backed dune profile in 0..1
    private static float Ridge(float p)
    {
        float s = 0.5f + 0.5f * Mathf.Sin(p * Mathf.PI * 2f);
        return s * s;
    }
}

/// <summary>
/// A sprinkling of neon saguaro cacti standing on the sand. They follow the ground as
/// the dunes shift so they never float or sink.
/// </summary>
public class DesertCacti : MonoBehaviour
{
    private readonly System.Collections.Generic.List<(Transform t, float x, float z)> cacti =
        new System.Collections.Generic.List<(Transform, float, float)>();

    private void Start()
    {
        var rng = new System.Random(11);

        Material skin = Resources.Load<Material>("PieceSets/Glow");
        skin = skin != null ? new Material(skin) : new Material(Shader.Find("Standard"));
        skin.color = new Color(0.07f, 0.32f, 0.3f);
        skin.EnableKeyword("_EMISSION");
        skin.SetColor("_EmissionColor", new Color(0.05f, 0.55f, 0.5f) * 0.55f); // soft neon glow
        skin.SetFloat("_Metallic", 0f);
        skin.SetFloat("_Glossiness", 0.3f);

        int count = 11;
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(rng.NextDouble() * Math.PI * 2);
            float dist = 60f + (float)rng.NextDouble() * 170f;
            float x = Mathf.Cos(angle) * dist, z = Mathf.Sin(angle) * dist;
            float scale = 1.8f + (float)rng.NextDouble() * 1.4f; // big enough to read from the board
            int arms = rng.Next(0, 3);
            Transform cactus = BuildCactus(skin, scale, arms, (float)rng.NextDouble() * 360f, rng);
            cactus.SetParent(transform, false);
            cacti.Add((cactus, x, z));
        }
    }

    public void Settle(DesertDunes ground, float time)
    {
        foreach (var c in cacti)
        {
            if (c.t == null) continue;
            c.t.localPosition = new Vector3(c.x, ground.HeightAt(c.x, c.z, time) - 0.4f, c.z);
        }
    }

    private static Transform BuildCactus(Material skin, float scale, int arms, float yaw, System.Random rng)
    {
        var root = new GameObject("Cactus").transform;
        root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        root.localScale = Vector3.one * scale;

        Part(root, skin, new Vector3(0f, 5f, 0f), new Vector3(1.6f, 5f, 1.6f));       // trunk, ~10 tall
        for (int a = 0; a < arms; a++)
        {
            float side = a == 0 ? 1f : -1f;
            float y = 3.2f + (float)rng.NextDouble() * 2.5f;
            float up = 2.2f + (float)rng.NextDouble() * 1.5f;
            // elbow reaching out, then the arm rising
            Part(root, skin, new Vector3(side * 1.4f, y, 0f), new Vector3(1.0f, 1.0f, 1.0f), Quaternion.Euler(0f, 0f, 90f), 1.6f);
            Part(root, skin, new Vector3(side * 2.4f, y + up * 0.5f, 0f), new Vector3(1.1f, up * 0.6f, 1.1f));
        }
        return root;
    }

    private static void Part(Transform parent, Material skin, Vector3 position, Vector3 size, Quaternion? rotation = null, float length = 0f)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        UnityEngine.Object.Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        if (rotation.HasValue) part.transform.localRotation = rotation.Value;
        part.transform.localScale = length > 0f ? new Vector3(size.x, length, size.z) : size;
        var r = part.GetComponent<MeshRenderer>();
        r.sharedMaterial = skin;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }
}

/// <summary>Slow warm dust drifting past the board in the desert</summary>
public class DesertDust : MonoBehaviour
{
    private void Start()
    {
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 18f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.75f, 0.45f, 0.55f), new Color(1f, 0.35f, 0.7f, 0.45f));
        main.maxParticles = 260;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;

        var emission = ps.emission;
        emission.rateOverTime = 18f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(70f, 34f, 70f);

        // Gentle breeze with a little wobble
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.15f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.15f;

        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;

        var renderer = GetComponent<ParticleSystemRenderer>();
        var material = new Material(Shader.Find("Sprites/Default"));
        material.mainTexture = NeonTheme.Circle.texture;
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        ps.Play();
    }
}

/// <summary>
/// Procedural equirectangular synthwave desert: violet-to-orange sunset sky, a striped
/// neon sun, two ridges of mountains and dunes with glowing rims, and banded sand
/// below the horizon. Pure C# so it can run on a worker thread.
/// </summary>
public static class DesertSky
{
    public static Color32[] Generate(int width, int height, int seed)
    {
        var rng = new System.Random(seed);
        var result = new Color32[width * height];

        const float horizon = 0.5f;          // v of the horizon line
        const float sunU = 0.25f;            // where the sun sits around the sky
        const float sunV = 0.405f;           // sun centre, just above the horizon
        const float sunR = 0.085f;           // radius in v units

        // Ridge heights (in v above the horizon), periodic around the sky
        float[] far = new float[width];
        float[] near = new float[width];
        for (int x = 0; x < width; x++)
        {
            float u = (float)x / width;
            far[x] = 0.035f + 0.05f * Ridge(u * 9f, 9, seed) + 0.02f * Ridge(u * 23f, 23, seed + 7);
            // Dunes: smooth rolling bumps
            near[x] = 0.012f + 0.016f * (0.5f + 0.5f * (float)Math.Sin(u * Math.PI * 2 * 7 + 1.3))
                              + 0.012f * (0.5f + 0.5f * (float)Math.Sin(u * Math.PI * 2 * 17 + 0.4)) * Ridge(u * 5f, 5, seed + 3);
        }

        // Faint stars in the upper sky
        var stars = new System.Collections.Generic.HashSet<int>();
        for (int k = 0; k < 1400; k++)
        {
            int sx = rng.Next(width), sy = rng.Next((int)(height * 0.3f));
            stars.Add(sy * width + sx);
        }

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color c;

                if (v < horizon)
                {
                    // Sky: deep violet at the top, magenta, then hot orange at the horizon
                    float t = v / horizon;
                    c = Gradient3(new Color(0.06f, 0.0f, 0.16f), new Color(0.42f, 0.08f, 0.45f), new Color(1.0f, 0.42f, 0.28f), Mathf.Pow(t, 1.6f));
                    if (stars.Contains(y * width + x)) c += Color.white * (0.35f * (1f - t));

                    // Sun glow
                    float du = WrapDelta(u - sunU) * 2f; // u spans 360°, v spans 180°
                    float dv = v - sunV;
                    float dist = (float)Math.Sqrt(du * du + dv * dv);
                    c += new Color(1f, 0.35f, 0.55f) * (0.45f * (float)Math.Exp(-dist * dist / (sunR * sunR * 6f)));

                    // Sun disc: yellow top to pink bottom, with synthwave stripes in its lower half
                    if (dist < sunR)
                    {
                        float st = (dv + sunR) / (2f * sunR); // 0 top, 1 bottom
                        Color sun = Color.Lerp(NeonTheme.Yellow, NeonTheme.Pink, st);
                        bool stripe = false;
                        if (st > 0.5f)
                        {
                            float band = (st - 0.5f) * 2f;          // 0..1 down the lower half
                            float gap = 0.06f + 0.10f * band;        // gaps widen toward the bottom
                            float period = 0.22f;
                            stripe = ((band + 0.08f) % period) < gap;
                        }
                        if (!stripe) c = Color.Lerp(c, sun, Mathf.Clamp01((sunR - dist) / 0.002f));
                    }

                    // Far mountains: deep purple with a pink rim
                    float farTop = horizon - far[x];
                    if (v > farTop)
                    {
                        float depth = (v - farTop) / far[x];
                        Color mount = Color.Lerp(new Color(0.26f, 0.06f, 0.38f), new Color(0.16f, 0.03f, 0.27f), depth);
                        float rim = Mathf.Clamp01(1f - (v - farTop) * height / 2.5f);
                        c = Color.Lerp(mount, NeonTheme.Pink, rim * 0.9f);
                    }

                    // Near dunes: darker, with a cyan rim
                    float nearTop = horizon - near[x];
                    if (v > nearTop)
                    {
                        float depth = (v - nearTop) / near[x];
                        Color dune = Color.Lerp(new Color(0.14f, 0.02f, 0.22f), new Color(0.08f, 0.0f, 0.15f), depth);
                        float rim = Mathf.Clamp01(1f - (v - nearTop) * height / 2f);
                        c = Color.Lerp(dune, new Color(0.16f, 0.94f, 1f), rim * 0.8f);
                    }
                }
                else
                {
                    // Sand below the horizon, seen past the 3D dunes: banded dune crests
                    // that crowd together toward the horizon
                    float below = v - horizon;                       // 0 at horizon
                    float depth = 0.02f / Math.Max(below, 0.0005f);  // distance across the sand
                    float wave = 0.5f + 0.5f * (float)Math.Sin((depth * 1.3f + 0.25f * Math.Sin(u * Math.PI * 2 * 6)) * Math.PI * 2);
                    Color sand = Color.Lerp(new Color(0.42f, 0.16f, 0.36f), new Color(0.9f, 0.5f, 0.5f), wave * wave);
                    Color haze = new Color(0.55f, 0.16f, 0.45f);
                    c = Color.Lerp(haze, sand, Mathf.Clamp01(below * 9f));
                    c = Color.Lerp(c, new Color(0.18f, 0.05f, 0.2f), Mathf.Clamp01((below - 0.25f) * 2f)); // darker straight down
                }

                c.a = 1f;
                result[(height - 1 - y) * width + x] = c; // Unity textures start at the bottom row
            }
        }
        return result;
    }

    private static Color Gradient3(Color a, Color b, Color c, float t)
    {
        return t < 0.5f ? Color.Lerp(a, b, t * 2f) : Color.Lerp(b, c, (t - 0.5f) * 2f);
    }

    private static float WrapDelta(float d)
    {
        if (d > 0.5f) d -= 1f;
        if (d < -0.5f) d += 1f;
        return d;
    }

    // Mountain-like ridge noise in 0..1; repeats every `period` so the sky has no seam
    private static float Ridge(float x, int period, int seed)
    {
        float sum = 0f, amp = 0.55f, norm = 0f;
        int freq = 1;
        for (int o = 0; o < 4; o++)
        {
            float n = Noise1(x * freq, period * freq, seed + o * 31);
            sum += amp * (1f - Math.Abs(n * 2f - 1f)); // ridged
            norm += amp;
            amp *= 0.5f;
            freq *= 2;
        }
        return sum / norm;
    }

    private static float Noise1(float x, int period, int seed)
    {
        int xi = (int)Math.Floor(x);
        float f = x - xi;
        float t = f * f * (3 - 2 * f);
        int a = ((xi % period) + period) % period, b = (((xi + 1) % period) + period) % period;
        return Hash(a, seed) * (1 - t) + Hash(b, seed) * t;
    }

    private static float Hash(int x, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + seed * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}

/// <summary>
/// Procedural equirectangular space sky: dark gradient, coloured nebula clouds,
/// a few spiral galaxies and thousands of stars. Pure C# so it can run on a worker thread.
/// </summary>
public static class SpaceSky
{
    public static Color32[] Generate(int width, int height, int seed, int galaxies = 5)
    {
        var rng = new System.Random(seed);
        var pixels = new float[width * height * 3];

        // Nebula: a few coloured clouds made of layered value noise
        var clouds = new (float u, float v, float radius, float r, float g, float b, float strength)[]
        {
            (0.18f, 0.42f, 0.22f, 0.85f, 0.18f, 0.60f, 0.55f),  // magenta
            (0.55f, 0.60f, 0.25f, 0.10f, 0.55f, 0.85f, 0.50f),  // cyan-blue
            (0.80f, 0.35f, 0.18f, 0.45f, 0.20f, 0.85f, 0.45f),  // violet
            (0.38f, 0.75f, 0.14f, 0.95f, 0.45f, 0.30f, 0.30f),  // ember
        };

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                int i = (y * width + x) * 3;

                // Deep space base, a touch lighter around the galactic band
                float band = (float)Math.Exp(-Math.Pow((v - 0.5f - 0.08f * Math.Sin(u * Math.PI * 2)) / 0.12f, 2));
                float r = 0.012f + 0.03f * band, g = 0.008f + 0.02f * band, b = 0.035f + 0.05f * band;

                float n = Fbm(u * 8f, v * 4f, seed);
                float dust = Fbm(u * 22f + 7f, v * 11f + 3f, seed + 1);
                foreach (var c in clouds)
                {
                    float du = WrapDelta(u - c.u), dv = (v - c.v) * 0.5f;
                    float d = (float)Math.Sqrt(du * du + dv * dv) / c.radius;
                    float falloff = Math.Max(0f, 1f - d * (0.6f + 0.8f * n));
                    float density = falloff * falloff * (0.5f + 0.9f * n) * (0.75f + 0.5f * dust) * c.strength;
                    r += c.r * density; g += c.g * density; b += c.b * density;
                }

                // Band brightness from unresolved stars
                float glow = band * 0.08f * (0.6f + 0.8f * dust);
                r += glow; g += glow * 0.9f; b += glow;

                pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b;
            }
        }

        // Spiral galaxies, kept away from the poles where the map stretches
        for (int k = 0; k < galaxies; k++)
        {
            float cu = (float)rng.NextDouble();
            float cv = 0.3f + 0.4f * (float)rng.NextDouble();
            float size = 0.018f + 0.03f * (float)rng.NextDouble();
            float tilt = (float)(rng.NextDouble() * Math.PI);
            float squash = 0.35f + 0.5f * (float)rng.NextDouble();
            float hue = (float)rng.NextDouble();
            DrawGalaxy(pixels, width, height, cu, cv, size, tilt, squash, hue, rng);
        }

        // Stars: spread evenly over the sphere, brighter ones rarer
        int stars = 9000;
        for (int k = 0; k < stars; k++)
        {
            double z = rng.NextDouble() * 2 - 1;
            double phi = rng.NextDouble() * Math.PI * 2;
            float u = (float)(phi / (Math.PI * 2));
            float v = (float)(Math.Acos(z) / Math.PI);
            double t = rng.NextDouble();
            float brightness = (float)(0.25 + 1.6 * Math.Pow(t, 6));
            float temp = (float)rng.NextDouble();
            float sr = temp < 0.2f ? 0.75f : 1f, sg = temp < 0.2f ? 0.85f : (temp > 0.85f ? 0.85f : 1f), sb = temp > 0.85f ? 0.7f : 1f;
            float radius = brightness > 1.2f ? 1.6f : 0.9f;
            // Stars near the poles are drawn wider so they stay round on screen
            float stretch = (float)(1.0 / Math.Max(0.15, Math.Sin(v * Math.PI)));
            Stamp(pixels, width, height, u * width, v * height, radius * stretch, radius, brightness, sr, sg, sb);
        }

        var result = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            // Unity textures start at the bottom row
            int row = height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                result[row * width + x] = new Color32(ToByte(pixels[i]), ToByte(pixels[i + 1]), ToByte(pixels[i + 2]), 255);
            }
        }
        return result;
    }

    private static void DrawGalaxy(float[] pixels, int width, int height, float cu, float cv, float size, float tilt, float squash, float hue, System.Random rng)
    {
        float coreR = 1f, coreG = 0.85f + 0.1f * hue, coreB = 0.7f + 0.3f * hue;
        float armR = 0.55f + 0.4f * (1 - hue), armG = 0.6f, armB = 0.9f + 0.1f * hue;
        float cos = (float)Math.Cos(tilt), sin = (float)Math.Sin(tilt);
        int samples = 2600;
        for (int s = 0; s < samples; s++)
        {
            // Two logarithmic spiral arms with scatter
            int arm = s % 2;
            float t = (float)rng.NextDouble();
            float angle = t * 9f + arm * (float)Math.PI;
            float rad = (float)Math.Exp(t * 1.6f) - 1f;
            rad /= (float)Math.Exp(1.6f) - 1f;
            float jitter = 0.12f * (float)(rng.NextDouble() - 0.5);
            float px = (rad + jitter) * (float)Math.Cos(angle);
            float py = (rad + jitter) * (float)Math.Sin(angle) * squash;
            float rx = px * cos - py * sin, ry = px * sin + py * cos;
            float u = cu + rx * size, v = cv + ry * size * 2f;
            float bright = 0.35f * (1f - t) + 0.08f;
            float mix = Math.Min(1f, t * 1.5f);
            Stamp(pixels, width, height, u * width, v * height, 1.4f, 1.4f, bright,
                coreR + (armR - coreR) * mix, coreG + (armG - coreG) * mix, coreB + (armB - coreB) * mix);
        }
        // Bright core
        Stamp(pixels, width, height, cu * width, cv * height, size * width * 0.12f, size * height * 0.24f, 0.9f, coreR, coreG, coreB);
    }

    // Additive soft dot (Gaussian), wrapping horizontally
    private static void Stamp(float[] pixels, int width, int height, float cx, float cy, float rx, float ry, float brightness, float r, float g, float b)
    {
        int x0 = (int)Math.Floor(cx - rx * 2.5f), x1 = (int)Math.Ceiling(cx + rx * 2.5f);
        int y0 = Math.Max(0, (int)Math.Floor(cy - ry * 2.5f)), y1 = Math.Min(height - 1, (int)Math.Ceiling(cy + ry * 2.5f));
        for (int y = y0; y <= y1; y++)
        {
            float dy = (y + 0.5f - cy) / ry;
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) / rx;
                float w = (float)Math.Exp(-(dx * dx + dy * dy)) * brightness;
                if (w < 0.002f) continue;
                int wx = ((x % width) + width) % width;
                int i = (y * width + wx) * 3;
                pixels[i] += r * w; pixels[i + 1] += g * w; pixels[i + 2] += b * w;
            }
        }
    }

    private static float WrapDelta(float d)
    {
        if (d > 0.5f) d -= 1f;
        if (d < -0.5f) d += 1f;
        return d;
    }

    private static byte ToByte(float value)
    {
        // Soft shoulder so bright cores don't clip harshly
        float mapped = value / (1f + value * 0.35f) * 1.35f;
        return (byte)Math.Max(0, Math.Min(255, (int)(mapped * 255f)));
    }

    // Value noise with several octaves; tiles horizontally every 8 units of u at the base scale
    private static float Fbm(float x, float y, int seed)
    {
        float sum = 0f, amp = 0.5f, freq = 1f;
        for (int o = 0; o < 5; o++)
        {
            sum += amp * ValueNoise(x * freq, y * freq, seed + o * 101, (int)(8 * freq));
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum;
    }

    private static float ValueNoise(float x, float y, int seed, int period)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        float a = Hash(xi, yi, seed, period), b = Hash(xi + 1, yi, seed, period);
        float c = Hash(xi, yi + 1, seed, period), d = Hash(xi + 1, yi + 1, seed, period);
        return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
    }

    private static float Hash(int x, int y, int seed, int period)
    {
        if (period > 0) x = ((x % period) + period) % period; // seamless where the sky wraps around
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * -2048144777;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}

using System;
using System.Threading.Tasks;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Near Earth Orbit backdrop: the board floats in low orbit.
//
//  - The sky is the Deep Space star field (SpaceSky).
//  - OrbitEarth: a huge Earth filling the bottom of the view, slowly turning, with oceans,
//    continents, ice caps and clouds (generated on a worker thread), lit by the sun so a
//    night side shows away from it, and a thin blue atmosphere glowing along its rim.
//  - OrbitSky: the sun low over the Earth's limb, and the moon higher up on the other side.
//    The moon is a real sphere lit by the same sun, so its phase is always right.
//  - OrbitSatellite: satellites (and one space station) whizzing past the board.
// ─────────────────────────────────────────────────────────────────────────────

public static class OrbitScenery
{
    public const float EarthRadius = 600f;
    public static readonly Vector3 EarthCenter = new Vector3(0f, -650f, 0f); // top 50 below the board; the horizon sits low in the view

    // Where the sun and moon sit, seen from the board (unit vectors)
    public static readonly Vector3 SunDirection = Quaternion.Euler(0f, 35f, 0f) * new Vector3(Mathf.Cos(12f * Mathf.Deg2Rad), Mathf.Sin(12f * Mathf.Deg2Rad), 0f);
    public static readonly Vector3 MoonDirection = Quaternion.Euler(0f, -40f, 0f) * new Vector3(Mathf.Cos(24f * Mathf.Deg2Rad), Mathf.Sin(24f * Mathf.Deg2Rad), 0f);

    public static void Build(Transform root)
    {
        var earth = new GameObject("Earth");
        earth.transform.SetParent(root, false);
        earth.AddComponent<OrbitEarth>();

        var sky = new GameObject("Sun and Moon");
        sky.transform.SetParent(root, false);
        sky.AddComponent<OrbitSky>();

        var sats = new GameObject("Satellites").transform;
        sats.SetParent(root, false);
        for (int i = 0; i < 6; i++)
        {
            var s = new GameObject(i == 0 ? "Space Station" : "Satellite");
            s.transform.SetParent(sats, false);
            s.AddComponent<OrbitSatellite>().Init(1000 + i * 37, i == 0);
        }
    }

    // ───────── Shared helpers ─────────

    public static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rotation = null)
    {
        Transform t = JungleScenery.Part(parent, type, pos, scale, mat);
        if (rotation.HasValue) t.localRotation = rotation.Value;
        return t;
    }

    public static Material Lit(Color color, Color emission, float metallic, float smoothness, Texture2D texture = null)
    {
        Material template = Resources.Load<Material>("PieceSets/Glow");
        Material m = template != null ? new Material(template) : new Material(Shader.Find("Standard"));
        m.mainTexture = texture;
        m.color = color;
        m.EnableKeyword("_EMISSION");
        m.SetTexture("_EmissionMap", null);
        m.SetColor("_EmissionColor", emission);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        return m;
    }

    /// <summary>A smooth UV sphere (the built-in one is too coarse at planet size); u around, v south to north</summary>
    public static Mesh Sphere(int around, int rings)
    {
        var verts = new Vector3[(around + 1) * (rings + 1)];
        var uvs = new Vector2[verts.Length];
        var normals = new Vector3[verts.Length];
        for (int r = 0; r <= rings; r++)
        {
            float lat = -Mathf.PI / 2f + Mathf.PI * r / rings;
            for (int a = 0; a <= around; a++)
            {
                float lon = Mathf.PI * 2f * a / around;
                int i = r * (around + 1) + a;
                normals[i] = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Sin(lat), Mathf.Cos(lat) * Mathf.Sin(lon));
                verts[i] = normals[i] * 0.5f;
                uvs[i] = new Vector2((float)a / around, (float)r / rings);
            }
        }
        var tris = new int[around * rings * 6];
        int t = 0;
        for (int r = 0; r < rings; r++)
        for (int a = 0; a < around; a++)
        {
            int i = r * (around + 1) + a, up = i + around + 1;
            // Wound so the outside faces out
            tris[t++] = i; tris[t++] = up; tris[t++] = i + 1;
            tris[t++] = i + 1; tris[t++] = up; tris[t++] = up + 1;
        }
        var mesh = new Mesh { name = "Smooth Sphere", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}

/// <summary>The Earth below the board: turns slowly; a rim of atmosphere glows around it</summary>
public class OrbitEarth : MonoBehaviour
{
    private const int TexW = 2048, TexH = 1024;
    private static Task<Color32[]> texTask;
    private static Texture2D texture;
    private Material material;
    private Transform globe, atmosphere;

    private void Start()
    {
        transform.position = OrbitScenery.EarthCenter;
        transform.localRotation = Quaternion.Euler(68f, 0f, 0f); // axis nearly level, so the land below rolls across the view

        globe = new GameObject("Globe").transform;
        globe.SetParent(transform, false);
        globe.localScale = Vector3.one * OrbitScenery.EarthRadius * 2f;
        globe.gameObject.AddComponent<MeshFilter>().sharedMesh = OrbitScenery.Sphere(256, 128);
        var r = globe.gameObject.AddComponent<MeshRenderer>();
        material = OrbitScenery.Lit(new Color(0.12f, 0.3f, 0.65f), Color.black, 0f, 0.12f, texture);
        if (texture != null) material.color = Color.white;
        r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        if (texture == null && texTask == null) texTask = Task.Run(() => EarthTexture.Generate(TexW, TexH, 1969));

        // Atmosphere: a glow ring facing the camera, sized to the Earth's outline (see LateUpdate)
        var glow = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(glow.GetComponent<Collider>());
        glow.name = "Atmosphere";
        glow.transform.SetParent(transform.parent, false);
        var gr = glow.GetComponent<MeshRenderer>();
        gr.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = AtmosphereTexture(), renderQueue = 2950 };
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        atmosphere = glow.transform;
    }

    private void Update()
    {
        // Spins slowly enough to feel huge, quickly enough to see it turn
        globe.Rotate(0f, -1.2f * Time.deltaTime, 0f, Space.Self);

        if (texture == null && texTask != null && texTask.IsCompleted)
        {
            if (!texTask.IsFaulted)
            {
                texture = new Texture2D(TexW, TexH, TextureFormat.RGBA32, true) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                texture.SetPixels32(texTask.Result);
                texture.Apply(true, true);
                Debug.Log("ORBIT: Earth texture ready");
            }
            else Debug.LogError($"OrbitEarth: texture failed: {texTask.Exception}");
            texTask = null;
        }
        if (texture != null && material.mainTexture != texture)
        {
            material.mainTexture = texture;
            material.color = Color.white;
        }
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null || atmosphere == null) return;
        // The outline seen from the camera is a circle in the plane R²/d from the centre toward the camera
        Vector3 c = OrbitScenery.EarthCenter, eye = cam.transform.position;
        Vector3 toEye = eye - c;
        float d = toEye.magnitude, R = OrbitScenery.EarthRadius;
        if (d <= R) return;
        float limb = R * Mathf.Sqrt(1f - R * R / (d * d));
        atmosphere.position = c + toEye / d * (R * R / d);
        atmosphere.rotation = Quaternion.LookRotation(-toEye, Vector3.up);
        atmosphere.localScale = Vector3.one * limb * 2f * AtmosphereScale;
    }

    private const float AtmosphereScale = 1.12f; // quad half-width, as a multiple of the outline radius

    // Blue haze hugging the outline: strongest right at the edge, fading into space
    private static Texture2D AtmosphereTexture()
    {
        const int size = 512;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        float edge = 1f / AtmosphereScale;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            float rho = Mathf.Sqrt(u * u + v * v);
            float a = rho < edge ? Mathf.Exp(-(edge - rho) / 0.012f) : Mathf.Exp(-(rho - edge) / 0.022f);
            if (rho > 1f) a = 0f;
            Color c = Color.Lerp(new Color(0.55f, 0.85f, 1f), new Color(0.25f, 0.45f, 1f), Mathf.Clamp01((rho - edge) / 0.06f));
            px[y * size + x] = new Color(c.r, c.g, c.b, a * 0.9f);
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}

/// <summary>Procedural Earth map (equirectangular, bottom row = south pole). Pure C# so it can run on a worker thread.</summary>
public static class EarthTexture
{
    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        Color deep = new Color(0.02f, 0.1f, 0.32f), shallow = new Color(0.05f, 0.42f, 0.6f);
        Color green = new Color(0.16f, 0.4f, 0.14f), dark = new Color(0.07f, 0.24f, 0.1f), desert = new Color(0.72f, 0.58f, 0.36f), rock = new Color(0.45f, 0.38f, 0.3f);
        for (int y = 0; y < height; y++)
        {
            float lat = -Mathf.PI / 2f + Mathf.PI * (y + 0.5f) / height;
            float cl = Mathf.Cos(lat), sl = Mathf.Sin(lat);
            float absLat = Mathf.Abs(lat) * Mathf.Rad2Deg;
            for (int x = 0; x < width; x++)
            {
                float lon = Mathf.PI * 2f * (x + 0.5f) / width;
                // Sample noise on the sphere itself so there's no seam and no pinching at the poles
                float px = cl * Mathf.Cos(lon), py = sl, pz = cl * Mathf.Sin(lon);
                float land = Fbm(px * 3.2f, py * 3.2f, pz * 3.2f, seed, 7);
                float detail = Fbm(px * 14f, py * 14f, pz * 14f, seed + 9, 3);
                Color c;
                float sea = 0.53f;
                if (land < sea)
                {
                    c = Color.Lerp(deep, shallow, Mathf.Pow(Mathf.Clamp01((land - 0.38f) / (sea - 0.38f)), 3f));
                }
                else
                {
                    float h = (land - sea) / (1f - sea);
                    // Deserts near the subtropics, forests elsewhere, rock on the highest ground
                    float dry = Mathf.Exp(-Mathf.Pow((absLat - 24f) / 11f, 2f)) * (0.6f + 0.8f * detail);
                    c = Color.Lerp(Color.Lerp(green, dark, detail), desert, Mathf.Clamp01(dry - 0.15f));
                    c = Color.Lerp(c, rock, Mathf.Clamp01((h - 0.35f) * 2.5f));
                }
                // Ice caps
                float ice = Mathf.Clamp01((absLat - 68f + 6f * (detail - 0.5f)) / 4f);
                c = Color.Lerp(c, new Color(0.92f, 0.95f, 1f), ice);

                // Clouds: swirled bands, thicker along the storm belts
                float swirl = Fbm(px * 7f + 1.5f * detail, py * 12f, pz * 7f, seed + 31, 5);
                float belts = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(lat * 3f));
                float cloud = Mathf.Clamp01((swirl * belts - 0.44f) * 3f);
                c = Color.Lerp(c, new Color(0.97f, 0.98f, 1f), cloud * 0.8f);

                result[y * width + x] = new Color32((byte)Mathf.Clamp(c.r * 255f, 0, 255), (byte)Mathf.Clamp(c.g * 255f, 0, 255), (byte)Mathf.Clamp(c.b * 255f, 0, 255), 255);
            }
        }
        return result;
    }

    private static float Fbm(float x, float y, float z, int seed, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Value(x, y, z, seed + o * 57);
            norm += amp;
            amp *= 0.5f;
            x *= 2.03f; y *= 2.03f; z *= 2.03f;
        }
        return sum / norm;
    }

    private static float Value(float x, float y, float z, int seed)
    {
        int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y), zi = (int)Math.Floor(z);
        float xf = x - xi, yf = y - yi, zf = z - zi;
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf), w = zf * zf * (3 - 2 * zf);
        float a = Lerp(Lerp(Hash(xi, yi, zi, seed), Hash(xi + 1, yi, zi, seed), u), Lerp(Hash(xi, yi + 1, zi, seed), Hash(xi + 1, yi + 1, zi, seed), u), v);
        float b = Lerp(Lerp(Hash(xi, yi, zi + 1, seed), Hash(xi + 1, yi, zi + 1, seed), u), Lerp(Hash(xi, yi + 1, zi + 1, seed), Hash(xi + 1, yi + 1, zi + 1, seed), u), v);
        return Lerp(a, b, w);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static float Hash(int x, int y, int z, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + z * 1274126177 + seed * -2048144777;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}

/// <summary>The sun and the moon, fixed in the sky wherever the camera goes</summary>
public class OrbitSky : MonoBehaviour
{
    private Transform sunDisc, sunGlow, sunFlare, moon;

    private void Start()
    {
        Shader sprite = Shader.Find("Sprites/Default");
        // Drawn after the opaque scene (it writes no depth), so the Earth can hide it if the view dips
        sunGlow = Quad("Sun Glow", new Material(sprite) { mainTexture = Glow(2f), color = new Color(1f, 0.85f, 0.6f, 0.75f), renderQueue = 2900 }, 420f);
        sunFlare = Quad("Sun Flare", new Material(sprite) { mainTexture = Flare(), color = new Color(1f, 0.95f, 0.85f, 0.5f), renderQueue = 2901 }, 520f);
        sunDisc = Quad("Sun", new Material(sprite) { mainTexture = Glow(0.35f), color = new Color(1f, 0.99f, 0.94f, 1f), renderQueue = 2902 }, 70f);

        // The moon: a grey sphere lit by the sun, so its phase is right from every angle
        moon = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
        Destroy(moon.GetComponent<Collider>());
        moon.name = "Moon";
        moon.SetParent(transform, false);
        moon.localScale = Vector3.one * 85f;
        moon.GetComponent<MeshFilter>().sharedMesh = OrbitScenery.Sphere(64, 32);
        var mr = moon.GetComponent<MeshRenderer>();
        mr.sharedMaterial = OrbitScenery.Lit(Color.white, Color.black, 0f, 0.1f, MoonTexture());
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // The sun is the light
        foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type != LightType.Directional) continue;
            light.transform.rotation = Quaternion.LookRotation(-OrbitScenery.SunDirection);
            light.color = new Color(1f, 0.97f, 0.92f);
        }
    }

    private Transform Quad(string name, Material mat, float size)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.transform.SetParent(transform, false);
        q.transform.localScale = new Vector3(size, size, 1f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return q.transform;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        if (cam.farClipPlane < 1600f) cam.farClipPlane = 1600f;
#if DEVELOPMENT_BUILD
        // Dev only: `defaults write BomSapo.Chess3D DebugOrbitView -int 1` (sun) or 2 (moon) points the camera at it
        int view = PlayerPrefs.GetInt("DebugOrbitView", 0);
        if (view > 0)
        {
            Vector3 target = view == 1 ? OrbitScenery.SunDirection : OrbitScenery.MoonDirection;
            Vector3 from = -new Vector3(target.x, 0f, target.z).normalized * 30f + Vector3.up * 6f;
            cam.transform.position = from;
            cam.transform.rotation = Quaternion.LookRotation(target * 900f - from);
        }
#endif
        Vector3 eye = cam.transform.position;
        Vector3 sun = eye + OrbitScenery.SunDirection * 1200f;
        foreach (Transform t in new[] { sunGlow, sunFlare, sunDisc })
        {
            t.position = sun;
            t.rotation = Quaternion.LookRotation(sun - eye, Vector3.up);
        }
        sunFlare.rotation *= Quaternion.Euler(0f, 0f, Time.time * 2f);
        moon.position = eye + OrbitScenery.MoonDirection * 900f;
        moon.Rotate(0f, 0.5f * Time.deltaTime, 0f);
    }

    // Soft round glow; `sharp` above 1 gives a tighter core
    private static Texture2D Glow(float sharp)
    {
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            float a = sharp < 1f ? Mathf.Clamp01((1f - d) / 0.08f) : Mathf.Pow(Mathf.Clamp01(1f - d), 1f + sharp);
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    // Thin rays around the sun
    private static Texture2D Flare()
    {
        const int size = 256;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v), a = Mathf.Atan2(v, u);
            float rays = Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3f)), 60f) + 0.6f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 5f + 0.4f)), 120f);
            float alpha = rays * Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
            px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }

    // Grey moon with dark maria and a scatter of bright craters
    private static Texture2D MoonTexture()
    {
        const int w = 512, h = 256;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        var rng = new System.Random(3);
        var craters = new (float x, float y, float r)[90];
        for (int i = 0; i < craters.Length; i++) craters[i] = ((float)rng.NextDouble() * w, h * 0.15f + (float)rng.NextDouble() * h * 0.7f, 2f + (float)Math.Pow(rng.NextDouble(), 3) * 18f);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float maria = Mathf.PerlinNoise(x * 0.012f + 4f, y * 0.02f + 9f) * 0.7f + Mathf.PerlinNoise(x * 0.05f, y * 0.08f) * 0.3f;
            float shade = 0.78f - Mathf.Clamp01((maria - 0.45f) * 1.8f) * 0.3f;
            foreach (var c in craters)
            {
                float dx = Mathf.Min(Mathf.Abs(x - c.x), w - Mathf.Abs(x - c.x)), dy = y - c.y;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c.r;
                if (d < 1.15f) shade += d < 0.85f ? -0.06f : 0.1f; // dark floor, bright rim
            }
            shade = Mathf.Clamp01(shade);
            px[y * w + x] = new Color(shade, shade, shade * 1.02f, 1f);
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}

/// <summary>
/// A satellite whizzing past the board on a straight pass; when it's gone it comes back on a
/// new pass from somewhere else. One of them is a slower, bigger space station.
/// </summary>
public class OrbitSatellite : MonoBehaviour
{
    private System.Random rng;
    private bool station;
    private Vector3 start, velocity;
    private float travelled, length, spin;
    private Transform blink;
    private static Material foil, panel, white, red, truss;

    public void Init(int seed, bool isStation)
    {
        rng = new System.Random(seed);
        station = isStation;
        if (foil == null)
        {
            foil = OrbitScenery.Lit(new Color(1f, 0.75f, 0.3f), new Color(0.25f, 0.15f, 0.02f), 0.9f, 0.7f);
            panel = OrbitScenery.Lit(Color.white, new Color(0.05f, 0.25f, 0.5f), 0.6f, 0.85f, PanelTexture());
            white = OrbitScenery.Lit(new Color(0.9f, 0.92f, 0.95f), new Color(0.1f, 0.1f, 0.12f), 0.3f, 0.6f);
            truss = OrbitScenery.Lit(new Color(0.55f, 0.57f, 0.6f), Color.black, 0.7f, 0.5f);
            red = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, 0.15f, 0.2f) };
        }
        if (station) BuildStation(); else BuildSatellite();
        NewPass(first: true);
    }

    private void BuildSatellite()
    {
        float s = 1.4f + (float)rng.NextDouble() * 1.2f;
        transform.localScale = Vector3.one * s;
        OrbitScenery.Part(transform, PrimitiveType.Cube, Vector3.zero, new Vector3(1.4f, 1.4f, 2f), foil);
        for (int side = -1; side <= 1; side += 2)
        {
            OrbitScenery.Part(transform, PrimitiveType.Cylinder, new Vector3(side * 1.3f, 0f, 0f), new Vector3(0.12f, 0.6f, 0.12f), truss, Quaternion.Euler(0f, 0f, 90f));
            OrbitScenery.Part(transform, PrimitiveType.Cube, new Vector3(side * 3.6f, 0f, 0f), new Vector3(3.8f, 0.06f, 1.6f), panel);
        }
        OrbitScenery.Part(transform, PrimitiveType.Sphere, new Vector3(0f, 0f, 1.25f), new Vector3(1.3f, 1.3f, 0.35f), white);
        OrbitScenery.Part(transform, PrimitiveType.Cylinder, new Vector3(0f, 0.95f, -0.4f), new Vector3(0.05f, 0.3f, 0.05f), truss);
        blink = OrbitScenery.Part(transform, PrimitiveType.Sphere, new Vector3(0f, 1.3f, -0.4f), Vector3.one * 0.22f, red);
    }

    private void BuildStation()
    {
        // A long truss with paired solar arrays, and a row of modules across the middle
        transform.localScale = Vector3.one * 2.2f;
        OrbitScenery.Part(transform, PrimitiveType.Cube, Vector3.zero, new Vector3(16f, 0.5f, 0.5f), truss);
        foreach (float x in new[] { -7f, -4.5f, 4.5f, 7f })
            for (int side = -1; side <= 1; side += 2)
                OrbitScenery.Part(transform, PrimitiveType.Cube, new Vector3(x, 0f, side * 3.4f), new Vector3(1.8f, 0.05f, 6f), panel);
        OrbitScenery.Part(transform, PrimitiveType.Cylinder, new Vector3(0f, 0f, 1.5f), new Vector3(1.1f, 2.6f, 1.1f), white, Quaternion.Euler(90f, 0f, 0f));
        OrbitScenery.Part(transform, PrimitiveType.Cylinder, new Vector3(0f, 0f, -2.2f), new Vector3(1.0f, 1.6f, 1.0f), white, Quaternion.Euler(90f, 0f, 0f));
        OrbitScenery.Part(transform, PrimitiveType.Cylinder, new Vector3(0f, 0.9f, 0.4f), new Vector3(0.9f, 0.9f, 0.9f), foil);
        foreach (float x in new[] { -2.5f, 2.5f })
            OrbitScenery.Part(transform, PrimitiveType.Cube, new Vector3(x, -0.6f, 0f), new Vector3(1.8f, 0.05f, 1.2f), white);
        blink = OrbitScenery.Part(transform, PrimitiveType.Sphere, new Vector3(8f, 0.4f, 0f), Vector3.one * 0.3f, red);
    }

    // A fresh straight pass across the sky around the board, never through the camera's space
    private void NewPass(bool first)
    {
        float miss = (station ? 90f : 70f) + (float)rng.NextDouble() * 100f;   // closest approach to the board
        float heading = (float)rng.NextDouble() * Mathf.PI * 2f;
        Vector3 dir = new Vector3(Mathf.Cos(heading), ((float)rng.NextDouble() - 0.5f) * 0.25f, Mathf.Sin(heading)).normalized;
        Vector3 side = Vector3.Cross(dir, Vector3.up).normalized;
        Vector3 closest = side * miss * (rng.NextDouble() < 0.5 ? -1f : 1f) + Vector3.up * (-10f + (float)rng.NextDouble() * 50f);
        length = 520f;
        start = closest - dir * length * 0.5f;
        float speed = station ? 9f + (float)rng.NextDouble() * 5f : 22f + (float)rng.NextDouble() * 30f;
        velocity = dir * speed;
        travelled = first ? (float)rng.NextDouble() * length : 0f;
        spin = station ? 0f : ((float)rng.NextDouble() - 0.5f) * 30f;
        transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, (float)rng.NextDouble() * 360f);
    }

    private void Update()
    {
        travelled += velocity.magnitude * Time.deltaTime;
        if (travelled > length) NewPass(first: false);
        transform.localPosition = start + velocity.normalized * travelled;
        if (spin != 0f) transform.Rotate(0f, 0f, spin * Time.deltaTime, Space.Self);
        if (blink != null) blink.gameObject.SetActive(Mathf.Repeat(Time.time + transform.GetInstanceID() * 0.37f, 1.2f) < 0.15f);
    }

    // Solar cells: deep blue squares with thin silver lines between them
    private static Texture2D PanelTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            bool line = x % 16 == 0 || y % 8 == 0;
            Color c = line ? new Color(0.7f, 0.72f, 0.78f) : Color.Lerp(new Color(0.06f, 0.12f, 0.4f), new Color(0.12f, 0.25f, 0.6f), (float)y / size);
            c.a = 1f;
            px[y * size + x] = c;
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}

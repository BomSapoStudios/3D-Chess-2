using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Graveyard backdrop: a moonlit cemetery far below the board.
//
//  - GraveyardSky: the 360° sky. Deep violet night, a huge green-tinged moon behind thin
//    purple clouds, a skyline of bare trees, a church spire and a crooked gate.
//  - GraveyardScenery: dark earth with grave mounds, tombstones and crosses edged in neon
//    green, an iron fence, bare trees, creeping ground mist, floating will-o'-wisps and the
//    odd bat flapping past.
//
// Everything sits well away from the board so the board stays the focus.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural graveyard night sky. Pure C# so it can run on a worker thread.</summary>
public static class GraveyardSky
{
    // Matches the fog so the far graves melt into the mist
    public static readonly Color HazeColor = new Color(0.08f, 0.06f, 0.15f);

    public const float MoonU = 0.27f;           // ahead of the default White view (+x)
    private const float MoonV = 0.36f;
    private const float MoonR = 0.07f;

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        var rng = new System.Random(seed);
        const float horizon = 0.5f;

        // Skyline: rolling hills with bare trees, and a church spire off to one side
        var hills = new float[width];
        var trees = new float[width];
        for (int x = 0; x < width; x++)
        {
            float u = (x + 0.5f) / width;
            hills[x] = 0.012f + 0.01f * (0.5f + 0.5f * Mathf.Sin(u * Mathf.PI * 2f * 5f + 1f)) + 0.006f * Mathf.Sin(u * Mathf.PI * 2f * 13f);
        }
        // Trees: a trunk and a few crooked branches each, drawn as heights per column
        for (int t = 0; t < 26; t++)
        {
            float cu = (float)rng.NextDouble();
            float h = 0.04f + (float)rng.NextDouble() * 0.05f;
            float trunk = 0.0016f + (float)rng.NextDouble() * 0.0012f;
            int cx = (int)(cu * width);
            for (int dx = -(int)(0.025f * width); dx <= (int)(0.025f * width); dx++)
            {
                int x = ((cx + dx) % width + width) % width;
                float d = Mathf.Abs(dx / (float)width);
                float hh = 0f;
                if (d < trunk) hh = h;
                // Branches: spikes that fan out and get shorter away from the trunk
                float branch = Mathf.Abs(Mathf.Sin(dx * 0.9f + t)) ;
                if (d < 0.022f && branch > 0.82f) hh = Mathf.Max(hh, h * (0.95f - d / 0.03f) * (0.7f + 0.3f * branch));
                trees[x] = Mathf.Max(trees[x], hh);
            }
        }
        // A church spire with a cross
        int spire = (int)(0.42f * width);
        for (int dx = -14; dx <= 14; dx++)
        {
            int x = spire + dx;
            float s = Mathf.Abs(dx) <= 10 ? 0.07f : 0f;
            float tip = Mathf.Abs(dx) <= 3 ? 0.1f - Mathf.Abs(dx) * 0.006f : 0f;
            trees[x] = Mathf.Max(trees[x], Mathf.Max(s, tip));
        }

        var stars = new HashSet<int>();
        for (int k = 0; k < 1500; k++) stars.Add(rng.Next((int)(height * 0.45f)) * width + rng.Next(width));

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color c;
                if (v < horizon)
                {
                    float t = v / horizon;
                    c = Color.Lerp(new Color(0.03f, 0.01f, 0.08f), new Color(0.2f, 0.12f, 0.3f), Mathf.Pow(t, 1.6f));
                    if (stars.Contains(y * width + x)) c += Color.white * (0.45f * (1f - t));

                    // The moon: big and pale green, with a soft halo and darker seas
                    float du = Wrap(u - MoonU) * 2f, dv = v - MoonV;
                    float d = Mathf.Sqrt(du * du + dv * dv);
                    c += new Color(0.35f, 0.55f, 0.35f) * (0.6f * Mathf.Exp(-d * d / (MoonR * MoonR * 5f)));
                    if (d < MoonR)
                    {
                        float seas = Mathf.PerlinNoise(du * 40f + 3f, dv * 40f + 7f);
                        Color moon = Color.Lerp(new Color(0.88f, 1f, 0.82f), new Color(0.6f, 0.75f, 0.58f), Mathf.Clamp01((seas - 0.45f) * 2.5f));
                        c = Color.Lerp(c, moon, Mathf.Clamp01((MoonR - d) / 0.002f));
                    }

                    // Thin clouds drifting across, lit purple at their edges
                    float cloud = Mathf.PerlinNoise(u * 14f, v * 40f + 2f) * Mathf.PerlinNoise(u * 5f + 9f, v * 9f);
                    float band = Mathf.Exp(-Mathf.Pow((v - 0.37f) / 0.06f, 2f));
                    c = Color.Lerp(c, new Color(0.25f, 0.15f, 0.35f), Mathf.Clamp01((cloud - 0.22f) * 4f) * band * 0.85f);

                    // Skyline silhouettes with a faint green rim from the moon
                    float top = horizon - Mathf.Max(hills[x], trees[x]);
                    if (v > top)
                    {
                        float rim = Mathf.Clamp01(1f - (v - top) * height / 2f);
                        c = Color.Lerp(new Color(0.02f, 0.01f, 0.04f), new Color(0.4f, 1f, 0.5f), rim * 0.35f);
                    }
                }
                else
                {
                    // Below the horizon: mist over dark ground
                    float below = v - horizon;
                    c = Color.Lerp(HazeColor * 1.4f, new Color(0.02f, 0.02f, 0.03f), Mathf.Clamp01(below * 5f));
                }
                result[(height - 1 - y) * width + x] = new Color32(B(c.r), B(c.g), B(c.b), 255); // Unity textures start at the bottom row
            }
        }
        return result;
    }

    private static float Wrap(float d) { if (d > 0.5f) d -= 1f; if (d < -0.5f) d += 1f; return d; }
    private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}

public static class GraveyardScenery
{
    public const float GroundY = -30f;

    public static void Build(Transform root)
    {
        var rng = new System.Random(1313);
        Material earth = new Material(Shader.Find("Standard")) { color = new Color(0.03f, 0.03f, 0.04f) };
        earth.SetFloat("_Glossiness", 0.05f);
        OrbitScenery.Part(root, PrimitiveType.Cylinder, new Vector3(0f, GroundY, 0f), new Vector3(800f, 0.05f, 800f), earth).name = "Earth";

        Material stone = OrbitScenery.Lit(new Color(0.32f, 0.3f, 0.36f), new Color(0.02f, 0.03f, 0.03f), 0.05f, 0.25f);
        Material mound = OrbitScenery.Lit(new Color(0.1f, 0.08f, 0.08f), Color.black, 0f, 0.1f);
        Material neon = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.4f, 1f, 0.45f) };
        Material violet = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.75f, 0.4f, 1f) };
        Material iron = OrbitScenery.Lit(new Color(0.08f, 0.08f, 0.1f), Color.black, 0.6f, 0.4f);
        Material bark = OrbitScenery.Lit(new Color(0.06f, 0.05f, 0.06f), Color.black, 0f, 0.15f);

        var still = new GameObject("Graves").transform;
        still.SetParent(root, false);

        // Rows of graves in a ring around the board, each facing it
        for (int row = 0; row < 3; row++)
        {
            float dist = 48f + row * 18f;
            int count = 14 + row * 5;
            for (int i = 0; i < count; i++)
            {
                if (rng.NextDouble() < 0.18) continue; // gaps
                float a = (i + (float)rng.NextDouble() * 0.4f + row * 0.3f) / count * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(a) * dist, GroundY, Mathf.Sin(a) * dist);
                Quaternion face = Quaternion.LookRotation(new Vector3(-at.x, 0f, -at.z)) * Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 14f, ((float)rng.NextDouble() - 0.5f) * 20f, ((float)rng.NextDouble() - 0.5f) * 12f);
                var grave = new GameObject("Grave").transform;
                grave.SetParent(still, false);
                grave.SetPositionAndRotation(at, face);
                float s = 1.6f + (float)rng.NextDouble() * 1.4f;
                grave.localScale = Vector3.one * s;

                // The mound in front of the stone
                Part(grave, PrimitiveType.Sphere, new Vector3(0f, 0f, 1.5f), new Vector3(1.6f, 0.5f, 2.8f), mound);
                int kind = rng.Next(4);
                if (kind == 0)
                {
                    // Cross
                    Part(grave, PrimitiveType.Cube, new Vector3(0f, 1.6f, 0f), new Vector3(0.35f, 3.2f, 0.35f), stone);
                    Part(grave, PrimitiveType.Cube, new Vector3(0f, 2.3f, 0f), new Vector3(1.6f, 0.35f, 0.35f), stone);
                    Part(grave, PrimitiveType.Cube, new Vector3(0f, 2.3f, 0.19f), new Vector3(1.2f, 0.12f, 0.02f), neon);
                    Part(grave, PrimitiveType.Cube, new Vector3(0f, 1.6f, 0.19f), new Vector3(0.12f, 2.6f, 0.02f), neon);
                }
                else
                {
                    // Rounded headstone with a glowing edge line and an epitaph bar
                    Part(grave, PrimitiveType.Cube, new Vector3(0f, 1f, 0f), new Vector3(1.6f, 2f, 0.35f), stone);
                    Part(grave, PrimitiveType.Cylinder, new Vector3(0f, 2f, 0f), new Vector3(1.6f, 0.175f, 1.6f), stone, Quaternion.Euler(90f, 0f, 0f));
                    Material glow = rng.NextDouble() < 0.25 ? violet : neon;
                    Part(grave, PrimitiveType.Cube, new Vector3(0f, 1.6f, 0.19f), new Vector3(1.0f, 0.14f, 0.02f), glow);
                    Part(grave, PrimitiveType.Cylinder, new Vector3(0f, 2f, 0f), new Vector3(1.75f, 0.12f, 1.75f), glow, Quaternion.Euler(90f, 0f, 0f)); // glowing rim round the top
                    Part(grave, PrimitiveType.Cube, new Vector3(0f, 1.2f, 0.19f), new Vector3(0.7f, 0.1f, 0.02f), glow);
                    if (kind == 3) Part(grave, PrimitiveType.Cube, new Vector3(0f, 0.8f, 0.19f), new Vector3(0.75f, 0.06f, 0.02f), glow);
                }
            }
        }

        // An iron fence on the far side of the graves
        float fence = 100f;
        for (int i = 0; i < 90; i++)
        {
            float a = i / 90f * Mathf.PI * 2f;
            Vector3 at = new Vector3(Mathf.Cos(a) * fence, GroundY, Mathf.Sin(a) * fence);
            Part(still, PrimitiveType.Cylinder, at + Vector3.up * 3f, new Vector3(0.18f, 3f, 0.18f), iron);
            Part(still, PrimitiveType.Cube, at + Vector3.up * 6.2f, new Vector3(0.4f, 0.4f, 0.4f), iron, Quaternion.Euler(45f, 0f, 45f));
        }
        for (int r = 0; r < 2; r++)
            for (int i = 0; i < 90; i++)
            {
                float a = (i + 0.5f) / 90f * Mathf.PI * 2f;
                Vector3 at = new Vector3(Mathf.Cos(a) * fence, GroundY + 1.5f + r * 3.6f, Mathf.Sin(a) * fence);
                Part(still, PrimitiveType.Cube, at, new Vector3(0.12f, 0.12f, fence * Mathf.PI * 2f / 90f), iron, Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))));
            }

        // Bare trees between the rows
        for (int i = 0; i < 9; i++)
        {
            float a = (i + (float)rng.NextDouble() * 0.5f) / 9f * Mathf.PI * 2f;
            float dist = 60f + (float)rng.NextDouble() * 30f;
            BareTree(still, new Vector3(Mathf.Cos(a) * dist, GroundY, Mathf.Sin(a) * dist), bark, rng);
        }
        StaticBatchingUtility.Combine(still.gameObject);

        root.gameObject.AddComponent<GraveMist>();
        root.gameObject.AddComponent<GraveWisps>();
        root.gameObject.AddComponent<GraveBats>();
    }

    // A crooked trunk with a few forking branches
    private static void BareTree(Transform parent, Vector3 at, Material bark, System.Random rng)
    {
        var tree = new GameObject("Tree").transform;
        tree.SetParent(parent, false);
        tree.position = at;
        tree.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        float h = 16f + (float)rng.NextDouble() * 10f;
        Part(tree, PrimitiveType.Cylinder, new Vector3(0f, h * 0.5f, 0f), new Vector3(1.4f, h * 0.5f, 1.4f), bark, Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 8f, 0f, ((float)rng.NextDouble() - 0.5f) * 8f));
        for (int b = 0; b < 6; b++)
        {
            float y = h * (0.45f + 0.5f * (float)rng.NextDouble());
            float len = 4f + (float)rng.NextDouble() * 6f;
            float yaw = (float)rng.NextDouble() * 360f, tilt = 30f + (float)rng.NextDouble() * 40f;
            Quaternion q = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(0f, 0f, tilt);
            Part(tree, PrimitiveType.Cylinder, new Vector3(0f, y, 0f) + q * Vector3.up * (len * 0.5f), new Vector3(0.45f, len * 0.5f, 0.45f), bark, q);
            Quaternion q2 = q * Quaternion.Euler(0f, 0f, -45f);
            Vector3 end = new Vector3(0f, y, 0f) + q * Vector3.up * len;
            Part(tree, PrimitiveType.Cylinder, end + q2 * Vector3.up * (len * 0.3f), new Vector3(0.25f, len * 0.3f, 0.25f), bark, q2);
        }
    }

    private static Transform Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        => OrbitScenery.Part(parent, type, pos, scale, mat, rot);
}

/// <summary>Low mist creeping over the graves</summary>
public class GraveMist : MonoBehaviour
{
    private void Start()
    {
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(14f, 26f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.7f, 0.75f, 0.16f), new Color(0.55f, 0.5f, 0.75f, 0.12f));
        main.maxParticles = 160;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 8f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 75f;
        shape.donutRadius = 35f;
        shape.rotation = new Vector3(90f, 0f, 0f);
        shape.position = new Vector3(0f, GraveyardScenery.GroundY + 2f, 0f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
        vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = Soft() };
        ps.Play();
    }

    // A very soft puff, so the mist has no edges
    private static Texture2D Soft()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)), 2.5f);
            px[y * size + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}

/// <summary>Will-o'-wisps: pale green lights bobbing and drifting among the graves</summary>
public class GraveWisps : MonoBehaviour
{
    private readonly List<(Transform t, Vector3 home, float phase, float speed)> wisps = new List<(Transform, Vector3, float, float)>();

    private void Start()
    {
        var rng = new System.Random(66);
        var mat = new Material(Shader.Find("Sprites/Default")) { mainTexture = NeonTheme.Circle.texture };
        for (int i = 0; i < 12; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 45f + (float)rng.NextDouble() * 45f;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(q.GetComponent<Collider>());
            q.name = "Wisp";
            q.SetParent(transform, false);
            var r = q.GetComponent<MeshRenderer>();
            r.material = mat;
            r.material.color = i % 3 == 0 ? new Color(0.7f, 0.5f, 1f, 0.9f) : new Color(0.55f, 1f, 0.6f, 0.9f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            wisps.Add((q, new Vector3(Mathf.Cos(a) * d, GraveyardScenery.GroundY + 4f + (float)rng.NextDouble() * 6f, Mathf.Sin(a) * d), (float)rng.NextDouble() * 10f, 0.3f + (float)rng.NextDouble() * 0.4f));
        }
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        float t = Time.time;
        foreach (var w in wisps)
        {
            Vector3 p = w.home + new Vector3(Mathf.Sin(t * w.speed + w.phase) * 6f, Mathf.Sin(t * 1.3f + w.phase) * 1.5f, Mathf.Cos(t * w.speed * 0.8f + w.phase) * 6f);
            w.t.position = p;
            float flicker = 1.6f + 0.5f * Mathf.Sin(t * 7f + w.phase * 3f) + 0.3f * Mathf.Sin(t * 13f + w.phase);
            w.t.localScale = Vector3.one * flicker;
            if (cam != null) w.t.rotation = Quaternion.LookRotation(p - cam.transform.position);
        }
    }
}

/// <summary>Now and then a few bats flap across the sky</summary>
public class GraveBats : MonoBehaviour
{
    private readonly List<(Transform body, Transform l, Transform r, float phase)> bats = new List<(Transform, Transform, Transform, float)>();
    private Vector3 from, to;
    private float started, next, duration = 9f;
    private bool flying;

    private void Start()
    {
        var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.03f, 0.02f, 0.05f) };
        for (int i = 0; i < 5; i++)
        {
            var body = new GameObject("Bat").transform;
            body.SetParent(transform, false);
            OrbitScenery.Part(body, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.6f, 0.5f, 1f), mat);
            Transform Wing(float side)
            {
                var pivot = new GameObject("Wing").transform;
                pivot.SetParent(body, false);
                OrbitScenery.Part(pivot, PrimitiveType.Cube, new Vector3(side * 1.1f, 0f, 0f), new Vector3(2.2f, 0.05f, 1f), mat);
                return pivot;
            }
            bats.Add((body, Wing(-1f), Wing(1f), i * 0.7f));
            body.gameObject.SetActive(false);
        }
        next = Time.time + 6f;
    }

    private void Update()
    {
        float t = Time.time;
        if (!flying)
        {
            if (t < next) return;
            float a = Random.Range(0f, Mathf.PI * 2f);
            from = new Vector3(Mathf.Cos(a) * 110f, Random.Range(10f, 30f), Mathf.Sin(a) * 110f);
            to = new Vector3(-from.x, from.y + Random.Range(-8f, 8f), -from.z) + new Vector3(Random.Range(-40f, 40f), 0f, Random.Range(-40f, 40f));
            started = t;
            flying = true;
            foreach (var b in bats) b.body.gameObject.SetActive(true);
        }
        float k = (t - started) / duration;
        if (k >= 1f)
        {
            flying = false;
            foreach (var b in bats) b.body.gameObject.SetActive(false);
            next = t + Random.Range(15f, 30f);
            return;
        }
        Vector3 dir = (to - from).normalized;
        for (int i = 0; i < bats.Count; i++)
        {
            var b = bats[i];
            Vector3 offset = new Vector3(Mathf.Sin(t * 2f + b.phase) * 3f, Mathf.Sin(t * 3f + b.phase * 2f) * 1.5f + i * 1.2f, i * 2.5f - 5f);
            b.body.position = Vector3.Lerp(from, to, k) + Quaternion.LookRotation(dir) * offset;
            b.body.rotation = Quaternion.LookRotation(dir);
            float flap = Mathf.Sin(t * 18f + b.phase) * 40f;
            b.l.localRotation = Quaternion.Euler(0f, 0f, flap);
            b.r.localRotation = Quaternion.Euler(0f, 0f, -flap);
        }
    }
}

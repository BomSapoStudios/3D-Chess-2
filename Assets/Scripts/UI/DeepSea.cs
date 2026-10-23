using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Deep Sea backdrop: the board hangs in dark open water above the sea floor.
//
//  - DeepSeaSky: the 360° water. Faint light and sun shafts from the surface far above,
//    fading to black in the depths, with drifting specks.
//  - DeepSeaScenery: a dim sea floor with rocks, glowing corals and vents, marine snow
//    drifting down, and bubbles.
//  - SeaFish: schools of neon tetras, angelfish, jellyfish, and two kinds of predator
//    (barracuda and anglerfish) that now and then chase down and gulp a smaller fish.
//  - SeaTentacle: very rarely a giant tentacle rises out of the deep, wraps a fish and
//    drags it down.
//
// Everything keeps well away from the board so the board stays the focus.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Procedural deep-water sky. Pure C# so it can run on a worker thread.</summary>
public static class DeepSeaSky
{
    // Matches the fog so far fish fade into the water
    public static readonly Color HazeColor = new Color(0.02f, 0.09f, 0.15f);

    public static Color32[] Generate(int width, int height, int seed)
    {
        var result = new Color32[width * height];
        var rng = new System.Random(seed);
        var specks = new HashSet<int>();
        for (int k = 0; k < 2500; k++) specks.Add(rng.Next(height) * width + rng.Next(width));

        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height; // 0 straight up, 1 straight down
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                Color c;
                if (v < 0.5f)
                {
                    float t = v / 0.5f; // 0 up, 1 at the horizon
                    c = Color.Lerp(new Color(0.12f, 0.45f, 0.6f), HazeColor, Mathf.Pow(t, 0.55f));
                    // Shafts of light slanting down from the surface, flickering in width
                    float shafts = 0f;
                    for (int s = 0; s < 3; s++)
                    {
                        float k = 9 + s * 7;
                        float w = Mathf.Sin((u * k + v * 1.5f + s * 0.37f) * Mathf.PI * 2f) * 0.5f + 0.5f;
                        shafts += Mathf.Pow(w, 14f + s * 6f) * (0.5f - 0.12f * s);
                    }
                    c += new Color(0.25f, 0.55f, 0.6f) * shafts * Mathf.Pow(1f - t, 1.6f) * 0.55f;
                }
                else
                {
                    float t = (v - 0.5f) / 0.5f; // 0 at the horizon, 1 straight down
                    c = Color.Lerp(HazeColor, new Color(0f, 0.01f, 0.025f), Mathf.Pow(t, 0.5f));
                }
                if (specks.Contains(y * width + x)) c += new Color(0.25f, 0.35f, 0.35f) * (0.3f + 0.4f * (1f - v));
                result[(height - 1 - y) * width + x] = new Color32(B(c.r), B(c.g), B(c.b), 255); // Unity textures start at the bottom row
            }
        }
        return result;
    }

    private static byte B(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
}

public static class DeepSeaScenery
{
    public const float FloorY = -70f;

    public static void Build(Transform root)
    {
        var rng = new System.Random(2718);

        // Sea floor: dark silt with rocks, glowing corals and a couple of bubbling vents
        // Plain Standard so the murk (fog) swallows the far floor
        var silt = new Material(Shader.Find("Standard")) { color = new Color(0.05f, 0.08f, 0.1f) };
        silt.SetFloat("_Glossiness", 0.05f);
        OrbitScenery.Part(root, PrimitiveType.Cylinder, new Vector3(0f, FloorY, 0f), new Vector3(700f, 0.05f, 700f), silt).name = "Sea Floor";
        Material rock = OrbitScenery.Lit(new Color(0.1f, 0.12f, 0.15f), Color.black, 0.1f, 0.3f);
        Material[] glow =
        {
            OrbitScenery.Lit(new Color(1f, 0.3f, 0.7f), new Color(1f, 0.25f, 0.65f) * 0.9f, 0f, 0.5f),
            OrbitScenery.Lit(new Color(0.2f, 1f, 0.9f), new Color(0.1f, 0.9f, 0.8f) * 0.9f, 0f, 0.5f),
            OrbitScenery.Lit(new Color(0.7f, 1f, 0.2f), new Color(0.5f, 0.9f, 0.1f) * 0.8f, 0f, 0.5f),
            OrbitScenery.Lit(new Color(1f, 0.6f, 0.2f), new Color(1f, 0.45f, 0.1f) * 0.8f, 0f, 0.5f),
        };
        var floor = new GameObject("Reef").transform;
        floor.SetParent(root, false);
        for (int i = 0; i < 40; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 30f + (float)rng.NextDouble() * 150f;
            Vector3 at = new Vector3(Mathf.Cos(a) * d, FloorY, Mathf.Sin(a) * d);
            float s = 3f + (float)rng.NextDouble() * 9f;
            OrbitScenery.Part(floor, PrimitiveType.Sphere, at, new Vector3(s * 1.4f, s * 0.7f, s), rock, Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f));
            if (rng.NextDouble() < 0.7)
            {
                // A cluster of glowing coral fingers on the rock
                Material m = glow[rng.Next(glow.Length)];
                int fingers = 3 + rng.Next(5);
                for (int f = 0; f < fingers; f++)
                {
                    Vector3 off = new Vector3(((float)rng.NextDouble() - 0.5f) * s, s * 0.3f, ((float)rng.NextDouble() - 0.5f) * s * 0.7f);
                    float h = 1.5f + (float)rng.NextDouble() * 3f;
                    OrbitScenery.Part(floor, PrimitiveType.Capsule, at + off + Vector3.up * h * 0.5f, new Vector3(0.5f, h * 0.5f, 0.5f), m,
                        Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 40f, 0f, ((float)rng.NextDouble() - 0.5f) * 40f));
                }
            }
        }
        StaticBatchingUtility.Combine(floor.gameObject);

        root.gameObject.AddComponent<SeaSnow>();
        var vents = root.gameObject.AddComponent<SeaBubbles>();
        vents.Init(rng);

        // The fish
        var life = root.gameObject.AddComponent<SeaLife>();
        life.Build(rng);
    }
}

/// <summary>Owns the fish: spawns them, sets up hunts, and every so often calls up the tentacle</summary>
public class SeaLife : MonoBehaviour
{
    public readonly List<SeaFish> fish = new List<SeaFish>();
    private float nextHunt, nextTentacle;
    private System.Random rng;
    private SeaTentacle tentacle;
    public SeaBubbles bubbles;

    public void Build(System.Random random)
    {
        rng = random;
        bubbles = GetComponent<SeaBubbles>();
        var holder = new GameObject("Fish").transform;
        holder.SetParent(transform, false);

        // Schools of neon tetras: a leader and followers holding loose formation behind it
        Color[] tetra = { new Color(0.2f, 1f, 0.95f), new Color(1f, 0.35f, 0.75f), new Color(0.75f, 1f, 0.25f) };
        for (int s = 0; s < 3; s++)
        {
            SeaFish leader = Spawn(holder, SeaFish.Kind.Tetra, tetra[s], null, Vector3.zero);
            for (int f = 0; f < 10; f++)
            {
                Vector3 offset = new Vector3(((float)rng.NextDouble() - 0.5f) * 7f, ((float)rng.NextDouble() - 0.5f) * 4f, -1.5f - (float)rng.NextDouble() * 8f);
                Spawn(holder, SeaFish.Kind.Tetra, tetra[s], leader, offset);
            }
        }
        Color[] angel = { new Color(1f, 0.85f, 0.2f), new Color(0.75f, 0.45f, 1f), new Color(1f, 0.55f, 0.25f) };
        for (int i = 0; i < 6; i++) Spawn(holder, SeaFish.Kind.Angelfish, angel[i % angel.Length], null, Vector3.zero);
        for (int i = 0; i < 5; i++) Spawn(holder, SeaFish.Kind.Jellyfish, i % 2 == 0 ? new Color(0.6f, 0.4f, 1f) : new Color(0.3f, 0.9f, 1f), null, Vector3.zero);
        for (int i = 0; i < 2; i++) Spawn(holder, SeaFish.Kind.Barracuda, new Color(0.65f, 0.75f, 0.85f), null, Vector3.zero);
        Spawn(holder, SeaFish.Kind.Anglerfish, new Color(0.18f, 0.12f, 0.2f), null, Vector3.zero);

        var t = new GameObject("Giant Tentacle");
        t.transform.SetParent(transform, false);
        tentacle = t.AddComponent<SeaTentacle>();

        nextHunt = Time.time + 8f;
        nextTentacle = Time.time + 40f + (float)rng.NextDouble() * 20f; // the first one comes fairly soon, then it's rare
#if DEVELOPMENT_BUILD
        if (PlayerPrefs.GetInt("DebugTentacle", 0) == 1) nextTentacle = Time.time + 6f;
#endif
    }

    private SeaFish Spawn(Transform holder, SeaFish.Kind kind, Color color, SeaFish leader, Vector3 offset)
    {
        var go = new GameObject(kind.ToString());
        go.transform.SetParent(holder, false);
        var f = go.AddComponent<SeaFish>();
        f.Init(this, kind, color, rng.Next(), leader, offset);
        fish.Add(f);
        return f;
    }

    private void Update()
    {
        if (Time.time >= nextHunt)
        {
            nextHunt = Time.time + 9f + (float)rng.NextDouble() * 12f;
            StartHunt();
        }
        if (Time.time >= nextTentacle && !tentacle.Busy)
        {
            if (StartTentacle()) nextTentacle = Time.time + 90f + (float)rng.NextDouble() * 80f;
            else nextTentacle = Time.time + 5f;
        }
    }

    private void StartHunt()
    {
        var hunters = fish.FindAll(f => f.IsPredator && f.Free);
        if (hunters.Count == 0) return;
        SeaFish hunter = hunters[rng.Next(hunters.Count)];
        SeaFish best = null;
        float bestD = 80f;
        foreach (SeaFish f in fish)
        {
            if (!f.IsPrey || !f.Free) continue;
            float d = Vector3.Distance(f.transform.position, hunter.transform.position);
            if (d < bestD) { bestD = d; best = f; }
        }
        if (best != null) hunter.Hunt(best);
    }

    private bool StartTentacle()
    {
        // A fish far enough out that the tentacle rises well clear of the board
        var choices = fish.FindAll(f => f.Free && !f.chased && f.kind != SeaFish.Kind.Jellyfish && new Vector2(f.transform.position.x, f.transform.position.z).magnitude > 45f);
        if (choices.Count == 0) return false;
        tentacle.Grab(choices[rng.Next(choices.Count)], this);
        return true;
    }
}

/// <summary>A fish (or jellyfish). Wanders around the board; schools follow their leader; predators hunt.</summary>
public class SeaFish : MonoBehaviour
{
    public enum Kind { Tetra, Angelfish, Jellyfish, Barracuda, Anglerfish }
    public Kind kind;
    public bool IsPredator => kind == Kind.Barracuda || kind == Kind.Anglerfish;
    public bool IsPrey => kind == Kind.Tetra || kind == Kind.Angelfish;
    public bool Free => !held && !eaten && prey == null && !away;

    private SeaLife life;
    private System.Random rng;
    private SeaFish leader;
    private Vector3 offset, target, velocity;
    private float speed, turn, phase, size;
    private Transform tail, body, mouth;
    private Transform[] tendrils;
    private SeaFish prey;
    private bool eaten, held, away;
    private float gulp;

    private const float MinRadius = 40f, MaxRadius = 115f;

    public void Init(SeaLife owner, Kind k, Color color, int seed, SeaFish lead, Vector3 off)
    {
        life = owner;
        kind = k;
        rng = new System.Random(seed);
        leader = lead;
        offset = off;
        phase = (float)rng.NextDouble() * 10f;
        body = new GameObject("Body").transform;
        body.SetParent(transform, false);
        switch (kind)
        {
            case Kind.Tetra: BuildTetra(color); speed = 9f; turn = 70f; size = 1f; break;
            case Kind.Angelfish: BuildAngelfish(color); speed = 5f; turn = 45f; size = 2f; break;
            case Kind.Jellyfish: BuildJellyfish(color); speed = 1.2f; turn = 15f; size = 2.5f; break;
            case Kind.Barracuda: BuildBarracuda(color); speed = 7f; turn = 40f; size = 4f; break;
            default: BuildAngler(color); speed = 4f; turn = 35f; size = 3f; break;
        }
        transform.position = RandomSpot();
        if (leader != null) transform.position = leader.transform.position + offset;
        target = RandomSpot();
        transform.rotation = Quaternion.LookRotation(target - transform.position);
    }

    private Vector3 RandomSpot()
    {
        float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = MinRadius + (float)rng.NextDouble() * (MaxRadius - MinRadius);
        float y = kind == Kind.Anglerfish ? -36f + (float)rng.NextDouble() * 14f : -30f + (float)rng.NextDouble() * 48f;
        return new Vector3(Mathf.Cos(a) * d, y, Mathf.Sin(a) * d);
    }

    // ───────── Bodies (each faces +Z) ─────────

    private static Material Mat(Color c, float glow, float metal = 0.2f, float smooth = 0.6f) => OrbitScenery.Lit(c, c * glow, metal, smooth);

    private Transform P(Transform parent, PrimitiveType t, Vector3 pos, Vector3 scale, Material m, Quaternion? rot = null) => OrbitScenery.Part(parent, t, pos, scale, m, rot);

    private void BuildTetra(Color c)
    {
        Material skin = Mat(new Color(0.55f, 0.6f, 0.7f), 0.1f, 0.6f, 0.8f), stripe = Mat(c, 1.2f);
        P(body, PrimitiveType.Capsule, Vector3.zero, new Vector3(0.45f, 0.8f, 0.6f), skin, Quaternion.Euler(90f, 0f, 0f));
        P(body, PrimitiveType.Cube, new Vector3(0f, 0.06f, 0.05f), new Vector3(0.47f, 0.1f, 1.2f), stripe);
        P(body, PrimitiveType.Sphere, new Vector3(0.18f, 0.08f, 0.55f), Vector3.one * 0.14f, Mat(Color.black, 0f));
        P(body, PrimitiveType.Sphere, new Vector3(-0.18f, 0.08f, 0.55f), Vector3.one * 0.14f, Mat(Color.black, 0f));
        tail = new GameObject("Tail").transform;
        tail.SetParent(body, false);
        tail.localPosition = new Vector3(0f, 0f, -0.7f);
        P(tail, PrimitiveType.Cube, new Vector3(0f, 0f, -0.3f), new Vector3(0.05f, 0.6f, 0.55f), stripe, Quaternion.Euler(0f, 0f, 0f));
        mouth = body;
    }

    private void BuildAngelfish(Color c)
    {
        Material skin = Mat(c, 0.35f, 0.1f, 0.6f), dark = Mat(new Color(0.08f, 0.06f, 0.12f), 0f);
        P(body, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.6f, 2.2f, 2.2f), skin);
        P(body, PrimitiveType.Cube, new Vector3(0f, 1.6f, -0.5f), new Vector3(0.06f, 1.8f, 0.9f), skin, Quaternion.Euler(-35f, 0f, 0f));  // dorsal fin
        P(body, PrimitiveType.Cube, new Vector3(0f, -1.6f, -0.5f), new Vector3(0.06f, 1.8f, 0.9f), skin, Quaternion.Euler(35f, 0f, 0f)); // anal fin
        foreach (float z in new[] { 0.35f, -0.35f }) P(body, PrimitiveType.Cube, new Vector3(0f, 0f, z), new Vector3(0.62f, 2f, 0.16f), dark);
        P(body, PrimitiveType.Sphere, new Vector3(0.22f, 0.25f, 0.8f), Vector3.one * 0.22f, dark);
        P(body, PrimitiveType.Sphere, new Vector3(-0.22f, 0.25f, 0.8f), Vector3.one * 0.22f, dark);
        tail = new GameObject("Tail").transform;
        tail.SetParent(body, false);
        tail.localPosition = new Vector3(0f, 0f, -1.05f);
        P(tail, PrimitiveType.Cube, new Vector3(0f, 0f, -0.4f), new Vector3(0.06f, 1.2f, 0.8f), skin);
        mouth = body;
    }

    private void BuildJellyfish(Color c)
    {
        // See-through glowing bell and trailing tendrils (unlit, so it glows in the dark water)
        var bell = new Material(Shader.Find("Sprites/Default")) { color = new Color(c.r, c.g, c.b, 0.45f) };
        var strand = new Material(Shader.Find("Sprites/Default")) { color = new Color(c.r, c.g, c.b, 0.6f) };
        P(body, PrimitiveType.Sphere, Vector3.zero, new Vector3(3f, 1.8f, 3f), bell);
        P(body, PrimitiveType.Sphere, new Vector3(0f, -0.2f, 0f), new Vector3(1.4f, 0.9f, 1.4f), new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 1f, 1f, 0.5f) });
        tendrils = new Transform[8];
        for (int i = 0; i < tendrils.Length; i++)
        {
            float a = i * Mathf.PI * 2f / tendrils.Length;
            var pivot = new GameObject("Tendril").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(Mathf.Cos(a) * 1.1f, -0.4f, Mathf.Sin(a) * 1.1f);
            float len = 4f + (float)rng.NextDouble() * 4f;
            P(pivot, PrimitiveType.Cylinder, new Vector3(0f, -len * 0.5f, 0f), new Vector3(0.08f, len * 0.5f, 0.08f), strand);
            tendrils[i] = pivot;
        }
    }

    private void BuildBarracuda(Color c)
    {
        Material skin = Mat(c, 0.12f, 0.7f, 0.85f), back = Mat(new Color(0.15f, 0.22f, 0.32f), 0.05f, 0.5f, 0.7f);
        P(body, PrimitiveType.Capsule, Vector3.zero, new Vector3(1f, 3.6f, 1.2f), skin, Quaternion.Euler(90f, 0f, 0f));
        P(body, PrimitiveType.Capsule, new Vector3(0f, 0.35f, 0f), new Vector3(0.7f, 3.2f, 0.7f), back, Quaternion.Euler(90f, 0f, 0f));
        P(body, PrimitiveType.Cube, new Vector3(0f, 0.75f, -0.8f), new Vector3(0.06f, 0.9f, 0.9f), back, Quaternion.Euler(-30f, 0f, 0f));
        P(body, PrimitiveType.Sphere, new Vector3(0.36f, 0.15f, 2.7f), Vector3.one * 0.3f, Mat(new Color(1f, 0.2f, 0.2f), 1.5f));
        P(body, PrimitiveType.Sphere, new Vector3(-0.36f, 0.15f, 2.7f), Vector3.one * 0.3f, Mat(new Color(1f, 0.2f, 0.2f), 1.5f));
        // Long jaw with teeth
        P(body, PrimitiveType.Cube, new Vector3(0f, -0.25f, 3.3f), new Vector3(0.5f, 0.2f, 1.2f), skin);
        for (int i = 0; i < 4; i++) P(body, PrimitiveType.Cube, new Vector3(0f, -0.08f, 2.9f + i * 0.25f), new Vector3(0.45f, 0.15f, 0.05f), Mat(Color.white, 0.4f));
        tail = new GameObject("Tail").transform;
        tail.SetParent(body, false);
        tail.localPosition = new Vector3(0f, 0f, -3.3f);
        P(tail, PrimitiveType.Cube, new Vector3(0f, 0f, -0.5f), new Vector3(0.08f, 2f, 1f), back);
        mouth = new GameObject("Mouth").transform;
        mouth.SetParent(body, false);
        mouth.localPosition = new Vector3(0f, 0f, 3.6f);
    }

    private void BuildAngler(Color c)
    {
        Material skin = Mat(c, 0.05f, 0.1f, 0.4f), teeth = Mat(new Color(0.95f, 0.95f, 0.85f), 0.3f);
        P(body, PrimitiveType.Sphere, Vector3.zero, new Vector3(3.2f, 2.8f, 3.4f), skin);
        P(body, PrimitiveType.Sphere, new Vector3(0f, -0.5f, 1.3f), new Vector3(2.6f, 1.2f, 1.4f), Mat(new Color(0.02f, 0f, 0.02f), 0f)); // gaping mouth
        for (int i = 0; i < 9; i++)
        {
            float x = -1.0f + i * 0.25f;
            P(body, PrimitiveType.Cube, new Vector3(x, -0.15f, 1.85f), new Vector3(0.07f, 0.5f, 0.07f), teeth, Quaternion.Euler(10f, 0f, 0f));
            P(body, PrimitiveType.Cube, new Vector3(x + 0.12f, -0.9f, 1.75f), new Vector3(0.07f, 0.45f, 0.07f), teeth, Quaternion.Euler(-10f, 0f, 0f));
        }
        P(body, PrimitiveType.Sphere, new Vector3(0.9f, 0.7f, 1.2f), Vector3.one * 0.5f, Mat(new Color(0.8f, 0.9f, 1f), 0.6f));
        P(body, PrimitiveType.Sphere, new Vector3(-0.9f, 0.7f, 1.2f), Vector3.one * 0.5f, Mat(new Color(0.8f, 0.9f, 1f), 0.6f));
        // The lure: a stalk arching forward with a glowing bulb
        P(body, PrimitiveType.Cylinder, new Vector3(0f, 2f, 1.2f), new Vector3(0.1f, 1f, 0.1f), skin, Quaternion.Euler(35f, 0f, 0f));
        P(body, PrimitiveType.Sphere, new Vector3(0f, 2.75f, 2.35f), Vector3.one * 0.55f, new Material(Shader.Find("Unlit/Color")) { color = new Color(0.6f, 1f, 0.9f) });
        var halo = P(body, PrimitiveType.Quad, new Vector3(0f, 2.75f, 2.35f), Vector3.one * 3f, new Material(Shader.Find("Sprites/Default")) { mainTexture = NeonTheme.Circle.texture, color = new Color(0.4f, 1f, 0.85f, 0.25f) });
        halo.gameObject.AddComponent<SeaFaceCamera>();
        tail = new GameObject("Tail").transform;
        tail.SetParent(body, false);
        tail.localPosition = new Vector3(0f, 0f, -1.6f);
        P(tail, PrimitiveType.Cube, new Vector3(0f, 0f, -0.6f), new Vector3(0.08f, 1.6f, 1.2f), skin);
        mouth = new GameObject("Mouth").transform;
        mouth.SetParent(body, false);
        mouth.localPosition = new Vector3(0f, -0.4f, 1.8f);
    }

    // ───────── Behaviour ─────────

    public void Hunt(SeaFish victim) { prey = victim; }

    /// <summary>Held by the tentacle: stop swimming and let it move us</summary>
    public void Hold(bool on) { held = on; }

    /// <summary>Gone (eaten or dragged down): hide, then come back later from far away</summary>
    public void Vanish(float comeBackAfter)
    {
        eaten = true;
        held = false;
        body.gameObject.SetActive(false);
        Invoke(nameof(Return), comeBackAfter);
    }

    private void Return()
    {
        eaten = false;
        body.gameObject.SetActive(true);
        body.localScale = Vector3.one;
        transform.position = RandomSpot() * 1.2f;
        if (leader != null && !leader.eaten) transform.position = leader.transform.position + leader.transform.rotation * offset;
        target = RandomSpot();
    }

    private void Update()
    {
        float t = Time.time, dt = Time.deltaTime;
        if (eaten) return;

        // Tail beat (faster while chasing), and jellyfish pulsing
        if (tail != null) tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * (prey != null ? 16f : 7f) + phase) * 25f, 0f);
        if (kind == Kind.Jellyfish)
        {
            float pulse = Mathf.Sin(t * 1.6f + phase);
            body.localScale = new Vector3(1f + 0.12f * pulse, 1f - 0.1f * pulse, 1f + 0.12f * pulse);
            for (int i = 0; i < tendrils.Length; i++) tendrils[i].localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f + i + phase) * 12f, 0f, Mathf.Cos(t * 1.1f + i * 1.7f) * 12f);
        }
        if (gulp > 0f) { gulp -= dt; body.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(gulp / 0.5f * Mathf.PI)); }
        if (held) return;

        float spd = speed;
        Vector3 goal;
        if (prey != null)
        {
            // Chase: dart at the prey; it was a long chase or the prey slipped away: give up
            if (!prey.Free && !prey.chased) { prey = null; return; }
            prey.chased = true;
            goal = prey.transform.position;
            spd = speed * 2.6f;
            Vector3 jaw = mouth != null ? mouth.position : transform.position;
            if (Vector3.Distance(jaw, goal) < size * 0.9f + 0.8f)
            {
                life.bubbles.Burst(goal, 14);
                prey.chased = false;
                prey.Vanish(10f + (float)rng.NextDouble() * 10f);
                prey = null;
                gulp = 0.5f;
            }
        }
        else if (leader != null && !leader.eaten)
        {
            // Hold formation behind the leader, with a little wobble
            goal = leader.transform.position + leader.transform.rotation * offset + new Vector3(Mathf.Sin(t + phase), Mathf.Sin(t * 0.7f + phase), 0f) * 0.8f;
            spd = Mathf.Lerp(speed * 0.6f, speed * 1.8f, Mathf.Clamp01(Vector3.Distance(transform.position, goal) / 6f));
        }
        else
        {
            if ((target - transform.position).sqrMagnitude < 25f) target = RandomSpot();
            goal = target;
        }

        // Fleeing: prey being chased speeds up a little
        if (chased) spd *= 1.6f;

        // Never cut through the board's space
        Vector3 flat = new Vector3(transform.position.x, 0f, transform.position.z);
        if (flat.magnitude < MinRadius - 6f) goal = transform.position + flat.normalized * 20f;

        Vector3 dir = goal - transform.position;
        if (dir.sqrMagnitude > 0.01f)
        {
            Quaternion want = Quaternion.LookRotation(dir.normalized);
            float rate = prey != null || leader != null ? turn * 2.5f : turn;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, rate * dt);
        }
        // Jellyfish drift upward with each pulse instead of swimming forward
        if (kind == Kind.Jellyfish)
            transform.position += (transform.forward * 0.4f + Vector3.up * Mathf.Max(0f, Mathf.Sin(Time.time * 1.6f + phase))) * spd * dt;
        else
            transform.position += transform.forward * spd * dt;
        if (kind == Kind.Jellyfish && transform.position.y > 22f) transform.position = new Vector3(transform.position.x, -30f, transform.position.z);
        // Keep the jellyfish bell upright whichever way it drifts
        if (kind == Kind.Jellyfish) body.rotation = Quaternion.identity;
    }

    [System.NonSerialized] public bool chased;
}

/// <summary>Keeps a glow quad turned to the camera</summary>
public class SeaFaceCamera : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}

/// <summary>
/// The giant tentacle: rises from the deep under a fish, coils round it, and drags it down.
/// Drawn as a chain of overlapping spheres tapering to the tip, with pale suckers.
/// </summary>
public class SeaTentacle : MonoBehaviour
{
    private const int Count = 46;
    private readonly Transform[] segs = new Transform[Count];
    private readonly Transform[] suckers = new Transform[Count];
    private SeaFish victim;
    private SeaLife life;
    private Vector3 basePos, grabPos;
    private float started;
    public bool Busy => victim != null;

    private void Awake()
    {
        Material skin = OrbitScenery.Lit(new Color(0.45f, 0.08f, 0.25f), new Color(0.2f, 0.02f, 0.1f), 0.2f, 0.7f);
        Material sucker = OrbitScenery.Lit(new Color(1f, 0.75f, 0.8f), new Color(0.35f, 0.2f, 0.25f), 0.1f, 0.5f);
        for (int i = 0; i < Count; i++)
        {
            float s = Mathf.Lerp(7f, 0.7f, Mathf.Pow((float)i / (Count - 1), 0.8f));
            segs[i] = OrbitScenery.Part(transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * s, skin);
            if (i > 6 && i % 2 == 0) suckers[i] = OrbitScenery.Part(transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(s * 0.45f, s * 0.45f, s * 0.2f), sucker);
        }
        gameObject.SetActive(false);
    }

    public void Grab(SeaFish fish, SeaLife owner)
    {
        victim = fish;
        life = owner;
        Vector3 p = fish.transform.position;
        Vector3 outward = new Vector3(p.x, 0f, p.z).normalized;
        basePos = new Vector3(p.x, DeepSeaScenery.FloorY - 14f, p.z) + outward * 25f; // out of the dark, beyond the fish
        started = Time.time;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (victim == null) { gameObject.SetActive(false); return; }
        float t = Time.time - started;
        const float rise = 3.2f, coil = 1.0f, hold = 0.6f, pull = 2.8f;

        Vector3 fishPos = victim.transform.position;
        float reach, curl;
        if (t < rise)
        {
            reach = Ease(t / rise); curl = 0f;
            if (t > rise - 0.6f && !victim.chased) victim.Hold(true); // the fish freezes as it closes in
        }
        else if (t < rise + coil + hold)
        {
            if (t - rise < Time.deltaTime * 1.5f) { grabPos = fishPos; victim.Hold(true); life.bubbles.Burst(fishPos, 18); }
            reach = 1f; curl = Ease(Mathf.Clamp01((t - rise) / coil));
        }
        else if (t < rise + coil + hold + pull)
        {
            float k = Ease((t - rise - coil - hold) / pull);
            reach = 1f - k * 0.9f; curl = 1f;
            // Drag the fish down with the coil
            victim.transform.position = Vector3.Lerp(grabPos, basePos + Vector3.up * 6f, k);
            victim.transform.rotation *= Quaternion.Euler(0f, 0f, 200f * Time.deltaTime);
        }
        else
        {
            victim.Vanish(25f);
            victim = null;
            gameObject.SetActive(false);
            return;
        }
        if (t >= rise) fishPos = victim.transform.position;
        Shape(fishPos, reach, curl, Time.time);
    }

    // Lay the spheres along a curve from the base up toward the fish; the last third coils round it
    private void Shape(Vector3 fish, float reach, float curl, float time)
    {
        Vector3 b = basePos;
        Vector3 tip = Vector3.Lerp(b, fish, reach);
        Vector3 up = Vector3.up * Mathf.Max(10f, (tip.y - b.y) * 0.6f);
        Vector3 side = Vector3.Cross((tip - b).normalized, Vector3.up).normalized;
        if (side.sqrMagnitude < 0.01f) side = Vector3.right;
        Vector3 c1 = b + up, c2 = tip - (tip - b).normalized * 6f + side * 8f + Vector3.up * 6f;
        float coilStart = 0.68f;
        for (int i = 0; i < Count; i++)
        {
            float s = (float)i / (Count - 1);
            float along = s * reach;
            // Bezier spine with a slow living sway
            float u = s;
            Vector3 p = Mathf.Pow(1 - u, 3) * b + 3 * Mathf.Pow(1 - u, 2) * u * c1 + 3 * (1 - u) * u * u * c2 + u * u * u * tip;
            p += side * Mathf.Sin(time * 1.4f + s * 5f) * 2.5f * s * (1f - curl);
            if (reach < 1f) p = Vector3.Lerp(b, p, Mathf.Clamp01(reach * 1.25f - s * 0.25f + 0.25f));
            // Coil: the last part winds round the fish
            if (curl > 0f && s > coilStart)
            {
                float k = (s - coilStart) / (1f - coilStart);
                float ang = k * Mathf.PI * 3.2f * curl;
                float r = 2.2f * (1f - 0.35f * k);
                Vector3 ring = fish + (side * Mathf.Cos(ang) + Vector3.Cross(side, Vector3.up) * Mathf.Sin(ang)) * r + Vector3.up * (k - 0.5f) * 3f;
                p = Vector3.Lerp(p, ring, Mathf.Clamp01(curl * 1.2f) * Mathf.SmoothStep(0f, 1f, k * 3f));
            }
            segs[i].position = p;
            if (suckers[i] != null)
            {
                Vector3 inner = fish - p;
                inner = (inner - Vector3.Project(inner, (i > 0 ? p - segs[i - 1].position : Vector3.up))).normalized;
                if (inner.sqrMagnitude < 0.01f) inner = side;
                suckers[i].position = p + inner * segs[i].localScale.x * 0.42f;
                suckers[i].rotation = Quaternion.LookRotation(inner);
            }
        }
    }

    private static float Ease(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
}

/// <summary>Marine snow: pale flecks drifting slowly down through the water</summary>
public class SeaSnow : MonoBehaviour
{
    private void Start()
    {
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(14f, 22f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.22f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.9f, 0.95f, 0.55f), new Color(0.5f, 0.8f, 0.85f, 0.35f));
        main.maxParticles = 600;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.prewarm = true;
        var emission = ps.emission;
        emission.rateOverTime = 34f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0f, 10f, 0f);
        shape.scale = new Vector3(110f, 50f, 110f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.6f, -0.25f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.2f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = NeonTheme.Circle.texture };
        ps.Play();
    }
}

/// <summary>Bubbles: streams rising from vents on the floor, and bursts when something gets eaten</summary>
public class SeaBubbles : MonoBehaviour
{
    private ParticleSystem ps;
    private Vector3[] vents;
    private float next;
    private System.Random rng;

    public void Init(System.Random random)
    {
        rng = random;
        vents = new Vector3[4];
        for (int i = 0; i < vents.Length; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 45f + (float)rng.NextDouble() * 60f;
            vents[i] = new Vector3(Mathf.Cos(a) * d, DeepSeaScenery.FloorY + 1f, Mathf.Sin(a) * d);
        }
        var go = new GameObject("Bubbles");
        go.transform.SetParent(transform, false);
        ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = 500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.enabled = false;
        var shape = ps.shape;
        shape.enabled = false;
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.6f;
        var fade = ps.colorOverLifetime;
        fade.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0.7f, 0.8f), new GradientAlphaKey(0f, 1f) });
        fade.color = g;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = BubbleTexture() };
        ps.Play();
    }

    public void Burst(Vector3 at, int count)
    {
        for (int i = 0; i < count; i++) Emit(at + Random.insideUnitSphere * 1.2f, 0.25f + Random.value * 0.5f, 3f + Random.value * 3f, 2.5f);
    }

    private void Emit(Vector3 at, float size, float rise, float life)
    {
        var p = new ParticleSystem.EmitParams { applyShapeToPosition = false, position = at, velocity = new Vector3(0f, rise, 0f), startSize = size, startLifetime = life, startColor = new Color(0.75f, 0.95f, 1f, 0.8f) };
        ps.Emit(p, 1);
    }

    private void Update()
    {
        if (ps == null || Time.time < next) return;
        next = Time.time + 0.18f;
        Vector3 v = vents[rng.Next(vents.Length)];
        Emit(v + new Vector3((float)rng.NextDouble() - 0.5f, 0f, (float)rng.NextDouble() - 0.5f), 0.3f + (float)rng.NextDouble() * 0.6f, 4f + (float)rng.NextDouble() * 2f, 12f);
    }

    // A clear ring with a bright highlight
    private static Texture2D BubbleTexture()
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
            float d = Mathf.Sqrt(u * u + v * v);
            float ring = Mathf.Exp(-Mathf.Pow((d - 0.85f) / 0.08f, 2f));
            float fill = d < 0.85f ? 0.12f : 0f;
            float shine = Mathf.Exp(-((u + 0.35f) * (u + 0.35f) + (v - 0.35f) * (v - 0.35f)) / 0.02f);
            float a = Mathf.Clamp01(ring + fill + shine);
            px[y * size + x] = new Color(1f, 1f, 1f, d > 1f ? 0f : a);
        }
        tex.SetPixels32(px);
        tex.Apply(true);
        return tex;
    }
}

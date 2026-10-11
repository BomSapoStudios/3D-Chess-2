using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual effects for piece moves, captures and conquests. Effects only touch throwaway
/// GameObjects (copies of a piece's meshes, shards, orbs, rings), never the game state,
/// so the rules carry on exactly as before while the effect plays.
///  - LandingRing: a ring of light that spreads from a square when a piece lands.
///  - Shatter: a captured piece holds still until the attacker reaches it, then bursts into glowing shards.
///  - Conquest: a checkmated king dissolves into orbs that fly to each of its pieces and turn them.
/// </summary>
public class PieceEffects : MonoBehaviour
{
    private static PieceEffects runner;
    private static Material glowMaterial;
    private static Mesh orbMesh, shardMesh, columnMesh;
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private static PieceEffects Runner
    {
        get
        {
            if (runner == null)
            {
                var go = new GameObject("Piece Effects");
                runner = go.AddComponent<PieceEffects>();
            }
            return runner;
        }
    }

    /// <summary>Unlit, see-through colour (Sprites/Default is always in builds)</summary>
    private static Material Glow
    {
        get
        {
            if (glowMaterial == null)
            {
                glowMaterial = new Material(Shader.Find("Sprites/Default")) { name = "Effect Glow" };
            }
            return glowMaterial;
        }
    }

    private static Mesh PrimitiveMesh(PrimitiveType type, ref Mesh cache)
    {
        if (cache == null)
        {
            GameObject temp = GameObject.CreatePrimitive(type);
            cache = temp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(temp);
        }
        return cache;
    }

    /// <summary>The glowing colour of a player's pieces in the current piece set</summary>
    public static Color GlowColor(PieceColor player)
    {
        Color c = PieceSets.Tint(PieceSets.Current, player);
        if (c.maxColorComponent < 0.35f) c = Color.Lerp(c, new Color(0.6f, 0.3f, 1f), 0.6f); // dark sets still need to glow
        return Color.Lerp(c, Color.white, 0.2f);
    }

    private static Transform PiecesContainer
    {
        get
        {
            GameObject container = GameObject.Find("Pieces Container");
            return container != null ? container.transform : null;
        }
    }

    private static float Cell => ChessBoard.Instance != null ? ChessBoard.Instance.cellSize : 2.8f;

    private static GameObject Solid(string name, Mesh mesh, Color color, Vector3 position, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = Glow;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        SetColor(r, color);
        return go;
    }

    private static void SetColor(Renderer r, Color color)
    {
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(ColorId, color);
        r.SetPropertyBlock(block);
    }

    /// <summary>A copy of a piece's meshes and materials, frozen where the piece stands now</summary>
    private static GameObject VisualCopy(ChessPiece piece)
    {
        var copy = new GameObject($"{piece.pieceColor} {piece.pieceType} (effect)");
        copy.transform.SetPositionAndRotation(piece.transform.position, piece.transform.rotation);
        foreach (MeshRenderer r in piece.GetComponentsInChildren<MeshRenderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            MeshFilter f = r.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null) continue;
            string n = r.gameObject.name.ToLowerInvariant();
            if (n.Contains("glow") || n.Contains("indicator") || n.Contains("highlight")) continue; // not the selection effects
            var part = new GameObject(r.gameObject.name);
            part.transform.SetParent(copy.transform, false);
            part.transform.SetPositionAndRotation(r.transform.position, r.transform.rotation);
            part.transform.localScale = Vector3.Scale(r.transform.lossyScale, Inverse(copy.transform.lossyScale));
            part.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
            part.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
        }
        return copy;
    }

    private static Vector3 Inverse(Vector3 v) => new Vector3(1f / v.x, 1f / v.y, 1f / v.z);

    private static Bounds BoundsOf(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position + Vector3.up, Vector3.one);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ───────────────────────── Landing ring ─────────────────────────

    public static void LandingRing(Vector3 worldPosition, Color color)
    {
        Runner.StartCoroutine(Runner.RingRoutine(worldPosition, color));
    }

    private IEnumerator RingRoutine(Vector3 position, Color color)
    {
        Transform container = PiecesContainer;
        var go = new GameObject("Landing Ring");
        go.transform.position = position + (container != null ? container.up : Vector3.up) * 0.05f;
        if (container != null) go.transform.rotation = container.rotation;
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = Glow;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = 48;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        float cell = Cell;
        const float duration = 0.45f;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            float k = t / duration;
            float radius = Mathf.Lerp(0.15f, 0.55f, 1f - (1f - k) * (1f - k)) * cell;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            }
            line.widthMultiplier = Mathf.Lerp(0.18f, 0.03f, k);
            Color c = color;
            c.a = 1f - k;
            line.startColor = line.endColor = c;
            yield return null;
        }
        Destroy(go);
    }

    // ───────────────────────── Capture: shatter ─────────────────────────

    /// <summary>
    /// Call just before a captured piece is removed. A copy of it waits on its square until
    /// the attacker gets close, then breaks apart.
    /// </summary>
    public static void Shatter(ChessPiece victim, ChessPiece attacker)
    {
        if (victim == null) return;
        GameObject copy = VisualCopy(victim);
        Runner.StartCoroutine(Runner.ShatterRoutine(copy, attacker, GlowColor(victim.pieceColor)));
    }

    private IEnumerator ShatterRoutine(GameObject copy, ChessPiece attacker, Color color)
    {
        Bounds bounds = BoundsOf(copy);
        Vector3 square = copy.transform.position;

        // Wait for the attacker to set off (the capture is handled just before it moves),
        // then for it to arrive on this square
        float waited = 0f;
        while (attacker != null && !attacker.IsMoving && waited < 0.25f)
        {
            waited += Time.deltaTime;
            yield return null;
        }
        waited = 0f;
        while (attacker != null && attacker.IsMoving && waited < 2f)
        {
            if (Vector3.Distance(attacker.transform.position, square) < Cell * 0.3f) break;
            waited += Time.deltaTime;
            yield return null;
        }

        Destroy(copy);
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube, ref shardMesh);
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);

        // Flash
        GameObject flash = Solid("Capture Flash", sphere, color, bounds.center, Vector3.one * 0.3f);

        // Shards
        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        var shards = new List<(Transform t, Renderer r, Vector3 v, Vector3 spin, float life, Color c)>();
        for (int i = 0; i < 26; i++)
        {
            Vector3 p = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * 0.8f);
            Vector3 outward = (p - bounds.center).normalized;
            if (outward.sqrMagnitude < 0.01f) outward = Random.onUnitSphere;
            Vector3 v = (outward * Random.Range(3f, 7f) + up * Random.Range(2f, 5f)) * (size / 2.2f);
            float s = Random.Range(0.07f, 0.17f) * size;
            Color c = Color.Lerp(color, Color.white, Random.Range(0f, 0.4f));
            GameObject shard = Solid("Shard", cube, c, p, new Vector3(s, s * Random.Range(0.4f, 1f), s * Random.Range(0.3f, 1f)));
            shard.transform.rotation = Random.rotation;
            shards.Add((shard.transform, shard.GetComponent<Renderer>(), v, Random.insideUnitSphere * 720f, Random.Range(0.7f, 1.1f), c));
        }

        float time = 0f;
        float gravity = 14f * size / 2.2f;
        while (time < 1.1f)
        {
            float dt = Time.deltaTime;
            time += dt;

            if (flash != null)
            {
                float k = time / 0.22f;
                if (k >= 1f) Destroy(flash);
                else
                {
                    flash.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 1.6f, k) * size;
                    Color fc = color; fc.a = 0.8f * (1f - k);
                    SetColor(flash.GetComponent<Renderer>(), fc);
                }
            }

            for (int i = 0; i < shards.Count; i++)
            {
                var s = shards[i];
                if (s.t == null) continue;
                s.v -= up * gravity * dt;
                s.v *= 1f - 1.5f * dt; // a little drag
                s.t.position += s.v * dt;
                s.t.Rotate(s.spin * dt, Space.Self);
                float fade = Mathf.Clamp01((s.life - time) / (s.life * 0.5f));
                if (time >= s.life) { Destroy(s.t.gameObject); continue; }
                Color c = s.c;
                c.a = fade;
                SetColor(s.r, c);
                shards[i] = s;
            }
            yield return null;
        }
        if (flash != null) Destroy(flash);
        foreach (var s in shards) if (s.t != null) Destroy(s.t.gameObject);
    }

    // ───────────────────────── Checkmate: absorb and turn ─────────────────────────

    /// <summary>
    /// The checkmated king breaks into orbs of the winner's colour that fly to each of its
    /// pieces; each piece keeps its old colour until its orb arrives, then flips with a pulse.
    /// Call before the king is removed and before the pieces change colour.
    /// </summary>
    public static ConquestEffect BeginConquest(ChessPiece king, PieceColor loser, PieceColor winner)
    {
        var effect = new ConquestEffect
        {
            kingCopy = king != null ? VisualCopy(king) : null,
            loserColor = GlowColor(loser),
            winnerColor = GlowColor(winner),
            loser = loser,
        };
        return effect;
    }

    public class ConquestEffect
    {
        public GameObject kingCopy;
        public Color loserColor, winnerColor;
        public PieceColor loser;
        public readonly List<ChessPiece> pieces = new List<ChessPiece>();

        /// <summary>Hold a converted piece in its old colour until its orb reaches it</summary>
        public void Add(ChessPiece piece)
        {
            if (piece == null) return;
            pieces.Add(piece);
            PieceColor now = piece.pieceColor;
            piece.pieceColor = loser;
            piece.ApplyMaterial();
            piece.pieceColor = now;
        }

        public void Play() => Runner.StartCoroutine(Runner.ConquestRoutine(this));
    }

    private IEnumerator ConquestRoutine(ConquestEffect e)
    {
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);
        Vector3 origin = e.kingCopy != null ? BoundsOf(e.kingCopy).center : (e.pieces.Count > 0 ? e.pieces[0].transform.position : Vector3.zero);
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        float cell = Cell;

        // The king flickers between the two colours, then gives way
        if (e.kingCopy != null)
        {
            Renderer[] parts = e.kingCopy.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < 8; i++)
            {
                bool on = i % 2 == 0;
                foreach (Renderer r in parts) r.enabled = on || i < 2;
                e.kingCopy.transform.position += up * 0.04f * cell;
                yield return new WaitForSeconds(0.06f);
            }
            Bounds kb = BoundsOf(e.kingCopy);
            Destroy(e.kingCopy);
            GameObject burst = Solid("King Burst", sphere, e.winnerColor, kb.center, Vector3.one * 0.3f);
            StartCoroutine(FadeOut(burst, 0.3f, 0.3f, 2.2f * Mathf.Max(kb.size.x, kb.size.y)));
        }

        // An orb to each piece, a few at a time
        int arrived = 0;
        for (int i = 0; i < e.pieces.Count; i++)
        {
            StartCoroutine(OrbRoutine(origin, e.pieces[i], e, up, cell, () => arrived++));
            yield return new WaitForSeconds(0.04f);
        }
        float wait = 0f;
        while (arrived < e.pieces.Count && wait < 4f) { wait += Time.deltaTime; yield return null; }
    }

    private IEnumerator OrbRoutine(Vector3 from, ChessPiece piece, ConquestEffect e, Vector3 up, float cell, System.Action done)
    {
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);
        GameObject orb = Solid("Conquest Orb", sphere, e.winnerColor, from, Vector3.one * 0.35f * cell / 2.8f);
        var trail = orb.AddComponent<TrailRenderer>();
        trail.sharedMaterial = Glow;
        trail.time = 0.25f;
        trail.widthMultiplier = 0.25f * cell / 2.8f;
        trail.startColor = e.winnerColor;
        trail.endColor = new Color(e.winnerColor.r, e.winnerColor.g, e.winnerColor.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        float distance = piece != null ? Vector3.Distance(from, piece.transform.position) : 1f;
        float duration = Mathf.Clamp(0.35f + distance / (cell * 6f), 0.4f, 1.1f);
        Vector3 lift = up * Mathf.Min(distance * 0.35f, cell * 1.5f);
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            if (piece == null) break;
            float k = t / duration;
            float eased = k * k * (3f - 2f * k);
            Vector3 to = piece.transform.position + up * cell * 0.35f;
            orb.transform.position = Vector3.Lerp(from, to, eased) + lift * Mathf.Sin(k * Mathf.PI);
            yield return null;
        }
        Destroy(orb, 0.3f);
        orb.GetComponent<Renderer>().enabled = false;

        if (piece != null)
        {
            piece.ApplyMaterial(); // now shows the winner's colour
            Pulse(piece);
            LandingRing(piece.transform.position, e.winnerColor);
        }
        done();
    }

    private IEnumerator FadeOut(GameObject go, float duration, float fromScale, float toScale)
    {
        Renderer r = go.GetComponent<Renderer>();
        Color c = r != null ? GetColor(r) : Color.white;
        for (float t = 0; t < duration && go != null; t += Time.deltaTime)
        {
            float k = t / duration;
            go.transform.localScale = Vector3.one * Mathf.Lerp(fromScale, toScale, k);
            c.a = 0.85f * (1f - k);
            if (r != null) SetColor(r, c);
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    private static Color GetColor(Renderer r)
    {
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        return block.GetColor(ColorId);
    }

    // ───────────────────────── Promotion: morph ─────────────────────────

    /// <summary>
    /// The pawn spins up and stretches into a thin column of light, and the new piece spins
    /// out of the column into its own shape. Call before the pawn is destroyed; the new piece
    /// must already be on its square.
    /// </summary>
    public static void Promote(ChessPiece pawn, ChessPiece promoted)
    {
        if (promoted == null) return;
        GameObject copy = pawn != null ? VisualCopy(pawn) : null;
        if (copy != null) copy.transform.SetPositionAndRotation(promoted.transform.position, copy.transform.rotation);
        Runner.StartCoroutine(Runner.PromoteRoutine(copy, promoted, GlowColor(promoted.pieceColor)));
    }

    private IEnumerator PromoteRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
    {
        Transform t = promoted.transform;
        // Let the new piece finish setting itself up (model, facing) before reading its pose
        SetSquash(t, 0f, 0f);
        yield return null;
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Quaternion restRotation = t.localRotation;
        RestScale(t);
        SetSquash(t, 0f, 0f); // hidden until the column opens
        float cell = Cell;
        Mesh cylinder = PrimitiveMesh(PrimitiveType.Cylinder, ref columnMesh);

        // A column of light rises around the pawn
        Vector3 floor = t.position;
        GameObject column = Solid("Promotion Column", cylinder, color, floor, new Vector3(0.05f, 0.01f, 0.05f) * cell);
        column.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

        // 1. The pawn spins faster and faster and stretches into a thin streak
        const float windUp = 0.55f;
        Quaternion pawnRest = pawnCopy != null ? pawnCopy.transform.rotation : Quaternion.identity;
        Vector3 pawnScale = pawnCopy != null ? pawnCopy.transform.localScale : Vector3.one;
        float angle = 0f;
        for (float time = 0; time < windUp; time += Time.deltaTime)
        {
            float k = time / windUp;
            angle += Mathf.Lerp(200f, 1400f, k * k) * Time.deltaTime;
            if (pawnCopy != null)
            {
                pawnCopy.transform.rotation = Quaternion.AngleAxis(angle, up) * pawnRest;
                pawnCopy.transform.localScale = new Vector3(pawnScale.x * Mathf.Lerp(1f, 0.15f, k * k), pawnScale.y * Mathf.Lerp(1f, 1.7f, k), pawnScale.z * Mathf.Lerp(1f, 0.15f, k * k));
            }
            float h = Mathf.Lerp(0.01f, 1.4f, k) * cell;
            column.transform.localScale = new Vector3(0.22f * cell, h, 0.22f * cell);
            column.transform.position = floor + up * h; // a cylinder is 2 units tall around its middle
            Color c = color; c.a = Mathf.Lerp(0.15f, 0.6f, k);
            SetColor(column.GetComponent<Renderer>(), c);
            yield return null;
        }
        if (pawnCopy != null) Destroy(pawnCopy);

        // A flash at the swap
        GameObject flash = Solid("Promotion Flash", PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh), color, floor + up * cell * 0.45f, Vector3.one * 0.3f);
        StartCoroutine(FadeOut(flash, 0.3f, 0.3f * cell, 1.1f * cell));

        // 2. The new piece spins out of the streak, slowing down, and settles with a bounce
        const float reveal = 0.6f;
        for (float time = 0; time < reveal && t != null; time += Time.deltaTime)
        {
            float k = time / reveal;
            angle += Mathf.Lerp(1400f, 0f, Mathf.Sqrt(k)) * Time.deltaTime;
            // Wide: thin streak to full width with a little overshoot; tall: stretched, then settles
            float wide = Mathf.Clamp01(k * 1.6f);
            wide = wide * wide * (3f - 2f * wide) + Mathf.Sin(k * Mathf.PI) * 0.12f;
            float tall = Mathf.Lerp(1.6f, 1f, Mathf.Clamp01(k * 1.4f)) - Mathf.Sin(Mathf.Clamp01((k - 0.7f) / 0.3f) * Mathf.PI) * 0.12f;
            SetSquash(t, Mathf.Max(0.05f, wide), tall);
            t.localRotation = Quaternion.AngleAxis(angle, Vector3.up) * restRotation;

            float h = Mathf.Lerp(1.4f, 0.01f, k) * cell;
            column.transform.localScale = new Vector3(0.22f * cell * (1f - k * 0.5f), h, 0.22f * cell * (1f - k * 0.5f));
            column.transform.position = floor + up * h;
            Color c = color; c.a = 0.6f * (1f - k);
            SetColor(column.GetComponent<Renderer>(), c);
            yield return null;
        }
        Destroy(column);
        if (t != null)
        {
            SetSquash(t, 1f, 1f);
            t.localRotation = restRotation;
            LandingRing(t.position, color);
        }
    }

    // ───────────────────────── Squash and pulse ─────────────────────────

    private static readonly Dictionary<Transform, Vector3> restScales = new Dictionary<Transform, Vector3>();

    /// <summary>The piece's normal scale, remembered the first time it's squashed</summary>
    private static Vector3 RestScale(Transform t)
    {
        if (!restScales.TryGetValue(t, out Vector3 s))
        {
            s = t.localScale;
            restScales[t] = s;
        }
        return s;
    }

    /// <summary>Squash a piece by (xz, y) factors of its normal scale</summary>
    public static void SetSquash(Transform t, float wide, float tall)
    {
        if (t == null) return;
        Vector3 rest = RestScale(t);
        t.localScale = new Vector3(rest.x * wide, rest.y * tall, rest.z * wide);
    }

    /// <summary>A landing squash that springs back</summary>
    public static void Land(ChessPiece piece)
    {
        if (piece == null) return;
        Runner.StartCoroutine(Runner.LandRoutine(piece.transform));
        LandingRing(piece.transform.position, GlowColor(piece.pieceColor));
    }

    private IEnumerator LandRoutine(Transform t)
    {
        const float duration = 0.28f;
        for (float time = 0; time < duration && t != null; time += Time.deltaTime)
        {
            float k = time / duration;
            // Damped spring: squashed on impact, a small overshoot, then rest
            float s = Mathf.Exp(-6f * k) * Mathf.Cos(k * Mathf.PI * 2.5f) * 0.22f;
            SetSquash(t, 1f + s * 0.6f, 1f - s);
            yield return null;
        }
        if (t != null) SetSquash(t, 1f, 1f);
    }

    /// <summary>A quick swell, for a piece that just changed sides</summary>
    public static void Pulse(ChessPiece piece)
    {
        if (piece == null) return;
        Runner.StartCoroutine(Runner.PulseRoutine(piece.transform));
    }

    private IEnumerator PulseRoutine(Transform t)
    {
        const float duration = 0.35f;
        for (float time = 0; time < duration && t != null; time += Time.deltaTime)
        {
            float k = time / duration;
            float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.25f;
            SetSquash(t, s, s);
            yield return null;
        }
        if (t != null) SetSquash(t, 1f, 1f);
    }
}

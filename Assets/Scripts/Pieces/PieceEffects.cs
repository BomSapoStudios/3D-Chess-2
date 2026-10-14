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

    private static float Cell => ChessBoard.Instance != null ? ChessBoard.Instance.cellSize : PreviewCell;

    /// <summary>Set while the Customize preview plays promotions off the board: its camera and square size</summary>
    public static Camera PreviewCamera;
    public static float PreviewCell = 2.8f;
    private static Camera ViewCamera => PreviewCamera != null ? PreviewCamera : Camera.main;

    /// <summary>
    /// Stop every running effect and remove whatever it left near this point (the Customize
    /// preview calls this when it switches animation or closes, so nothing lingers on its stage)
    /// </summary>
    public static void ClearAround(Vector3 centre, float radius, GameObject keep)
    {
        if (runner != null) runner.StopAllCoroutines();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).GetRootGameObjects())
            {
                if (root == keep || root == (runner != null ? runner.gameObject : null)) continue;
                if (Vector3.Distance(root.transform.position, centre) < radius) Destroy(root);
            }
        }

    }

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

    /// <summary>Effects are skipped while a resumed game is rebuilt from its history</summary>
    private static bool Enabled => !OnlineSession.Replaying;

    public static void LandingRing(Vector3 worldPosition, Color color)
    {
        if (!Enabled) return;
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
        if (victim == null || !Enabled) return;
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
            enabled = Enabled,
            kingCopy = king != null && Enabled ? VisualCopy(king) : null,
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
        public bool enabled = true;

        /// <summary>Hold a converted piece in its old colour until its orb reaches it</summary>
        public void Add(ChessPiece piece)
        {
            if (piece == null || !enabled) return;
            pieces.Add(piece);
            PieceColor now = piece.pieceColor;
            piece.pieceColor = loser;
            piece.ApplyMaterial();
            piece.pieceColor = now;
        }

        public void Play()
        {
            if (enabled) Runner.StartCoroutine(Runner.ConquestRoutine(this));
        }
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
    /// Promotion, in the style picked in Customize (PromotionStyle).
    /// Glitch rebuild: the pawn flickers and jitters like a corrupted hologram, its colours
    /// splitting apart, then breaks into small glowing cubes that swirl and snap together into
    /// the new piece's shape. Call before the pawn is destroyed; the new piece must already be
    /// on its square.
    /// </summary>
    public static void Promote(ChessPiece pawn, ChessPiece promoted, PromotionStyle.Kind? style = null)
    {
        if (promoted == null || !Enabled) return;
        GameObject copy = pawn != null ? VisualCopy(pawn) : null;
        if (copy != null) copy.transform.SetPositionAndRotation(promoted.transform.position, copy.transform.rotation);
        Color color = GlowColor(promoted.pieceColor);
        PromotionStyle.Kind kind = style ?? PromotionStyle.Current;
        if (kind == PromotionStyle.Kind.SpinMorph) Runner.StartCoroutine(Runner.SpinPromoteRoutine(copy, promoted, color));
        else if (kind == PromotionStyle.Kind.Coronation) Runner.StartCoroutine(Runner.CoronationRoutine(copy, promoted, color));
        else if (kind == PromotionStyle.Kind.BlackHole) Runner.StartCoroutine(Runner.BlackHoleRoutine(copy, promoted, color));
        else Runner.StartCoroutine(Runner.PromoteRoutine(copy, promoted, color));
    }

    private IEnumerator PromoteRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
    {
        Transform t = promoted.transform;
        // Let the new piece finish setting itself up (model, facing) before reading its shape
        SetSquash(t, 0f, 0f);
        yield return null;
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        RestScale(t);
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Camera cam = ViewCamera;
        Vector3 side = cam != null ? cam.transform.right : Vector3.right;
        float cell = Cell;

        // Where the cubes will land: points on the new piece's surface
        SetSquash(t, 1f, 1f);
        var targets = SurfacePoints(promoted, 46);
        SetSquash(t, 0f, 0f);

        // 1. Glitch: jitter, flicker, and a cyan / pink colour split
        Bounds pawnBounds = pawnCopy != null ? BoundsOf(pawnCopy) : new Bounds(t.position + up * cell * 0.35f, Vector3.one * cell * 0.5f);
        var ghosts = new List<GameObject>();
        if (pawnCopy != null)
        {
            foreach (Color tint in new[] { NeonTheme.Cyan, NeonTheme.Pink })
            {
                GameObject ghost = Instantiate(pawnCopy);
                foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = Glow;
                    SetColor(r, new Color(tint.r, tint.g, tint.b, 0.55f));
                }
                ghosts.Add(ghost);
            }
        }
        Vector3 pawnHome = pawnCopy != null ? pawnCopy.transform.position : t.position;
        const float glitch = 0.5f;
        float nextJump = 0f;
        Vector3 jitter = Vector3.zero;
        for (float time = 0; time < glitch; time += Time.deltaTime)
        {
            float k = time / glitch;
            if (time >= nextJump)
            {
                // Snap to a new wrong position every few frames, more violently as it goes
                nextJump = time + Random.Range(0.03f, 0.07f);
                jitter = (side * Random.Range(-1f, 1f) + up * Random.Range(-0.4f, 0.4f)) * cell * Mathf.Lerp(0.02f, 0.09f, k);
                if (pawnCopy != null)
                {
                    bool show = Random.value > Mathf.Lerp(0.05f, 0.4f, k);
                    foreach (Renderer r in pawnCopy.GetComponentsInChildren<Renderer>()) r.enabled = show;
                }
            }
            if (pawnCopy != null) pawnCopy.transform.position = pawnHome + jitter;
            float split = cell * Mathf.Lerp(0.03f, 0.11f, k) * (0.6f + 0.4f * Mathf.Sin(time * 60f));
            for (int i = 0; i < ghosts.Count; i++)
                ghosts[i].transform.position = pawnHome + jitter * 0.5f + side * split * (i == 0 ? -1f : 1f);
            yield return null;
        }
        if (pawnCopy != null) Destroy(pawnCopy);
        foreach (GameObject g in ghosts) Destroy(g);

        // 2. Break into cubes that burst, swirl round, and snap into the new shape
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube, ref shardMesh);
        var bits = new List<(Transform t, Renderer r, Vector3 start, Vector3 burst, Vector3 target, float size, float delay, Color c)>();
        for (int i = 0; i < targets.Count; i++)
        {
            Vector3 p = pawnBounds.center + Vector3.Scale(Random.insideUnitSphere, pawnBounds.extents * 0.8f);
            Vector3 outward = (p - pawnBounds.center); if (outward.sqrMagnitude < 0.0001f) outward = Random.onUnitSphere;
            Vector3 burst = p + outward.normalized * cell * Random.Range(0.25f, 0.45f) + up * cell * Random.Range(0.05f, 0.3f);
            float s = cell * Random.Range(0.045f, 0.075f);
            Color c = Random.value < 0.25f ? Color.Lerp(color, Color.white, 0.6f) : color;
            GameObject bit = Solid("Glitch Cube", cube, c, p, Vector3.one * s);
            bit.transform.rotation = Random.rotation;
            bits.Add((bit.transform, bit.GetComponent<Renderer>(), p, burst, targets[i], s, Random.Range(0f, 0.12f), c));
        }

        const float swirl = 0.65f;
        for (float time = 0; time < swirl; time += Time.deltaTime)
        {
            float k = time / swirl;
            foreach (var b in bits)
            {
                if (b.t == null) continue;
                float local = Mathf.Clamp01((k - b.delay) / (1f - b.delay));
                Vector3 pos;
                if (local < 0.35f)
                {
                    // Burst outward
                    float e = local / 0.35f;
                    pos = Vector3.Lerp(b.start, b.burst, 1f - (1f - e) * (1f - e));
                }
                else
                {
                    // Swirl round the piece's axis while closing in, then snap
                    float e = (local - 0.35f) / 0.65f;
                    float ease = e * e * (3f - 2f * e);
                    pos = Vector3.Lerp(b.burst, b.target, ease);
                    Vector3 axisPoint = t.position + Vector3.Project(pos - t.position, up);
                    pos = axisPoint + Quaternion.AngleAxis((1f - ease) * 220f, up) * (pos - axisPoint);
                }
                b.t.position = pos;
                b.t.rotation = Quaternion.Slerp(b.t.rotation, Quaternion.LookRotation(Vector3.Cross(up, side), up), local * 0.3f);
            }
            yield return null;
        }

        // 3. The piece blinks into being over its cubes, with one last glitch
        for (int i = 0; i < 4 && t != null; i++)
        {
            SetSquash(t, i % 2 == 0 ? 1f : 0f, i % 2 == 0 ? 1f : 0f);
            yield return new WaitForSeconds(0.04f);
        }
        if (t != null) SetSquash(t, 1f, 1f);
        const float fade = 0.25f;
        for (float time = 0; time < fade; time += Time.deltaTime)
        {
            float k = time / fade;
            foreach (var b in bits)
            {
                if (b.t == null) continue;
                b.t.localScale = Vector3.one * b.size * (1f - k);
                Color c = b.c; c.a = 1f - k;
                SetColor(b.r, c);
            }
            yield return null;
        }
        foreach (var b in bits) if (b.t != null) Destroy(b.t.gameObject);
        if (t != null) LandingRing(t.position, color);
    }

    /// <summary>
    /// Spin morph (the other choice in Customize): the pawn spins up and stretches into a thin
    /// column of light, and the new piece spins out of the column into its own shape.
    /// </summary>
    private IEnumerator SpinPromoteRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
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

    /// <summary>
    /// Coronation (a choice in Customize): a glowing ring floats down around the pawn and
    /// squeezes it into a point of light; the ring flashes, the new piece rises out of it, and a
    /// shockwave ripples out across that floor of the board.
    /// </summary>
    private IEnumerator CoronationRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
    {
        Transform t = promoted.transform;
        SetSquash(t, 0f, 0f);
        yield return null;
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        Vector3 rest = RestScale(t);
        SetSquash(t, 0f, 0f);
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Quaternion flat = container != null ? container.rotation : Quaternion.identity;
        float cell = Cell;
        Vector3 floor = t.position;
        Bounds pawnBounds = pawnCopy != null ? BoundsOf(pawnCopy) : new Bounds(floor + up * cell * 0.35f, Vector3.one * cell * 0.5f);
        float top = Vector3.Dot(pawnBounds.max - floor, up);
        Vector3 waist = floor + up * Mathf.Max(0.2f * cell, top * 0.55f);

        LineRenderer ring = RingLine("Crown Ring", flat, 0.06f * cell);
        void DrawRing(Vector3 centre, float radius, float width, Color c)
        {
            ring.transform.position = centre;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            }
            ring.widthMultiplier = width;
            ring.startColor = ring.endColor = c;
        }

        // 1. The ring floats down from above and settles round the pawn's waist
        const float descend = 0.4f;
        Vector3 high = floor + up * (top + cell * 0.9f);
        for (float time = 0; time < descend; time += Time.deltaTime)
        {
            float k = time / descend;
            float e = 1f - (1f - k) * (1f - k);
            Color c = color; c.a = Mathf.Lerp(0.2f, 1f, k);
            DrawRing(Vector3.Lerp(high, waist, e), cell * 0.42f, 0.07f * cell, c);
            yield return null;
        }

        // 2. It tightens, squeezing the pawn into a growing point of light
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);
        GameObject spark = Solid("Crown Spark", sphere, Color.Lerp(color, Color.white, 0.5f), waist, Vector3.one * 0.01f);
        Vector3 pawnScale = pawnCopy != null ? pawnCopy.transform.localScale : Vector3.one;
        Vector3 pawnPos = pawnCopy != null ? pawnCopy.transform.position : floor;
        const float squeeze = 0.35f;
        for (float time = 0; time < squeeze; time += Time.deltaTime)
        {
            float k = time / squeeze;
            float e = k * k;
            if (pawnCopy != null)
            {
                pawnCopy.transform.localScale = pawnScale * Mathf.Lerp(1f, 0.02f, e);
                pawnCopy.transform.position = Vector3.Lerp(pawnPos, waist, e);
            }
            spark.transform.localScale = Vector3.one * cell * Mathf.Lerp(0.02f, 0.22f, e);
            Color c = color; c.a = 1f;
            DrawRing(waist, cell * Mathf.Lerp(0.42f, 0.12f, e), Mathf.Lerp(0.07f, 0.11f, e) * cell, c);
            yield return null;
        }
        if (pawnCopy != null) Destroy(pawnCopy);

        // 3. Flash: the new piece rises out of the ring, which bursts outward as a shockwave
        StartCoroutine(FadeOut(spark, 0.25f, 0.22f * cell, 0.9f * cell));
        GameObject flash = Solid("Crown Flash", sphere, color, waist, Vector3.one * 0.3f);
        StartCoroutine(FadeOut(flash, 0.3f, 0.3f * cell, 1.3f * cell));
        StartCoroutine(Shockwave(floor + up * 0.04f, flat, color, cell));

        const float rise = 0.45f;
        for (float time = 0; time < rise && t != null; time += Time.deltaTime)
        {
            float k = time / rise;
            float e = 1f - (1f - k) * (1f - k);
            // Up out of the floor with a small overshoot, stretched then settling
            float lift = Mathf.Lerp(-0.45f, 0f, e) * cell + Mathf.Sin(k * Mathf.PI) * 0.08f * cell;
            t.position = floor + up * lift;
            SetSquash(t, Mathf.Lerp(0.6f, 1f, e), Mathf.Lerp(1.25f, 1f, e));
            Color c = color; c.a = 1f - k;
            DrawRing(waist + up * cell * 0.3f * e, cell * Mathf.Lerp(0.12f, 0.5f, e), 0.06f * cell * (1f - k), c);
            yield return null;
        }
        Destroy(ring.gameObject);
        if (t != null)
        {
            t.position = floor;
            t.localScale = rest;
            SetSquash(t, 1f, 1f);
        }
    }

    /// <summary>
    /// Black hole (a choice in Customize): the pawn twists and collapses into a black ball
    /// circled by a glowing ring, light streaming in; the ball throbs, then morphs into the new
    /// piece's black silhouette, which lights up and dissolves to reveal the piece.
    /// </summary>
    private IEnumerator BlackHoleRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
    {
        Transform t = promoted.transform;
        SetSquash(t, 0f, 0f);
        yield return null;
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        RestScale(t);
        SetSquash(t, 0f, 0f);
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Quaternion flat = container != null ? container.rotation : Quaternion.identity;
        float cell = Cell;
        Vector3 floor = t.position;
        Bounds pawnBounds = pawnCopy != null ? BoundsOf(pawnCopy) : new Bounds(floor + up * cell * 0.35f, Vector3.one * cell * 0.5f);
        Vector3 core = floor + up * Mathf.Max(0.25f * cell, Vector3.Dot(pawnBounds.center - floor, up));
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);

        GameObject ball = Solid("Black Hole", sphere, new Color(0.02f, 0f, 0.05f, 1f), core, Vector3.one * 0.01f);
        LineRenderer disk = RingLine("Black Hole Disk", flat * Quaternion.Euler(18f, 0f, 0f), 0.05f * cell);
        disk.transform.position = core;

        // Light streaming in: motes that spiral from round the pawn into the ball
        var motes = new List<(Transform t, Renderer r, Vector3 from, float angle, float delay)>();
        for (int i = 0; i < 18; i++)
        {
            Vector3 from = pawnBounds.center + Vector3.Scale(Random.insideUnitSphere, pawnBounds.extents) * 1.3f;
            GameObject m = Solid("Mote", sphere, Color.Lerp(color, Color.white, 0.4f), from, Vector3.one * cell * 0.05f);
            motes.Add((m.transform, m.GetComponent<Renderer>(), from, Random.Range(120f, 300f), Random.Range(0f, 0.35f)));
        }

        float spin = 0f;
        void Disk(float radius, float alpha)
        {
            spin += Time.deltaTime * 4f;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f + spin;
                float wobble = 1f + 0.06f * Mathf.Sin(a * 3f + spin * 2f);
                disk.SetPosition(i, new Vector3(Mathf.Cos(a) * radius * wobble, 0, Mathf.Sin(a) * radius * wobble));
            }
            Color c = color; c.a = alpha;
            disk.startColor = disk.endColor = c;
        }

        // 1. Collapse: the pawn twists and shrinks into the ball as it forms
        const float collapse = 0.55f;
        Vector3 pawnScale = pawnCopy != null ? pawnCopy.transform.localScale : Vector3.one;
        Vector3 pawnPos = pawnCopy != null ? pawnCopy.transform.position : floor;
        Quaternion pawnRot = pawnCopy != null ? pawnCopy.transform.rotation : Quaternion.identity;
        float ballSize = 0.34f * cell;
        for (float time = 0; time < collapse; time += Time.deltaTime)
        {
            float k = time / collapse;
            float e = k * k;
            if (pawnCopy != null)
            {
                pawnCopy.transform.localScale = pawnScale * Mathf.Lerp(1f, 0.01f, e);
                pawnCopy.transform.position = Vector3.Lerp(pawnPos, core, e);
                pawnCopy.transform.rotation = Quaternion.AngleAxis(e * 540f, up) * pawnRot;
            }
            float size = ballSize * Mathf.SmoothStep(0f, 1f, k);
            ball.transform.localScale = Vector3.one * size;
            Disk(Mathf.Lerp(0.15f, 0.42f, k) * cell, k);
            foreach (var m in motes)
            {
                if (m.t == null) continue;
                float local = Mathf.Clamp01((k - m.delay) / (1f - m.delay));
                Vector3 p = Vector3.Lerp(m.from, core, local * local);
                Vector3 axis = core + Vector3.Project(p - core, up);
                m.t.position = axis + Quaternion.AngleAxis(local * m.angle, up) * (p - axis);
                m.t.localScale = Vector3.one * cell * 0.05f * (1f - local * 0.8f);
            }
            yield return null;
        }
        if (pawnCopy != null) Destroy(pawnCopy);
        foreach (var m in motes) if (m.t != null) Destroy(m.t.gameObject);

        // 2. The ball throbs
        const float throb = 0.3f;
        for (float time = 0; time < throb; time += Time.deltaTime)
        {
            float k = time / throb;
            float pulse = 1f + 0.12f * Mathf.Sin(k * Mathf.PI * 3f);
            ball.transform.localScale = Vector3.one * ballSize * pulse;
            Disk(0.42f * cell * pulse, 1f);
            yield return null;
        }

        // 3. The ball itself morphs into the piece: a black silhouette of the new piece starts
        //    squeezed into the ball's round shape, stretches into the piece's outline, then
        //    lights up in the piece's colour and dissolves to reveal it
        SetSquash(t, 1f, 1f);
        GameObject shape = VisualCopy(promoted);
        SetSquash(t, 0f, 0f);
        var shapeRenderers = new List<Renderer>(shape.GetComponentsInChildren<Renderer>());
        foreach (Renderer r in shapeRenderers) { r.sharedMaterial = Glow; SetColor(r, Color.black); }
        Bounds rest = BoundsOf(shape);
        Vector3 restPos = shape.transform.position;
        Vector3 restScale = shape.transform.localScale;
        Vector3 restCentreOffset = rest.center - restPos;
        float width = Mathf.Max(rest.size.x, rest.size.z), height = Mathf.Max(0.01f, rest.size.y);
        // Squash and stretch that make the piece as round and as big as the ball
        float roundWide = ballSize / Mathf.Max(0.01f, width), roundTall = ballSize / height;
        Destroy(ball);

        const float morph = 0.6f;
        for (float time = 0; time < morph; time += Time.deltaTime)
        {
            float k = time / morph;
            float e = k * k * (3f - 2f * k);
            // Jelly: overshoot past the final shape and settle
            float jelly = Mathf.Sin(k * Mathf.PI * 2.5f) * (1f - k) * 0.12f;
            float wide = Mathf.Lerp(roundWide, 1f, e) * (1f - jelly);
            float tall = Mathf.Lerp(roundTall, 1f, e) * (1f + jelly);
            shape.transform.localScale = new Vector3(restScale.x * wide, restScale.y * tall, restScale.z * wide);
            // Its middle travels from the ball's centre down to where the piece stands
            Vector3 centre = Vector3.Lerp(core, restPos + restCentreOffset, e);
            shape.transform.position = centre - Vector3.Scale(restCentreOffset, new Vector3(wide, tall, wide));
            Disk(Mathf.Lerp(0.42f, 0.6f, e) * cell, 1f - k * 0.6f);
            yield return null;
        }
        shape.transform.SetPositionAndRotation(restPos, shape.transform.rotation);
        shape.transform.localScale = restScale * 1.02f; // just over the real piece while it fades

        // Light up and dissolve into the real piece
        if (t != null) SetSquash(t, 1f, 1f);
        GameObject flash = Solid("Black Hole Flash", sphere, color, rest.center, Vector3.one * 0.3f);
        StartCoroutine(FadeOut(flash, 0.3f, 0.3f * cell, 0.9f * cell));
        const float reveal = 0.35f;
        for (float time = 0; time < reveal; time += Time.deltaTime)
        {
            float k = time / reveal;
            Color c = k < 0.4f ? Color.Lerp(Color.black, Color.Lerp(color, Color.white, 0.3f), k / 0.4f) : Color.Lerp(color, Color.white, 0.3f);
            c.a = k < 0.4f ? 1f : 1f - (k - 0.4f) / 0.6f;
            foreach (Renderer r in shapeRenderers) if (r != null) SetColor(r, c);
            Disk(0.6f * cell, 0.4f * (1f - k));
            yield return null;
        }
        Destroy(shape);
        Destroy(disk.gameObject);
        if (t != null) LandingRing(t.position, color);
    }

    /// <summary>A wide ring of light rippling out across the floor</summary>
    private IEnumerator Shockwave(Vector3 centre, Quaternion flat, Color color, float cell)
    {
        LineRenderer wave = RingLine("Shockwave", flat, 0.1f * cell);
        wave.transform.position = centre;
        const float duration = 0.7f;
        for (float time = 0; time < duration; time += Time.deltaTime)
        {
            float k = time / duration;
            float radius = Mathf.Lerp(0.2f, 2.2f, 1f - (1f - k) * (1f - k)) * cell;
            for (int i = 0; i < 64; i++)
            {
                float a = i / 64f * Mathf.PI * 2f;
                wave.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
            }
            wave.widthMultiplier = Mathf.Lerp(0.14f, 0.02f, k) * cell;
            Color c = color; c.a = 0.9f * (1f - k);
            wave.startColor = wave.endColor = c;
            yield return null;
        }
        Destroy(wave.gameObject);
    }

    private static LineRenderer RingLine(string name, Quaternion rotation, float width)
    {
        var go = new GameObject(name);
        go.transform.rotation = rotation;
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = Glow;
        line.useWorldSpace = false;
        line.loop = true;
        line.positionCount = name == "Shockwave" ? 64 : 48;
        line.widthMultiplier = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    /// <summary>Points spread over a piece's meshes (world space)</summary>
    private static List<Vector3> SurfacePoints(ChessPiece piece, int count)
    {
        var verts = new List<Vector3>();
        foreach (MeshFilter f in piece.GetComponentsInChildren<MeshFilter>())
        {
            if (f.sharedMesh == null || !f.sharedMesh.isReadable) continue;
            string n = f.gameObject.name.ToLowerInvariant();
            if (n.Contains("glow") || n.Contains("indicator") || n.Contains("highlight")) continue;
            Vector3[] v = f.sharedMesh.vertices;
            Matrix4x4 m = f.transform.localToWorldMatrix;
            int step = Mathf.Max(1, v.Length / 400);
            for (int i = 0; i < v.Length; i += step) verts.Add(m.MultiplyPoint3x4(v[i]));
        }
        var points = new List<Vector3>(count);
        if (verts.Count == 0)
        {
            // Unreadable meshes: fill the piece's box instead
            Bounds b = BoundsOf(piece.gameObject);
            for (int i = 0; i < count; i++) points.Add(b.center + Vector3.Scale(Random.insideUnitSphere, b.extents * 0.8f));
            return points;
        }
        for (int i = 0; i < count; i++) points.Add(verts[Random.Range(0, verts.Count)]);
        return points;
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
        if (piece == null || !Enabled) return;
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

using System.Collections;
using UnityEngine;

/// <summary>
/// Queen Blitz effects: pawns knocked flying as the sliding queen hits them, and the dust
/// (or, on an ice floor, ice spray) she kicks up behind her.
/// </summary>
public partial class PieceEffects
{
    private static readonly Color DustColor = new Color(0.78f, 0.68f, 0.55f);
    private static readonly Color IceColor = new Color(0.88f, 0.97f, 1f);

    /// <summary>
    /// The queen is sliding through this pawn: when she reaches its square it's knocked flying
    /// the way she's going (and a little up and aside), tumbling, then breaks up.
    /// </summary>
    public static void KnockAway(ChessPiece victim, ChessPiece queen, Vector3 worldDirection, bool ice)
    {
        if (victim == null || !Enabled) return;
        GameObject copy = VisualCopy(victim);
        Runner.StartCoroutine(Runner.KnockRoutine(copy, queen, worldDirection.normalized, ice, GlowColor(victim.pieceColor)));
    }

    private IEnumerator KnockRoutine(GameObject copy, ChessPiece queen, Vector3 dir, bool ice, Color color)
    {
        Vector3 square = copy.transform.position;
        // Wait for the queen to set off, then to reach this square
        float waited = 0f;
        while (queen != null && !queen.IsMoving && waited < 0.25f) { waited += Time.deltaTime; yield return null; }
        waited = 0f;
        while (queen != null && queen.IsMoving && waited < 3f)
        {
            if (Vector3.Distance(queen.transform.position, square) < Cell * 0.45f) break;
            waited += Time.deltaTime;
            yield return null;
        }
        if (copy == null) yield break;
        Haptics.Tick(); // a light tick per pawn swept (bunched up ones are dropped)
        if (AccessibilityPrefs.ReduceMotion)
        {
            yield return FadeAwayRoutine(copy, 0.25f); // Reduce Motion: it fades where it stood
            yield break;
        }

        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Vector3 side = Vector3.Cross(dir, up);
        if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dir, Vector3.right);
        side.Normalize();
        float cell = Cell;

        Puff(square, up, ice, 10, cell);

        // Flung ahead and up, veering to one side, spinning end over end
        Vector3 v = (dir * Random.Range(7f, 10f) + up * Random.Range(5f, 8f) + side * Random.Range(-3f, 3f)) * (cell / 2.8f);
        Vector3 spinAxis = Vector3.Cross(up, dir);
        if (spinAxis.sqrMagnitude < 0.01f) spinAxis = side;
        float spin = Random.Range(540f, 900f);
        float gravity = 22f * cell / 2.8f;
        Vector3 startScale = copy.transform.localScale;
        const float flight = 0.75f;
        for (float t = 0f; t < flight && copy != null; t += Time.deltaTime)
        {
            float dt = Time.deltaTime;
            v -= up * gravity * dt;
            copy.transform.position += v * dt;
            copy.transform.Rotate(spinAxis, spin * dt, Space.World);
            float k = t / flight;
            if (k > 0.6f) copy.transform.localScale = startScale * Mathf.Lerp(1f, 0.2f, (k - 0.6f) / 0.4f);
            yield return null;
        }
        if (copy == null) yield break;

        // Breaks up where it ends
        Vector3 end = copy.transform.position;
        Destroy(copy);
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube, ref shardMesh);
        for (int i = 0; i < 10; i++)
        {
            float s = Random.Range(0.05f, 0.12f) * cell;
            Color c = Color.Lerp(color, Color.white, Random.Range(0f, 0.4f));
            GameObject shard = Solid("Knock Shard", cube, c, end + Random.insideUnitSphere * cell * 0.15f, Vector3.one * s);
            shard.transform.rotation = Random.rotation;
            StartCoroutine(Fly(shard, (Random.onUnitSphere * 3f + up * 2f) * (cell / 2.8f), gravity * 0.7f, 0.6f, c));
        }
    }

    /// <summary>A bit of dust (or ice spray) left behind the sliding queen at this point</summary>
    public static void SlideTrail(Vector3 worldPosition, Vector3 worldDirection, bool ice)
    {
        if (!Enabled) return;
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        float cell = Cell;
        Mesh mesh = ice ? PrimitiveMesh(PrimitiveType.Cube, ref shardMesh) : PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);
        Vector3 back = -worldDirection.normalized;
        Vector3 side = Vector3.Cross(worldDirection, up).normalized;
        int count = ice ? 3 : 2;
        for (int i = 0; i < count; i++)
        {
            Vector3 at = worldPosition + up * cell * 0.05f + side * Random.Range(-0.2f, 0.2f) * cell;
            if (ice)
            {
                // Glittering chips sprayed out sideways and back
                float s = Random.Range(0.03f, 0.07f) * cell;
                Color c = Color.Lerp(IceColor, Color.white, Random.Range(0f, 0.6f));
                c.a = 0.9f;
                GameObject chip = Solid("Ice Spray", mesh, c, at, Vector3.one * s);
                chip.transform.rotation = Random.rotation;
                Vector3 v = (back * Random.Range(1f, 2.5f) + side * Random.Range(-3f, 3f) + up * Random.Range(1.5f, 3.5f)) * (cell / 2.8f);
                Runner.StartCoroutine(Runner.Fly(chip, v, 14f * cell / 2.8f, Random.Range(0.35f, 0.55f), c));
            }
            else
            {
                // Soft puffs that billow and fade
                Color c = Color.Lerp(DustColor, Color.white, Random.Range(0f, 0.25f));
                c.a = 0.45f;
                GameObject puff = Solid("Dust", mesh, c, at, Vector3.one * cell * 0.12f);
                Vector3 v = (back * Random.Range(0.4f, 1f) + side * Random.Range(-0.6f, 0.6f) + up * Random.Range(0.3f, 0.9f)) * (cell / 2.8f);
                Runner.StartCoroutine(Runner.Billow(puff, v, cell * Random.Range(0.35f, 0.5f), Random.Range(0.55f, 0.8f), c));
            }
        }
    }

    /// <summary>A burst of dust or ice where a pawn is hit</summary>
    private static void Puff(Vector3 at, Vector3 up, bool ice, int count, float cell)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 d = Random.onUnitSphere;
            if (Vector3.Dot(d, up) < 0f) d -= 2f * Vector3.Dot(d, up) * up;
            if (ice)
            {
                Color c = IceColor; c.a = 0.9f;
                GameObject chip = Solid("Ice Burst", PrimitiveMesh(PrimitiveType.Cube, ref shardMesh), c, at, Vector3.one * Random.Range(0.04f, 0.08f) * cell);
                chip.transform.rotation = Random.rotation;
                Runner.StartCoroutine(Runner.Fly(chip, d * Random.Range(2f, 4f) * (cell / 2.8f), 12f * cell / 2.8f, 0.5f, c));
            }
            else
            {
                Color c = DustColor; c.a = 0.5f;
                GameObject puff = Solid("Dust Burst", PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh), c, at, Vector3.one * cell * 0.12f);
                Runner.StartCoroutine(Runner.Billow(puff, d * Random.Range(0.8f, 1.6f) * (cell / 2.8f), cell * 0.5f, 0.7f, c));
            }
        }
    }

    /// <summary>A bit thrown into the air: falls, spins, fades</summary>
    private IEnumerator Fly(GameObject go, Vector3 v, float gravity, float life, Color color)
    {
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Renderer r = go.GetComponent<Renderer>();
        Vector3 spin = Random.insideUnitSphere * 600f;
        for (float t = 0f; t < life && go != null; t += Time.deltaTime)
        {
            v -= up * gravity * Time.deltaTime;
            go.transform.position += v * Time.deltaTime;
            go.transform.Rotate(spin * Time.deltaTime, Space.World);
            Color c = color; c.a = color.a * (1f - t / life);
            SetColor(r, c);
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    /// <summary>A puff that drifts, swells to its size and fades</summary>
    private IEnumerator Billow(GameObject go, Vector3 v, float size, float life, Color color)
    {
        Renderer r = go.GetComponent<Renderer>();
        float start = go.transform.localScale.x;
        for (float t = 0f; t < life && go != null; t += Time.deltaTime)
        {
            float k = t / life;
            v *= 1f - 2.5f * Time.deltaTime; // air slows it
            go.transform.position += v * Time.deltaTime;
            go.transform.localScale = Vector3.one * Mathf.Lerp(start, size, Mathf.Sqrt(k));
            Color c = color; c.a = color.a * (1f - k) * (1f - k);
            SetColor(r, c);
            yield return null;
        }
        if (go != null) Destroy(go);
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Queen Blitz bombs going off: when the queen reaches the square that set it off (and, in a chain,
/// a beat later per link), a hot flash swells out to the 3x3x3 cube it clears, sparks fly, and every
/// pawn it caught is thrown straight out from the bomb, tumbling, then breaks up.
/// </summary>
public partial class PieceEffects
{
    private static readonly Color BlastHot = new Color(1f, 0.85f, 0.35f);
    private static readonly Color BlastEdge = new Color(1f, 0.3f, 0.55f);

    /// <summary>
    /// A bomb at worldCentre goes off. bomb (its model, or null) is removed then. It waits for
    /// `mover` (the queen, or the Mirror Queen; `moving` says she's under way) to reach worldTrigger (the bomb she passed that started it), then `link` beats for a
    /// chain. The victims are still on the board: copies are made now, before they're removed.
    /// </summary>
    public static void BombBlast(Vector3 worldCentre, Vector3 worldTrigger, Transform mover, System.Func<bool> moving, int link, GameObject bomb, List<ChessPiece> victims)
    {
        var copies = new List<GameObject>();
        if (Enabled) foreach (ChessPiece v in victims) if (v != null) copies.Add(VisualCopy(v));
        Runner.StartCoroutine(Runner.BlastRoutine(worldCentre, worldTrigger, mover, moving, link, bomb, copies));
    }

    private IEnumerator BlastRoutine(Vector3 centre, Vector3 trigger, Transform mover, System.Func<bool> moving, int link, GameObject bomb, List<GameObject> copies)
    {
        // Wait for the queen (or the Mirror Queen) to set off, then to reach the bomb that started it
        float waited = 0f;
        while (mover != null && !moving() && waited < 0.25f) { waited += Time.deltaTime; yield return null; }
        waited = 0f;
        while (mover != null && moving() && waited < 3f)
        {
            if (Vector3.Distance(mover.position, trigger) < Cell * 0.45f) break;
            waited += Time.deltaTime;
            yield return null;
        }
        if (link > 0) yield return new WaitForSeconds(0.16f * link);
        if (bomb != null) Destroy(bomb);
        if (!Enabled)
        {
            foreach (GameObject c in copies) if (c != null) Destroy(c);
            yield break;
        }
        Haptics.Heavy();
        GameSounds.BombBlast(link);
        float cell = Cell;
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;

        if (AccessibilityPrefs.ReduceMotion)
        {
            // Reduce Motion: a soft glow where it was, and the pawns fade where they stood
            GameObject soft = Solid("Bomb Glow", PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh), new Color(BlastHot.r, BlastHot.g, BlastHot.b, 0.4f), centre, Vector3.one * cell * 1.5f);
            StartCoroutine(FadeOrb(soft, 0.4f, 1f));
            foreach (GameObject c in copies) if (c != null) StartCoroutine(FadeAwayRoutine(c, 0.3f));
            yield break;
        }

        // The flash: a bright core and a pink shell swelling out to the edge of the cube it clears
        GameObject core = Solid("Bomb Flash", PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh), BlastHot, centre, Vector3.one * cell * 0.4f);
        GameObject shell = Solid("Bomb Shell", PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh), new Color(BlastEdge.r, BlastEdge.g, BlastEdge.b, 0.35f), centre, Vector3.one * cell * 0.6f);
        StartCoroutine(FadeOrb(core, 0.28f, cell * 1.6f));
        StartCoroutine(FadeOrb(shell, 0.45f, cell * 3.2f));
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube, ref shardMesh);
        for (int i = 0; i < 18; i++)
        {
            Color c = Color.Lerp(BlastHot, BlastEdge, Random.value);
            GameObject spark = Solid("Bomb Spark", cube, c, centre, Vector3.one * Random.Range(0.04f, 0.09f) * cell);
            spark.transform.rotation = Random.rotation;
            StartCoroutine(Fly(spark, Random.onUnitSphere * Random.Range(4f, 8f) * (cell / 2.8f), 10f * cell / 2.8f, 0.55f, c));
        }

        // Every pawn caught is thrown out from the bomb
        float gravity = 22f * cell / 2.8f;
        foreach (GameObject copy in copies)
        {
            if (copy == null) continue;
            Vector3 dir = copy.transform.position + up * cell * 0.3f - centre;
            if (dir.sqrMagnitude < 0.0001f) dir = up;
            dir.Normalize();
            StartCoroutine(Thrown(copy, (dir * Random.Range(6f, 9f) + up * Random.Range(3f, 6f)) * (cell / 2.8f), gravity, GlowColor(QueenBlitz.PawnColor)));
        }
    }

    private IEnumerator Thrown(GameObject copy, Vector3 v, float gravity, Color color)
    {
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Vector3 spinAxis = Random.onUnitSphere;
        float spin = Random.Range(540f, 900f), cell = Cell;
        Vector3 startScale = copy.transform.localScale;
        const float flight = 0.7f;
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
        Vector3 end = copy.transform.position;
        Destroy(copy);
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube, ref shardMesh);
        for (int i = 0; i < 8; i++)
        {
            Color c = Color.Lerp(color, Color.white, Random.Range(0f, 0.4f));
            GameObject shard = Solid("Blast Shard", cube, c, end + Random.insideUnitSphere * cell * 0.15f, Vector3.one * Random.Range(0.05f, 0.11f) * cell);
            shard.transform.rotation = Random.rotation;
            StartCoroutine(Fly(shard, (Random.onUnitSphere * 3f + up * 2f) * (cell / 2.8f), gravity * 0.7f, 0.6f, c));
        }
    }

    /// <summary>An orb that swells to `size` across and fades out</summary>
    private IEnumerator FadeOrb(GameObject orb, float life, float size)
    {
        Renderer r = orb.GetComponent<Renderer>();
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        Color c = block.GetColor(ColorId);
        Vector3 from = orb.transform.localScale;
        for (float t = 0f; t < life && orb != null; t += Time.deltaTime)
        {
            float k = t / life;
            orb.transform.localScale = Vector3.Lerp(from, Vector3.one * size, 1f - (1f - k) * (1f - k));
            SetColor(r, new Color(c.r, c.g, c.b, c.a * (1f - k)));
            yield return null;
        }
        if (orb != null) Destroy(orb);
    }
}

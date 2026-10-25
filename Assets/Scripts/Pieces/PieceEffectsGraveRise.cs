using System.Collections;
using UnityEngine;

/// <summary>
/// Grave Rise (a promotion choice in Customize): the pawn cracks and crumbles into a heap of
/// dust, a mound of dirt heaves up on its square, and the new piece claws its way up out of the
/// earth, shaking off clods, while the mound sinks back down.
/// </summary>
public partial class PieceEffects
{
    private IEnumerator GraveRiseRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
    {
        Transform t = promoted.transform;
        SetSquash(t, 0f, 0f);
        yield return null;
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Quaternion restRotation = t.localRotation;
        Vector3 rest = RestScale(t);
        SetSquash(t, 0f, 0f);
        float cell = Cell;
        Vector3 floor = t.position;
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);
        Mesh cube = PrimitiveMesh(PrimitiveType.Cube, ref shardMesh);
        Color dust = new Color(0.62f, 0.56f, 0.5f), dirt = new Color(0.3f, 0.2f, 0.14f);

        // 1. The pawn trembles, then sags and crumbles: shrinks downward as dust pours off it
        Vector3 pawnScale = pawnCopy != null ? pawnCopy.transform.localScale : Vector3.one;
        Bounds pawnBounds = pawnCopy != null ? BoundsOf(pawnCopy) : new Bounds(floor + up * cell * 0.35f, Vector3.one * cell * 0.5f);
        const float shake = 0.35f, crumble = 0.6f;
        for (float time = 0; time < shake; time += Time.deltaTime)
        {
            if (pawnCopy != null) pawnCopy.transform.position = floor + Random.insideUnitSphere * 0.025f * cell;
            yield return null;
        }
        float nextGrain = 0f;
        for (float time = 0; time < crumble; time += Time.deltaTime)
        {
            float k = time / crumble;
            if (pawnCopy != null)
            {
                pawnCopy.transform.position = floor;
                pawnCopy.transform.localScale = new Vector3(pawnScale.x * (1f + 0.2f * k), pawnScale.y * (1f - k), pawnScale.z * (1f + 0.2f * k));
            }
            if (time >= nextGrain)
            {
                nextGrain = time + 0.02f;
                Vector3 from = pawnBounds.center + Vector3.Scale(Random.insideUnitSphere, pawnBounds.extents) * (1f - k);
                StartCoroutine(Grain(cube, dust, from, up, cell));
            }
            yield return null;
        }
        if (pawnCopy != null) Destroy(pawnCopy);
        // What's left: a little heap of dust
        GameObject heap = Solid("Dust Heap", sphere, dust, floor, new Vector3(0.5f, 0.12f, 0.5f) * cell);
        heap.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

        // 2. A mound of dirt heaves up, cracking, as something stirs underneath
        GameObject moundObj = Solid("Grave Mound", sphere, dirt, floor, Vector3.zero);
        moundObj.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        const float heave = 0.45f;
        for (float time = 0; time < heave; time += Time.deltaTime)
        {
            float k = time / heave;
            float bulge = Mathf.SmoothStep(0f, 1f, k) * (1f + 0.15f * Mathf.Sin(time * 40f));
            moundObj.transform.localScale = new Vector3(0.75f, 0.32f * bulge, 0.75f) * cell;
            heap.transform.localScale = new Vector3(0.5f, 0.12f, 0.5f) * cell * (1f - k);
            yield return null;
        }
        Destroy(heap);

        // 3. The new piece claws up out of the earth, wobbling, throwing clods
        const float rise = 0.75f;
        float height = Mathf.Max(0.6f * cell, rest.y);
        float nextClod = 0f;
        for (float time = 0; time < rise && t != null; time += Time.deltaTime)
        {
            float k = time / rise;
            float eased = 1f - Mathf.Pow(1f - k, 2f);
            SetSquash(t, 1f, 1f);
            t.position = floor - up * (1f - eased) * height * 1.05f;   // starts buried, the mound hides it
            float wobble = Mathf.Sin(time * 28f) * 9f * (1f - k);
            t.localRotation = restRotation * Quaternion.Euler(wobble, 0f, wobble * 0.6f);
            moundObj.transform.localScale = new Vector3(0.75f + 0.25f * k, 0.32f * (1f - 0.4f * k), 0.75f + 0.25f * k) * cell;
            if (time >= nextClod)
            {
                nextClod = time + 0.05f;
                StartCoroutine(Clod(sphere, dirt, floor + up * 0.2f * cell, up, cell));
            }
            yield return null;
        }
        if (t != null)
        {
            t.position = floor;
            t.localRotation = restRotation;
            Land(promoted);
            LandingRing(floor, color);
        }

        // 4. The mound sinks back into the floor
        const float sink = 0.6f;
        for (float time = 0; time < sink; time += Time.deltaTime)
        {
            float k = time / sink;
            moundObj.transform.localScale = new Vector3(1f, 0.19f * (1f - k), 1f) * cell;
            yield return null;
        }
        Destroy(moundObj);
    }

    // A grain of dust sliding off the crumbling pawn and settling
    private IEnumerator Grain(Mesh cube, Color c, Vector3 from, Vector3 up, float cell)
    {
        GameObject g = Solid("Dust", cube, c, from, Vector3.one * Random.Range(0.025f, 0.05f) * cell);
        Vector3 drift = (Random.insideUnitSphere * 0.4f + up * 0.15f) * cell;
        for (float time = 0; time < 0.7f && g != null; time += Time.deltaTime)
        {
            drift -= up * cell * 2.5f * Time.deltaTime;
            g.transform.position += drift * Time.deltaTime;
            Color fade = c; fade.a = 1f - time / 0.7f;
            SetColor(g.GetComponent<Renderer>(), fade);
            yield return null;
        }
        if (g != null) Destroy(g);
    }

    // A clod of dirt flung up out of the grave
    private IEnumerator Clod(Mesh sphere, Color c, Vector3 from, Vector3 up, float cell)
    {
        GameObject g = Solid("Clod", sphere, c, from, Vector3.one * Random.Range(0.05f, 0.1f) * cell);
        Vector3 side = Vector3.ProjectOnPlane(Random.onUnitSphere, up).normalized;
        Vector3 vel = (side * Random.Range(0.6f, 1.2f) + up * Random.Range(1.2f, 2.2f)) * cell;
        for (float time = 0; time < 0.8f && g != null; time += Time.deltaTime)
        {
            vel -= up * cell * 6f * Time.deltaTime;
            g.transform.position += vel * Time.deltaTime;
            Color fade = c; fade.a = Mathf.Clamp01((0.8f - time) / 0.3f);
            SetColor(g.GetComponent<Renderer>(), fade);
            yield return null;
        }
        if (g != null) Destroy(g);
    }
}

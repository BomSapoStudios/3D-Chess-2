using System.Collections;
using UnityEngine;

/// <summary>
/// Splat (a promotion choice in Customize): the new piece comes flying in from above, flips
/// once and lands smack on the pawn, which flattens into a big cartoon splat of paint in the
/// team colour; drops fly out and the new piece wobbles back into shape.
/// </summary>
public partial class PieceEffects
{
    private IEnumerator SplatRoutine(GameObject pawnCopy, ChessPiece promoted, Color color)
    {
        Transform t = promoted.transform;
        SetSquash(t, 0f, 0f);
        yield return null;
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        Transform container = PiecesContainer;
        Vector3 up = container != null ? container.up : Vector3.up;
        Quaternion restRotation = t.localRotation;
        RestScale(t);
        float cell = Cell;
        Vector3 floor = t.position;
        Camera cam = ViewCamera;
        Vector3 side = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, up).normalized : Vector3.right;

        // 1. The pawn looks up nervously: a little shiver
        Vector3 pawnScale = pawnCopy != null ? pawnCopy.transform.localScale : Vector3.one;
        for (float time = 0; time < 0.3f; time += Time.deltaTime)
        {
            if (pawnCopy != null) pawnCopy.transform.position = floor + side * Mathf.Sin(time * 70f) * 0.03f * cell;
            yield return null;
        }

        // 2. The new piece flies in on an arc from high up, speeding up, stretched with speed, flipping once
        SetSquash(t, 0.8f, 1.3f);
        Vector3 start = floor + up * cell * 3.2f - side * cell * 2.2f;
        const float fly = 0.42f;
        for (float time = 0; time < fly && t != null; time += Time.deltaTime)
        {
            float k = time / fly;
            float fall = k * k; // accelerating
            t.position = Vector3.Lerp(start, floor, fall) + up * Mathf.Sin(k * Mathf.PI) * cell * 0.6f;
            t.localRotation = Quaternion.AngleAxis(Mathf.Lerp(-360f, 0f, Mathf.Sqrt(k)), Vector3.forward) * restRotation;
            yield return null;
        }
        if (t == null) { if (pawnCopy != null) Destroy(pawnCopy); yield break; }
        t.position = floor;
        t.localRotation = restRotation;

        // 3. SPLAT: the pawn flattens to a pancake and paint bursts out across the square
        if (pawnCopy != null)
        {
            pawnCopy.transform.position = floor;
            pawnCopy.transform.localScale = new Vector3(pawnScale.x * 1.9f, pawnScale.y * 0.06f, pawnScale.z * 1.9f);
            Destroy(pawnCopy, 0.12f);
        }
        Mesh sphere = PrimitiveMesh(PrimitiveType.Sphere, ref orbMesh);
        var blobs = new System.Collections.Generic.List<(GameObject go, Vector3 size)>();
        Quaternion flat = Quaternion.FromToRotation(Vector3.up, up);
        int count = 9;
        for (int i = 0; i < count; i++)
        {
            float a = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
            Vector3 dir = flat * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float reach = Random.Range(0.3f, 0.65f) * cell;
            Vector3 size = new Vector3(Random.Range(0.22f, 0.4f), 0.07f, Random.Range(0.14f, 0.26f)) * cell; // domed blobs, so they read from a low view too
            GameObject blob = Solid("Splat", sphere, color, floor + dir * reach + up * 0.01f * cell, Vector3.zero);
            blob.transform.rotation = Quaternion.LookRotation(dir, up);
            blobs.Add((blob, size));
        }
        GameObject puddle = Solid("Splat", sphere, color, floor + up * 0.008f * cell, Vector3.zero);
        puddle.transform.rotation = flat;
        blobs.Add((puddle, new Vector3(1.0f, 0.06f, 1.0f) * cell));
        // Drops thrown up and out, landing as little spots
        for (int i = 0; i < 10; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            Vector3 dir = flat * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            StartCoroutine(SplatDrop(sphere, color, floor, dir, up, cell));
        }

        // The piece squashes on impact, then springs back with a wobble while the paint spreads
        const float settle = 0.55f;
        for (float time = 0; time < settle && t != null; time += Time.deltaTime)
        {
            float k = time / settle;
            float wobble = Mathf.Exp(-5f * k) * Mathf.Cos(k * Mathf.PI * 4f);
            SetSquash(t, 1f + 0.35f * wobble, 1f - 0.35f * wobble);
            float spread = 1f - Mathf.Pow(1f - Mathf.Clamp01(k * 3f), 3f);
            foreach (var b in blobs) if (b.go != null) b.go.transform.localScale = b.size * spread;
            yield return null;
        }
        if (t != null)
        {
            SetSquash(t, 1f, 1f);
            LandingRing(t.position, color);
        }

        // The paint fades away
        for (float time = 0; time < 1.1f; time += Time.deltaTime)
        {
            float k = time / 1.1f;
            Color c = color; c.a = 1f - k * k;
            foreach (var b in blobs) if (b.go != null) SetColor(b.go.GetComponent<Renderer>(), c);
            yield return null;
        }
        foreach (var b in blobs) if (b.go != null) Destroy(b.go);
    }

    private IEnumerator SplatDrop(Mesh sphere, Color color, Vector3 floor, Vector3 dir, Vector3 up, float cell)
    {
        GameObject drop = Solid("Splat Drop", sphere, color, floor, Vector3.one * 0.11f * cell);
        float speed = Random.Range(0.8f, 1.6f) * cell, lift = Random.Range(1.2f, 2.2f) * cell;
        float flight = 2f * lift / (9f * cell);
        Vector3 pos = floor + up * 0.2f * cell;
        for (float time = 0; time < flight && drop != null; time += Time.deltaTime)
        {
            float h = lift * time - 4.5f * cell * time * time;
            drop.transform.position = pos + dir * speed * time + up * h;
            yield return null;
        }
        if (drop == null) yield break;
        // Lands as a flat spot, then fades
        drop.transform.position = floor + dir * speed * flight + up * 0.01f * cell;
        drop.transform.rotation = Quaternion.FromToRotation(Vector3.up, up);
        drop.transform.localScale = new Vector3(0.14f, 0.015f, 0.14f) * cell;
        for (float time = 0; time < 0.9f && drop != null; time += Time.deltaTime)
        {
            Color c = color; c.a = 1f - time / 0.9f;
            SetColor(drop.GetComponent<Renderer>(), c);
            yield return null;
        }
        if (drop != null) Destroy(drop);
    }
}

using UnityEngine;

/// <summary>
/// After a checkmate win the winner's pieces dance where they stand: staggered, bouncy hops with
/// squash and stretch, side-to-side wiggles and now and then a quick spin, each piece on its own
/// beat so it looks like a crowd. The winning king hops higher and spins more; the mated king
/// wobbles and tips over. Only each piece's model (its visual child) moves, never the piece,
/// its cell or the game, and every model goes back exactly where it was when the dance ends
/// (a few seconds of party, then a calmer bounce, up to 12 s) or the scene changes.
/// Reduce Motion: a small bob, and the mated king just leans.
/// </summary>
public class VictoryDance : MonoBehaviour
{
    private const int MaxDancers = 64;
    private const float Party = 3f, MaxTime = 12f, Delay = 0.5f; // the mating move lands first

    private struct Dancer
    {
        public Transform model;
        public Vector3 pos, scale, pivot;   // rest pose in the piece's space; pivot = middle of its base
        public Quaternion rot;
        public float height, phase, rate, spinEvery;
        public bool king, mated;
    }

    private static VictoryDance instance;
    private readonly Dancer[] dancers = new Dancer[MaxDancers];
    private int count;
    private float startedAt;
    private bool gentle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        var go = new GameObject("Victory Dance");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<VictoryDance>();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => instance.count = 0; // the pieces went with the scene
    }

    /// <summary>Start the winner's dance (and the mated king's fall)</summary>
    public static void Begin(PieceColor winner, PieceColor? mated)
    {
        if (instance != null) instance.Run(winner, mated);
    }

    private void Run(PieceColor winner, PieceColor? mated)
    {
        Restore();
        gentle = AccessibilityPrefs.ReduceMotion;
        startedAt = Time.unscaledTime + Delay;
        foreach (ChessPiece piece in FindObjectsByType<ChessPiece>(FindObjectsSortMode.None))
        {
            if (count >= MaxDancers) break;
            if (piece == null || !piece.gameObject.activeInHierarchy) continue;
            bool isMatedKing = mated.HasValue && piece.pieceColor == mated.Value && piece is King;
            if (piece.pieceColor != winner && !isMatedKing) continue;
            Transform model = piece.transform.Find(PieceModels.ModelName);
            if (model == null) continue;

            // The model's base, in the piece's space, so hops and spins turn it about its own middle
            // whatever its pivot (the sets come from different modelling tools)
            Renderer[] parts = model.GetComponentsInChildren<Renderer>(false);
            if (parts.Length == 0) continue;
            Bounds world = parts[0].bounds;
            for (int i = 1; i < parts.Length; i++) world.Encapsulate(parts[i].bounds);
            Transform parent = model.parent;
            Vector3 centre = parent.InverseTransformPoint(world.center);
            Vector3 bottom = parent.InverseTransformPoint(new Vector3(world.center.x, world.min.y, world.center.z));
            float height = parent.InverseTransformVector(Vector3.up * world.size.y).magnitude;

            ref Dancer d = ref dancers[count++];
            d.model = model;
            d.pos = model.localPosition;
            d.rot = model.localRotation;
            d.scale = model.localScale;
            d.pivot = new Vector3(centre.x, bottom.y, centre.z);
            d.height = height;
            d.king = piece is King && !isMatedKing;
            d.mated = isMatedKing;
            d.phase = Random.Range(0f, 1f);
            d.rate = Random.Range(1.7f, 2.3f);     // hops a second
            d.spinEvery = Random.Range(3, 6);       // every few hops, a spin
        }
    }

    private void Update()
    {
        if (count == 0) return;
        float t = Time.unscaledTime - startedAt;
        if (t < 0f) return;
        if (t > MaxTime || GameStateManager.Instance == null || GameStateManager.Instance.currentState != GameState.GameOver)
        {
            Restore();
            return;
        }
        // Full party for a few seconds, then a calmer bounce
        float energy = gentle ? 0.12f : t < Party ? 1f : Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((t - Party) / 1.5f));
        for (int i = 0; i < count; i++)
        {
            ref Dancer d = ref dancers[i];
            if (d.model == null) continue;
            Quaternion turn;
            Vector3 squash, lift;
            if (d.mated) Fall(ref d, t, out turn, out squash, out lift);
            else Hop(ref d, t, energy, out turn, out squash, out lift);
            // Turn and squash about the base's middle, then lift
            Vector3 fromPivot = d.pos - d.pivot;
            d.model.localPosition = d.pivot + turn * Vector3.Scale(squash, fromPivot) + lift;
            d.model.localRotation = turn * d.rot;
            d.model.localScale = Vector3.Scale(d.scale, squash);
        }
    }

    private void Hop(ref Dancer d, float t, float energy, out Quaternion turn, out Vector3 squash, out Vector3 lift)
    {
        float beats = t * d.rate + d.phase;
        int hop = Mathf.FloorToInt(beats);
        float k = beats - hop;                          // 0..1 through this hop
        float air = Mathf.Sin(k * Mathf.PI);            // up and down
        float high = (d.king ? 0.55f : 0.32f) * d.height * energy;
        lift = Vector3.up * air * high;
        // Squash on landing and take-off, stretch at the top
        float land = Mathf.Max(0f, 1f - k * 6f) + Mathf.Max(0f, (k - 0.85f) * 6.6f);
        float stretch = air * 0.12f - land * 0.2f;
        float s = stretch * energy;
        squash = new Vector3(1f - s * 0.5f, 1f + s, 1f - s * 0.5f);
        // A wiggle every hop, and every few hops a full spin in the air (the king spins more)
        float wiggle = Mathf.Sin(beats * Mathf.PI) * 14f * energy;
        bool spins = !gentle && (d.king ? hop % 2 == 0 : hop % Mathf.Max(2, Mathf.RoundToInt(d.spinEvery)) == 0);
        float spin = spins ? Mathf.SmoothStep(0f, 360f, k) * Mathf.Min(1f, energy * 1.5f) : 0f;
        turn = Quaternion.Euler(0f, spin, wiggle);
    }

    private void Fall(ref Dancer d, float t, out Quaternion turn, out Vector3 squash, out Vector3 lift)
    {
        // Wobble, wobble, then over it goes (Reduce Motion: a slow lean)
        float angle;
        if (gentle) angle = Mathf.SmoothStep(0f, 14f, Mathf.Clamp01(t / 1.5f));
        else if (t < 0.9f) angle = Mathf.Sin(t * 18f) * 9f * (t / 0.9f);
        else angle = Mathf.Lerp(0f, 82f, EaseOutBounce(Mathf.Clamp01((t - 0.9f) / 0.7f)));
        turn = Quaternion.Euler(angle, 0f, 0f);
        squash = Vector3.one;
        lift = Vector3.up * (Mathf.Sin(angle * Mathf.Deg2Rad) * d.height * 0.12f); // keep it out of the floor
    }

    private static float EaseOutBounce(float x)
    {
        if (x < 1f / 2.75f) return 7.5625f * x * x;
        if (x < 2f / 2.75f) { x -= 1.5f / 2.75f; return 7.5625f * x * x + 0.75f; }
        if (x < 2.5f / 2.75f) { x -= 2.25f / 2.75f; return 7.5625f * x * x + 0.9375f; }
        x -= 2.625f / 2.75f; return 7.5625f * x * x + 0.984375f;
    }

    /// <summary>Every model back exactly as it was</summary>
    private void Restore()
    {
        for (int i = 0; i < count; i++)
        {
            ref Dancer d = ref dancers[i];
            if (d.model == null) continue;
            d.model.localPosition = d.pos;
            d.model.localRotation = d.rot;
            d.model.localScale = d.scale;
            d.model = null;
        }
        count = 0;
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Background music. Arcade Rush 1 plays in the menus; in a game, music plays only when
/// "Music while you play" is on, using the game's own track (Resources/Audio/Music/game, or
/// zombies / blitz for those modes) and falling back to the menu track, quieter.
/// Tracks fade over a second when the scene changes.
/// </summary>
public class Music : MonoBehaviour
{
    private const string MenuScene = "MainMenu";
    private const float Fade = 1.2f;

    private AudioSource a, b; // crossfade pair; a is the one playing
    private string playing;
    private float trackGain = 1f;
    private float volume; // SoundPrefs.MusicVolume, read again only when a setting changes

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("Music");
        DontDestroyOnLoad(go);
        go.AddComponent<Music>();
    }

    private void Awake()
    {
        a = NewSource();
        b = NewSource();
        SceneManager.activeSceneChanged += (_, __) => Invoke(nameof(Choose), 0.1f); // after the mode flags are set
        SoundPrefs.Changed += Choose;
        Choose();
    }

    private AudioSource NewSource()
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.loop = true;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        s.priority = 0;
        s.volume = 0f;
        return s;
    }

    private void Choose()
    {
        volume = SoundPrefs.MusicVolume;
        bool menu = SceneManager.GetActiveScene().name == MenuScene;
        string track = null;
        float gain = 1f;
        if (menu) track = "menu";
        else if (SoundPrefs.MusicInGames)
        {
            string wanted = ZombieMode.Active ? "zombies" : QueenBlitz.Active ? "blitz" : "game";
            if (Resources.Load<AudioClip>("Audio/Music/" + wanted) != null) track = wanted;
            else if (wanted != "game" && Resources.Load<AudioClip>("Audio/Music/game") != null) track = "game";
            else { track = "menu"; gain = 0.55f; }
        }
        if (!SoundPrefs.Music) track = null;
        trackGain = gain;
        if (track == playing) return;
        playing = track;
        // swap: the old track fades out on b, the new one fades in on a
        (a, b) = (b, a);
        if (track == null) { a.Stop(); a.clip = null; return; }
        AudioClip clip = Resources.Load<AudioClip>("Audio/Music/" + track);
        if (clip == null) { playing = null; return; }
        a.clip = clip;
        a.volume = 0f;
        a.Play();
    }

    private void Update()
    {
        float target = volume * trackGain;
        float step = Time.unscaledDeltaTime / Fade;
        a.volume = Mathf.MoveTowards(a.volume, a.clip != null ? target : 0f, step * Mathf.Max(target, 0.1f));
        b.volume = Mathf.MoveTowards(b.volume, 0f, step);
        if (b.isPlaying && b.volume <= 0f) b.Stop();
    }
}

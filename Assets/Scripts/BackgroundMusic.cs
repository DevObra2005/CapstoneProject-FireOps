using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

// Persistent looping background music for the FRONT-END scenes
// (login, event selection, main menu, results). Uses DontDestroyOnLoad + a
// singleton guard so it plays continuously across those scenes without
// restarting or duplicating.
[RequireComponent(typeof(AudioSource))]
public class BackgroundMusic : MonoBehaviour
{
    public static BackgroundMusic Instance { get; private set; }

    [Tooltip("Optional: assign here, or set the clip directly on the AudioSource.")]
    public AudioClip musicClip;

    [Tooltip("This is the MIX. The player's Music slider scales it via the " +
             "mixer group, rather than replacing it.")]
    [Range(0f, 1f)]
    public float volume = 0.4f;

    // -------------------------------------------------------
    // WHICH SCENES ARE SILENT, NOT WHICH ONES PLAY.
    //
    // Listing the three environments is shorter than listing every front-end
    // scene, and it stays correct on its own: add a new results or settings
    // screen later and it gets music automatically, with nothing to update
    // here. Forgetting to add a scene to a "plays music" list would be a
    // silent bug; forgetting to add one here is not possible, because the
    // environments are a fixed set.
    // -------------------------------------------------------
    [Tooltip("Scenes where this music must NOT play. Everything else plays.")]
    public string[] silentScenes =
    {
        "Office3DScene",
        "Kitchen3DScene",
        "Classroom3DScene"
    };

    private AudioSource source;

    private void Awake()
    {
        // Singleton guard: destroy any duplicate that appears after a scene
        // load, so only the original keeps playing seamlessly.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.volume = volume;

        if (musicClip != null)
            source.clip = musicClip;

        if (source.clip == null)
            Debug.LogWarning("[BackgroundMusic] No AudioClip assigned!");

        // Subscribed AFTER the singleton guard, so only the surviving
        // instance listens. A duplicate that destroys itself never gets here.
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        RouteToMixer();

        // sceneLoaded does NOT fire for the scene that was already open when
        // the game started, so the very first scene is handled here.
        if (!IsSilentScene(SceneManager.GetActiveScene().name))
            Play();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // -------------------------------------------------------
    // THE BUG THIS FIXES
    //
    // Play() used to live only in Awake(). That runs exactly once, because
    // this object is DontDestroyOnLoad and never reloads.
    //
    // So: enter a simulation, LoadingScreen calls Stop(), quit back to the
    // menu — MainMenuScene's own BackgroundMusic copy hits the singleton
    // guard above and destroys itself, leaving the original, still stopped.
    // Nothing ever called Play() again, so the menu sat in silence.
    //
    // Deciding here, on every scene load, means the music state is derived
    // from where the player actually is rather than from scattered Play()
    // and Stop() calls that have to stay in sync.
    // -------------------------------------------------------
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-routed on every load for the same reason AudioManager is: the
        // Inspector's Output field only applies to whichever instance loaded
        // first, and SettingsManager may not have existed at that point.
        RouteToMixer();

        if (IsSilentScene(scene.name))
            Stop();
        else
            Play();
    }

    private void RouteToMixer()
    {
        if (source == null) return;

        // Null when no SettingsManager exists yet, which leaves the source
        // unrouted — the next scene load tries again.
        AudioMixerGroup music = SettingsManager.MusicGroup;
        if (music != null) source.outputAudioMixerGroup = music;
    }

    private bool IsSilentScene(string sceneName)
    {
        if (silentScenes == null) return false;

        for (int i = 0; i < silentScenes.Length; i++)
        {
            if (silentScenes[i] == sceneName) return true;
        }
        return false;
    }

    /// <summary>
    /// Starts the music, or does nothing if it is already running.
    ///
    /// The isPlaying guard matters: without it, navigating from the main menu
    /// to results and back would restart the track from the beginning each
    /// time, which is very audible.
    /// </summary>
    public void Play()
    {
        if (source == null || source.clip == null) return;
        if (source.isPlaying) return;

        source.volume = volume;
        source.Play();
    }

    public void SetVolume(float v)
    {
        volume = Mathf.Clamp01(v);
        if (source != null) source.volume = volume;
    }

    public void Stop()
    {
        if (source != null) source.Stop();
    }
}
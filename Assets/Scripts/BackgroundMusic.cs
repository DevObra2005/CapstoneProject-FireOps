using UnityEngine;

// Persistent looping background music for the FRONT-END scenes
// (Main Menu + Scenario Selection). Uses DontDestroyOnLoad + a singleton
// guard so it plays continuously across those scenes without restarting
// or duplicating. Stopped by LoadingScreen.Show() when entering an environment.
[RequireComponent(typeof(AudioSource))]
public class BackgroundMusic : MonoBehaviour
{
    public static BackgroundMusic Instance { get; private set; }

    [Tooltip("Optional: assign here, or set the clip directly on the AudioSource.")]
    public AudioClip musicClip;

    [Range(0f, 1f)]
    public float volume = 0.4f;

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

        if (source.clip != null)
            source.Play();
        else
            Debug.LogWarning("[BackgroundMusic] No AudioClip assigned!");
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
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Plays voice-over audio for dialogue lines and briefings.
///
/// Uses ONE AudioSource on purpose: starting a new line automatically
/// stops the previous one, so two officer voices can never overlap.
///
/// Lives on the same GameObject as SettingsManager (DontDestroyOnLoad),
/// so it survives scene changes and is reachable from any scene via
/// VoiceOverManager.Instance.
/// </summary>
public class VoiceOverManager : MonoBehaviour
{
    public static VoiceOverManager Instance { get; private set; }

    // PlayerPrefs key for the on/off toggle (same pattern as LoginManager)
    public const string KEY_VOICE_ENABLED = "voice_enabled";

    [Header("Audio Mixer")]
    [Tooltip("Drag the SFX group from MainMixer here. " +
             "Swap to a dedicated Voice group later if you add one.")]
    [SerializeField] private AudioMixerGroup voiceGroup;

    private AudioSource source;

    private void Awake()
    {
        // Standard singleton guard: if one already exists, this copy is a
        // duplicate from a reloaded scene and destroys itself.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Build the AudioSource in code so it can never be misconfigured
        // in the Inspector.
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;               // 2D - voice is not positional
        source.outputAudioMixerGroup = voiceGroup;
    }

    /// <summary>True if voice-over is switched on (default: on).</summary>
    public bool IsEnabled => PlayerPrefs.GetInt(KEY_VOICE_ENABLED, 1) == 1;

    /// <summary>True while a line is still speaking.</summary>
    public bool IsPlaying => source != null && source.isPlaying;

    /// <summary>
    /// Plays a voice line. Returns the clip length in seconds so the caller
    /// can pace a typewriter effect to match. Returns 0 if nothing will play
    /// (no clip assigned, or voice-over turned off).
    /// </summary>
    public float Play(AudioClip clip)
    {
        if (source == null) return 0f;

        source.Stop();                          // cut off the previous line

        if (clip == null || !IsEnabled) return 0f;

        source.clip = clip;
        source.Play();
        return clip.length;
    }

    /// <summary>Stops the current line immediately (panel closed, skipped, etc.).</summary>
    public void Stop()
    {
        if (source != null) source.Stop();
    }

    /// <summary>Call from a Settings toggle if you add one later.</summary>
    public void SetEnabled(bool enabled)
    {
        PlayerPrefs.SetInt(KEY_VOICE_ENABLED, enabled ? 1 : 0);
        PlayerPrefs.Save();
        if (!enabled) Stop();
    }
}
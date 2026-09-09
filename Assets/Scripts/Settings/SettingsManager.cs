using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Single source of truth for player settings (music, SFX, look sensitivity).
///
/// LAYER RESPONSIBILITIES:
///   - Storage : PlayerPrefs (survives app restarts)
///   - Apply   : pushes audio values into the AudioMixer
///   - UI      : NOT here. Sliders call the Set* methods below.
///
/// Also hands out the mixer groups, so scripts that create their own
/// AudioSource at runtime can route into the mixer instead of bypassing it.
///
/// Place this on a GameObject in your FIRST scene (Main Menu).
/// It survives scene loads via DontDestroyOnLoad.
/// </summary>
public class SettingsManager : MonoBehaviour
{
    public static SettingsManager Instance { get; private set; }

    [Header("Audio Mixer")]
    [Tooltip("Drag MainMixer here. Safe to leave empty - script will simply skip audio.")]
    [SerializeField] private AudioMixer audioMixer;

    [Tooltip("Must match the exposed parameter name in the mixer EXACTLY.")]
    [SerializeField] private string musicParameter = "MusicVolume";

    [Tooltip("Must match the exposed parameter name in the mixer EXACTLY.")]
    [SerializeField] private string sfxParameter = "SFXVolume";

    // -------------------------------------------------------
    // MIXER GROUPS FOR RUNTIME-CREATED SOURCES.
    //
    // An AudioSource added with AddComponent has outputAudioMixerGroup set
    // to null, and null means "go straight to the AudioListener". The mixer
    // never sees it, so no slider can ever affect it.
    //
    // AudioManager, FootstepController and ExtinguisherSprayVFX all build
    // their sources in code, so they read these two references instead of
    // each carrying an Inspector field that would need assigning in every
    // scene and on every prefab.
    // -------------------------------------------------------
    [Header("Mixer Groups")]
    [Tooltip("Drag the Music group out of MainMixer.")]
    [SerializeField] private AudioMixerGroup musicGroup;

    [Tooltip("Drag the SFX group out of MainMixer.")]
    [SerializeField] private AudioMixerGroup sfxGroup;

    /// <summary>Music group, or null if no SettingsManager exists.</summary>
    public static AudioMixerGroup MusicGroup =>
        Instance != null ? Instance.musicGroup : null;

    /// <summary>SFX group, or null if no SettingsManager exists.</summary>
    public static AudioMixerGroup SfxGroup =>
        Instance != null ? Instance.sfxGroup : null;

    // ---------------------------------------------------------
    //  PlayerPrefs keys  (like localStorage keys - never change
    //  these after release or saved settings are orphaned)
    // ---------------------------------------------------------
    private const string KEY_MUSIC = "Settings_MusicVolume";
    private const string KEY_SFX = "Settings_SfxVolume";
    private const string KEY_SENSITIVITY = "Settings_LookSensitivity";

    // ---------------------------------------------------------
    //  Defaults - used on first launch, and by Reset
    // ---------------------------------------------------------
    private const float DEFAULT_MUSIC = 0.75f;
    private const float DEFAULT_SFX = 0.75f;
    private const float DEFAULT_SENSITIVITY = 1.0f;

    // ---------------------------------------------------------
    //  Sensitivity slider bounds. 1.0 = your current feel.
    // ---------------------------------------------------------
    public const float MIN_SENSITIVITY = 0.25f;
    public const float MAX_SENSITIVITY = 3.0f;

    // ---------------------------------------------------------
    //  Current values. Read-only from outside - the ONLY way to
    //  change them is through the Set* methods, so nothing can
    //  drift out of sync with PlayerPrefs.
    // ---------------------------------------------------------
    public float MusicVolume { get; private set; }
    public float SfxVolume { get; private set; }
    public float LookSensitivity { get; private set; }

    /// <summary>
    /// Safe static accessor for the camera controller.
    /// Falls back to the default if no SettingsManager exists, so opening
    /// Kitchen/Classroom directly in the Editor never throws.
    /// Usage: float sens = SettingsManager.Sensitivity;
    /// </summary>
    public static float Sensitivity =>
        Instance != null ? Instance.LookSensitivity : DEFAULT_SENSITIVITY;

    // =========================================================
    //  LIFECYCLE
    // =========================================================

    private void Awake()
    {
        // Standard singleton guard - if one already exists, this
        // duplicate destroys itself instead of fighting over Instance.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        Load();
    }

    private void Start()
    {
        // Deliberately in Start(), not Awake().
        // Unity loads AudioMixer snapshots after Awake, which can silently
        // overwrite values set too early. Start() runs after that.
        ApplyAudioToMixer();
    }

    private void OnApplicationPause(bool paused)
    {
        // Android backgrounding - flush to disk so nothing is lost
        // if the OS kills the app while it is in the background.
        if (paused) PlayerPrefs.Save();
    }

    private void OnApplicationQuit()
    {
        PlayerPrefs.Save();
    }

    // =========================================================
    //  LOAD / SAVE
    // =========================================================

    private void Load()
    {
        // GetFloat's 2nd argument is the fallback used when the key
        // does not exist yet - i.e. the very first launch.
        MusicVolume = PlayerPrefs.GetFloat(KEY_MUSIC, DEFAULT_MUSIC);
        SfxVolume = PlayerPrefs.GetFloat(KEY_SFX, DEFAULT_SFX);
        LookSensitivity = PlayerPrefs.GetFloat(KEY_SENSITIVITY, DEFAULT_SENSITIVITY);

        Debug.Log($"[Settings] Loaded -> Music={MusicVolume:F2}  SFX={SfxVolume:F2}  Sensitivity={LookSensitivity:F2}");
    }

    /// <summary>
    /// Flushes PlayerPrefs from memory to disk.
    /// Call this when the settings panel closes. SetFloat alone only
    /// writes to an in-memory cache.
    /// </summary>
    public void SaveToDisk()
    {
        PlayerPrefs.Save();
        Debug.Log("[Settings] Saved to disk.");
    }

    // =========================================================
    //  SETTERS - these are what the UI sliders call
    // =========================================================

    public void SetMusicVolume(float value)
    {
        MusicVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_MUSIC, MusicVolume);
        ApplyMusicToMixer();
    }

    public void SetSfxVolume(float value)
    {
        SfxVolume = Mathf.Clamp01(value);
        PlayerPrefs.SetFloat(KEY_SFX, SfxVolume);
        ApplySfxToMixer();
    }

    public void SetLookSensitivity(float value)
    {
        LookSensitivity = Mathf.Clamp(value, MIN_SENSITIVITY, MAX_SENSITIVITY);
        PlayerPrefs.SetFloat(KEY_SENSITIVITY, LookSensitivity);
        // No "apply" needed - the camera controller reads
        // SettingsManager.Sensitivity directly each frame.
    }

    public void ResetToDefaults()
    {
        SetMusicVolume(DEFAULT_MUSIC);
        SetSfxVolume(DEFAULT_SFX);
        SetLookSensitivity(DEFAULT_SENSITIVITY);
        SaveToDisk();
        Debug.Log("[Settings] Reset to defaults.");
    }

    // =========================================================
    //  APPLY TO MIXER
    // =========================================================

    private void ApplyAudioToMixer()
    {
        ApplyMusicToMixer();
        ApplySfxToMixer();
    }

    private void ApplyMusicToMixer()
    {
        if (audioMixer == null) return;   // null-guard: no mixer assigned yet
        audioMixer.SetFloat(musicParameter, LinearToDecibel(MusicVolume));
    }

    private void ApplySfxToMixer()
    {
        if (audioMixer == null) return;   // null-guard: no mixer assigned yet
        audioMixer.SetFloat(sfxParameter, LinearToDecibel(SfxVolume));
    }

    /// <summary>
    /// Sliders are linear (0..1). AudioMixer volume is decibels (-80..0)
    /// and logarithmic. Human hearing is logarithmic too, so without this
    /// conversion a slider at 50% would sound almost silent.
    /// </summary>
    private static float LinearToDecibel(float linear)
    {
        if (linear <= 0.0001f) return -80f;   // -80 dB is the mixer's "off"
        return Mathf.Log10(linear) * 20f;
    }
}
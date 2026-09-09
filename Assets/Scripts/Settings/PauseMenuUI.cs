using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Pause menu for Office / Kitchen / Classroom.
/// Contains: Resume, Music, SFX, Look sensitivity, Quit to menu.
///
/// Quitting DISCARDS the run. Nothing is posted to Laravel until the
/// Win/Lose screen, so walking away leaves no game_sessions row behind
/// and does not consume an attempt_number.
///
/// Put this on the PauseCanvas in each environment scene.
/// </summary>
public class PauseMenuUI : MonoBehaviour
{
    /// <summary>
    /// True while the pause panel is open.
    ///
    /// IMPORTANT: Update() keeps running at Time.timeScale = 0. Any script
    /// that reads taps or rotates the camera must early-out on this, or the
    /// player will interact with hazards through the pause panel:
    ///
    ///     if (PauseMenuUI.IsPaused) return;
    /// </summary>
    public static bool IsPaused { get; private set; }

    [Header("Panels")]
    [SerializeField] private GameObject pausePanel;

    [Tooltip("Optional. Leave empty to quit immediately with no confirmation.")]
    [SerializeField] private GameObject confirmQuitPanel;

    [Header("Sliders")]
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider sensitivitySlider;

    [Header("Value labels (all optional)")]
    [SerializeField] private TMP_Text musicValueLabel;
    [SerializeField] private TMP_Text sfxValueLabel;
    [SerializeField] private TMP_Text sensitivityValueLabel;

    [Header("Quit")]
    [SerializeField] private string mainMenuSceneName = "MainMenuScene";

    [Tooltip("Shown on the loading screen while returning to the menu.")]
    [SerializeField] private string quitTagline = "RETURNING TO MENU";

    // Fallbacks used only if no SettingsManager exists in the scene,
    // which happens when you open an environment scene directly in the Editor.
    private const float FALLBACK_VOLUME = 0.75f;
    private const float FALLBACK_SENSITIVITY = 1.0f;

    // =========================================================
    //  LIFECYCLE
    // =========================================================

    private void Awake()
    {
        // A freshly loaded scene must always start unpaused, whatever
        // state the previous scene left this static flag in.
        IsPaused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (pausePanel != null) pausePanel.SetActive(false);
        if (confirmQuitPanel != null) confirmQuitPanel.SetActive(false);
    }

    private void Start()
    {
        SetupSliders();
    }

    private void OnDestroy()
    {
        // Safety net. If this scene unloads while still paused, the next
        // scene would load with timeScale stuck at 0 and appear frozen.
        Time.timeScale = 1f;
        AudioListener.pause = false;
        IsPaused = false;

        if (musicSlider != null) musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
        if (sensitivitySlider != null) sensitivitySlider.onValueChanged.RemoveListener(OnSensitivityChanged);
    }

    // =========================================================
    //  PAUSE / RESUME
    // =========================================================

    /// <summary>Wire your in-game Pause button's OnClick to this.</summary>
    public void Pause()
    {
        if (IsPaused) return;
        IsPaused = true;

        Time.timeScale = 0f;          // freezes physics, animation, Time.deltaTime
        AudioListener.pause = true;   // freezes ALL audio except sources with
                                      // ignoreListenerPause, which AudioManager
                                      // sets on its SFX source so UI clicks
                                      // stay audible in here

        if (pausePanel != null) pausePanel.SetActive(true);
        if (confirmQuitPanel != null) confirmQuitPanel.SetActive(false);

        // The main menu may have changed these since the run started.
        RefreshSlidersFromSettings();
    }

    /// <summary>Wire the Resume button's OnClick to this.</summary>
    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;

        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (pausePanel != null) pausePanel.SetActive(false);
        if (confirmQuitPanel != null) confirmQuitPanel.SetActive(false);

        // Slider changes only wrote to the in-memory cache. Flush to disk now.
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SaveToDisk();
    }

    // =========================================================
    //  QUIT  (run is discarded)
    // =========================================================

    /// <summary>Wire the "Quit to menu" button's OnClick to this.</summary>
    public void OnQuitButtonClick()
    {
        if (confirmQuitPanel != null)
        {
            confirmQuitPanel.SetActive(true);
            return;
        }
        ConfirmQuit();   // no confirmation panel assigned
    }

    /// <summary>Wire the confirmation panel's "Cancel" button to this.</summary>
    public void CancelQuit()
    {
        if (confirmQuitPanel != null) confirmQuitPanel.SetActive(false);
    }

    /// <summary>Wire the confirmation panel's "Quit" button to this.</summary>
    public void ConfirmQuit()
    {
        // DISCARD. There is deliberately no API call here. SimulationManager
        // only posts on the Win/Lose screen, so an abandoned run is invisible
        // to Laravel: no row, no attempt consumed, no fail_reason.

        IsPaused = false;

        // ---- ORDER MATTERS BELOW THIS LINE ----
        //
        // Both of these are GLOBAL and survive scene loads. Leaving either
        // set would carry the frozen, silent state into the main menu with
        // no error to follow - the menu would simply sit there mute and
        // motionless.
        //
        // They also have to be released BEFORE the music fade starts. A fade
        // into a paused AudioListener is inaudible, so the player would hear
        // a hard cut instead of a fade.
        Time.timeScale = 1f;
        AudioListener.pause = false;

        // Stop the simulation music and room tone. AudioManager fades with
        // unscaled time, so the fade runs underneath the loading bar rather
        // than cutting the moment the scene swaps.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.StopMusic();
            AudioManager.Instance.StopAmbient();
        }

        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SaveToDisk();

        LoadMainMenu();
    }

    /// <summary>
    /// Same pattern as SceneLoader.Go - use the persistent loading screen
    /// when one exists, otherwise load directly. The fallback matters when
    /// testing an environment scene in isolation, where LoadingScreen was
    /// never created because MainMenuScene never ran.
    /// </summary>
    private void LoadMainMenu()
    {
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(mainMenuSceneName, quitTagline);
        else
            SceneManager.LoadScene(mainMenuSceneName);
    }

    // =========================================================
    //  SLIDERS
    // =========================================================

    private void SetupSliders()
    {
        if (musicSlider != null)
        {
            musicSlider.minValue = 0f;
            musicSlider.maxValue = 1f;
            musicSlider.onValueChanged.AddListener(OnMusicChanged);
        }

        if (sfxSlider != null)
        {
            sfxSlider.minValue = 0f;
            sfxSlider.maxValue = 1f;
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        }

        if (sensitivitySlider != null)
        {
            // Read the bounds from SettingsManager so the slider and the
            // clamp inside SetLookSensitivity can never disagree.
            sensitivitySlider.minValue = SettingsManager.MIN_SENSITIVITY;
            sensitivitySlider.maxValue = SettingsManager.MAX_SENSITIVITY;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        }

        RefreshSlidersFromSettings();
    }

    /// <summary>
    /// Pushes the stored values into the slider handles.
    ///
    /// Uses SetValueWithoutNotify because assigning .value directly fires
    /// onValueChanged, which would immediately write the value back to
    /// storage - harmless here, but a real bug the moment you add anything
    /// that reacts to a change (a preview sound, a dirty flag, an API call).
    /// </summary>
    private void RefreshSlidersFromSettings()
    {
        SettingsManager s = SettingsManager.Instance;

        float music = s != null ? s.MusicVolume : FALLBACK_VOLUME;
        float sfx = s != null ? s.SfxVolume : FALLBACK_VOLUME;
        float sens = s != null ? s.LookSensitivity : FALLBACK_SENSITIVITY;

        if (musicSlider != null) musicSlider.SetValueWithoutNotify(music);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(sfx);
        if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(sens);

        UpdatePercentLabel(musicValueLabel, music);
        UpdatePercentLabel(sfxValueLabel, sfx);
        UpdateMultiplierLabel(sensitivityValueLabel, sens);
    }

    private void OnMusicChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SetMusicVolume(value);

        UpdatePercentLabel(musicValueLabel, value);
    }

    private void OnSfxChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SetSfxVolume(value);

        UpdatePercentLabel(sfxValueLabel, value);
    }

    private void OnSensitivityChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SetLookSensitivity(value);

        UpdateMultiplierLabel(sensitivityValueLabel, value);
    }

    // =========================================================
    //  LABELS
    // =========================================================

    private static void UpdatePercentLabel(TMP_Text label, float value01)
    {
        if (label == null) return;
        label.text = Mathf.RoundToInt(value01 * 100f) + "%";
    }

    private static void UpdateMultiplierLabel(TMP_Text label, float multiplier)
    {
        if (label == null) return;
        label.text = multiplier.ToString("0.00") + "x";
    }
}
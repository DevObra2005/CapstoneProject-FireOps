using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Main menu settings screen. Four tabs: Account, Event, Audio & controls, About.
///
/// Every Inspector field is null-guarded, so you can wire this up one tab at a
/// time and test as you go. Unassigned fields are skipped, not crashed on.
///
/// Put this on SettingsUI in MainMenuScene. That object must stay ACTIVE —
/// it hides its child SettingsRoot, not itself. A disabled GameObject cannot
/// receive a Button OnClick, so putting this on the hidden object would make
/// the Settings button dead.
/// </summary>
public class MainMenuSettingsUI : MonoBehaviour
{
    [System.Serializable]
    public class SettingsTab
    {
        [Tooltip("Label only. Helps you tell the entries apart in the Inspector.")]
        public string name;
        public Button button;
        public GameObject pane;
    }

    // =========================================================
    //  INSPECTOR
    // =========================================================

    [Header("Root")]
    [Tooltip("The whole settings screen. Gets hidden on Awake.")]
    [SerializeField] private GameObject settingsRoot;

    [Tooltip("The main menu panel. Hidden while settings is open, restored on close.")]
    [SerializeField] private GameObject mainMenuPanel;

    [Header("Tabs (array order = display order)")]
    [SerializeField] private SettingsTab[] tabs;
    [SerializeField] private Color activeTabColor = new Color(0.753f, 0.224f, 0.169f); // #c0392b
    [Tooltip("White (alpha 255) leaves the button sprite untinted. Transparent erases it.")]
    [SerializeField] private Color inactiveTabColor = Color.white;
    [SerializeField] private Color activeTabTextColor = Color.white;
    [SerializeField] private Color inactiveTabTextColor = new Color(0.541f, 0.584f, 0.639f);

    [Header("Audio & controls tab")]
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider sensitivitySlider;
    [SerializeField] private TMP_Text musicValueLabel;
    [SerializeField] private TMP_Text sfxValueLabel;
    [SerializeField] private TMP_Text sensitivityValueLabel;
    [SerializeField] private Button resetDefaultsButton;

    // -------------------------------------------------------
    // EVENT TAB — "training pass" layout.
    //
    // The red date chip reuses the same day/month structure as the cards
    // in EventSelectionScene, so the card a participant tapped and the
    // pane they see afterwards are visibly the same object.
    // -------------------------------------------------------
    [Header("Event tab")]
    [Tooltip("Big number in the red chip, e.g. 14")]
    [SerializeField] private TMP_Text eventDayText;
    [Tooltip("Short month in the red chip, e.g. AUG")]
    [SerializeField] private TMP_Text eventMonthText;
    [SerializeField] private TMP_Text eventNameText;

    [Tooltip("Root of the pass card. Hidden when no event is selected.")]
    [SerializeField] private GameObject eventPassCard;
    [Tooltip("Message shown when no event has been selected yet.")]
    [SerializeField] private GameObject noEventSelectedText;
    [SerializeField] private Button switchEventButton;
    [SerializeField] private string eventSelectionSceneName = "EventSelectionScene";

    [Header("Account tab")]
    [SerializeField] private TMP_Text accountNameText;
    [SerializeField] private TMP_Text accountEmailText;
    [SerializeField] private Button signOutButton;
    [Tooltip("Optional. Leave empty to sign out with no confirmation.")]
    [SerializeField] private GameObject confirmSignOutPanel;
    [SerializeField] private string loginSceneName = "LoginScene";

    [Header("About tab")]
    [Tooltip("Auto-filled from Player Settings > Version.")]
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private string versionPrefix = "Version ";

    // -------------------------------------------------------
    // No PlayerPrefs key fields here on purpose.
    //
    // Auth keys come from LoginManager.KEY_*, event keys from
    // EventSelectionManager.KEY_EVENT_*. The screen that writes a key and
    // the screen that reads it can never disagree.
    //
    // The old Inspector defaults said "token" while LoginManager actually
    // wrote "participant_token" — sign out deleted a key that did not
    // exist, left the real token behind, and auto-login pulled the user
    // straight back in. No error, no warning, just a loop.
    // -------------------------------------------------------

    // Shown when a PlayerPrefs value is missing, so a wrong key name is
    // visible on screen instead of failing silently as an empty label.
    private const string MISSING = "—";

    private const float FALLBACK_VOLUME = 0.75f;
    private const float FALLBACK_SENSITIVITY = 1.0f;

    // =========================================================
    //  LIFECYCLE
    // =========================================================

    private void Awake()
    {
        if (settingsRoot != null) settingsRoot.SetActive(false);
        if (confirmSignOutPanel != null) confirmSignOutPanel.SetActive(false);
    }

    private void Start()
    {
        WireTabs();
        WireSliders();
        WireButtons();
        FillAboutTab();
    }

    private void OnDestroy()
    {
        // Listeners added in code must be removed in code, or they keep a
        // reference to this object after the scene unloads.
        if (musicSlider != null) musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
        if (sfxSlider != null) sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
        if (sensitivitySlider != null) sensitivitySlider.onValueChanged.RemoveListener(OnSensitivityChanged);
    }

    // =========================================================
    //  OPEN / CLOSE
    // =========================================================

    /// <summary>Wire the main menu's Settings button OnClick to this.</summary>
    public void Open()
    {
        if (settingsRoot != null) settingsRoot.SetActive(true);

        // Hide rather than just cover the menu. A disabled object is not
        // drawn and cannot be tapped, so nothing bleeds through the card
        // and "Start Simulation" cannot be hit by accident.
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        if (confirmSignOutPanel != null) confirmSignOutPanel.SetActive(false);

        // Re-read everything. The participant may have switched events or
        // changed volumes in a pause menu since this screen was last open.
        RefreshSliders();
        RefreshEventTab();
        RefreshAccountTab();

        ShowTab(0);
    }

    /// <summary>Wire the Close button OnClick to this.</summary>
    public void Close()
    {
        if (settingsRoot != null) settingsRoot.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (confirmSignOutPanel != null) confirmSignOutPanel.SetActive(false);

        // Slider changes only wrote to the in-memory cache. Flush to disk.
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SaveToDisk();
    }

    // =========================================================
    //  TABS
    // =========================================================

    private void WireTabs()
    {
        if (tabs == null) return;

        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == null || tabs[i].button == null) continue;

            // Copied into a local so the closure below captures the VALUE,
            // not the loop variable itself. Without this, every button
            // would open the LAST tab.
            int index = i;
            tabs[i].button.onClick.AddListener(() => ShowTab(index));
        }
    }

    /// <summary>Shows one pane and hides the rest. Also repaints the tab buttons.</summary>
    public void ShowTab(int index)
    {
        if (tabs == null || tabs.Length == 0) return;
        index = Mathf.Clamp(index, 0, tabs.Length - 1);

        for (int i = 0; i < tabs.Length; i++)
        {
            if (tabs[i] == null) continue;
            bool isActive = (i == index);

            if (tabs[i].pane != null)
                tabs[i].pane.SetActive(isActive);

            if (tabs[i].button == null) continue;

            Image bg = tabs[i].button.GetComponent<Image>();
            if (bg != null)
                bg.color = isActive ? activeTabColor : inactiveTabColor;

            TMP_Text label = tabs[i].button.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.color = isActive ? activeTabTextColor : inactiveTabTextColor;
        }
    }

    // =========================================================
    //  AUDIO & CONTROLS TAB
    // =========================================================

    private void WireSliders()
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
            // Bounds come from SettingsManager so the slider range and the
            // clamp inside SetLookSensitivity can never drift apart.
            sensitivitySlider.minValue = SettingsManager.MIN_SENSITIVITY;
            sensitivitySlider.maxValue = SettingsManager.MAX_SENSITIVITY;
            sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        }

        RefreshSliders();
    }

    /// <summary>
    /// Pushes stored values into the handles using SetValueWithoutNotify.
    /// Assigning .value directly would fire onValueChanged and write the
    /// value straight back to storage.
    /// </summary>
    private void RefreshSliders()
    {
        SettingsManager s = SettingsManager.Instance;

        float music = s != null ? s.MusicVolume : FALLBACK_VOLUME;
        float sfx = s != null ? s.SfxVolume : FALLBACK_VOLUME;
        float sens = s != null ? s.LookSensitivity : FALLBACK_SENSITIVITY;

        if (musicSlider != null) musicSlider.SetValueWithoutNotify(music);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(sfx);
        if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(sens);

        SetPercentLabel(musicValueLabel, music);
        SetPercentLabel(sfxValueLabel, sfx);
        SetMultiplierLabel(sensitivityValueLabel, sens);
    }

    private void OnMusicChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SetMusicVolume(value);
        SetPercentLabel(musicValueLabel, value);
    }

    private void OnSfxChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SetSfxVolume(value);
        SetPercentLabel(sfxValueLabel, value);
    }

    private void OnSensitivityChanged(float value)
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SetLookSensitivity(value);
        SetMultiplierLabel(sensitivityValueLabel, value);
    }

    /// <summary>Wire the Reset to defaults button OnClick to this.</summary>
    public void OnResetDefaultsClick()
    {
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.ResetToDefaults();

        RefreshSliders();   // pull the new values back into the handles
    }

    // =========================================================
    //  EVENT TAB
    // =========================================================

    private void RefreshEventTab()
    {
        string name = PlayerPrefs.GetString(EventSelectionManager.KEY_EVENT_NAME, string.Empty);
        string day = PlayerPrefs.GetString(EventSelectionManager.KEY_EVENT_DAY, string.Empty);
        string month = PlayerPrefs.GetString(EventSelectionManager.KEY_EVENT_MONTH, string.Empty);

        // No event picked yet. Show the empty state instead of a pass card
        // full of dashes — which reads as broken rather than as "not set".
        bool hasEvent = !string.IsNullOrEmpty(name);

        if (eventPassCard != null) eventPassCard.SetActive(hasEvent);
        if (noEventSelectedText != null) noEventSelectedText.SetActive(!hasEvent);

        if (!hasEvent) return;

        if (eventNameText != null) eventNameText.text = name;

        // Each chip part hides itself when empty, so a date the API sent in
        // an unexpected format degrades to a blank chip rather than showing
        // raw text like "2026-08-14T00:00:00" in a box sized for two chars.
        SetOrHide(eventDayText, day);
        SetOrHide(eventMonthText, month);
    }

    /// <summary>Wire the Switch event button OnClick to this.</summary>
    public void OnSwitchEventClick()
    {
        // Nothing to clear. EventSelectionScene overwrites the selected
        // event keys when the participant picks a new one, and their score
        // for the old event stays in Laravel either way.
        if (SettingsManager.Instance != null)
            SettingsManager.Instance.SaveToDisk();

        SceneManager.LoadScene(eventSelectionSceneName);
    }

    // =========================================================
    //  ACCOUNT TAB
    // =========================================================

    private void RefreshAccountTab()
    {
        // Keys come from LoginManager, so these read exactly what login wrote.
        if (accountNameText != null) accountNameText.text = ReadPref(LoginManager.KEY_NAME);
        if (accountEmailText != null) accountEmailText.text = ReadPref(LoginManager.KEY_EMAIL);
    }

    /// <summary>Wire the Sign out button OnClick to this.</summary>
    public void OnSignOutClick()
    {
        if (confirmSignOutPanel != null)
        {
            confirmSignOutPanel.SetActive(true);
            return;
        }
        ConfirmSignOut();
    }

    /// <summary>Wire the confirmation panel's Cancel button to this.</summary>
    public void CancelSignOut()
    {
        if (confirmSignOutPanel != null) confirmSignOutPanel.SetActive(false);
    }

    /// <summary>Wire the confirmation panel's Sign out button to this.</summary>
    public void ConfirmSignOut()
    {
        // LoginManager owns the auth keys, so LoginManager clears them.
        // If those key names ever change, they change in one file.
        LoginManager.ClearSession();

        // Event selection belongs to this screen, not to LoginManager.
        // Cleared too, so the next participant on a shared training device
        // does not inherit the previous one's event.
        PlayerPrefs.DeleteKey(EventSelectionManager.KEY_EVENT_ID);
        PlayerPrefs.DeleteKey(EventSelectionManager.KEY_EVENT_NAME);
        PlayerPrefs.DeleteKey(EventSelectionManager.KEY_EVENT_DAY);
        PlayerPrefs.DeleteKey(EventSelectionManager.KEY_EVENT_MONTH);
        PlayerPrefs.Save();

        // Audio settings and SimulationMode deliberately survive. They
        // belong to the device, not to whoever is signed in.
        Debug.Log("[Settings] Signed out. Auth and event keys cleared.");
        SceneManager.LoadScene(loginSceneName);
    }

    // =========================================================
    //  ABOUT TAB
    // =========================================================

    private void FillAboutTab()
    {
        if (versionText == null) return;

        // Application.version reads Player Settings > Version, so this label
        // updates itself on every build with no code change.
        versionText.text = versionPrefix + Application.version;
    }

    // =========================================================
    //  BUTTON WIRING
    // =========================================================

    private void WireButtons()
    {
        // Wired in code rather than the Inspector so a forgotten drag
        // cannot silently produce a dead button.
        if (resetDefaultsButton != null)
            resetDefaultsButton.onClick.AddListener(OnResetDefaultsClick);

        if (switchEventButton != null)
            switchEventButton.onClick.AddListener(OnSwitchEventClick);

        if (signOutButton != null)
            signOutButton.onClick.AddListener(OnSignOutClick);
    }

    // =========================================================
    //  HELPERS
    // =========================================================

    /// <summary>
    /// Sets the label, or hides the whole object when the value is empty.
    /// Prevents a stray "—" sitting in a layout that was sized for real text.
    /// </summary>
    private static void SetOrHide(TMP_Text label, string value)
    {
        if (label == null) return;

        bool has = !string.IsNullOrEmpty(value);
        label.gameObject.SetActive(has);
        if (has) label.text = value;
    }

    private static string ReadPref(string key)
    {
        string value = PlayerPrefs.GetString(key, string.Empty);
        return string.IsNullOrEmpty(value) ? MISSING : value;
    }

    private static void SetPercentLabel(TMP_Text label, float value01)
    {
        if (label == null) return;
        label.text = Mathf.RoundToInt(value01 * 100f) + "%";
    }

    private static void SetMultiplierLabel(TMP_Text label, float multiplier)
    {
        if (label == null) return;
        label.text = multiplier.ToString("0.00") + "x";
    }
}
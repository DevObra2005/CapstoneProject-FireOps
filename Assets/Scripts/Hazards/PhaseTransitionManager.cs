using UnityEngine;
using UnityEngine.SceneManagement;

public class PhaseTransitionManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // Watches for all hazards being complete, then runs:
    //   1. Prompt dialogue  ("well done, now find the alarm")
    //   2. Highlights the fire alarm pull station
    //   3. Player taps it → alarm teaching dialogue
    //   4. Highlights the fire extinguisher
    //   5. Player taps it → teaching dialogue (types + TPASS intro)
    //   6. Confirm screen → loading screen → Phase 2
    // Replaces the old CompletionManager.
    // -------------------------------------------------------

    [Header("Prompt — plays when all hazards are done")]
    public DialogueLine[] promptLines;

    [Header("Fire Alarm")]
    public GameObject alarmPanel;

    [Header("Teaching — plays when alarm is tapped")]
    public DialogueLine[] alarmLines;

    [Header("Extinguisher")]
    public GameObject fireExtinguisher;

    [Header("Teaching — plays when extinguisher is tapped")]
    public DialogueLine[] extinguisherLines;

    [Header("Confirm Screen")]
    public GameObject confirmPanel;

    [Header("Scene")]
    [Tooltip("Scene to load for Phase 2 — usually the same scene reloaded")]
    public string phaseTwoScene = "Office3DScene";

    [Tooltip("Text shown on the loading screen during the transition")]
    public string loadingTagline = "PREPARING SIMULATION";

    private bool hasTriggered = false;
    private bool awaitingAlarm = false;
    private bool awaitingExtinguisher = false;

    void Start()
    {
        if (confirmPanel != null)
            confirmPanel.SetActive(false);
    }

    void Update()
    {
        if (hasTriggered) return;
        if (HazardCounterManager.Instance == null) return;
        if (!HazardCounterManager.Instance.AllHazardsFound) return;
        if (DialogueManager.Instance == null) return;
        if (DialogueManager.Instance.IsDialogueActive()) return;

        hasTriggered = true;
        StartPromptDialogue();
    }

    void StartPromptDialogue()
    {
        if (promptLines == null || promptLines.Length == 0)
        {
            HighlightAlarm();
            return;
        }

        DialogueManager.Instance.StartDialogue(
            promptLines,
            HighlightAlarm,
            true);
    }

    // -------------------------------------------------------
    // STAGE 1 — FIRE ALARM
    // -------------------------------------------------------

    void HighlightAlarm()
    {
        if (alarmPanel == null)
        {
            Debug.LogWarning("[PhaseTransition] No alarm panel assigned — skipping to extinguisher.");
            HighlightExtinguisher();
            return;
        }

        awaitingAlarm = true;

        AlarmTarget target = alarmPanel.GetComponent<AlarmTarget>();
        if (target == null)
            target = alarmPanel.AddComponent<AlarmTarget>();

        target.Setup(this);
    }

    public void OnAlarmTapped()
    {
        if (!awaitingAlarm) return;
        awaitingAlarm = false;

        if (alarmLines == null || alarmLines.Length == 0)
        {
            HighlightExtinguisher();
            return;
        }

        DialogueManager.Instance.StartDialogue(
            alarmLines,
            HighlightExtinguisher,
            true);
    }

    // -------------------------------------------------------
    // STAGE 2 — FIRE EXTINGUISHER
    // -------------------------------------------------------

    void HighlightExtinguisher()
    {
        if (fireExtinguisher == null)
        {
            Debug.LogWarning("[PhaseTransition] No fire extinguisher assigned!");
            ShowConfirmScreen();
            return;
        }

        awaitingExtinguisher = true;

        ExtinguisherTarget target =
            fireExtinguisher.GetComponent<ExtinguisherTarget>();
        if (target == null)
            target = fireExtinguisher.AddComponent<ExtinguisherTarget>();

        target.Setup(this);
    }

    public void OnExtinguisherTapped()
    {
        if (!awaitingExtinguisher) return;
        awaitingExtinguisher = false;

        if (extinguisherLines == null || extinguisherLines.Length == 0)
        {
            ShowConfirmScreen();
            return;
        }

        DialogueManager.Instance.StartDialogue(
            extinguisherLines,
            ShowConfirmScreen,
            true);
    }

    // -------------------------------------------------------
    // FINISH
    // -------------------------------------------------------

    void ShowConfirmScreen()
    {
        if (confirmPanel != null)
        {
            confirmPanel.SetActive(true);
            Debug.Log("[PhaseTransition] Confirm screen shown.");
        }
        else
        {
            LoadPhaseTwo();
        }
    }

    // Hook this to the confirm button's OnClick in the Inspector
    public void LoadPhaseTwo()
    {
        PlayerPrefs.SetInt("FireLearningCompleted", 1);
        PlayerPrefs.SetInt("SimulationMode", 1);
        PlayerPrefs.Save();

        // Tells GameModeManager this is a legitimate Phase 2 transition,
        // so it won't reset SimulationMode back to 0 on scene load.
        GameModeManager.intentionalTransition = true;

        Debug.Log("[PhaseTransition] Loading Phase 2...");

        // Hide the confirm screen so it isn't visible behind the loader
        if (confirmPanel != null)
            confirmPanel.SetActive(false);

        // Use the persistent loading screen if it exists,
        // otherwise fall back to a direct scene load.
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(phaseTwoScene, loadingTagline);
        else
            SceneManager.LoadScene(phaseTwoScene);
    }
}
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.SceneManagement;

public class PhaseTransitionManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // Watches for all hazards being complete, then runs:
    //   1. Prompt dialogue  ("well done — now let's learn the response")
    //   2. Highlights Teaching Target 1 (Office: fire alarm | Kitchen: wet blanket)
    //   3. Player taps it → target 1 teaching dialogue
    //   4. Highlights Teaching Target 2 (Office: extinguisher | Kitchen: LPG valve)
    //   5. Player taps it → target 2 teaching dialogue
    //   6. Confirm screen → loading screen → Phase 2
    //
    // The two teaching targets are GENERIC. Assign whatever objects the
    // environment needs and write matching dialogue — the script just
    // highlights each target and plays its lines, in order.
    //
    // NOTE: [FormerlySerializedAs] preserves the OLD field assignments
    // (alarmPanel, alarmLines, fireExtinguisher, extinguisherLines) so the
    // existing Office scene keeps its references after this rename — no
    // re-wiring needed.
    //
    // The tap callbacks are named OnTarget1Tapped / OnTarget2Tapped.
    // AlarmTarget.cs and ExtinguisherTarget.cs call these — keep all three
    // files in sync if you rename again.
    // -------------------------------------------------------

    [Header("Prompt — plays when all hazards are done")]
    public DialogueLine[] promptLines;

    [Header("Teaching Target 1")]
    [Tooltip("Office: fire alarm | Kitchen: wet blanket")]
    [FormerlySerializedAs("alarmPanel")]
    public GameObject teachTarget1;

    [Header("Target 1 Teaching Lines")]
    [Tooltip("Plays when Teaching Target 1 is tapped")]
    [FormerlySerializedAs("alarmLines")]
    public DialogueLine[] teach1Lines;

    [Header("Teaching Target 2")]
    [Tooltip("Office: fire extinguisher | Kitchen: LPG valve")]
    [FormerlySerializedAs("fireExtinguisher")]
    public GameObject teachTarget2;

    [Header("Target 2 Teaching Lines")]
    [Tooltip("Plays when Teaching Target 2 is tapped")]
    [FormerlySerializedAs("extinguisherLines")]
    public DialogueLine[] teach2Lines;

    [Header("Confirm Screen")]
    public GameObject confirmPanel;

    [Header("Scene")]
    [Tooltip("Phase 2 always reloads the CURRENT scene. This field is kept " +
             "for reference only and is no longer used to pick the scene.")]
    public string phaseTwoScene = "Office3DScene";

    [Tooltip("Text shown on the loading screen during the transition")]
    public string loadingTagline = "PREPARING SIMULATION";

    private bool hasTriggered = false;
    private bool awaitingTarget1 = false;
    private bool awaitingTarget2 = false;

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
            HighlightTarget1();
            return;
        }

        DialogueManager.Instance.StartDialogue(
            promptLines,
            HighlightTarget1,
            true);
    }

    // -------------------------------------------------------
    // STAGE 1 — TEACHING TARGET 1 (alarm / wet blanket)
    // -------------------------------------------------------

    void HighlightTarget1()
    {
        if (teachTarget1 == null)
        {
            Debug.LogWarning("[PhaseTransition] No Teaching Target 1 assigned — skipping to Target 2.");
            HighlightTarget2();
            return;
        }

        awaitingTarget1 = true;

        AlarmTarget target = teachTarget1.GetComponent<AlarmTarget>();
        if (target == null)
            target = teachTarget1.AddComponent<AlarmTarget>();

        target.Setup(this);
    }

    // Called by AlarmTarget.OnClicked()
    public void OnTarget1Tapped()
    {
        if (!awaitingTarget1) return;
        awaitingTarget1 = false;

        if (teach1Lines == null || teach1Lines.Length == 0)
        {
            HighlightTarget2();
            return;
        }

        DialogueManager.Instance.StartDialogue(
            teach1Lines,
            HighlightTarget2,
            true);
    }

    // -------------------------------------------------------
    // STAGE 2 — TEACHING TARGET 2 (extinguisher / LPG valve)
    // -------------------------------------------------------

    void HighlightTarget2()
    {
        if (teachTarget2 == null)
        {
            Debug.LogWarning("[PhaseTransition] No Teaching Target 2 assigned!");
            ShowConfirmScreen();
            return;
        }

        awaitingTarget2 = true;

        ExtinguisherTarget target =
            teachTarget2.GetComponent<ExtinguisherTarget>();
        if (target == null)
            target = teachTarget2.AddComponent<ExtinguisherTarget>();

        target.Setup(this);
    }

    // Called by ExtinguisherTarget.OnClicked()
    public void OnTarget2Tapped()
    {
        if (!awaitingTarget2) return;
        awaitingTarget2 = false;

        if (teach2Lines == null || teach2Lines.Length == 0)
        {
            ShowConfirmScreen();
            return;
        }

        DialogueManager.Instance.StartDialogue(
            teach2Lines,
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

        // Phase 2 is ALWAYS the same scene reloaded. Use the active scene
        // name so this works in Office, Kitchen, and Classroom without any
        // per-scene hardcoding. (Fixes the bug where Kitchen loaded Office.)
        string sceneToLoad = SceneManager.GetActiveScene().name;

        // Use the persistent loading screen if it exists,
        // otherwise fall back to a direct scene load.
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(sceneToLoad, loadingTagline);
        else
            SceneManager.LoadScene(sceneToLoad);
    }
}
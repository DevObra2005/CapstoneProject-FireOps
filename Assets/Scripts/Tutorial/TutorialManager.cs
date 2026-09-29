using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// -------------------------------------------------------
// ONE STEP OF GUIDANCE — filled in per scene, in the Inspector.
// -------------------------------------------------------
[System.Serializable]
public class TutorialStepGuide
{
    [Tooltip("Editor label only, e.g. '1 Sound Alarm'. Makes the list readable.")]
    public string label;

    [Tooltip("SimulationManager step number this guide belongs to.\n" +
             "Office/Classroom: 1 Alarm, 2 Grab, 3 Twist, 4 Pull, 5 Aim, " +
             "6 Squeeze, 7 Sweep, 8 Evacuate.")]
    public int step = 1;

    [Tooltip("Banner title, e.g. 'Sound the fire alarm'.")]
    public string title;

    [TextArea]
    [Tooltip("Banner hint, e.g. 'Follow the arrows to the red alarm panel, then tap it.'")]
    public string hint;

    [Tooltip("Voice clip for this instruction (en-PH-JamesNeural). Optional.")]
    public AudioClip voiceClip;

    [Tooltip("Voice clip played on a WRONG action during this step. Optional. " +
             "The amber tip text is shown by SimulationManager either way.")]
    public AudioClip wrongVoiceClip;

    [Tooltip("Where the floor arrows lead on this step. " +
             "Leave EMPTY for no floor arrows on this step.")]
    public Transform target;

    [Tooltip("Optional corner points the floor arrows pass through on the way " +
             "to the target, e.g. around a desk. In walking order.")]
    public Transform[] waypoints;
}

// -------------------------------------------------------
// WHAT THIS DOES:
// The brain of the PRACTICE run. Does nothing at all in a real run.
//
// In practice (PracticeRun.IsActive):
//   1. Hides objects that do not belong in practice (e.g. the timer box)
//   2. Plays the practice intro, then starts the run
//      (replaces Phase2Briefing, which switches itself off in practice)
//   3. STEP GUIDANCE — watches SimulationManager.CurrentStep. Each time the
//      step changes: banner text, voice clip, floor arrows.
//      On a wrong action: that step's "try again" voice clip.
//
// NO ▼ MARKER ARROW IN PRACTICE. The floor arrows do the guiding. The
// shared MarkerArrowManager (Phase 1's green arrow) positions itself from
// an object's bounds, and the Phase 2 targets have children far from the
// object itself, so it floated in the wrong place. It is kept hidden for
// the whole practice run - including SimulationManager's evacuate arrow on
// the last step, which uses the same shared arrow. Real runs are untouched:
// this script is switched off outside practice.
//   4. Listens for PracticeRun.Ended:
//        finished correctly -> "Practice complete" lines -> REAL Phase 2
//        wrong decision     -> "Try again" lines         -> practice again
//
// It never touches the timer, penalties or Laravel — SimulationManager's
// practice guards already skip those. It only READS CurrentStep; it never
// changes the run.
//
// WHY POLL CurrentStep INSTEAD OF AN EVENT:
// Reading a public property needs no change to SimulationManager. The step
// only changes a handful of times per run, and comparing one int per frame
// costs nothing.
//
// BOTH ENDINGS RELOAD THE SCENE through PhaseTransitionManager.LoadPhaseTwo,
// the same path Phase 1 uses, so Phase 2 starts completely fresh.
// -------------------------------------------------------

public class TutorialManager : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    [Tooltip("This scene's PhaseTransitionManager. Used to reload the scene " +
             "into the real Phase 2 (or into practice again), exactly the way " +
             "Phase 1 does it.")]
    public PhaseTransitionManager phaseTransition;

    [Tooltip("Objects hidden during practice only, e.g. the timer box. " +
             "In a real run this list is ignored.")]
    public GameObject[] hideDuringPractice;

    [Header("Practice Intro — plays before the run starts")]
    public DialogueLine[] introLines;
    public string introButtonText = "START PRACTICE";

    [Header("Practice Complete — plays after a correct finish")]
    public DialogueLine[] completeLines;
    public string completeButtonText = "START SIMULATION";

    [Header("Try Again — plays after a wrong decision")]
    public DialogueLine[] tryAgainLines;
    public string tryAgainButtonText = "TRY AGAIN";

    [Header("Step Guidance — one entry per step")]
    public TutorialStepGuide[] steps;

    [Tooltip("Banner step counter. {0} = current step, {1} = total steps.")]
    public string stepCounterFormat = "STEP {0} OF {1}";

    [Header("Banner UI (optional until built)")]
    [Tooltip("The banner's root object. Shown during the run, hidden otherwise.")]
    public GameObject bannerRoot;
    public TMP_Text bannerStepText;
    public TMP_Text bannerTitleText;
    public TMP_Text bannerHintText;

    [Header("Floor Arrows (optional until set up)")]
    public TutorialFloorPath floorPath;

    [Header("Timing")]
    [Tooltip("Short pause before the intro appears, so the scene has settled.")]
    public float startDelay = 0.5f;

    // Run state
    private bool runStarted = false;
    private bool runEnded = false;
    private bool subscribed = false;

    // The step currently on the banner. -1 = nothing shown yet.
    private int shownStep = -1;
    private TutorialStepGuide currentGuide;

    private void Start()
    {
        // Banner hidden in every mode until a practice run is live.
        if (bannerRoot != null) bannerRoot.SetActive(false);

        // REAL RUN (or Phase 1): switch off completely. Nothing below runs.
        if (!PracticeRun.IsActive)
        {
            enabled = false;
            return;
        }

        Debug.Log("[TutorialManager] PRACTICE run started.");

        foreach (GameObject go in hideDuringPractice)
            if (go != null) go.SetActive(false);

        PracticeRun.Ended += OnPracticeEnded;
        PracticeRun.WrongAction += OnWrongAction;
        subscribed = true;

        Invoke(nameof(PlayIntro), startDelay);
    }

    private void OnDestroy()
    {
        if (!subscribed) return;
        PracticeRun.Ended -= OnPracticeEnded;
        PracticeRun.WrongAction -= OnWrongAction;
        subscribed = false;
    }

    // -------------------------------------------------------
    // 1. INTRO -> START THE RUN
    // -------------------------------------------------------
    private void PlayIntro()
    {
        if (!HasLines(introLines) || DialogueManager.Instance == null)
        {
            BeginRun();
            return;
        }

        DialogueManager.Instance.StartDialogue(introLines, BeginRun, true, introButtonText);
    }

    private void BeginRun()
    {
        if (runStarted) return;
        runStarted = true;

        SceneAudioProfile.Instance?.BeginPhaseAudio();

        if (SimulationManager.Instance == null)
        {
            Debug.LogError("[TutorialManager] No SimulationManager in scene - practice cannot start!");
            return;
        }

        SimulationManager.Instance.BeginSimulation();

        if (bannerRoot != null) bannerRoot.SetActive(true);

        Debug.Log("[TutorialManager] Practice run is live (no timer, no score).");
    }

    // -------------------------------------------------------
    // 2. STEP GUIDANCE — reacts whenever the step changes
    // -------------------------------------------------------
    private void Update()
    {
        if (!runStarted || runEnded) return;
        if (SimulationManager.Instance == null) return;

        int step = SimulationManager.Instance.CurrentStep;
        if (step == shownStep) return;

        shownStep = step;
        ShowStep(step);
    }

    // NO ▼ MARKER DURING PRACTICE.
    // LateUpdate runs after every Update AND after coroutines resume, so it
    // also catches SimulationManager's delayed evacuate arrow in the same
    // frame it appears - the arrow is never drawn. Hide() on an already
    // hidden arrow does nothing, so calling it every frame is cheap.
    private void LateUpdate()
    {
        if (!runStarted || runEnded) return;
        if (MarkerArrowManager.Instance != null)
            MarkerArrowManager.Instance.Hide();
    }

    private void ShowStep(int step)
    {
        currentGuide = FindGuide(step);

        if (currentGuide == null)
        {
            Debug.Log($"[TutorialManager] No guide set up for step {step}.");
            HideArrows();
            return;
        }

        Debug.Log($"[TutorialManager] Step {step}: {currentGuide.title}");

        // Banner
        if (bannerStepText != null)
            bannerStepText.text = string.Format(stepCounterFormat, step, SimulationManager.Instance.MaxStep);
        if (bannerTitleText != null) bannerTitleText.text = currentGuide.title;
        if (bannerHintText != null) bannerHintText.text = currentGuide.hint;

        // Voice
        PlayVoice(currentGuide.voiceClip);

        // Floor arrows from the player to the target
        if (floorPath != null)
        {
            if (currentGuide.target != null) floorPath.Show(currentGuide.target, currentGuide.waypoints);
            else floorPath.Hide();
        }
    }

    // Raised by SimulationManager's practice guard on a wrong action.
    private void OnWrongAction(string tip)
    {
        if (runEnded) return;
        if (currentGuide != null)
            PlayVoice(currentGuide.wrongVoiceClip);
    }

    private TutorialStepGuide FindGuide(int step)
    {
        if (steps == null) return null;
        foreach (TutorialStepGuide g in steps)
            if (g != null && g.step == step) return g;
        return null;
    }

    private void PlayVoice(AudioClip clip)
    {
        if (clip == null || VoiceOverManager.Instance == null) return;
        VoiceOverManager.Instance.Play(clip);
    }

    private void HideArrows()
    {
        if (MarkerArrowManager.Instance != null) MarkerArrowManager.Instance.Hide();
        if (floorPath != null) floorPath.Hide();
    }

    // -------------------------------------------------------
    // 3. THE RUN ENDED — raised by SimulationManager's practice guard
    // -------------------------------------------------------
    private void OnPracticeEnded(bool won, string failReason)
    {
        if (runEnded) return;
        runEnded = true;

        Debug.Log($"[TutorialManager] Practice ended - won: {won}, reason: '{failReason}'.");

        if (bannerRoot != null) bannerRoot.SetActive(false);
        HideArrows();

        if (won)
            PlayThen(completeLines, completeButtonText, StartRealSimulation);
        else
            PlayThen(tryAgainLines, tryAgainButtonText, RestartPractice);
    }

    private void PlayThen(DialogueLine[] lines, string buttonText, System.Action next)
    {
        if (!HasLines(lines) || DialogueManager.Instance == null)
        {
            next();
            return;
        }

        DialogueManager.Instance.StartDialogue(lines, () => next(), true, buttonText);
    }

    // -------------------------------------------------------
    // 4. WHERE TO GO NEXT
    // -------------------------------------------------------
    private void StartRealSimulation()
    {
        Debug.Log("[TutorialManager] Loading the REAL simulation.");
        PracticeRun.Finish();
        LoadPhaseTwo();
    }

    private void RestartPractice()
    {
        Debug.Log("[TutorialManager] Restarting practice.");
        PracticeRun.RequestForScene(SceneManager.GetActiveScene().name);
        LoadPhaseTwo();
    }

    private void LoadPhaseTwo()
    {
        if (phaseTransition == null)
        {
            Debug.LogError("[TutorialManager] No PhaseTransitionManager assigned - cannot reload Phase 2!");
            return;
        }

        phaseTransition.LoadPhaseTwo();
    }

    private static bool HasLines(DialogueLine[] lines)
    {
        return lines != null && lines.Length > 0;
    }
}
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class SimulationManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // The central brain of Phase 2. Owns the 90-second timer,
    // tracks which step the player is on (1-8), subtracts time
    // for wrong actions, collects missed tips AND structured
    // step results, and triggers Win or Lose when the simulation
    // ends.
    //
    // EXTINGUISHER ANIMATION:
    // Every correct TPASS action also plays the matching Blender
    // clip on the extinguisher. That is what makes the PIN twist
    // and pull out, and the HOSE bend and sweep.
    //
    // NEW IN THIS VERSION - ALARM PRESS:
    // Step 1 now also plays the right-hand press animation through
    // PressAlarmController. That is a HAND animation driven by IK,
    // not an extinguisher clip, so it is dispatched separately -
    // see the note inside PlayClipForStep.
    //
    // WHY IT LIVES HERE:
    // RegisterCorrectAction already fires exactly once per
    // correct step, and already knows WHICH step it was. Putting
    // the animation call here means one place to maintain, and
    // it can never fire on a wrong action.
    //
    // WHY THE HANDS DO NOT NEED THEIR OWN CLIPS:
    // Grip_Pin is parented to Pin, and Grip_Nozzle to the hose
    // tip bone. Those are the IK targets. So when a clip moves
    // the part, the marker moves, and the hand follows for free.
    // One system animates the prop; IK keeps the hands attached.
    //
    // Step breakdown (Office/Classroom — TPASS):
    // 1 = Sound Alarm          <- right hand press (PressAlarmController)
    // 2 = Grab Extinguisher    <- handled by ExtinguisherGrab
    // 3 = TPASS Twist
    // 4 = TPASS Pull
    // 5 = TPASS Aim
    // 6 = TPASS Squeeze
    // 7 = TPASS Sweep
    // 8 = Evacuate (completed by Door, not RegisterCorrectAction)
    // -------------------------------------------------------

    // --- SINGLETON ---
    public static SimulationManager Instance { get; private set; }

    // --- INSPECTOR SETTINGS ---
    [SerializeField] private float totalTime = 90f;
    [SerializeField] private UnityEvent onWin;
    [SerializeField] private UnityEvent onLose;

    [Header("Results Submission")]
    [SerializeField] private ResultsSubmitter resultsSubmitter;

    // -------------------------------------------------------
    [Header("Alarm Press Animation")]
    [Tooltip("The PressAlarmController. Plays the right-hand reach and " +
             "press when the player sounds the alarm on Step 1. " +
             "Leave empty to skip the animation entirely - the step still " +
             "registers normally.")]
    [SerializeField] private PressAlarmController pressAlarm;
    // -------------------------------------------------------

    // -------------------------------------------------------
    [Header("Extinguisher Animation")]
    [Tooltip("The Animator on FireExtinguisher_ABC. Plays the six Blender clips.")]
    [SerializeField] private Animator extinguisherAnimator;

    [Tooltip("Clip state names as they appear in the Animator Controller. " +
             "Change these only if you renamed the states.")]
    [SerializeField] private string clipTwist = "Twist";
    [SerializeField] private string clipPull = "Pull";
    [SerializeField] private string clipPinDrop = "PinDrop";
    [SerializeField] private string clipAim = "Aim";
    [SerializeField] private string clipSqueeze = "Squeeze";
    [SerializeField] private string clipSweep = "Sweep";

    [Tooltip("Seconds to wait after Pull before the pin drops to the floor.")]
    [SerializeField] private float pinDropDelay = 0.4f;

    [Header("After the Pin Drops")]
    [Tooltip("The Pin object on the extinguisher. It is hidden once the " +
             "PinDrop clip finishes - the pin has left the extinguisher, so " +
             "it should not stay floating in the scene.")]
    [SerializeField] private GameObject pinObject;

    [Tooltip("The LeftHandIKController. Its ReleaseAll() is called when " +
             "PinDrop finishes, so the left hand lets go and returns to rest " +
             "instead of hanging in mid-air where the pin used to be.")]
    [SerializeField] private LeftHandIKController leftHandIK;

    [Tooltip("How long the PinDrop clip runs. The pin is hidden and the hand " +
             "released after this. Check the clip length in the Inspector.")]
    [SerializeField] private float pinDropClipLength = 0.5f;
    // -------------------------------------------------------

    // --- RUNTIME STATE ---
    private float timeRemaining;
    [SerializeField] private int currentStep = 1;
    [SerializeField] private bool simActive = false;
    private List<string> missedTips = new List<string>();
    private List<StepResult> stepResults = new List<StepResult>();
    private int totalPenaltySeconds = 0;

    // --- PUBLIC READ-ONLY PROPERTIES ---
    public float TimeRemaining => timeRemaining;
    public int CurrentStep => currentStep;
    public List<string> MissedTips => missedTips;
    public bool IsSimActive => simActive;
    public int TotalPenaltySeconds => totalPenaltySeconds;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        // Bring the pin back for a fresh run - it was hidden last time.
        if (pinObject != null) pinObject.SetActive(true);

        // Withdraw the right arm and unfold the fingers, in case a run
        // was abandoned mid-press. Without this the hand could still be
        // held out at the button when the next run starts.
        if (pressAlarm != null) pressAlarm.ResetState();

        timeRemaining = totalTime;
        currentStep = 1;
        simActive = false;
        totalPenaltySeconds = 0;
        missedTips.Clear();
        stepResults.Clear();
    }

    // -------------------------------------------------------
    // BEGIN SIMULATION
    // -------------------------------------------------------
    public void BeginSimulation()
    {
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1)
        {
            Debug.LogWarning("[SimulationManager] BeginSimulation called outside Phase 2 — ignored.");
            return;
        }

        if (simActive) return;

        simActive = true;
        Debug.Log("[SimulationManager] Simulation started. Timer running.");
    }

    private void Update()
    {
        if (!simActive) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    // -------------------------------------------------------
    // REGISTER CORRECT ACTION
    // -------------------------------------------------------
    public void RegisterCorrectAction(SimulationInteractable.SimStep step, string chosenAction)
    {
        if (!simActive) return;

        stepResults.Add(new StepResult
        {
            step_name = step.ToString(),
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = true,
            penalty_seconds = 0
        });

        // >>> FEEDBACK LOG: green row with a friendly label of the completed step.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowCorrect(StepNames.Friendly(step.ToString()));

        // >>> ANIMATION: play the hand or extinguisher animation for this step.
        PlayClipForStep(step);

        if (currentStep < 8)
            currentStep++;
    }

    // -------------------------------------------------------
    // STEP ANIMATION
    //
    // Matched on the step's NAME rather than by comparing enum
    // members directly. That keeps this working even if the enum
    // is renamed or reordered later - it only cares that the name
    // contains "Alarm", "Twist", "Pull", and so on.
    //
    // ORDER MATTERS HERE.
    // The alarm press is a HAND animation driven by IK. It does not
    // use extinguisherAnimator at all. So it must be dispatched
    // BEFORE the extinguisherAnimator null guard below - otherwise a
    // scene with no Animator assigned would silently swallow the
    // press, with no error and nothing on screen to explain why.
    // -------------------------------------------------------
    private void PlayClipForStep(SimulationInteractable.SimStep step)
    {
        string name = step.ToString();

        // --- HAND ANIMATION (no extinguisher Animator involved) ---
        if (name.Contains("Alarm"))
        {
            if (pressAlarm != null)
            {
                pressAlarm.PlayAlarmPress();
                Debug.Log("[SimulationManager] Alarm press animation started.");
            }
            return;
        }

        if (name.Contains("Grab"))
        {
            // Cancel any press still in flight. The press runs for about a
            // second, and nothing stops the player tapping Grab while it is
            // still withdrawing. If that happened, PressAlarmController would
            // be fading rightArmIK.weight 1 -> 0 while ExtinguisherGrab fades
            // the SAME value 0 -> 1 - two writers on one value, which stutters
            // or snaps the arm. ResetState() stops the coroutine and zeroes
            // the weight, so ExtinguisherGrab inherits a clean slate.
            if (pressAlarm != null)
            {
                pressAlarm.ResetState();
                Debug.Log("[SimulationManager] Alarm press cancelled - grab takes the arm.");
            }
            return;
        }

        // --- EXTINGUISHER CLIPS from here down ---
        if (extinguisherAnimator == null) return;

        if (name.Contains("Twist"))
        {
            PlayClip(clipTwist);
        }
        else if (name.Contains("Pull"))
        {
            PlayClip(clipPull);
            // The pin leaves the hand, falls, then disappears - and the
            // left hand lets go and returns to rest.
            StartCoroutine(PullSequence());
        }
        else if (name.Contains("Aim"))
        {
            PlayClip(clipAim);
        }
        else if (name.Contains("Squeeze"))
        {
            PlayClip(clipSqueeze);
        }
        else if (name.Contains("Sweep"))
        {
            PlayClip(clipSweep);
        }
        // Evacuate has no animation.
    }

    // Play a state from its first frame. Play() rather than
    // CrossFade() because the six clips animate different parts,
    // so there is nothing to blend between - and no exit-time
    // transitions exist, so playback stops at the end of the clip.
    private void PlayClip(string stateName)
    {
        if (extinguisherAnimator == null || string.IsNullOrEmpty(stateName)) return;

        extinguisherAnimator.Play(stateName, 0, 0f);
        Debug.Log($"[SimulationManager] Extinguisher clip: {stateName}");
    }

    // -------------------------------------------------------
    // PULL SEQUENCE
    //
    // Runs the whole post-Pull choreography in one place, so the
    // timing is easy to read and easy to retune:
    //
    //   1. wait  -> the Pull clip finishes carrying the pin out
    //   2. drop  -> PinDrop clip plays, pin falls
    //   3. wait  -> the drop finishes
    //   4. hide  -> pin disappears (it is gone from the extinguisher)
    //   5. release -> left hand lets go and returns to rest, so it is
    //                 not left hanging where the pin used to be
    // -------------------------------------------------------
    private IEnumerator PullSequence()
    {
        // 1 + 2: let the Pull clip carry the pin out, then drop it.
        yield return new WaitForSeconds(pinDropDelay);
        PlayClip(clipPinDrop);

        // 3: let the drop finish.
        yield return new WaitForSeconds(pinDropClipLength);

        // 4: the pin is gone from the extinguisher - hide it.
        if (pinObject != null)
        {
            pinObject.SetActive(false);
            Debug.Log("[SimulationManager] Pin hidden after drop.");
        }

        // 5: the left hand has nothing left to hold - return it to rest.
        if (leftHandIK != null)
        {
            leftHandIK.ReleaseAll();
            Debug.Log("[SimulationManager] Left hand released to rest.");
        }
    }

    // -------------------------------------------------------
    // REGISTER WRONG ACTION
    // -------------------------------------------------------
    public void RegisterWrongAction(SimulationInteractable.SimStep step, string chosenAction, float timePenalty, string tip)
    {
        if (!simActive) return;

        timeRemaining -= timePenalty;
        missedTips.Add(tip);

        int penaltyInt = Mathf.RoundToInt(timePenalty);
        totalPenaltySeconds += penaltyInt;

        stepResults.Add(new StepResult
        {
            step_name = step.ToString(),
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = false,
            penalty_seconds = penaltyInt
        });

        // >>> FEEDBACK LOG: red row. The hint points at the CURRENT step
        // the player should be doing (currentStep), NOT the button they
        // mis-tapped — so it guides them to the right next move.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowWrong(StepNames.Hint(currentStep), penaltyInt);

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    public void EndSimulation(bool won)
    {
        if (!simActive) return;
        simActive = false;

        GameModeManager modeManager = FindFirstObjectByType<GameModeManager>();
        if (modeManager != null)
            modeManager.ResetToPhase1();

        if (won)
        {
            int finalScore = Mathf.RoundToInt(timeRemaining);
            Debug.Log($"[SimulationManager] Completed. Submitting for validation. Local score: {finalScore}");

            ResultsUIManager resultsUI = FindFirstObjectByType<ResultsUIManager>();
            if (resultsUI != null)
                resultsUI.ShowSubmitting();

            onWin.Invoke();

            if (resultsSubmitter != null)
            {
                resultsSubmitter.Submit(finalScore, totalPenaltySeconds, stepResults);
            }
            else
            {
                Debug.LogWarning("[SimulationManager] No ResultsSubmitter assigned — results were NOT sent to Laravel.");
            }
        }
        else
        {
            Debug.Log("[SimulationManager] LOSE.");
            onLose.Invoke();
        }
    }
}
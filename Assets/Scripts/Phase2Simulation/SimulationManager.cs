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
    // The timer does not start the moment the scene loads.
    // Phase2Briefing plays the BFP officer's intro first, then
    // calls BeginSimulation() when the player taps "START".
    // Until then simActive stays false, so the clock is frozen
    // and no penalties can be taken.
    //
    // NEW THIS UPDATE: TotalPenaltySeconds is now exposed as a
    // public property. ResultsUIManager reads it to show the
    // penalties chip on the win panel — the Laravel response
    // doesn't carry that number back, but we already have it
    // locally from tracking it during the run.
    //
    // Every correct/wrong action is also recorded as a StepResult
    // (step name, what was tapped, was it correct, penalty). On a
    // WIN, that full list is handed to ResultsSubmitter, which
    // POSTs it to Laravel.
    //
    // Step breakdown (Office/Classroom — TPASS):
    // 1 = Sound Alarm
    // 2 = Grab Extinguisher
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
    // Drag the GameObject holding ResultsSubmitter here.
    // On a WIN, we call Submit() on this with everything we tracked.
    [SerializeField] private ResultsSubmitter resultsSubmitter;

    // --- RUNTIME STATE ---
    private float timeRemaining;
    [SerializeField] private int currentStep = 1;
    [SerializeField] private bool simActive = false;
    private List<string> missedTips = new List<string>();

    // The structured list Laravel expects. One entry per
    // correct or wrong action taken during the simulation.
    private List<StepResult> stepResults = new List<StepResult>();

    // Running total of penalty seconds — matches total_penalties
    // in the payload exactly, since it's built from the same events.
    private int totalPenaltySeconds = 0;

    // --- PUBLIC READ-ONLY PROPERTIES ---
    public float TimeRemaining => timeRemaining;
    public int CurrentStep => currentStep;
    public List<string> MissedTips => missedTips;
    public bool IsSimActive => simActive;

    // NEW: read by ResultsUIManager for the win panel's penalty chip.
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

    private void Start()
    {
        timeRemaining = totalTime;

        // The clock does NOT start here. Phase2Briefing calls
        // BeginSimulation() once the intro dialogue is done.
        simActive = false;
    }

    // -------------------------------------------------------
    // BEGIN SIMULATION
    // Called by Phase2Briefing when the player dismisses the
    // officer's intro. This is the moment the 90 seconds start.
    // -------------------------------------------------------
    public void BeginSimulation()
    {
        // Guard: only ever run in Phase 2.
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1)
        {
            Debug.LogWarning("[SimulationManager] BeginSimulation called outside Phase 2 — ignored.");
            return;
        }

        // Guard: don't restart a sim that's already running or finished.
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
            // Fail Type A: timer ran out. Per your spec, Unity
            // NEVER posts to Laravel on this path.
            EndSimulation(won: false);
        }
    }

    // -------------------------------------------------------
    // REGISTER CORRECT ACTION
    // Called by SimulationInteractable / TPASSButtonManager when
    // the player taps the right object/button for the current step.
    //
    // step        = which of the 8 steps this action belongs to
    // chosenAction = a readable label for what was tapped, e.g. "AlarmPanel"
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

        if (currentStep < 8)
            currentStep++;
    }

    // -------------------------------------------------------
    // REGISTER WRONG ACTION
    // step         = the step the player WAS ON when they got it wrong
    // chosenAction = what they actually tapped instead, e.g. "WrongObject"
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
            Debug.Log($"[SimulationManager] WIN. Score: {finalScore}");

            // Show the win screen immediately using the score we
            // already know locally — we don't make the player wait
            // on the network before seeing their result.
            onWin.Invoke();

            // Fire the submission in the background. ResultsSubmitter's
            // own UnityEvents (onSaved/onRetry/onDuplicate/onConnectionError)
            // handle whatever happens after Laravel responds.
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
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
    [SerializeField] private ResultsSubmitter resultsSubmitter;

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

        if (currentStep < 8)
            currentStep++;
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
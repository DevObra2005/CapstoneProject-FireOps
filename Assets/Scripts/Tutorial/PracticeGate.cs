using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

// -------------------------------------------------------
// WHAT THIS DOES:
// The "door" between Phase 1 and Phase 2.
//
// When the player taps Continue after Phase 1, this decides:
//
//   Not completed -> FIRST-TIMER -> practice is REQUIRED (no choice)
//   Completed     -> RETURNING   -> choice pop-up:
//                                     Practice again / Go to simulation
//
// It reuses the SAME request as PerformanceResultsScene
// (GET /api/participant/results?event_id=...), so no Laravel change.
//
// PREFETCH — WHY THE POP-UP APPEARS INSTANTLY:
// The check is started in the BACKGROUND as soon as Phase 1 loads.
// The player spends minutes finding hazards, so by the time Continue is
// tapped the answer is already here. Same idea as loading data in a
// React useEffect when the page opens instead of when a button is clicked.
//
//   - Answer ready when Continue is tapped -> route immediately
//   - Still waiting                       -> wait for it (button disabled)
//   - Background check FAILED             -> try once more on Continue,
//     so a brief network drop at the start of Phase 1 cannot force a
//     returning player into practice.
//
// HOW PRACTICE IS STARTED:
// PracticeRun.RequestForScene(...) turns the practice switch on for the
// next load of this scene, then PhaseTransitionManager.LoadPhaseTwo()
// reloads it exactly the way it always has.
//
// NO CHOICE PANEL? Returning players go straight to the simulation,
// exactly like before this script existed.
// -------------------------------------------------------

public class PracticeGate : MonoBehaviour
{
    [Header("References (assign in Inspector)")]
    [Tooltip("This scene's PhaseTransitionManager. The gate hands control " +
             "back to it to load Phase 2, so the loading screen, music fade " +
             "and SimulationMode flag all work exactly as before.")]
    public PhaseTransitionManager phaseTransition;

    [Tooltip("This scene's ResultsSubmitter. Its Environment field " +
             "(office / kitchen / classroom) is the one source of truth for " +
             "which environment this is.")]
    public ResultsSubmitter resultsSubmitter;

    [Header("Optional UI")]
    [Tooltip("The confirm panel's button. Disabled while waiting for Laravel, " +
             "so a double tap cannot route twice.")]
    public Button confirmButton;

    [Tooltip("The RETURNING player pop-up (Practice again / Go to simulation). " +
             "Leave EMPTY and returning players go straight to the simulation.")]
    public GameObject choicePanel;

    [Header("Network")]
    [Tooltip("Ask Laravel in the background as soon as Phase 1 loads, so the " +
             "answer is ready by the time Continue is tapped. Untick to ask " +
             "only when Continue is tapped (slower, the old behaviour).")]
    public bool prefetchDuringPhase1 = true;

    [Tooltip("MAXIMUM seconds to wait for Laravel before giving up. This does " +
             "not make the request faster - it only limits the worst case. On " +
             "timeout the player is treated as a first-timer (practice required).")]
    public int requestTimeoutSeconds = 8;

    // Background check state
    private enum CheckState { NotStarted, Running, Done }
    private CheckState checkState = CheckState.NotStarted;
    private bool cachedReturning = false;
    private bool lastCheckSucceeded = false;

    // Guards against a double tap routing twice.
    private bool isRouting = false;

    private void Start()
    {
        if (choicePanel != null)
            choicePanel.SetActive(false);

        // Only in Phase 1 - that is where the Continue button lives.
        if (prefetchDuringPhase1 && PlayerPrefs.GetInt("SimulationMode", 0) == 0)
        {
            Debug.Log("[PracticeGate] Prefetching participant status in the background.");
            StartCheck();
        }
    }

    // -------------------------------------------------------
    // Hook the confirm button's OnClick to this.
    // -------------------------------------------------------
    public void OnConfirmPressed()
    {
        if (isRouting) return;
        StartCoroutine(RouteWhenReady());
    }

    private IEnumerator RouteWhenReady()
    {
        isRouting = true;
        if (confirmButton != null) confirmButton.interactable = false;

        // The background check failed earlier (e.g. no internet at the start
        // of Phase 1). Try once more now rather than trusting a fallback.
        if (checkState == CheckState.Done && !lastCheckSucceeded)
        {
            Debug.Log("[PracticeGate] Earlier check failed - trying again.");
            checkState = CheckState.NotStarted;
        }

        StartCheck();                                    // no-op if already running or done
        while (checkState != CheckState.Done)            // usually already done
            yield return null;

        if (confirmButton != null) confirmButton.interactable = true;
        isRouting = false;

        Route(cachedReturning);
    }

    private void Route(bool isReturning)
    {
        string environment = CurrentEnvironment();

        // ---- FIRST-TIMER: practice is required, no choice ----
        if (!isReturning)
        {
            Debug.Log($"[PracticeGate] FIRST-TIMER - '{environment}' not completed. Practice is REQUIRED.");
            GoToPractice();
            return;
        }

        // ---- RETURNING: let them choose ----
        Debug.Log($"[PracticeGate] RETURNING player - '{environment}' already completed.");

        if (choicePanel == null)
        {
            Debug.Log("[PracticeGate] No Choice Panel assigned - going straight to the simulation.");
            GoToSimulation();
            return;
        }

        HideConfirmPanel();
        choicePanel.SetActive(true);
    }

    // -------------------------------------------------------
    // CHOICE PANEL BUTTONS — hook these in the Inspector
    // -------------------------------------------------------
    public void ChoosePractice()
    {
        if (choicePanel != null) choicePanel.SetActive(false);
        Debug.Log("[PracticeGate] Returning player chose PRACTICE.");
        GoToPractice();
    }

    public void ChooseSimulation()
    {
        if (choicePanel != null) choicePanel.SetActive(false);
        Debug.Log("[PracticeGate] Returning player chose the SIMULATION.");
        GoToSimulation();
    }

    // -------------------------------------------------------
    // WHERE TO GO
    // -------------------------------------------------------
    private void GoToPractice()
    {
        PracticeRun.RequestForScene(SceneManager.GetActiveScene().name);
        LoadPhaseTwo();
    }

    private void GoToSimulation()
    {
        LoadPhaseTwo();
    }

    private void LoadPhaseTwo()
    {
        if (phaseTransition != null)
            phaseTransition.LoadPhaseTwo();
        else
            Debug.LogError("[PracticeGate] No PhaseTransitionManager assigned - cannot load Phase 2!");
    }

    private void HideConfirmPanel()
    {
        if (phaseTransition != null && phaseTransition.confirmPanel != null)
            phaseTransition.confirmPanel.SetActive(false);
    }

    private string CurrentEnvironment()
    {
        return resultsSubmitter != null ? resultsSubmitter.environment : "";
    }

    // -------------------------------------------------------
    // THE CHECK — runs once, in the background
    // -------------------------------------------------------
    private void StartCheck()
    {
        if (checkState != CheckState.NotStarted) return;
        checkState = CheckState.Running;
        StartCoroutine(RunCheck());
    }

    private IEnumerator RunCheck()
    {
        string environment = CurrentEnvironment();
        if (string.IsNullOrEmpty(environment))
            Debug.LogWarning("[PracticeGate] No ResultsSubmitter assigned - cannot tell which environment this is.");

        float startedAt = Time.realtimeSinceStartup;

        yield return FetchCompleted(environment, (completed, ok) =>
        {
            cachedReturning = completed;
            lastCheckSucceeded = ok;
        });

        checkState = CheckState.Done;

        float took = Time.realtimeSinceStartup - startedAt;
        Debug.Log($"[PracticeGate] Check finished in {took:F1}s - " +
                  $"{(lastCheckSucceeded ? (cachedReturning ? "RETURNING" : "FIRST-TIMER") : "FAILED (fallback: first-timer)")}.");
    }

    // -------------------------------------------------------
    // Asks Laravel for this participant's results.
    // Reports (completed, ok):
    //   completed - THIS environment is completed
    //   ok        - the request actually worked (false = fallback used)
    //
    // SAFE FALLBACK: any failure reports completed = false, which means
    // "first-timer, practice required". Practicing when unsure is safer
    // than skipping when unsure.
    // -------------------------------------------------------
    private IEnumerator FetchCompleted(string environment, System.Action<bool, bool> onDone)
    {
        string token = PlayerPrefs.GetString("participant_token", "");
        int eventId = PlayerPrefs.GetInt("participant_event_id", 0);

        if (string.IsNullOrEmpty(token))
        {
            Debug.LogWarning("[PracticeGate] No participant_token (not logged in?) - treating as first-timer.");
            onDone(false, false);
            yield break;
        }

        string url = ApiConfig.ResultsUrl + "?event_id=" + eventId;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.SetRequestHeader("Accept", "application/json");
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
            {
                Debug.LogWarning($"[PracticeGate] Check failed ({request.responseCode}: {request.error}) - treating as first-timer.");
                onDone(false, false);
                yield break;
            }

            ResultsResponse data = JsonUtility.FromJson<ResultsResponse>(request.downloadHandler.text);

            if (data == null || data.environments == null)
            {
                Debug.LogWarning("[PracticeGate] Could not read the response - treating as first-timer.");
                onDone(false, false);
                yield break;
            }

            foreach (EnvironmentResult env in data.environments)
            {
                if (env.environment == environment)
                {
                    onDone(env.completed, true);
                    yield break;
                }
            }

            // This environment was not in the list at all -> never played.
            onDone(false, true);
        }
    }
}
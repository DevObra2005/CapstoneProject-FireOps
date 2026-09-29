using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// -------------------------------------------------------
// WHAT THIS DOES:
// The PRACTICE switch. One question, answered everywhere:
// "Is this run a practice?"
//
//   PracticeGate      turns it ON right before loading a practice run
//   SimulationManager reads it to skip the timer, penalties and the
//                     Laravel submission (practice guards only)
//   TutorialManager   reads it to show arrows, instructions and tips
//
// WHY IT IS SAFE — A REAL RUN CAN NEVER BE TREATED AS PRACTICE:
//
// 1. MEMORY ONLY. A static value, not PlayerPrefs, so it is OFF every
//    time the app starts. Same idea as GameModeManager.intentionalTransition.
//
// 2. ONE SCENE LOAD ONLY. Practice is REQUESTED for a specific scene.
//    It switches ON only when that scene finishes loading. ANY other
//    scene load switches it OFF — so quitting to the main menu in the
//    middle of practice cannot leave it on for the next real run.
//
// 3. EDITOR RESET. With "Enter Play Mode Options" turned on, statics can
//    survive between Play presses. ResetStatics() clears everything at
//    the start of every Play, so an old test can never leak into a new one.
//
// THIS FILE CHANGES NOTHING ON ITS OWN. Until something calls
// RequestForScene(), IsActive is always false and every guard that
// reads it is skipped.
// -------------------------------------------------------

public static class PracticeRun
{
    // TRUE only while a practice run is being played.
    public static bool IsActive { get; private set; }

    // The scene practice was requested for, waiting for it to load.
    private static string pendingScene;

    // -------------------------------------------------------
    // EVENTS — SimulationManager reports, TutorialManager listens.
    // Only ever raised from inside practice guards.
    // -------------------------------------------------------

    // A wrong action during practice. Carries the educational tip.
    public static event Action<string> WrongAction;

    // The practice run ended. won = finished all steps, failReason
    // e.g. "wrong_decision" (Office two-fire scenario).
    public static event Action<bool, string> Ended;

    // -------------------------------------------------------
    // CALLED BY PracticeGate
    // -------------------------------------------------------

    // Ask for the NEXT load of this scene to be a practice run.
    public static void RequestForScene(string sceneName)
    {
        pendingScene = sceneName;
        Debug.Log($"[PracticeRun] Practice requested for '{sceneName}'.");
    }

    // -------------------------------------------------------
    // CALLED BY TutorialManager when practice is over, right
    // before loading the real Phase 2.
    // -------------------------------------------------------
    public static void Finish()
    {
        IsActive = false;
        pendingScene = null;
        Debug.Log("[PracticeRun] Practice finished - switch OFF.");
    }

    // -------------------------------------------------------
    // CALLED BY SimulationManager — inside practice guards only.
    // -------------------------------------------------------
    public static void ReportWrongAction(string tip) => WrongAction?.Invoke(tip);
    public static void ReportEnded(bool won, string failReason) => Ended?.Invoke(won, failReason);

    // -------------------------------------------------------
    // AUTOMATIC SAFETY — no one needs to call these.
    // -------------------------------------------------------

    // Runs before anything else at the start of every Play / app launch.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsActive = false;
        pendingScene = null;
        WrongAction = null;
        Ended = null;

        // Remove first so it can never be registered twice.
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // Runs every time a scene finishes loading — after Awake, before Start.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Additive loads (UI overlays etc.) are not a new run.
        if (mode != LoadSceneMode.Single) return;

        if (pendingScene != null && scene.name == pendingScene)
        {
            IsActive = true;
            pendingScene = null;
            Debug.Log($"[PracticeRun] '{scene.name}' loaded as PRACTICE - switch ON.");
        }
        else
        {
            if (IsActive)
                Debug.Log($"[PracticeRun] '{scene.name}' loaded - practice switch OFF.");
            IsActive = false;
        }
    }
}

using System.Collections.Generic;

// -------------------------------------------------------
// WHAT THIS DOES:
// Two jobs in one file:
//
// 1. DATA CLASSES — C# shapes that match the JSON the Laravel
//    getMyResults() endpoint returns. Unity's JsonUtility fills
//    these in from the response. Field names MUST match the JSON
//    keys exactly (environment, completed, percentage_score, etc.).
//
// 2. HANDOFF — a static holder that carries the environment the
//    player tapped from PerformanceResultsScene into the separate
//    ResultsDetailScene. Static = it survives the scene change,
//    so the detail scene can read it without re-fetching from the
//    network. We overwrite it every time a card is tapped.
// -------------------------------------------------------

// ── One step in the breakdown (matches a SimulationStep row) ──
[System.Serializable]
public class ResultStep
{
    public string step_name;
    public int sub_step;
    public string chosen_action;
    public bool was_correct;
    public int penalty_seconds;
}

// ── One environment's result (office / kitchen / classroom) ──
[System.Serializable]
public class EnvironmentResult
{
    public string environment;       // "office" / "kitchen" / "classroom"
    public bool completed;           // false = not played yet (locked card)

    // These are only meaningful when completed == true:
    public int percentage_score;     // the real score (0-100)
    public string score_label;       // "Excellent" / "Good" / "Passed"
    public int time_remaining;       // seconds left (phase2_score)
    public int total_penalties;      // total penalty seconds
    public string played_at;         // "2026-08-14"
    public List<ResultStep> steps;   // the full step breakdown
}

// ── The whole response from getMyResults() ──
[System.Serializable]
public class ResultsResponse
{
    public int event_id;
    public string event_name;
    public List<EnvironmentResult> environments;  // always 3
}

// ── Static handoff between the two scenes ──
public static class ResultsHandoff
{
    // The environment card the player tapped. PerformanceResults sets
    // this right before loading ResultsDetailScene; the detail scene
    // reads it in Start(). Overwritten on every tap.
    public static EnvironmentResult Selected;

    // The event name, carried along so the detail scene can show it too.
    public static string EventName;
}
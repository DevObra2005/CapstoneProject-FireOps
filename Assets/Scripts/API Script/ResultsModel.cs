using System;
using System.Collections.Generic;

// ── One entry in the "steps" array ──────────────────────────────────
[System.Serializable]
public class StepResult
{
    public string step_name;       // e.g. "SoundAlarm", "GrabExtinguisher", "TPASS_Twist"
    public string sub_step;        // e.g. null, or a TPASS sub-action name
    public string chosen_action;   // e.g. "AlarmPanel", "WrongObject"
    public bool was_correct;
    public int penalty_seconds;    // 0 if correct, 10 or 20 if wrong
}

// ── The full request body Unity sends to Laravel ────────────────────
[System.Serializable]
public class ResultsPayload
{
    public int event_id;
    public string environment;         // "office", "kitchen", "classroom"
    public int phase2_score;           // seconds remaining (0 on a timeout)
    public int total_penalties;        // total seconds deducted
    public bool phase2_passed;         // false when the timer ran out
    public List<StepResult> steps;
}

// ── What POST /api/participant/results sends back ────────────────────
// NOTE THE NAME. There is already a ResultsResponse used by
// PerformanceResultsManager for the GET endpoint — a completely
// different shape (environments, event_name). Two endpoints, two
// classes, two names.
//
// Every attempt UP TO AND INCLUDING the first pass is recorded. After
// that the participant's record is final: they can keep playing, but
// nothing is written and already_recorded comes back true.
//
// IMPORTANT: branch on 'passed' for Win vs Lose, never on 'saved'.
[System.Serializable]
public class SubmitResultResponse
{
    public bool saved;             // false on a practice run — not a Win/Lose signal
    public bool already_recorded;  // true = they had already passed; nothing saved
    public bool passed;            // true = Win screen, false = Lose screen
    public bool retry;             // opposite of passed — offer another go
    public string fail_reason;     // "" on a pass, "timeout" or "low_score" on a fail
    public int attempt_number;     // 1, 2, 3... — 0 on a practice run
    public string message;
    public int session_id;
    public int percentage_score;
    public string score_label;     // "Excellent" / "Good" / "Passed" / "Failed"
    public int time_remaining;
}
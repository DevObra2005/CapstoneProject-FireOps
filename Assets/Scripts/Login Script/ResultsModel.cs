using System;
using System.Collections.Generic;

// ── One entry in the "steps" array ──────────────────────────────────
// This mirrors ONE object inside the steps: [ ... ] list in the JSON spec.
// [System.Serializable] tells Unity "this class can be converted to/from JSON"
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
// This is the "form" we fill out and POST — matches your spec exactly.
[System.Serializable]
public class ResultsPayload
{
    public int event_id;
    public string environment;         // "office", "kitchen", "classroom"
    public int phase2_score;           // seconds remaining
    public int total_penalties;        // total seconds deducted
    public bool phase2_passed;         // Unity only ever sends true (per spec)
    public List<StepResult> steps;
}

// ── What Laravel sends back on a PASS ────────────────────────────────
[System.Serializable]
public class ResultsSuccessResponse
{
    public bool saved;
    public string message;
    public int session_id;
    public int percentage_score;
    public string score_label;     // "Excellent" / "Good" / "Passed"
    public int time_remaining;
}

// ── What Laravel sends back on FAIL / DUPLICATE ──────────────────────
// saved will be false. "retry" only appears on Fail Type B, so it's
// nullable-ish here — we just check its value defensively in code.
[System.Serializable]
public class ResultsFailResponse
{
    public bool saved;
    public bool retry;
    public string message;
    public int percentage_score;
}
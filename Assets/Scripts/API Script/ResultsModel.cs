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
    public bool phase2_passed;         // false when the run ended in a loss

    // WHY THE RUN ENDED — but only when Unity knows something the server
    // cannot work out for itself.
    //
    //   ""                 no reason given; Laravel infers as it always has
    //   "wrong_decision"   OFFICE: cleared the wrong fire, exit was blocked
    //
    // Laravel can already tell a timeout from a low score: no time left
    // means the clock ran out, time left with a sub-50% score means too
    // many mistakes. Everything it needs for those two is in the fields
    // above.
    //
    // A wrong fire choice is invisible to it. That run can finish with 67%
    // and 26 seconds still on the clock — nothing in the data says it
    // should have ended at all. Unity's lose panel said WRONG DECISION and
    // the admin panel said "Ran out of time" for the same attempt, which is
    // the gap this field closes.
    //
    // JsonUtility WRITES A NULL STRING AS "" rather than dropping the key,
    // so Laravel always receives this field. Its validation has to treat an
    // empty value as "not given" and fall back to the old inference — which
    // is also what keeps Kitchen, Classroom and every existing record
    // working with no change at all.
    public string fail_reason;

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

    // "" on a pass. On a fail: "timeout", "low_score", or "wrong_decision".
    //
    // The first two are worked out by Laravel. The third is whatever Unity
    // sent up in ResultsPayload above — the server stores it and hands it
    // straight back, so what the admin panel shows and what the player saw
    // are the same string.
    public string fail_reason;

    public int attempt_number;     // 1, 2, 3... — 0 on a practice run
    public string message;
    public int session_id;
    public int percentage_score;
    public string score_label;     // "Excellent" / "Good" / "Passed" / "Failed"
    public int time_remaining;
}
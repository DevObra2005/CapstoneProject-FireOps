using UnityEngine;
using TMPro;

// -------------------------------------------------------
// WHAT THIS DOES:
// Listens for the simulation's outcome and shows the right panel.
// Displays what SimulationManager and ResultsSubmitter tell it — it
// calculates nothing itself. Laravel is the source of truth for
// pass/fail (>= 50% penalty-based score).
//
// LOSE REASONS (each sets its own kicker so the panel label matches):
//   TIME EXPIRED        -> timer hit zero, player didn't finish
//   TOO MANY MISTAKES   -> finished but scored < 50%
//   WRONG DECISION      -> cleared the wrong fire first (Office only)
//   COULDN'T SAVE       -> connection / unknown error
//
// ONE HANDLER FOR BOTH SERVER FAILURES:
// Every attempt is submitted now, including timeouts, so a failed run
// comes back from Laravel with fail_reason telling us WHICH kind it
// was. ShowLoseResult reads that field and picks the right wording.
//
// PRACTICE RUNS:
// Recording stops at the first pass. A run played after that comes
// back with already_recorded = true and nothing written to the
// database. The player still sees Win or Lose based on how they
// actually played — but the panel says the run did not count, so a
// 95% practice score is not mistaken for a new record when their
// certificate says 70%.
//
// Tips were removed from these modals — the full penalty breakdown
// lives in the Performance Results screen.
//
// -------------------------------------------------------
// THE WRONG-DECISION LOSS (OFFICE ONLY)
//
// The Office decision scenario ends the run when the player clears the
// far fire instead of the one blocking the door. That is a LOSS, but it
// is not a timeout and it is not a low score — the clock may still have
// fifty seconds on it.
//
// TWO PLACES WOULD HAVE MISLABELLED IT.
//
//   1. ShowLoseTimerRanOut is wired to SimulationManager's onLose event
//      in the Inspector, so it fires the instant ANY loss happens. It
//      would announce "TIME EXPIRED" over a clock that was still running.
//
//   2. ShowLoseResult then refreshes the panel when Laravel replies.
//      The server only knows "timeout" or "low_score" — it has no idea a
//      decision was made — so it would overwrite the panel with the wrong
//      label a moment later, even if 1 had been fixed alone.
//
// Both now ask TwoFireDecision whether the wrong fire was chosen, and
// that answer wins over anything the server says about the reason.
//
// WHY READ THE FLAG RATHER THAN KEEP A COPY. TwoFireDecision already
// owns wrongFireChosen and already clears it in ResetForReplay. A second
// copy here would need its own reset, and a missed reset would mean the
// NEXT run's timeout still reported a wrong decision — silently, and only
// on replays, which is exactly the kind of bug that survives testing.
//
// NULL IN KITCHEN AND CLASSROOM, so those scenes get the original
// wording with no change at all.
// -------------------------------------------------------

public class ResultsUIManager : MonoBehaviour
{
    [Header("Submitting State (brief, while waiting on Laravel)")]
    public GameObject submittingPanel;
    public TextMeshProUGUI submittingText;

    [Header("Win Panel")]
    public GameObject winPanel;
    public TextMeshProUGUI winTitleText;
    public TextMeshProUGUI winScoreLabelText;
    public TextMeshProUGUI winPercentText;
    public TextMeshProUGUI winTimeText;

    [Tooltip("Optional — shows total penalty seconds taken during the run")]
    public TextMeshProUGUI winPenaltyText;

    [Tooltip("Optional — note line on the win panel. Used for 'passed on " +
             "attempt N' and for the practice-run notice.")]
    public TextMeshProUGUI winNoteText;

    [Tooltip("Optional — the stats row (Score/Rating/Time chips).")]
    public GameObject winStatsRow;

    [Header("Lose Panel")]
    [Tooltip("Optional — the small kicker label above the title " +
             "(e.g. 'TIME EXPIRED' / 'TOO MANY MISTAKES').")]
    public TextMeshProUGUI loseKickerText;
    public GameObject losePanel;
    public TextMeshProUGUI loseTitleText;
    public TextMeshProUGUI loseMessageText;

    [Tooltip("Optional — shows which attempt this was, e.g. 'Attempt 3'. " +
             "Shows 'Practice run' instead when the participant has already " +
             "passed and the run was not recorded.")]
    public TextMeshProUGUI loseAttemptText;

    [Header("Wrong Decision Loss (Office only)")]
    [Tooltip("Kicker shown when the run ended because the player cleared the " +
             "WRONG FIRE first. Overrides the timeout and low-score labels, " +
             "because neither is what actually happened — the clock may still " +
             "have most of its time left.\n\n" +
             "Never shown in Kitchen or Classroom: there is no TwoFireDecision " +
             "in those scenes, so the check that reaches this is always false.")]
    [SerializeField] private string wrongDecisionKicker = "WRONG DECISION";

    [TextArea]
    [Tooltip("Message on the lose panel for a wrong fire choice.\n\n" +
             "Say what they did and what it cost. The player needs to " +
             "understand the ORDER was the mistake, not the technique — they " +
             "performed TPASS correctly, on the wrong target.")]
    [SerializeField]
    private string wrongDecisionMessage =
        "You cleared the far fire first. The fire by the door spread and " +
        "blocked your only way out. Always clear the fire nearest your exit.";

    // Looked up once. Null in Kitchen and Classroom, which is exactly how
    // those scenes keep the original wording.
    private TwoFireDecision twoFireDecision;

    private void Start()
    {
        if (submittingPanel != null) submittingPanel.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);

        twoFireDecision = FindFirstObjectByType<TwoFireDecision>();
    }

    // -------------------------------------------------------
    // Did this run end because the player chose the wrong fire?
    //
    // Asks TwoFireDecision rather than keeping a copy of the answer — see
    // the header note on why a second copy would eventually go stale.
    // -------------------------------------------------------
    private bool LostByWrongDecision()
    {
        return twoFireDecision != null && twoFireDecision.WrongFireChosen;
    }

    // -------------------------------------------------------
    // SUBMITTING — brief "please wait" until the server responds.
    // -------------------------------------------------------
    public void ShowSubmitting()
    {
        HideAllPanels();

        if (submittingPanel != null)
        {
            submittingPanel.SetActive(true);
            if (submittingText != null)
                submittingText.text = "Submitting your result...";
        }
    }

    // -------------------------------------------------------
    // WIN — the run passed
    // Hook to: ResultsSubmitter -> On Saved
    // -------------------------------------------------------
    public void ShowWin(SubmitResultResponse response)
    {
        HideAllPanels();
        if (winPanel == null) return;

        winPanel.SetActive(true);

        if (winStatsRow != null) winStatsRow.SetActive(true);

        if (winTitleText != null)
            winTitleText.text = "Fire Contained!";

        if (winScoreLabelText != null)
            winScoreLabelText.text = response.score_label;

        if (winPercentText != null)
            winPercentText.text = response.percentage_score + "%";

        if (winTimeText != null)
            winTimeText.text = response.time_remaining + "s";

        if (winPenaltyText != null)
        {
            int penalties = SimulationManager.Instance != null
                ? SimulationManager.Instance.TotalPenaltySeconds
                : 0;
            winPenaltyText.text = penalties + "s";
        }

        if (winNoteText != null)
        {
            if (response.already_recorded)
            {
                // A practice run. The score above is real — it just was
                // not saved, because their passing attempt is already on
                // record. Without this line a 95% practice run looks like
                // a new result while the certificate still says 70%.
                winNoteText.gameObject.SetActive(true);
                winNoteText.text = "Practice run — your recorded result for this event is unchanged.";
            }
            else if (response.attempt_number > 1)
            {
                // Passing on a later try is worth acknowledging — it took
                // them more than one go and they got there.
                winNoteText.gameObject.SetActive(true);
                winNoteText.text = "Passed on attempt " + response.attempt_number + ".";
            }
            else
            {
                winNoteText.gameObject.SetActive(false);
            }
        }
    }

    // -------------------------------------------------------
    // LOSE — the run did not pass.
    // Hook to: ResultsSubmitter -> On Retry
    //
    // Replaces both ShowLoseRetry and ShowLoseTimerRanOut. The server
    // sees every failure now, so fail_reason distinguishes them:
    //   "timeout"   -> the clock ran out before they finished
    //   "low_score" -> finished in time, too many wrong actions
    //
    // EXCEPT for a wrong fire choice, which the server cannot know about.
    // Unity sends won: false for that run, so Laravel reports it as a
    // timeout — and without the check below this method would overwrite
    // the correct label a second after it appeared.
    // -------------------------------------------------------
    public void ShowLoseResult(SubmitResultResponse response)
    {
        string kicker;
        string message;

        if (LostByWrongDecision())
        {
            // OFFICE ONLY. Takes priority over fail_reason: the clock may
            // still have most of its time on it, so "TIME EXPIRED" would be
            // plainly untrue on screen.
            kicker = wrongDecisionKicker;
            message = wrongDecisionMessage;
        }
        // JsonUtility turns a JSON null into an EMPTY STRING, not null,
        // so compare against "timeout" directly rather than null-checking.
        else if (response.fail_reason == "timeout")
        {
            kicker = "TIME EXPIRED";
            message = "The fire got out of control before you finished. " +
                      "Review what went wrong, then try again.";
        }
        else
        {
            kicker = "TOO MANY MISTAKES";
            message = "You finished, but made too many mistakes. Your score was " +
                      response.percentage_score + "%, below the 50% needed to pass.";
        }

        // Failing a practice run must not read as losing something they
        // already earned — their passing attempt still stands.
        if (response.already_recorded)
        {
            message += " This was a practice run — your recorded result is unchanged.";
        }

        ShowLosePanel(kicker, "Fire Spread!", message);

        if (loseAttemptText != null)
        {
            loseAttemptText.text = response.already_recorded
                ? "Practice run"
                : "Attempt " + response.attempt_number;
        }
    }

    // -------------------------------------------------------
    // LOSE — local fallback, shown the instant the run ends.
    //
    // Wired to SimulationManager's onLose UnityEvent, which fires BEFORE
    // the server responds. So this shows immediately, then ShowLoseResult
    // refreshes it with the real attempt number a moment later.
    //
    // It is also the only thing the player sees if the POST fails, which
    // is exactly when you want a local fallback.
    //
    // THE NAME IS NOW SLIGHTLY WRONG. It handles two local losses, not
    // just the timer. Renaming it would break the Inspector wiring on
    // three scenes' onLose events — a silent break, because a missing
    // method on a UnityEvent just does nothing. Left as it is on purpose;
    // rename it after defense if you want, and re-wire all three.
    // -------------------------------------------------------
    public void ShowLoseTimerRanOut()
    {
        if (LostByWrongDecision())
        {
            ShowLosePanel(
                kicker: wrongDecisionKicker,
                title: "Fire Spread!",
                message: wrongDecisionMessage);
            return;
        }

        ShowLosePanel(
            kicker: "TIME EXPIRED",
            title: "Fire Spread!",
            message: "The fire got out of control before you finished. " +
                     "Review what went wrong, then try again.");
    }

    // -------------------------------------------------------
    // LOSE — NETWORK / UNKNOWN ERROR
    // Hook to: ResultsSubmitter -> On Connection Error / On Unknown Error
    // -------------------------------------------------------
    public void ShowLoseNetworkIssue(string message)
    {
        ShowLosePanel(
            kicker: "COULDN'T SAVE",
            title: "Connection Problem",
            message: message);
    }

    // -------------------------------------------------------
    // Shared setup for all lose states.
    // -------------------------------------------------------
    private void ShowLosePanel(string kicker, string title, string message)
    {
        HideAllPanels();
        if (losePanel == null) return;

        losePanel.SetActive(true);

        if (loseKickerText != null)
            loseKickerText.text = kicker;

        if (loseTitleText != null)
            loseTitleText.text = title;

        if (loseMessageText != null)
            loseMessageText.text = message;

        if (loseAttemptText != null)
            loseAttemptText.text = "";
    }

    // Hides all result panels so only one shows at a time.
    private void HideAllPanels()
    {
        if (submittingPanel != null) submittingPanel.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }
}
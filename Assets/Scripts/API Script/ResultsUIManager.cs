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
// WHY THE TIMEOUT MESSAGE NAMES THE PENALTIES
//
// "The fire got out of control before you finished" is true but useless.
// It reads as "you were too slow", and slowness is almost never what
// actually happened.
//
// The clock loses time two ways: walking, and PENALTIES. A run that
// spends 50 seconds on wrong actions has 40 left for everything else,
// so it hits zero with the player moving at a perfectly reasonable pace.
// They then read a message telling them to hurry up, and hurry up on the
// next attempt — which is the wrong lesson, and usually makes the next
// run worse.
//
// So the message reports the penalty total when there is one. A player
// who reads "mistakes cost you 50 seconds" knows what to fix. One who
// genuinely ran the clock down with no penalties still gets the plain
// version, because for them the original wording was right.
//
// The number comes from SimulationManager.TotalPenaltySeconds — the same
// value already shown on the win panel and already sent to Laravel, so
// there is no second source to drift.
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
//
// -------------------------------------------------------
// THE LOSE NOTE LINE NOW MATCHES THE WIN NOTE
//
// The win panel ends with a note line: "Practice run — your recorded
// result for this event is unchanged", or "Passed on attempt N". The
// lose panel had the same object in the scene (LoseDryRun Text) but it
// never appeared, for three separate reasons:
//
//   1. THE FIELD WAS EMPTY. loseAttemptText was never assigned in the
//      Inspector, so every write to it went nowhere.
//
//   2. NOTHING TURNED IT ON. The win path calls SetActive(true) on its
//      note; the lose path never did. Its GameObject sits inactive in the
//      scene, so even an assigned field would have stayed invisible.
//
//   3. THE PRACTICE TEXT WAS ALSO GLUED ONTO THE MESSAGE. ShowLoseResult
//      appended "This was a practice run..." to the body AND wrote
//      "Practice run" to the note — the same fact twice in one panel, in
//      two different places, with two different wordings.
//
// Fixed here by giving the lose panel the same shape the win panel
// already had: one note line, turned on when it has something to say and
// off when it does not, and the body message left to describe the LOSS
// rather than the recording status.
//
// THE RULES are parallel, the WORDS are not. Each panel owns its own
// note sentences in the Inspector, so either can be reworded without
// touching the other.
//
// The rules they share: the practice notice whenever the run was not
// recorded, the attempt line from the SECOND attempt onward, and nothing
// at all on a first attempt — rather than announcing "Attempt 1" to
// someone who has only just started.
//
// The one thing to watch, now that the sentences are separate: the
// practice notice describes the same fact on both panels. Reword one and
// leave the other and the game explains the same rule two ways, with
// which one you get depending on whether you won.
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

    [Tooltip("Optional — the note line at the bottom of the lose panel. " +
             "DRAG LoseDryRun Text HERE — it is the twin of Win Note Text.\n\n" +
             "Shows Lose Practice Note when the run was not recorded, and " +
             "Lose Attempt Note from the second attempt onward. Hidden by " +
             "itself when there is nothing to say: a first attempt, the " +
             "moment a run ends before the server has replied, or a " +
             "connection failure where no attempt number exists.")]
    public TextMeshProUGUI loseNoteText;

    [Header("Timeout Wording")]
    [TextArea]
    [Tooltip("Shown when the clock hit zero and PENALTIES were the reason — " +
             "{0} is replaced with the penalty total in seconds.\n\n" +
             "This is the common case and the one worth getting right. A " +
             "player who lost 50 seconds to wrong actions was not slow; " +
             "telling them the fire got ahead of them makes them rush the " +
             "next attempt, which usually goes worse.")]
    [SerializeField]
    private string timeoutWithPenaltiesMessage =
        "Time ran out. Wrong actions cost you {0} seconds — that is what " +
        "emptied the clock, not your pace.";

    [TextArea]
    [Tooltip("Shown when the clock hit zero with NO penalties recorded. Here " +
             "the player really did run the time down, so the original " +
             "wording is the honest one.")]
    [SerializeField]
    private string timeoutNoPenaltiesMessage =
        "The fire got out of control before you finished. Review what went " +
        "wrong, then try again.";

    [Header("Note Line Wording")]
    [TextArea]
    [Tooltip("WIN panel, practice run — shown when the participant has " +
             "already passed this event, so nothing was saved.")]
    [SerializeField]
    private string winPracticeNote =
        "Practice run — your recorded result for this event is unchanged.";

    [TextArea]
    [Tooltip("LOSE panel, practice run — same situation, its own wording. " +
             "WRITE WHATEVER YOU WANT HERE.\n\n" +
             "Separate from the win version by request. Worth knowing what " +
             "that costs: these two describe the SAME fact, so if you edit " +
             "one and forget the other, the game will explain the same rule " +
             "two different ways depending on whether the player won. Keep " +
             "them saying the same thing even when the words differ.")]
    [SerializeField]
    private string losePracticeNote =
        "Practice run — your recorded result for this event is unchanged.";

    [Tooltip("Shown on the LOSE panel from the SECOND attempt onward — {0} " +
             "is the attempt number.\n\n" +
             "Mirrors the win panel's 'Passed on attempt N'. Like that one, " +
             "it stays hidden on a first attempt: the player knows it was " +
             "their first go, so the line would only be stating the obvious " +
             "under a message about losing.")]
    [SerializeField]
    private string loseAttemptNote = "Attempt {0}.";

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
    // The timeout message, with the penalty total filled in when there is
    // one.
    //
    // Reads TotalPenaltySeconds live rather than taking it from the server
    // response, so the local fallback panel can say the same thing before
    // Laravel has replied — and so the two can never disagree.
    // -------------------------------------------------------
    private string BuildTimeoutMessage()
    {
        int penalties = SimulationManager.Instance != null
            ? SimulationManager.Instance.TotalPenaltySeconds
            : 0;

        if (penalties <= 0)
            return timeoutNoPenaltiesMessage;

        return string.Format(timeoutWithPenaltiesMessage, penalties);
    }

    // -------------------------------------------------------
    // Writes a note line, or hides it when there is nothing to say.
    //
    // Shared by both panels so they can never drift apart: the same
    // decision, the same wording, one place to change it.
    //
    // HIDING MATTERS AS MUCH AS WRITING. A note left on screen with stale
    // text from the previous run is worse than no note at all — the
    // player reads it as being about THIS run.
    // -------------------------------------------------------
    private void SetNote(TextMeshProUGUI note, string text)
    {
        if (note == null) return;

        if (string.IsNullOrEmpty(text))
        {
            note.gameObject.SetActive(false);
            return;
        }

        note.gameObject.SetActive(true);
        note.text = text;
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

        if (response.already_recorded)
        {
            // A practice run. The score above is real — it just was not
            // saved, because their passing attempt is already on record.
            // Without this line a 95% practice run looks like a new result
            // while the certificate still says 70%.
            SetNote(winNoteText, winPracticeNote);
        }
        else if (response.attempt_number > 1)
        {
            // Passing on a later try is worth acknowledging — it took them
            // more than one go and they got there.
            SetNote(winNoteText, "Passed on attempt " + response.attempt_number + ".");
        }
        else
        {
            SetNote(winNoteText, null);
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
            message = BuildTimeoutMessage();
        }
        else
        {
            kicker = "TOO MANY MISTAKES";
            message = "You finished, but made too many mistakes. Your score was " +
                      response.percentage_score + "%, below the 50% needed to pass.";
        }

        // The practice notice is NO LONGER glued onto this message. It goes
        // on the note line below, exactly where the win panel puts it —
        // otherwise a practice loss states the same fact twice, in two
        // different wordings, in one panel.
        ShowLosePanel(kicker, "Fire Spread!", message);

        // Written AFTER ShowLosePanel, which clears the note. Order matters
        // here: move this above and the note is wiped a frame after it is set.
        //
        // THE SAME THREE CASES AS THE WIN PANEL, in the same order, so the
        // two never disagree about when a note belongs on screen:
        //   practice run   -> the notice, word for word as the win shows it
        //   attempt 2+     -> which attempt this was
        //   first attempt  -> nothing
        if (response.already_recorded)
        {
            SetNote(loseNoteText, losePracticeNote);
        }
        else if (response.attempt_number > 1)
        {
            SetNote(loseNoteText, string.Format(loseAttemptNote, response.attempt_number));
        }
        else
        {
            SetNote(loseNoteText, null);
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
    // NO NOTE LINE HERE, deliberately. The attempt number and the recorded
    // status are both the server's answers, and it has not replied yet.
    // ShowLosePanel leaves the note hidden, and ShowLoseResult fills it in
    // a moment later — so the line appears once, with the truth, instead of
    // guessing and then correcting itself on screen.
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
            message: BuildTimeoutMessage());
    }

    // -------------------------------------------------------
    // LOSE — NETWORK / UNKNOWN ERROR
    // Hook to: ResultsSubmitter -> On Connection Error / On Unknown Error
    //
    // The note stays hidden: nothing was saved, so there is no attempt
    // number to report and no way to know whether it would have counted.
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

        // Hidden rather than blanked. An empty but ACTIVE text object still
        // takes up its layout space, which shifts the button below it by a
        // line — so the panel would visibly jump when ShowLoseResult filled
        // the note in a moment later.
        SetNote(loseNoteText, null);
    }

    // Hides all result panels so only one shows at a time.
    private void HideAllPanels()
    {
        if (submittingPanel != null) submittingPanel.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }
}
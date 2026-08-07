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
//                          (ShowLoseTimerRanOut, from SimulationManager onLose)
//   TOO MANY MISTAKES   -> finished but scored < 50%
//                          (ShowLoseRetry, from ResultsSubmitter onRetry)
//   COULDN'T SAVE       -> connection / unknown error
//                          (ShowLoseNetworkIssue)
//
// Tips were removed from these modals — the full penalty breakdown
// lives in the Performance Results screen (data is stored in the DB).
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

    [Tooltip("Optional — dry-run note line on the win panel.")]
    public TextMeshProUGUI winNoteText;

    [Tooltip("Optional — the stats row (Score/Rating/Time chips). Hidden on a dry run.")]
    public GameObject winStatsRow;

    [Header("Lose Panel")]
    [Tooltip("Optional — the small kicker label above the title " +
             "(e.g. 'TIME EXPIRED' / 'TOO MANY MISTAKES'). Set per lose reason.")]
    public TextMeshProUGUI loseKickerText;
    public GameObject losePanel;
    public TextMeshProUGUI loseTitleText;
    public TextMeshProUGUI loseMessageText;

    private void Start()
    {
        if (submittingPanel != null) submittingPanel.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
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
    // WIN — Laravel confirmed the run was saved (a real pass)
    // Hook to: ResultsSubmitter -> On Saved
    // -------------------------------------------------------
    public void ShowWin(ResultsSuccessResponse response)
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
            winNoteText.gameObject.SetActive(false);
    }

    // -------------------------------------------------------
    // ALREADY RECORDED (replay / dry run) — no score shown
    // Hook to: ResultsSubmitter -> On Duplicate
    // -------------------------------------------------------
    public void ShowAlreadyRecorded(string message)
    {
        HideAllPanels();
        if (winPanel == null) return;

        winPanel.SetActive(true);

        if (winTitleText != null)
            winTitleText.text = "Fire Contained!";

        if (winStatsRow != null) winStatsRow.SetActive(false);

        if (winScoreLabelText != null) winScoreLabelText.text = "";
        if (winPercentText != null) winPercentText.text = "";
        if (winTimeText != null) winTimeText.text = "";
        if (winPenaltyText != null) winPenaltyText.text = "";

        if (winNoteText != null)
        {
            winNoteText.gameObject.SetActive(true);
            winNoteText.text = "You've already completed this event. This was a practice run — your first attempt is the one that's recorded.";
        }
    }

    // -------------------------------------------------------
    // LOSE — TOO MANY MISTAKES (finished but scored < 50%)
    // Hook to: ResultsSubmitter -> On Retry
    // -------------------------------------------------------
    public void ShowLoseRetry(ResultsFailResponse response)
    {
        ShowLosePanel(
            kicker: "TOO MANY MISTAKES",
            title: "Fire Spread!",
            message: "You finished, but made too many mistakes. Your score was " + response.percentage_score + "%, below the 50% needed to pass.");
    }

    // -------------------------------------------------------
    // LOSE — TIME EXPIRED (timer hit zero, didn't finish)
    // Hook to: SimulationManager -> On Lose
    // -------------------------------------------------------
    public void ShowLoseTimerRanOut()
    {
        ShowLosePanel(
            kicker: "TIME EXPIRED",
            title: "Fire Spread!",
            message: "The fire got out of control before you finished. Review what went wrong, then try again.");
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
    }

    // Hides all result panels so only one shows at a time.
    private void HideAllPanels()
    {
        if (submittingPanel != null) submittingPanel.SetActive(false);
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }
}
using UnityEngine;
using TMPro;

// -------------------------------------------------------
// WHAT THIS DOES:
// Listens for the simulation's outcome and shows the right panel
// with the right information. This script doesn't calculate
// anything itself — it just DISPLAYS what SimulationManager and
// ResultsSubmitter already tell it.
//
// Four methods below are meant to be connected to events in the
// Inspector (explained in the setup steps):
//   ShowWin()            <- ResultsSubmitter.onSaved
//   ShowLoseRetry()       <- ResultsSubmitter.onRetry
//   ShowLoseTimerRanOut() <- SimulationManager.onLose
//   ShowLoseNetworkIssue()<- ResultsSubmitter.onConnectionError /
//                            onUnknownError / onDuplicate
// -------------------------------------------------------

public class ResultsUIManager : MonoBehaviour
{
    [Header("Win Panel")]
    public GameObject winPanel;
    public TextMeshProUGUI winTitleText;
    public TextMeshProUGUI winScoreLabelText;
    public TextMeshProUGUI winPercentText;
    public TextMeshProUGUI winTimeText;

    [Header("Lose Panel")]
    public GameObject losePanel;
    public TextMeshProUGUI loseTitleText;
    public TextMeshProUGUI loseMessageText;
    public TextMeshProUGUI loseTipsText;

    private void Start()
    {
        // Both panels start hidden — same idea as display:none in CSS.
        // We only reveal one once we actually know the outcome.
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }

    // ── Called when Laravel confirms the run was saved (a real pass) ──
    // Hook this to: ResultsSubmitter -> On Saved (Dynamic ResultsSuccessResponse)
    public void ShowWin(ResultsSuccessResponse response)
    {
        winPanel.SetActive(true);

        winTitleText.text = "Simulation Passed!";
        winScoreLabelText.text = response.score_label;          // "Excellent" / "Good" / "Passed"
        winPercentText.text = response.percentage_score + "%";  // e.g. "78%"
        winTimeText.text = "Time Remaining: " + response.time_remaining + "s";
    }

    // ── Called when Laravel rejects the run for scoring too low ────────
    // Hook this to: ResultsSubmitter -> On Retry (Dynamic ResultsFailResponse)
    public void ShowLoseRetry(ResultsFailResponse response)
    {
        losePanel.SetActive(true);

        loseTitleText.text = "Simulation Failed";
        loseMessageText.text = "Too many mistakes — score: " + response.percentage_score + "%";
        loseTipsText.text = BuildTipsText();
    }

    // ── Called when the 90s timer ran out (no Laravel involved at all) ──
    // Hook this to: SimulationManager -> On Lose
    public void ShowLoseTimerRanOut()
    {
        losePanel.SetActive(true);

        loseTitleText.text = "Time's Up!";
        loseMessageText.text = "You ran out of time before completing the simulation.";
        loseTipsText.text = BuildTipsText();
    }

    // ── Called for connection errors, duplicates, or unknown issues ────
    // Hook this to: ResultsSubmitter -> On Connection Error / On Unknown
    // Error / On Duplicate (all three send a string message)
    public void ShowLoseNetworkIssue(string message)
    {
        losePanel.SetActive(true);

        loseTitleText.text = "Something Went Wrong";
        loseMessageText.text = message;
        loseTipsText.text = "";
    }

    // ── Builds a readable list of tips from SimulationManager's
    // MissedTips list, one per line ────────────────────────────────────
    private string BuildTipsText()
    {
        if (SimulationManager.Instance == null) return "";

        var tips = SimulationManager.Instance.MissedTips;
        if (tips == null || tips.Count == 0) return "";

        return string.Join("\n", tips);
    }
}
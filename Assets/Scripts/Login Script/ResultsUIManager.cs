using System.Collections.Generic;
using UnityEngine;
using TMPro;

// -------------------------------------------------------
// WHAT THIS DOES:
// Listens for the simulation's outcome and shows the right panel
// with the right information. Displays what SimulationManager and
// ResultsSubmitter already tell it — it calculates nothing itself.
//
// NEW THIS UPDATE:
//  - The LOSE panel now caps its tips too (loseMaxTips, default 4).
//    Before, it showed every mistake, which pushed the rows down
//    over the buttons when a player made many. Since there are only
//    8 steps and duplicate tips are already de-duped, 4 covers
//    almost every real case while keeping the panel tidy.
//  - The WIN panel keeps its own tighter cap (winMaxTips, default 2).
// -------------------------------------------------------

public class ResultsUIManager : MonoBehaviour
{
    [Header("Win Panel")]
    public GameObject winPanel;
    public TextMeshProUGUI winTitleText;
    public TextMeshProUGUI winScoreLabelText;
    public TextMeshProUGUI winPercentText;
    public TextMeshProUGUI winTimeText;

    [Tooltip("Optional — shows total penalty seconds taken during the run")]
    public TextMeshProUGUI winPenaltyText;

    [Header("Win Panel — Tips Section (shown only if they made mistakes)")]
    [Tooltip("The whole 'you passed, but remember' block. Hidden on a clean run.")]
    public GameObject winTipsSection;

    [Tooltip("The container the win tip rows get spawned into.")]
    public Transform winTipsContainer;

    [Tooltip("Most tips to show on the WIN panel, so it stays compact. 2 is a good default.")]
    public int winMaxTips = 2;

    [Header("Lose Panel")]
    public GameObject losePanel;
    public TextMeshProUGUI loseTitleText;
    public TextMeshProUGUI loseMessageText;

    [Header("Lose Panel — Tips Section")]
    [Tooltip("The whole 'What went wrong' block — heading plus row container.")]
    public GameObject loseTipsSection;

    [Tooltip("The container the lose tip rows get spawned into.")]
    public Transform loseTipsContainer;

    [Tooltip("Max tips shown on the LOSE panel, so the rows don't overflow the buttons. 4 is a good default.")]
    public int loseMaxTips = 4;

    [Header("Shared")]
    [Tooltip("The prefab for one tip row — icon plus text, amber left bar. Used by both panels.")]
    public GameObject tipRowPrefab;

    private void Start()
    {
        if (winPanel != null) winPanel.SetActive(false);
        if (losePanel != null) losePanel.SetActive(false);
    }

    // -------------------------------------------------------
    // WIN — Laravel confirmed the run was saved (a real pass)
    // Hook to: ResultsSubmitter -> On Saved (Dynamic ResultsSuccessResponse)
    // -------------------------------------------------------
    public void ShowWin(ResultsSuccessResponse response)
    {
        if (winPanel == null) return;

        winPanel.SetActive(true);

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

        // Show mistakes even on a win, but capped so the panel
        // stays compact.
        BuildTipRows(winTipsSection, winTipsContainer, winMaxTips);
    }

    // -------------------------------------------------------
    // LOSE (score too low) — Laravel rejected the run
    // Hook to: ResultsSubmitter -> On Retry (Dynamic ResultsFailResponse)
    // -------------------------------------------------------
    public void ShowLoseRetry(ResultsFailResponse response)
    {
        ShowLosePanel(
            title: "Fire Spread!",
            message: "Too many mistakes. Your score was " + response.percentage_score + "%, below the 60% needed to pass.",
            showTips: true);
    }

    // -------------------------------------------------------
    // LOSE (timer ran out) — no Laravel involvement at all
    // Hook to: SimulationManager -> On Lose
    // -------------------------------------------------------
    public void ShowLoseTimerRanOut()
    {
        ShowLosePanel(
            title: "Time's Up!",
            message: "The fire got out of control before you finished. Review what went wrong, then try again.",
            showTips: true);
    }

    // -------------------------------------------------------
    // NETWORK ISSUE — connection error, duplicate, or unknown.
    // NOT a player failure, so we hide the tips list. The message
    // explains what happened.
    //
    // Hook to: ResultsSubmitter -> On Connection Error /
    //          On Unknown Error / On Duplicate
    // -------------------------------------------------------
    public void ShowLoseNetworkIssue(string message)
    {
        ShowLosePanel(
            title: "Couldn't Save",
            message: message,
            showTips: false);
    }

    // -------------------------------------------------------
    // Shared setup for all three lose states.
    // -------------------------------------------------------
    private void ShowLosePanel(string title, string message, bool showTips)
    {
        if (losePanel == null) return;

        losePanel.SetActive(true);

        if (loseTitleText != null)
            loseTitleText.text = title;

        if (loseMessageText != null)
            loseMessageText.text = message;

        if (showTips)
            BuildTipRows(loseTipsSection, loseTipsContainer, loseMaxTips);
        else if (loseTipsSection != null)
            loseTipsSection.SetActive(false);
    }

    // -------------------------------------------------------
    // Spawns one row per missed tip into the given container.
    //
    // maxRows caps how many show. 0 means no cap (show all).
    //
    // If there are no tips (a clean run), the whole section hides.
    // -------------------------------------------------------
    private void BuildTipRows(GameObject section, Transform container, int maxRows)
    {
        if (container == null || tipRowPrefab == null)
        {
            Debug.LogWarning("[ResultsUIManager] Tips container or row prefab not assigned.");
            return;
        }

        foreach (Transform child in container)
            Destroy(child.gameObject);

        List<string> tips = SimulationManager.Instance != null
            ? SimulationManager.Instance.MissedTips
            : null;

        if (tips == null || tips.Count == 0)
        {
            if (section != null) section.SetActive(false);
            return;
        }

        if (section != null) section.SetActive(true);

        HashSet<string> alreadyShown = new HashSet<string>();
        int shown = 0;

        foreach (string tip in tips)
        {
            if (maxRows > 0 && shown >= maxRows) break;

            if (string.IsNullOrEmpty(tip)) continue;
            if (!alreadyShown.Add(tip)) continue;

            GameObject row = Instantiate(tipRowPrefab, container);
            row.SetActive(true);

            TextMeshProUGUI rowText = row.GetComponentInChildren<TextMeshProUGUI>();
            if (rowText != null)
                rowText.text = tip;

            shown++;
        }
    }
}
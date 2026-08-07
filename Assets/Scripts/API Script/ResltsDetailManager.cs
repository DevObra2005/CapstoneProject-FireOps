using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// -------------------------------------------------------
// WHAT THIS DOES:
// On ResultsDetailScene. Reads the environment the player tapped
// (from ResultsHandoff.Selected) and fills the detail screen:
//   - Header: environment name
//   - Stats:  score, time left, penalties
//   - Step breakdown: one row per step (reuses the ResultStepRow variant)
//
// WRONG rows now MIRROR the live feedback log: instead of the tapped
// step name, they show the CORRECT hint the player should have done
// (e.g. "Twist the pin!"). We work out which step they were on by
// counting how many steps before it were correct:
//     currentStepNumber = (correct so far) + 1
// then StepNames.Hint(currentStepNumber) gives the command.
//
// CORRECT rows show the friendly step name, as before.
// -------------------------------------------------------

public class ResultsDetailManager : MonoBehaviour
{
    [Header("Header")]
    public TextMeshProUGUI environmentText;

    [Header("Stats Row")]
    public TextMeshProUGUI statScoreText;
    public TextMeshProUGUI statRatingText;
    public TextMeshProUGUI statTimeLeftText;
    public TextMeshProUGUI statPenaltyText;

    [Header("Step Breakdown")]
    [Tooltip("Reuse the ResultStepRow prefab (children: Title, Message, Penalty, Accent).")]
    public GameObject stepRowPrefab;
    [Tooltip("Container the step rows spawn into (should have a Vertical Layout Group).")]
    public Transform stepContainer;

    [Header("Row Colors")]
    public Color correctColor = new Color(0.18f, 0.80f, 0.44f);
    public Color wrongColor = new Color(0.91f, 0.30f, 0.24f);

    [Header("Optional")]
    public GameObject noDataIndicator;

    private void Start()
    {
        EnvironmentResult env = ResultsHandoff.Selected;

        if (env == null)
        {
            Debug.LogWarning("[ResultsDetail] No handoff data. Showing fallback.");
            if (noDataIndicator != null) noDataIndicator.SetActive(true);
            return;
        }

        FillHeader(env);
        FillStats(env);
        BuildStepRows(env);
    }

    private void FillHeader(EnvironmentResult env)
    {
        if (environmentText != null)
            environmentText.text = Capitalize(env.environment);
    }

    private void FillStats(EnvironmentResult env)
    {
        if (statScoreText != null)
            statScoreText.text = env.percentage_score + "%";

        if (statRatingText != null)
            statRatingText.text = env.score_label;

        if (statTimeLeftText != null)
            statTimeLeftText.text = env.time_remaining + "s";

        if (statPenaltyText != null)
            statPenaltyText.text = env.total_penalties + "s";
    }

    private void BuildStepRows(EnvironmentResult env)
    {
        if (stepContainer == null || stepRowPrefab == null)
        {
            Debug.LogWarning("[ResultsDetail] Step container or row prefab not assigned.");
            return;
        }

        foreach (Transform child in stepContainer)
            Destroy(child.gameObject);

        if (env.steps == null) return;

        // Tracks which step the player was ON: it only advances when a
        // step is completed CORRECTLY (same rule as SimulationManager).
        int correctSoFar = 0;

        foreach (ResultStep step in env.steps)
        {
            GameObject row = Instantiate(stepRowPrefab, stepContainer);
            row.SetActive(true);

            Color accent = step.was_correct ? correctColor : wrongColor;

            string message;
            if (step.was_correct)
            {
                // Correct row: show the friendly name of what they did.
                message = StepNames.Friendly(step.step_name);
                correctSoFar++;   // player advanced to the next step
            }
            else
            {
                // Wrong row: show the CORRECT hint for the step they were on.
                // Step number they were on = correctSoFar + 1.
                message = StepNames.Hint(correctSoFar + 1);
            }

            SetChildText(row, "Title", step.was_correct ? "CORRECT" : "WRONG", accent);
            SetChildText(row, "Message", message, Color.white);
            SetChildText(row, "Penalty",
                step.penalty_seconds > 0 ? "-" + step.penalty_seconds + "s" : "", accent);

            Transform accentStrip = row.transform.Find("Accent");
            if (accentStrip != null)
            {
                var img = accentStrip.GetComponent<Image>();
                if (img != null) img.color = accent;
            }

            CanvasGroup cg = row.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 1f;
        }

        StartCoroutine(RefreshLayoutNextFrame());
    }

    private IEnumerator RefreshLayoutNextFrame()
    {
        RectTransform rt = stepContainer as RectTransform;
        if (rt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        yield return null;

        foreach (Transform child in stepContainer)
        {
            Transform msg = child.Find("Message");
            if (msg != null)
            {
                TextMeshProUGUI tmp = msg.GetComponent<TextMeshProUGUI>();
                if (tmp != null) tmp.ForceMeshUpdate();
            }
        }
    }

    private void SetChildText(GameObject row, string childName, string value, Color color)
    {
        Transform child = row.transform.Find(childName);
        if (child == null) return;
        TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
        if (tmp == null) return;

        tmp.text = value;
        tmp.color = color;
        child.gameObject.SetActive(!string.IsNullOrEmpty(value));
    }

    private string Capitalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpper(s[0]) + s.Substring(1);
    }
}
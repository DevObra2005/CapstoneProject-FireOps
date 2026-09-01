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
// WRONG rows MIRROR the live feedback log: instead of the tapped step name,
// they show the CORRECT hint the player should have done. We work out which
// step they were on by counting how many steps before it were correct:
//     currentStepNumber = (correct so far) + 1
// then StepNames.Hint(...) gives the command.
//
// CORRECT rows show the friendly step name.
//
// -------------------------------------------------------
// THE HINT IS COMPUTED HERE, NOT STORED
//
// Worth knowing before debugging this screen: the wrong-row text does NOT
// come from the database. Laravel stores the step name and the penalty; the
// instruction is worked out fresh every time this screen loads.
//
// That means a wrong hint here is a bug in THIS file, not bad data — and it
// also means fixing it retroactively corrects every past attempt, because
// nothing wrong was ever written down.
//
// -------------------------------------------------------
// WHY THE ENVIRONMENT HAS TO BE PASSED TO Hint()
//
// This used to call StepNames.Hint(correctSoFar + 1) — the single-argument
// overload, which only knows the TPASS sequence.
//
// Hint is keyed by STEP NUMBER, and both environments number from 1. Office
// step 3 is "Twist the pin"; Kitchen step 3 is "Cover the flame". So a Kitchen
// attempt rendered every wrong row with Office instructions: a player who
// mis-tapped in a kitchen with no extinguisher in it was told to twist a pin.
//
// Nothing errored. Every integer had an answer, so the lookup returned
// confidently wrong text.
//
// The live feedback log already got this right — SimulationManager passes its
// own useKitchenHints flag. This screen has no manager to ask, so it reads the
// environment off the record it is displaying, which is the same information
// arriving by a different route.
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

    // -------------------------------------------------------
    // Which hint table to read.
    //
    // Case-insensitive on purpose. The value comes from a database column
    // written by Unity's ResultsSubmitter, and that is a free-text Inspector
    // field — one scene saved with "Kitchen" instead of "kitchen" would
    // otherwise silently fall back to Office hints, which is exactly the bug
    // this method exists to prevent.
    // -------------------------------------------------------
    private bool IsKitchen(EnvironmentResult env)
    {
        return env != null &&
               string.Equals(env.environment, "kitchen",
                             System.StringComparison.OrdinalIgnoreCase);
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

        bool isKitchen = IsKitchen(env);

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
                // Friendly() is keyed by ENUM NAME rather than number, and
                // holds both sequences, so it needs no environment flag —
                // "WCTL_Wet" and "TPASS_Twist" can never collide.
                message = StepNames.Friendly(step.step_name);
                correctSoFar++;   // player advanced to the next step
            }
            else
            {
                // Wrong row: show the CORRECT hint for the step they were on.
                // Step number they were on = correctSoFar + 1.
                //
                // isKitchen picks the table. Without it this returned TPASS
                // instructions for every environment — see the header.
                message = StepNames.Hint(correctSoFar + 1, isKitchen);
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
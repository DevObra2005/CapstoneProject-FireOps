using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using TMPro;

// -------------------------------------------------------
// WHAT THIS DOES:
// On PerformanceResultsScene. Fetches the participant's results for
// the CURRENT event (from getMyResults) and fills the three fixed
// environment cards (Office / Kitchen / Classroom).
//
// - Completed environment  → ScorePercentage shows "100%", ScoreLabel
//                            shows the label; card is tappable and opens
//                            ResultsDetailScene with that data.
// - Not completed          → ScorePercentage shows "Locked", ScoreLabel
//                            shows "Not completed yet"; card is greyed
//                            and not tappable.
//
// No separate "locked" text objects — we reuse the same two score texts
// to show either the score or the locked message.
// -------------------------------------------------------

[System.Serializable]
public class EnvironmentCardUI
{
    [Tooltip("Which environment this card is for — must match the API value " +
             "(lowercase): office / kitchen / classroom.")]
    public string environmentKey = "office";

    [Tooltip("The card's Button (tapping opens the detail).")]
    public Button cardButton;

    [Tooltip("The CanvasGroup on the card, used to grey it out when locked.")]
    public CanvasGroup canvasGroup;

    [Tooltip("Shows the score % when completed, or 'Locked' when not.")]
    public TextMeshProUGUI scorePercentageText;

    [Tooltip("Shows the label (Excellent/Good/Passed) when completed, " +
             "or 'Not completed yet' when not.")]
    public TextMeshProUGUI scoreLabelText;
}

public class PerformanceResultsManager : MonoBehaviour
{
    [Header("Event Header (optional)")]
    public TextMeshProUGUI eventNameText;

    [Header("Environment Cards (assign all three)")]
    public EnvironmentCardUI officeCard;
    public EnvironmentCardUI kitchenCard;
    public EnvironmentCardUI classroomCard;

    [Header("Locked Text (what not-completed cards show)")]
    public string lockedPercentText = "Locked";
    public string lockedLabelText = "Not completed yet";

    [Header("Text Colors")]
    [Tooltip("Color for the score/label text when the environment IS completed.")]
    public Color completedColor = new Color(0.18f, 0.80f, 0.44f); // green
    [Tooltip("Color for the text when the environment is NOT completed (locked).")]
    public Color lockedColor = new Color(0.60f, 0.64f, 0.69f);    // muted grey

    [Header("States (optional)")]
    public GameObject loadingIndicator;
    public GameObject errorIndicator;

    [Header("Scene")]
    [Tooltip("Exact name of the detail scene to load when a card is tapped.")]
    public string detailSceneName = "ResultsDetailScene";

    private void Start()
    {
        // Start every card locked until data arrives.
        SetCardLocked(officeCard);
        SetCardLocked(kitchenCard);
        SetCardLocked(classroomCard);

        if (errorIndicator != null) errorIndicator.SetActive(false);

        StartCoroutine(FetchResults());
    }

    private IEnumerator FetchResults()
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(true);

        string token = PlayerPrefs.GetString("participant_token", "");
        int eventId = PlayerPrefs.GetInt("participant_event_id", 0);

        string url = ApiConfig.ResultsUrl + "?event_id=" + eventId;

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (loadingIndicator != null) loadingIndicator.SetActive(false);

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError ||
            request.responseCode != 200)
        {
            Debug.LogError("[PerformanceResults] Fetch failed: " + request.error +
                           " (code " + request.responseCode + ")");
            if (errorIndicator != null) errorIndicator.SetActive(true);
            yield break;
        }

#if UNITY_EDITOR
        Debug.Log("[PerformanceResults] Response: " + request.downloadHandler.text);
#endif

        ResultsResponse data = JsonUtility.FromJson<ResultsResponse>(request.downloadHandler.text);

        if (data == null || data.environments == null)
        {
            Debug.LogError("[PerformanceResults] Could not parse response.");
            if (errorIndicator != null) errorIndicator.SetActive(true);
            yield break;
        }

        if (eventNameText != null)
            eventNameText.text = data.event_name;
        ResultsHandoff.EventName = data.event_name;

        foreach (EnvironmentResult env in data.environments)
        {
            EnvironmentCardUI card = CardFor(env.environment);
            if (card != null)
                FillCard(card, env);
        }
    }

    private EnvironmentCardUI CardFor(string environment)
    {
        if (environment == officeCard.environmentKey) return officeCard;
        if (environment == kitchenCard.environmentKey) return kitchenCard;
        if (environment == classroomCard.environmentKey) return classroomCard;
        return null;
    }

    private void FillCard(EnvironmentCardUI card, EnvironmentResult env)
    {
        if (env.completed)
        {
            // Completed — show real score + label, in the completed color.
            if (card.scorePercentageText != null)
            {
                card.scorePercentageText.text = env.percentage_score + "%";
                card.scorePercentageText.color = completedColor;
            }
            if (card.scoreLabelText != null)
            {
                card.scoreLabelText.text = env.score_label;
                card.scoreLabelText.color = completedColor;
            }

            if (card.canvasGroup != null)
            {
                card.canvasGroup.alpha = 1f;
                card.canvasGroup.interactable = true;
            }

            if (card.cardButton != null)
            {
                card.cardButton.interactable = true;
                card.cardButton.onClick.RemoveAllListeners();
                EnvironmentResult captured = env;
                card.cardButton.onClick.AddListener(() => OpenDetail(captured));
            }
        }
        else
        {
            SetCardLocked(card);
        }
    }

    // Greys out a card and shows the locked message in the score texts.
    private void SetCardLocked(EnvironmentCardUI card)
    {
        if (card == null) return;

        if (card.scorePercentageText != null)
        {
            card.scorePercentageText.text = lockedPercentText;   // "Locked"
            card.scorePercentageText.color = lockedColor;        // muted, not green
        }
        if (card.scoreLabelText != null)
        {
            card.scoreLabelText.text = lockedLabelText;           // "Not completed yet"
            card.scoreLabelText.color = lockedColor;
        }

        if (card.canvasGroup != null)
        {
            card.canvasGroup.alpha = 0.5f;       // greyed
            card.canvasGroup.interactable = false;
        }

        if (card.cardButton != null)
        {
            card.cardButton.interactable = false;
            card.cardButton.onClick.RemoveAllListeners();
        }
    }

    private void OpenDetail(EnvironmentResult env)
    {
        ResultsHandoff.Selected = env;
        SceneManager.LoadScene(detailSceneName);
    }
}
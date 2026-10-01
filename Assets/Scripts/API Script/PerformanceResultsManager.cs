using System;
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
//
// OFFLINE:
// Every successful answer from Laravel is saved on the phone
// (OfflineCache). With no internet, the cards are filled from that saved
// copy instead of all showing "Locked" - which would wrongly tell a
// participant they had completed nothing.
//
// The Offline Notice says so: "Offline - showing your results from
// Oct 1, 9:45 PM." It also counts runs still waiting in the outbox,
// because a run played offline is not in Laravel's answer yet.
//
// The saved copy belongs to the participant who was logged in, so on a
// shared tablet nobody sees someone else's results.
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

    [Header("Offline (optional)")]
    [Tooltip("Small text that explains offline / waiting uploads, e.g. " +
             "'Offline - showing your results from Oct 1, 9:45 PM.' " +
             "Shown only when there is something to say; hidden otherwise.")]
    public TextMeshProUGUI offlineNoticeText;

    [Tooltip("Seconds to wait for Laravel before using the saved copy.")]
    public int requestTimeoutSeconds = 8;

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
        SetNotice(null);

        StartCoroutine(FetchResults());
    }

    private IEnumerator FetchResults()
    {
        if (loadingIndicator != null) loadingIndicator.SetActive(true);

        string token = PlayerPrefs.GetString("participant_token", "");
        int eventId = PlayerPrefs.GetInt("participant_event_id", 0);
        string cacheKey = OfflineCache.KeyFor("results", "event" + eventId);

        string url = ApiConfig.ResultsUrl + "?event_id=" + eventId;

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.SetRequestHeader("Accept", "application/json");
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            if (loadingIndicator != null) loadingIndicator.SetActive(false);

            bool ok = request.result == UnityWebRequest.Result.Success && request.responseCode == 200;

            if (ok)
            {
#if UNITY_EDITOR
                Debug.Log("[PerformanceResults] Response: " + request.downloadHandler.text);
#endif
                string json = request.downloadHandler.text;

                if (Render(json))
                {
                    // Remember this answer for the next time there is no internet.
                    OfflineCache.Save(cacheKey, json);
                    SetNotice(PendingUploadsLine());
                }
                else if (errorIndicator != null)
                {
                    errorIndicator.SetActive(true);
                }
                yield break;
            }

            Debug.LogWarning("[PerformanceResults] Fetch failed: " + request.error +
                             " (code " + request.responseCode + ") - trying the saved copy.");
        }

        // ── NO INTERNET (or the server did not answer): use the saved copy ──
        if (OfflineCache.TryLoad(cacheKey, out string savedJson, out DateTime savedAt) && Render(savedJson))
        {
            string notice = $"Offline - showing your results from {OfflineCache.FriendlyTime(savedAt)}.";
            string pending = PendingUploadsLine();
            if (!string.IsNullOrEmpty(pending)) notice += "\n" + pending;
            SetNotice(notice);
            yield break;
        }

        // Never loaded online on this device - nothing to show.
        if (errorIndicator != null) errorIndicator.SetActive(true);
        string fallback = "Offline - connect to the internet to see your results.";
        string waiting = PendingUploadsLine();
        if (!string.IsNullOrEmpty(waiting)) fallback += "\n" + waiting;
        SetNotice(fallback);
    }

    // Fills the screen from a getMyResults answer. False if it cannot be read.
    private bool Render(string json)
    {
        ResultsResponse data = JsonUtility.FromJson<ResultsResponse>(json);

        if (data == null || data.environments == null)
        {
            Debug.LogError("[PerformanceResults] Could not parse response.");
            return false;
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

        return true;
    }

    // "2 results waiting to upload." - runs played offline are in the outbox,
    // not in Laravel's answer yet, so the player should know they are safe.
    private static string PendingUploadsLine()
    {
        int waiting = OfflineOutbox.Count;
        if (waiting <= 0) return null;
        return waiting == 1 ? "1 result waiting to upload." : $"{waiting} results waiting to upload.";
    }

    private void SetNotice(string text)
    {
        if (offlineNoticeText == null) return;

        bool show = !string.IsNullOrEmpty(text);
        offlineNoticeText.gameObject.SetActive(show);
        if (show) offlineNoticeText.text = text;
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
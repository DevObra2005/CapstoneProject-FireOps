using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

// ── Data Classes ─────────────────────────────────────────────────────
[System.Serializable]
public class EventData
{
    public int id;
    public string name;
    public string date;
}

[System.Serializable]
public class EventListWrapper
{
    public List<EventData> events;
}

// ── Main Script ───────────────────────────────────────────────────────
//
// OFFLINE:
// Every successful events list from Laravel is saved on the phone
// (OfflineCache). LoginManager auto-logins on a saved token, so with no
// internet the app still lands here - and instead of "no events", the
// participant sees the events from the last time they were online, with
// a notice saying so. They can pick one and play; results wait in the
// outbox and upload when the internet returns.
//
// The saved list belongs to the participant who was logged in, so on a
// shared tablet nobody sees someone else's events.
//
// A saved list can include an event staff closed since. That is fine:
// submitResult does not check is_open, so the run still uploads and counts.
public class EventSelectionManager : MonoBehaviour
{
    // -------------------------------------------------------
    // SELECTED EVENT KEYS, AS CONSTANTS.
    //
    // MainMenuSettingsUI reads these back to fill the Event tab, and
    // clears them on sign out. Loose string literals in two files fail
    // silently: GetString on a key that was never written just returns
    // empty, so a label goes blank and nothing tells you why.
    //
    // DAY and MONTH are stored separately on purpose. The settings Event
    // tab reuses the same red date chip as the cards on this screen, and
    // that chip wants "14" and "AUG" as two fields.
    // -------------------------------------------------------
    public const string KEY_EVENT_ID = "participant_event_id";
    public const string KEY_EVENT_NAME = "participant_event_name";
    public const string KEY_EVENT_DAY = "participant_event_day";
    public const string KEY_EVENT_MONTH = "participant_event_month";

    private const string LOGIN_SCENE = "LoginScene";
    private const string MAIN_MENU_SCENE = "MainMenuScene";

    [Header("UI References")]
    public GameObject eventButtonPrefab;  // Style A card prefab
    public Transform contentParent;       // Content object inside Scroll View
    public GameObject noEventsText;       // Shown when API returns no open events

    [Header("Offline (optional)")]
    [Tooltip("Small text explaining the list is a saved copy, e.g. " +
             "'Offline - showing events from Oct 1, 9:45 PM.' " +
             "Shown only offline; hidden otherwise.")]
    public TextMeshProUGUI offlineNoticeText;

    [Tooltip("Seconds to wait for Laravel before using the saved list.")]
    public int requestTimeoutSeconds = 8;

    void Start()
    {
        // Null-guarded. Opening this scene directly in the Editor with an
        // unassigned field used to throw here before any request ran.
        if (noEventsText != null) noEventsText.SetActive(false);
        SetNotice(null);

        StartCoroutine(FetchMyEvents());
    }

    IEnumerator FetchMyEvents()
    {
        // Token key comes from LoginManager, so the screen that writes it
        // and the screen that reads it cannot disagree.
        string token = LoginManager.GetToken();
        string url = ApiConfig.EventsUrl;
        string cacheKey = OfflineCache.KeyFor("events");

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Bearer " + token);
            request.SetRequestHeader("Accept", "application/json");
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            bool noConnection =
                request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError ||
                request.responseCode == 0;

            if (!noConnection)
            {
#if UNITY_EDITOR
                Debug.Log("Events response (" + request.responseCode + "): " + request.downloadHandler.text);
#endif

                // ── 401 = the saved token is no longer valid ──
                //
                // This matters now that LoginManager auto-logins on a saved token.
                // If staff deletes the participant, or the token is revoked, the
                // token still SITS on the device — so the app would skip the login
                // screen, land here, fail, and show "no events" forever with no way
                // back. Clearing the session breaks that loop: next launch shows
                // the login form again.
                if (request.responseCode == 401)
                {
                    Debug.LogWarning("[EventSelection] Token rejected (401). Clearing session.");
                    LoginManager.ClearSession();
                    SceneManager.LoadScene(LOGIN_SCENE);
                    yield break;
                }

                if (request.responseCode == 200)
                {
                    string json = request.downloadHandler.text;

                    // Remember this list for the next time there is no internet.
                    OfflineCache.Save(cacheKey, json);

                    if (!ShowEvents(json)) ShowNoEvents();
                    yield break;
                }

                // Any other answer (500 etc.) - the server is having trouble.
                // Treated like being offline: the saved list is better than nothing.
                Debug.LogError("FetchMyEvents failed: " + request.responseCode + " - trying the saved list.");
            }
            else
            {
                Debug.LogWarning("FetchMyEvents error: " + request.error + " - trying the saved list.");
            }
        }

        // ── NO INTERNET (or the server did not answer): use the saved list ──
        if (OfflineCache.TryLoad(cacheKey, out string savedJson, out DateTime savedAt))
        {
            SetNotice($"Offline - showing events from {OfflineCache.FriendlyTime(savedAt)}.");
            if (!ShowEvents(savedJson)) ShowNoEvents();
            yield break;
        }

        // Never loaded online on this device for this participant.
        SetNotice("Offline - connect to the internet to load your events.");
        ShowNoEvents();
    }

    // Builds one card per event in a getMyEvents answer.
    // Returns false when there are no events to show.
    bool ShowEvents(string json)
    {
        string wrappedJson = "{\"events\":" + json + "}";
        EventListWrapper wrapper = JsonUtility.FromJson<EventListWrapper>(wrappedJson);

        if (wrapper == null || wrapper.events == null || wrapper.events.Count == 0)
            return false;

        foreach (EventData ev in wrapper.events)
        {
            CreateEventCard(ev);
        }
        return true;
    }

    void ShowNoEvents()
    {
        if (noEventsText != null) noEventsText.SetActive(true);
    }

    void SetNotice(string text)
    {
        if (offlineNoticeText == null) return;

        bool show = !string.IsNullOrEmpty(text);
        offlineNoticeText.gameObject.SetActive(show);
        if (show) offlineNoticeText.text = text;
    }

    // ── Creates one Style A card from the prefab template ─────────────
    void CreateEventCard(EventData ev)
    {
        GameObject cardObj = Instantiate(eventButtonPrefab, contentParent);

        // Event name
        SetText(cardObj, "EventNameText", ev.name);

        // Split the date ("2026-08-14") into day ("14") and month ("AUG")
        string day, month;
        ParseDate(ev.date, out day, out month);

        SetText(cardObj, "EventDayText", day);
        SetText(cardObj, "EventMonthText", month);

        Button btn = cardObj.GetComponent<Button>();
        if (btn != null)
        {
            EventData capturedEvent = ev;
            btn.onClick.AddListener(() => OnEventSelected(capturedEvent));
        }
    }

    // Finds a TMP child ANYWHERE under the card (recursive) and sets its text.
    // Uses a recursive search so it finds fields nested inside sub-objects
    // like DateBlock — transform.Find only checks DIRECT children, which is why
    // the date fields (children of DateBlock) weren't being found before.
    void SetText(GameObject root, string childName, string value)
    {
        Transform child = FindDeep(root.transform, childName);
        if (child == null)
        {
            Debug.LogWarning("[EventSelection] Card is missing child: " + childName);
            return;
        }

        TextMeshProUGUI tmp = child.GetComponent<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = value;
    }

    // Recursive search through all descendants for a child by name.
    Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            Transform found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    // Parses a date into day number + short month name (e.g. 29 / JUL).
    // Handles both "2026-08-14" and a full timestamp like
    // "2026-08-14T00:00:00", which some Laravel casts return.
    void ParseDate(string raw, out string day, out string month)
    {
        day = raw;
        month = "";

        if (string.IsNullOrEmpty(raw)) return;

        string datePart = raw;
        int tIndex = raw.IndexOfAny(new char[] { 'T', ' ' });
        if (tIndex > 0) datePart = raw.Substring(0, tIndex);

        System.DateTime parsed;

        bool ok = System.DateTime.TryParseExact(
                      datePart, "yyyy-MM-dd",
                      CultureInfo.InvariantCulture,
                      DateTimeStyles.None,
                      out parsed)
                  || System.DateTime.TryParse(datePart, out parsed);

        if (!ok) return;

        day = parsed.Day.ToString("00");                                        // "29", "01"
        month = parsed.ToString("MMM", CultureInfo.InvariantCulture).ToUpper(); // "JUL"
    }

    void OnEventSelected(EventData ev)
    {
        PlayerPrefs.SetInt(KEY_EVENT_ID, ev.id);
        PlayerPrefs.SetString(KEY_EVENT_NAME, ev.name);

        // Day and month split here so the settings Event tab can reuse the
        // same red date chip as the cards on this screen.
        string day, month;
        ParseDate(ev.date, out day, out month);
        PlayerPrefs.SetString(KEY_EVENT_DAY, day);
        PlayerPrefs.SetString(KEY_EVENT_MONTH, month);

        PlayerPrefs.Save();

#if UNITY_EDITOR
        Debug.Log("Selected event: " + ev.name + " (ID: " + ev.id + ")");
#endif

        SceneManager.LoadScene(MAIN_MENU_SCENE);
    }
}
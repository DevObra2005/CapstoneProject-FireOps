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

    void Start()
    {
        // Null-guarded. Opening this scene directly in the Editor with an
        // unassigned field used to throw here before any request ran.
        if (noEventsText != null) noEventsText.SetActive(false);

        StartCoroutine(FetchMyEvents());
    }

    IEnumerator FetchMyEvents()
    {
        // Token key comes from LoginManager, so the screen that writes it
        // and the screen that reads it cannot disagree.
        string token = LoginManager.GetToken();
        string url = ApiConfig.EventsUrl;

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            Debug.LogError("FetchMyEvents error: " + request.error);
            ShowNoEvents();
            yield break;
        }

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
            string wrappedJson = "{\"events\":" + json + "}";
            EventListWrapper wrapper = JsonUtility.FromJson<EventListWrapper>(wrappedJson);

            if (wrapper == null || wrapper.events == null || wrapper.events.Count == 0)
            {
                ShowNoEvents();
                yield break;
            }

            foreach (EventData ev in wrapper.events)
            {
                CreateEventCard(ev);
            }
        }
        else
        {
            Debug.LogError("FetchMyEvents failed: " + request.responseCode);
            ShowNoEvents();
        }
    }

    void ShowNoEvents()
    {
        if (noEventsText != null) noEventsText.SetActive(true);
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
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
    [Header("UI References")]
    public GameObject eventButtonPrefab;  // Style A card prefab
    public Transform contentParent;       // Content object inside Scroll View
    public GameObject noEventsText;       // Shown when API returns no open events

    void Start()
    {
        noEventsText.SetActive(false);
        StartCoroutine(FetchMyEvents());
    }

    IEnumerator FetchMyEvents()
    {
        string token = PlayerPrefs.GetString("participant_token", "");
        string url = ApiConfig.EventsUrl;

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.SetRequestHeader("Accept", "application/json");

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            Debug.LogError("FetchMyEvents error: " + request.error);
            noEventsText.SetActive(true);
            yield break;
        }

#if UNITY_EDITOR
        Debug.Log("Events response (" + request.responseCode + "): " + request.downloadHandler.text);
#endif

        if (request.responseCode == 200)
        {
            string json = request.downloadHandler.text;
            string wrappedJson = "{\"events\":" + json + "}";
            EventListWrapper wrapper = JsonUtility.FromJson<EventListWrapper>(wrappedJson);

            if (wrapper.events == null || wrapper.events.Count == 0)
            {
                noEventsText.SetActive(true);
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
            noEventsText.SetActive(true);
        }
    }

    // ── Creates one Style A card from the prefab template ─────────────
    void CreateEventCard(EventData ev)
    {
        GameObject cardObj = Instantiate(eventButtonPrefab, contentParent);

        // Event name
        SetText(cardObj, "EventNameText", ev.name);

        // Split the date ("2026-08-14") into day ("14") and month ("Aug")
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
    // Uses GetComponentsInChildren so it finds fields nested inside sub-objects
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

    // Parses "yyyy-MM-dd" into day number + short month name (e.g. 29 / Jul).
    void ParseDate(string raw, out string day, out string month)
    {
        day = raw;
        month = "";

        if (string.IsNullOrEmpty(raw)) return;

        // Some APIs send a full timestamp ("2026-08-14T00:00:00").
        string datePart = raw;
        int tIndex = raw.IndexOfAny(new char[] { 'T', ' ' });
        if (tIndex > 0) datePart = raw.Substring(0, tIndex);

        if (System.DateTime.TryParseExact(
                datePart, "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out System.DateTime parsed))
        {
            day = parsed.Day.ToString("00");   // "29", "01"
            month = parsed.ToString("MMM", CultureInfo.InvariantCulture).ToUpper(); // "JUL"
        }
        else if (System.DateTime.TryParse(datePart, out parsed))
        {
            day = parsed.Day.ToString("00");
            month = parsed.ToString("MMM", CultureInfo.InvariantCulture).ToUpper();
        }
    }

    void OnEventSelected(EventData ev)
    {
        PlayerPrefs.SetInt("participant_event_id", ev.id);
        PlayerPrefs.SetString("participant_event_name", ev.name);
        PlayerPrefs.Save();

#if UNITY_EDITOR
        Debug.Log("Selected event: " + ev.name + " (ID: " + ev.id + ")");
#endif

        SceneManager.LoadScene("MainMenuScene");
    }
}
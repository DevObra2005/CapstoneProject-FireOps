using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Networking;
using UnityEngine.UI;
using TMPro;

// ── Data Classes ─────────────────────────────────────────────────────
// These mirror the JSON structure returned by GET /api/participant/events
// [System.Serializable] = tells Unity these can be converted to/from JSON
// Same pattern used in LoginManager.cs for LoginResponse, ParticipantData

[System.Serializable]
public class EventData
{
    public int id;
    public string name;
    public string date;
}

// JsonUtility cannot parse JSON arrays directly — it needs a root object
// So we wrap the array in a class before parsing
// From API:  [{"id":1,"name":"..."},{"id":2,"name":"..."}]
// We wrap:   {"events":[{"id":1,"name":"..."},{"id":2,"name":"..."}]}
[System.Serializable]
public class EventListWrapper
{
    public List<EventData> events;
}

// ── Main Script ───────────────────────────────────────────────────────
public class EventSelectionManager : MonoBehaviour
{
    [Header("UI References")]
    public GameObject eventButtonPrefab;  // Button template from Assets/Prefabs
    public Transform contentParent;       // Content object inside Scroll View
    public GameObject noEventsText;       // Shown when API returns no open events

    // ── Unity calls this automatically when the scene loads ───────────
    // Same idea as useEffect(() => { fetchEvents() }, []) in React
    void Start()
    {
        // Hide no events text by default
        // SetActive(false) = like display:none in CSS
        noEventsText.SetActive(false);

        // Start fetching events from the API
        // StartCoroutine = starts an async function without freezing the game
        StartCoroutine(FetchMyEvents());
    }

    // ── API Call ──────────────────────────────────────────────────────
    // IEnumerator = this is a Coroutine — Unity's version of async/await
    // It can pause mid-execution (yield return) while waiting for the server
    // The rest of the game keeps running normally during the wait
    // Same pattern as LoginCoroutine in LoginManager.cs
    IEnumerator FetchMyEvents()
    {
        // Get the participant's token saved by LoginManager.cs
        // PlayerPrefs.GetString = like localStorage.getItem() in JavaScript
        // Second argument = default value if key doesn't exist
        string token = PlayerPrefs.GetString("participant_token", "");

        // Build the full endpoint URL — reads directly from ApiConfig,
        // which already includes the "/api" segment. Previously this
        // concatenated "/participant/events" onto a BASE_URL that no
        // longer included "/api", which sent requests to the website
        // instead of the API. ApiConfig.EventsUrl is always correct.
        string url = ApiConfig.EventsUrl;

        // Create a GET request
        // UnityWebRequest.Get = Unity's version of axios.get()
        UnityWebRequest request = UnityWebRequest.Get(url);

        // Attach the Sanctum token to the request header
        // This is how Laravel knows WHO is making the request
        // Equivalent to: axios.defaults.headers.Authorization = `Bearer ${token}`
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.SetRequestHeader("Accept", "application/json");

        // PAUSE HERE and wait for the server to respond
        // yield return = "pause this coroutine until this finishes"
        // Identical pattern to LoginManager.cs
        yield return request.SendWebRequest();

        // ── Check for network-level failure ──
        // Matches the same error check in LoginManager.cs
        // Fires if Laravel isn't running or there's no internet
        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            Debug.LogError("FetchMyEvents error: " + request.error);
            noEventsText.SetActive(true);
            yield break; // stop the coroutine — like a return statement
        }

#if UNITY_EDITOR
        // Only runs in Unity Editor — stripped from final APK
        // Lets you inspect the raw response during development
        Debug.Log("Events response (" + request.responseCode + "): " + request.downloadHandler.text);
#endif

        // ── HTTP 200 = success ────────────────────────────────────────
        if (request.responseCode == 200)
        {
            // Get the raw JSON string from the response
            string json = request.downloadHandler.text;

            // Wrap the JSON array so JsonUtility can parse it
            // JsonUtility.FromJson = like JSON.parse() in JavaScript
            string wrappedJson = "{\"events\":" + json + "}";
            EventListWrapper wrapper = JsonUtility.FromJson<EventListWrapper>(wrappedJson);

            // If no open events found — show the no events message
            if (wrapper.events == null || wrapper.events.Count == 0)
            {
                noEventsText.SetActive(true);
                yield break;
            }

            // Loop through each event and create a button for it
            // Like .map() in React — one button rendered per event
            foreach (EventData ev in wrapper.events)
            {
                CreateEventButton(ev);
            }
        }
        else
        {
            // Server responded but returned an error
            Debug.LogError("FetchMyEvents failed: " + request.responseCode);
            noEventsText.SetActive(true);
        }
    }

    // ── Creates one button from the prefab template ───────────────────
    // Called once per event returned by the API
    // Instantiate = clones the prefab — like React rendering a component
    void CreateEventButton(EventData ev)
    {
        // Clone the prefab and place it inside Content (the scroll view container)
        // Instantiate(template, parent) = create a copy as a child of parent
        GameObject buttonObj = Instantiate(eventButtonPrefab, contentParent);

        // Find the EventNameText child inside the cloned button and set its text
        // transform.Find("name") = like document.querySelector in JavaScript
        TextMeshProUGUI nameText = buttonObj
            .transform.Find("EventNameText")
            .GetComponent<TextMeshProUGUI>();
        nameText.text = ev.name;

        // Find the EventDateText child and set the date
        TextMeshProUGUI dateText = buttonObj
            .transform.Find("EventDateText")
            .GetComponent<TextMeshProUGUI>();
        dateText.text = ev.date;

        // Get the Button component and add a click listener
        // AddListener = Unity's version of onClick in React
        Button btn = buttonObj.GetComponent<Button>();

        // IMPORTANT: copy ev to a local variable before using it in the lambda
        // In C# foreach loops, lambdas capture the variable by REFERENCE
        // Without this copy, every button would fire with the LAST event in the loop
        // This is a common C# gotcha — the local copy freezes the value
        EventData capturedEvent = ev;
        btn.onClick.AddListener(() => OnEventSelected(capturedEvent));
    }

    // ── Called when participant taps an event button ───────────────────
    void OnEventSelected(EventData ev)
    {
        // Save the chosen event to PlayerPrefs
        // PlayerPrefs = Unity's localStorage — persists across scenes
        // SimulationManager.cs will read these values later
        PlayerPrefs.SetInt("participant_event_id", ev.id);
        PlayerPrefs.SetString("participant_event_name", ev.name);
        PlayerPrefs.Save(); // flush to disk immediately — same as LoginManager.cs

#if UNITY_EDITOR
        Debug.Log("Selected event: " + ev.name + " (ID: " + ev.id + ")");
#endif

        // Navigate to MainMenuScene
        // SceneManager.LoadScene = like navigate() in React Router
        SceneManager.LoadScene("MainMenuScene");
    }
}
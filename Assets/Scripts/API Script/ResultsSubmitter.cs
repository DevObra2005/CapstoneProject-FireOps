using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Events;

// Tiny helper class just to "peek" at the response and check the
// "saved" field before deciding which full class to parse it into.
[System.Serializable]
internal class SavedFlagPeek
{
    public bool saved;
}

// ── WHY THESE EXIST ───────────────────────────────────────────────
// Unity's Inspector can only let you drag a piece of data (like the
// score) into another script's method if the event is declared as
// its OWN named class — not just a raw generic UnityEvent<T>. This
// is a Unity-specific quirk, not a C# rule. Think of it like this:
// UnityEvent<ResultsSuccessResponse> is the "shape", but Unity needs
// a concrete, named box to actually show it in the Inspector.
[System.Serializable]
public class SavedResultEvent : UnityEvent<ResultsSuccessResponse> { }

[System.Serializable]
public class RetryResultEvent : UnityEvent<ResultsFailResponse> { }

[System.Serializable]
public class StringResultEvent : UnityEvent<string> { }

public class ResultsSubmitter : MonoBehaviour
{
    [Header("API")]
    // Now reads from ApiConfig.cs instead of being hardcoded here.
    // Update the IP in ONE place (ApiConfig.cs) when your network changes.

    [Header("Environment")]
    [Tooltip("Which scene this is — must match what Laravel expects, e.g. 'office'")]
    public string environment = "office";

    [Header("Events — hook these up in the Inspector")]
    public SavedResultEvent onSaved;         // pass -> saved successfully
    public RetryResultEvent onRetry;         // fail Type B -> score < 50%
    public StringResultEvent onDuplicate;    // already recorded
    public StringResultEvent onConnectionError; // Laravel unreachable
    public StringResultEvent onUnknownError;    // anything unexpected

    // ── Called by SimulationManager when the player finishes ─────────
    // This is the ONLY method the rest of your game needs to know about.
    public void Submit(int phase2Score, int totalPenalties, List<StepResult> steps)
    {
        StartCoroutine(SubmitCoroutine(phase2Score, totalPenalties, steps));
    }

    private IEnumerator SubmitCoroutine(int phase2Score, int totalPenalties, List<StepResult> steps)
    {
        // ── Read what we need out of PlayerPrefs ─────────────────────
        // Same idea as reading a token out of localStorage before an axios call.
        string token = PlayerPrefs.GetString("participant_token", "");
        int eventId = PlayerPrefs.GetInt("participant_event_id", 0);

        if (string.IsNullOrEmpty(token))
        {
            onUnknownError?.Invoke("No participant_token found — is the player logged in?");
            yield break;
        }

        // ── Build the request body ────────────────────────────────────
        ResultsPayload payload = new ResultsPayload
        {
            event_id = eventId,
            environment = environment,
            phase2_score = phase2Score,
            total_penalties = totalPenalties,
            phase2_passed = true, // per spec: Unity only ever submits when true
            steps = steps
        };

        string jsonBody = JsonUtility.ToJson(payload);

#if UNITY_EDITOR
        Debug.Log("Submitting results: " + jsonBody);
#endif

        // ── Set up the POST, same shape as LoginManager's request ────
        UnityWebRequest request = new UnityWebRequest(ApiConfig.ResultsUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");

        // THE NEW PART vs. LoginManager: attach the Bearer token.
        // This is what tells Laravel/Sanctum "this request comes from
        // an authenticated participant" — same as an Authorization
        // header you'd set in Postman or axios defaults.
        request.SetRequestHeader("Authorization", "Bearer " + token);

        yield return request.SendWebRequest();

        // ── Network-level failure (Laravel not running, no internet) ──
        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            onConnectionError?.Invoke("Cannot connect to server. Results not saved yet — will need to retry.");
            yield break;
        }

#if UNITY_EDITOR
        Debug.Log("Results response (" + request.responseCode + "): " + request.downloadHandler.text);
#endif

        string responseText = request.downloadHandler.text;

        // ── Peek at "saved" first to decide how to parse the rest ─────
        SavedFlagPeek peek = JsonUtility.FromJson<SavedFlagPeek>(responseText);

        if (peek.saved)
        {
            // PASS — parse the full success shape
            ResultsSuccessResponse success = JsonUtility.FromJson<ResultsSuccessResponse>(responseText);
            onSaved?.Invoke(success);
        }
        else
        {
            // Not saved — could be Fail Type B (retry) or a duplicate.
            ResultsFailResponse fail = JsonUtility.FromJson<ResultsFailResponse>(responseText);

            // Duplicate responses don't include "retry" in your spec,
            // so we use the message text to tell the two apart.
            if (fail.message != null && fail.message.ToLower().Contains("already recorded"))
            {
                onDuplicate?.Invoke(fail.message);
            }
            else
            {
                onRetry?.Invoke(fail);
            }
        }
    }
}
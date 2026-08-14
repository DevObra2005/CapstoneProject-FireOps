using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Events;

// ── WHY THESE EXIST ───────────────────────────────────────────────
// Unity's Inspector can only show a UnityEvent that carries data if
// the event is declared as its OWN named class — not a raw generic.
// A Unity quirk, not a C# rule.
//
// Both now carry SubmitResultResponse. The backend returns ONE shape
// for every outcome, so there is no longer a separate fail class.
[System.Serializable]
public class SavedResultEvent : UnityEvent<SubmitResultResponse> { }

[System.Serializable]
public class RetryResultEvent : UnityEvent<SubmitResultResponse> { }

[System.Serializable]
public class StringResultEvent : UnityEvent<string> { }

public class ResultsSubmitter : MonoBehaviour
{
    [Header("Environment")]
    [Tooltip("Which scene this is — must match what Laravel expects, e.g. 'office'")]
    public string environment = "office";

    [Header("Events — hook these up in the Inspector")]
    [Tooltip("The run PASSED. Show the Win screen.")]
    public SavedResultEvent onSaved;

    [Tooltip("The run FAILED — timeout or score below 50%. Show the Lose " +
             "screen. The attempt is recorded unless the participant had " +
             "already passed this environment.")]
    public RetryResultEvent onRetry;

    [Tooltip("Laravel unreachable. The attempt was NOT recorded.")]
    public StringResultEvent onConnectionError;

    [Tooltip("Anything unexpected — validation errors, bad token, 500s.")]
    public StringResultEvent onUnknownError;

    // ── Called by SimulationManager when the run ends ─────────────────
    //
    // NOTE THE PARAMETER. This used to be called only on a win, and
    // hardcoded phase2_passed = true. Every attempt is submitted now, so
    // the caller has to say which kind it was:
    //
    //   passed = true   → finished all steps with time left
    //   passed = false  → the timer hit zero
    //
    // A low-score failure still comes through as passed = true here —
    // the player DID beat the clock. Laravel works out the score and
    // sends back passed = false. That decision is the server's, not ours.
    public void Submit(int phase2Score, int totalPenalties, List<StepResult> steps, bool passed)
    {
        StartCoroutine(SubmitCoroutine(phase2Score, totalPenalties, steps, passed));
    }

    private IEnumerator SubmitCoroutine(int phase2Score, int totalPenalties,
                                        List<StepResult> steps, bool passed)
    {
        string token = PlayerPrefs.GetString("participant_token", "");
        int eventId = PlayerPrefs.GetInt("participant_event_id", 0);

        if (string.IsNullOrEmpty(token))
        {
            onUnknownError?.Invoke("No participant_token found — is the player logged in?");
            yield break;
        }

        // Laravel validates steps as required|array. An empty list is fine,
        // but a null one serialises to no key at all and fails validation
        // with a 422 — which would look like "attempts are not saving".
        if (steps == null) steps = new List<StepResult>();

        ResultsPayload payload = new ResultsPayload
        {
            event_id = eventId,
            environment = environment,
            phase2_score = phase2Score,
            total_penalties = totalPenalties,
            phase2_passed = passed,   // was hardcoded true — now real
            steps = steps
        };

        string jsonBody = JsonUtility.ToJson(payload);

#if UNITY_EDITOR
        Debug.Log("Submitting results: " + jsonBody);
#endif

        UnityWebRequest request = new UnityWebRequest(ApiConfig.ResultsUrl, "POST");
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonBody);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Accept", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + token);

        yield return request.SendWebRequest();

        if (request.result == UnityWebRequest.Result.ConnectionError ||
            request.result == UnityWebRequest.Result.DataProcessingError)
        {
            onConnectionError?.Invoke("Cannot connect to server. Results not saved yet — will need to retry.");
            yield break;
        }

        string responseText = request.downloadHandler.text;

#if UNITY_EDITOR
        Debug.Log("Results response (" + request.responseCode + "): " + responseText);
#endif

        // A recorded attempt returns 201 Created. A PRACTICE RUN — one
        // played after the participant already passed this environment —
        // returns 200 OK with already_recorded true and nothing written.
        // Both are valid outcomes, so both must pass this gate.
        //
        // Anything else is a real error: a 422 validation failure, an
        // expired token, a 500. Without this check a bad response would
        // quietly deserialise into an all-defaults object and read as a
        // failed run rather than a broken request.
        if (request.responseCode != 201 && request.responseCode != 200)
        {
            onUnknownError?.Invoke("Server returned " + request.responseCode + ": " + responseText);
            yield break;
        }

        // ── ONE SHAPE, ONE PARSE ──────────────────────────────────────
        // The old SavedFlagPeek trick is gone. It existed because the
        // server used to send two different JSON shapes and we had to
        // sniff 'saved' before choosing which class to parse into.
        //
        // Now every response is the same shape. 'saved' tells you whether
        // a row was written; 'passed' is the actual verdict, and it is
        // the ONLY thing that should pick Win vs Lose.
        SubmitResultResponse result = JsonUtility.FromJson<SubmitResultResponse>(responseText);

        if (result == null)
        {
            onUnknownError?.Invoke("Could not parse server response.");
            yield break;
        }

#if UNITY_EDITOR
        Debug.Log($"[ResultsSubmitter] Attempt #{result.attempt_number} — " +
                  $"passed: {result.passed}, score: {result.percentage_score}%, " +
                  $"fail_reason: '{result.fail_reason}', " +
                  $"recorded: {!result.already_recorded}");
#endif

        // Win or Lose reflects how they actually PLAYED, even on a
        // practice run. Whether it counted is a separate matter, and the
        // UI says so via already_recorded — a player who scores 91% on
        // practice should still see the Win screen.
        if (result.passed)
            onSaved?.Invoke(result);
        else
            onRetry?.Invoke(result);
    }
}
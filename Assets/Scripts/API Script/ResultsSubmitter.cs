using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
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

// -------------------------------------------------------
// OFFLINE RESULTS — HOW A RUN IS SENT NOW
//
// Every finished run goes THROUGH THE OUTBOX, online or not:
//
//   1. Build the payload, with a unique participant_attempt_id (the
//      "order number") and played_at (when it finished, UTC).
//   2. Save it in OfflineOutbox — a file on the phone.
//   3. Ask OutboxUploader to send everything waiting, oldest first.
//   4. Wait for the uploader to report THIS run:
//        Laravel answered  -> onSaved (Win) / onRetry (Lose), as before
//        no internet       -> onSavedOffline - the run stays on the phone
//                             and uploads by itself when the internet returns
//
// WHY ALWAYS THROUGH THE OUTBOX, EVEN ONLINE:
// Order. If an older offline run is still waiting, it must reach Laravel
// BEFORE this one, because Laravel numbers attempts in arrival order.
// Online, the extra step takes milliseconds - the player sees no change.
//
// THE RUN IS SAVED UNDER THE TOKEN OF WHOEVER PLAYED IT, so on a shared
// tablet it still uploads as them after someone else logs in.
// -------------------------------------------------------
public class ResultsSubmitter : MonoBehaviour
{
    [Header("Environment")]
    [Tooltip("Which scene this is — must match what Laravel expects, e.g. 'office'")]
    public string environment = "office";

    [Header("Events — hook these up in the Inspector")]
    [Tooltip("The run PASSED. Show the Win screen.")]
    public SavedResultEvent onSaved;

    [Tooltip("The run FAILED — timeout, wrong decision, or score below 50%. " +
             "Show the Lose screen. The attempt is recorded unless the " +
             "participant had already passed this environment.")]
    public RetryResultEvent onRetry;

    [Tooltip("NO INTERNET — the run was SAVED ON THE PHONE and will upload by " +
             "itself when the connection returns. Show the 'Saved! Your score " +
             "will appear once you're back online' panel.\n\n" +
             "Leave EMPTY and On Connection Error is used instead, exactly as " +
             "before this event existed.")]
    public StringResultEvent onSavedOffline;

    [Tooltip("FALLBACK for no internet, used only when On Saved Offline has " +
             "nothing connected. The run is still saved on the phone either way.")]
    public StringResultEvent onConnectionError;

    [Tooltip("Anything unexpected — validation errors, bad token, 500s.")]
    public StringResultEvent onUnknownError;

    [Header("Waiting")]
    [Tooltip("Longest the result screen waits for this run's answer before " +
             "treating it as saved offline. Safety net only - the uploader " +
             "normally answers well before this.")]
    public float maxWaitSeconds = 45f;

    // ── Called by SimulationManager when the run ends ─────────────────
    //
    // NOTE THE PARAMETERS. This used to be called only on a win, and
    // hardcoded phase2_passed = true. Every attempt is submitted now, so
    // the caller has to say which kind it was:
    //
    //   passed = true   → finished all steps with time left
    //   passed = false  → the run ended in a loss
    //
    // A low-score failure still comes through as passed = true here —
    // the player DID beat the clock. Laravel works out the score and
    // sends back passed = false. That decision is the server's, not ours.
    //
    // ── WHY failReason WAS ADDED ──────────────────────────────────────
    //
    // Laravel used to INFER the reason from what it could see: not
    // passed, no time left, so it wrote "timeout". That inference was
    // right while a timeout was the only loss Unity could detect.
    //
    // It stopped being right when the Office decision scenario started
    // ending runs early. A player who clears the wrong fire loses with
    // fifty seconds still on the clock — Unity's own lose panel says
    // WRONG DECISION, and the admin panel said "Ran out of time" for the
    // same attempt. Two screens describing one run differently, and the
    // one staff read was the wrong one.
    //
    // The server cannot work this out. Only Unity knows a decision was
    // made, so only Unity can say so.
    //
    // EMPTY IS THE SAFE DEFAULT. Leave it out and Laravel falls back to
    // exactly the inference it does today, so Kitchen, Classroom and
    // every existing record keep working with no change.
    public void Submit(int phase2Score, int totalPenalties, List<StepResult> steps,
                       bool passed, string failReason = null)
    {
        StartCoroutine(SubmitCoroutine(phase2Score, totalPenalties, steps, passed, failReason));
    }

    private IEnumerator SubmitCoroutine(int phase2Score, int totalPenalties,
                                        List<StepResult> steps, bool passed,
                                        string failReason)
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

        // ── 1. BUILD THE PAYLOAD ─────────────────────────────────────
        // The order number and play time are created ONCE, here. The outbox
        // stores the finished JSON, so every retry sends exactly this.
        string attemptId = Guid.NewGuid().ToString();
        string playedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        ResultsPayload payload = new ResultsPayload
        {
            event_id = eventId,
            environment = environment,
            phase2_score = phase2Score,
            total_penalties = totalPenalties,
            phase2_passed = passed,   // was hardcoded true — now real

            // JsonUtility writes a null string as "" rather than omitting
            // the key, so Laravel sees an empty string when there is no
            // reason to send. Its validation must accept that as "not
            // given" — see the note above on falling back to inference.
            fail_reason = failReason,

            participant_attempt_id = attemptId,
            played_at = playedAt,

            steps = steps
        };

        string jsonBody = JsonUtility.ToJson(payload);

#if UNITY_EDITOR
        Debug.Log("Submitting results: " + jsonBody);
#endif

        // ── 2. SAVE IT ON THE PHONE FIRST ────────────────────────────
        // From this line on, the run cannot be lost: even if the app closes
        // right now, it is in the outbox and uploads on the next start.
        OfflineOutbox.Add(new OutboxEntry
        {
            attemptId = attemptId,
            token = token,
            environment = environment,
            payloadJson = jsonBody,
            createdAtUtc = playedAt,
            tries = 0,
            lastError = ""
        });

        OutboxUploader uploader = OutboxUploader.Instance;
        if (uploader == null)
        {
            // Should never happen - the uploader creates itself at startup.
            // The run is safe in the outbox and uploads on the next start.
            Debug.LogWarning("[ResultsSubmitter] No OutboxUploader running - result kept on the phone.");
            ReportSavedOffline();
            yield break;
        }

        // ── 3 + 4. SEND, AND WAIT FOR THIS RUN'S ANSWER ──────────────
        bool gotAnswer = false;
        bool uploaderIdle = false;
        UploadOutcome outcome = UploadOutcome.Offline;
        string responseText = "";

        Action<OutboxEntry, UploadOutcome, string> onEntry = (entry, o, text) =>
        {
            if (entry.attemptId != attemptId) return;   // someone else's run
            gotAnswer = true;
            outcome = o;
            responseText = text;
        };
        Action onIdle = () => uploaderIdle = true;

        OutboxUploader.EntryProcessed += onEntry;
        OutboxUploader.FlushFinished += onIdle;

        try
        {
            uploader.RequestFlush();

            float waitUntil = Time.unscaledTime + maxWaitSeconds;

            // Stop waiting when: this run got an answer, OR the uploader went
            // idle without reaching it (an older run hit "no internet" first),
            // OR the safety time ran out.
            while (!gotAnswer && !uploaderIdle && Time.unscaledTime < waitUntil)
                yield return null;
        }
        finally
        {
            OutboxUploader.EntryProcessed -= onEntry;
            OutboxUploader.FlushFinished -= onIdle;
        }

        if (!gotAnswer)
        {
            ReportSavedOffline();
            yield break;
        }

        switch (outcome)
        {
            case UploadOutcome.Uploaded:
                HandleServerAnswer(responseText, failReason);
                break;

            case UploadOutcome.Offline:
            case UploadOutcome.ServerError:
                // Still in the outbox - uploads by itself later.
                ReportSavedOffline();
                break;

            case UploadOutcome.AuthFailed:
                onUnknownError?.Invoke("Login not accepted by the server. The result is kept on this device. " + responseText);
                break;

            case UploadOutcome.Rejected:
                onUnknownError?.Invoke("Server rejected the result (422): " + responseText);
                break;
        }
    }

    // -------------------------------------------------------
    // LARAVEL ANSWERED - same handling as before the outbox existed.
    // -------------------------------------------------------
    private void HandleServerAnswer(string responseText, string failReason)
    {
#if UNITY_EDITOR
        Debug.Log("Results response: " + responseText);
#endif

        // ── ONE SHAPE, ONE PARSE ──────────────────────────────────────
        // Every response is the same shape. 'saved' tells you whether a
        // row was written; 'passed' is the actual verdict, and it is the
        // ONLY thing that should pick Win vs Lose.
        SubmitResultResponse result = JsonUtility.FromJson<SubmitResultResponse>(responseText);

        if (result == null)
        {
            onUnknownError?.Invoke("Could not parse server response.");
            return;
        }

#if UNITY_EDITOR
        Debug.Log($"[ResultsSubmitter] Attempt #{result.attempt_number} — " +
                  $"passed: {result.passed}, score: {result.percentage_score}%, " +
                  $"sent fail_reason: '{failReason}', " +
                  $"stored fail_reason: '{result.fail_reason}', " +
                  $"recorded: {!result.already_recorded}, duplicate: {result.duplicate}");
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

    // -------------------------------------------------------
    // NO INTERNET - the run is safe on the phone.
    // Uses On Saved Offline when it is connected; otherwise falls back to
    // On Connection Error, so a scene without the new panel still shows
    // something rather than freezing on the submitting screen.
    // -------------------------------------------------------
    private void ReportSavedOffline()
    {
        const string message = "Saved! Your score will appear once you're back online.";

        Debug.Log($"[ResultsSubmitter] Offline - result saved on this device. Waiting to upload: {OfflineOutbox.Count}.");

        if (onSavedOffline != null && onSavedOffline.GetPersistentEventCount() > 0)
            onSavedOffline.Invoke(message);
        else
            onConnectionError?.Invoke(message);
    }
}
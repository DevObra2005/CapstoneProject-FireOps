using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

// -------------------------------------------------------
// WHAT THIS DOES:
// Sends the runs waiting in OfflineOutbox to Laravel, oldest first.
// Same idea as a Laravel queue worker: always running in the background,
// retrying jobs that could not be delivered.
//
// SETS ITSELF UP. Created by code when the app starts and kept alive
// across every scene (DontDestroyOnLoad), so no scene needs an object
// for it and none can forget one.
//
// WHEN IT TRIES:
//   - app start
//   - every scene load
//   - the app coming back from the background
//   - the phone's connection coming back
//   - every retryInterval seconds while anything is waiting
//
// WHAT EACH ANSWER MEANS:
//   200 / 201          -> Laravel has it (201 new, 200 duplicate or
//                         already-passed practice). Removed from the outbox.
//   no connection,     -> still offline. STOP this round, keep the order,
//   timeout              try again later.
//   5xx                -> server trouble. STOP this round, try later.
//   401                -> that participant's token is not accepted. Keep
//                         their runs, SKIP them for this round so other
//                         players' runs are not stuck behind them.
//   422                -> the data itself is invalid; retrying can never
//                         help. Moved to the failed file so it stops
//                         blocking the queue.
//
// ONE ROUND AT A TIME. A request for another round while one is running
// is remembered and run straight after, so the order can never mix.
// FlushFinished is raised only when the uploader is fully idle.
//
// EACH RUN IS SENT WITH ITS OWN TOKEN - the participant who played it,
// stored in the outbox entry - never whoever is logged in now. On a shared
// tablet the next participant may already be playing.
// -------------------------------------------------------

public enum UploadOutcome
{
    Uploaded,     // Laravel has it - removed from the outbox
    Offline,      // no connection - kept, round stopped
    ServerError,  // 5xx or unexpected - kept, round stopped
    AuthFailed,   // 401 - kept, that player's runs skipped this round
    Rejected      // 422 - moved to the failed file
}

public class OutboxUploader : MonoBehaviour
{
    public static OutboxUploader Instance { get; private set; }

    // Raised for every run the uploader tries, with Laravel's raw answer
    // (empty when there was no answer). ResultsSubmitter listens for its
    // own run here.
    public static event Action<OutboxEntry, UploadOutcome, string> EntryProcessed;

    // Raised when the uploader goes fully idle - no round running, none
    // waiting to run.
    public static event Action FlushFinished;

    [Tooltip("Seconds between automatic retries while runs are waiting.")]
    public float retryInterval = 20f;

    [Tooltip("Seconds to wait for Laravel on each upload before treating it as offline.")]
    public int requestTimeoutSeconds = 15;

    private bool isFlushing = false;
    private bool flushAgain = false;
    private float nextAutoTry = 0f;
    private NetworkReachability lastReachability;

    /// <summary>True while a round is running or another one is queued.</summary>
    public bool IsBusy => isFlushing || flushAgain;

    // -------------------------------------------------------
    // SELF SETUP
    // -------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
        EntryProcessed = null;
        FlushFinished = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateOnStartup()
    {
        if (Instance != null) return;

        GameObject go = new GameObject("OutboxUploader (runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<OutboxUploader>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        lastReachability = Application.internetReachability;
    }

    private void OnEnable()  { SceneManager.sceneLoaded += OnSceneLoaded; }
    private void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; }

    private void Start()
    {
        if (OfflineOutbox.Count > 0)
        {
            Debug.Log($"[OutboxUploader] {OfflineOutbox.Count} run(s) waiting from before - trying to upload.");
            RequestFlush();
        }
    }

    // -------------------------------------------------------
    // AUTOMATIC TRIGGERS
    // -------------------------------------------------------

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (OfflineOutbox.Count > 0) RequestFlush();
    }

    private void OnApplicationPause(bool paused)
    {
        // paused == false means the app just came back to the foreground.
        if (!paused && OfflineOutbox.Count > 0) RequestFlush();
    }

    private void Update()
    {
        // The phone's connection came back.
        NetworkReachability now = Application.internetReachability;
        if (now != lastReachability)
        {
            lastReachability = now;
            if (now != NetworkReachability.NotReachable && OfflineOutbox.Count > 0)
            {
                Debug.Log("[OutboxUploader] Connection is back - uploading waiting runs.");
                RequestFlush();
            }
        }

        // Regular retry while something is waiting. internetReachability is
        // only a hint (Wi-Fi can be connected with no internet), so this
        // still tries on its own schedule rather than trusting it fully.
        if (!IsBusy && OfflineOutbox.Count > 0 && Time.unscaledTime >= nextAutoTry)
        {
            nextAutoTry = Time.unscaledTime + retryInterval;
            if (now != NetworkReachability.NotReachable) RequestFlush();
        }
    }

    // -------------------------------------------------------
    // PUBLIC
    // -------------------------------------------------------

    /// <summary>Upload everything waiting. Safe to call any time: if a round
    /// is already running, one more round runs straight after it.</summary>
    public void RequestFlush()
    {
        if (isFlushing)
        {
            flushAgain = true;
            return;
        }
        StartCoroutine(FlushLoop());
    }

    // -------------------------------------------------------
    // THE ROUND
    // -------------------------------------------------------

    private IEnumerator FlushLoop()
    {
        isFlushing = true;

        do
        {
            flushAgain = false;
            yield return FlushOnce();
        }
        while (flushAgain);

        isFlushing = false;
        nextAutoTry = Time.unscaledTime + retryInterval;
        FlushFinished?.Invoke();
    }

    private IEnumerator FlushOnce()
    {
        List<OutboxEntry> queue = OfflineOutbox.Snapshot();   // oldest first
        if (queue.Count == 0) yield break;

        // Tokens Laravel refused this round - their runs are skipped, in
        // order, so nobody else's runs wait behind them.
        HashSet<string> refusedTokens = new HashSet<string>();

        foreach (OutboxEntry entry in queue)
        {
            if (refusedTokens.Contains(entry.token ?? "")) continue;

            UploadOutcome outcome = UploadOutcome.Offline;
            string responseText = "";

            yield return Send(entry, (o, text) => { outcome = o; responseText = text; });

            switch (outcome)
            {
                case UploadOutcome.Uploaded:
                    OfflineOutbox.Remove(entry.attemptId);
                    break;

                case UploadOutcome.Rejected:
                    OfflineOutbox.MoveToFailed(entry.attemptId, "422: " + responseText);
                    break;

                case UploadOutcome.AuthFailed:
                    OfflineOutbox.MarkFailedTry(entry.attemptId, "401: token not accepted");
                    refusedTokens.Add(entry.token ?? "");
                    break;

                case UploadOutcome.Offline:
                case UploadOutcome.ServerError:
                    OfflineOutbox.MarkFailedTry(entry.attemptId, outcome + ": " + responseText);
                    break;
            }

            EntryProcessed?.Invoke(entry, outcome, responseText);

            // No connection or server trouble: stop here so the ORDER is kept.
            // Everything behind this run waits for the next round.
            if (outcome == UploadOutcome.Offline || outcome == UploadOutcome.ServerError)
            {
                Debug.Log($"[OutboxUploader] {outcome} - stopping. {OfflineOutbox.Count} run(s) still waiting.");
                yield break;
            }
        }
    }

    private IEnumerator Send(OutboxEntry entry, Action<UploadOutcome, string> onDone)
    {
        if (string.IsNullOrEmpty(entry.token))
        {
            onDone(UploadOutcome.AuthFailed, "no token stored with this run");
            yield break;
        }

        using (UnityWebRequest request = new UnityWebRequest(ApiConfig.ResultsUrl, "POST"))
        {
            byte[] body = System.Text.Encoding.UTF8.GetBytes(entry.payloadJson ?? "");
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + entry.token);
            request.timeout = requestTimeoutSeconds;

            yield return request.SendWebRequest();

            string text = request.downloadHandler != null ? request.downloadHandler.text : "";
            long code = request.responseCode;

            // No answer at all: no connection, DNS failure, timeout.
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError ||
                code == 0)
            {
                onDone(UploadOutcome.Offline, request.error ?? "");
                yield break;
            }

            if (code == 200 || code == 201) { onDone(UploadOutcome.Uploaded, text); yield break; }
            if (code == 401)                { onDone(UploadOutcome.AuthFailed, text); yield break; }
            if (code == 422)                { onDone(UploadOutcome.Rejected, text); yield break; }

            onDone(UploadOutcome.ServerError, $"{code}: {text}");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// -------------------------------------------------------
// ONE FINISHED RUN WAITING TO BE UPLOADED.
// -------------------------------------------------------
[Serializable]
public class OutboxEntry
{
    // Same value as participant_attempt_id inside payloadJson. Used to find,
    // update and remove this entry, and by Laravel to spot a re-upload.
    public string attemptId;

    // THE TOKEN OF THE PARTICIPANT WHO PLAYED IT — not whoever is logged in
    // when the upload finally happens. On a shared tablet, participant A can
    // play offline and participant B log in before the internet returns;
    // A's run must still be uploaded AS A.
    public string token;

    // For logs and the "waiting to upload" count only.
    public string environment;

    // The exact JSON body to POST, built once when the run finished. Stored
    // finished rather than rebuilt, so every retry is byte-for-byte the same
    // request - which is what lets Laravel recognise a duplicate.
    public string payloadJson;

    // When the run was put in the outbox (UTC, ISO 8601). Informational.
    public string createdAtUtc;

    // How many upload attempts failed, and why the last one did.
    public int tries;
    public string lastError;
}

[Serializable]
internal class OutboxFile
{
    // JsonUtility cannot save a bare list, so the list lives in a wrapper.
    public List<OutboxEntry> entries = new List<OutboxEntry>();
}

// -------------------------------------------------------
// WHAT THIS DOES:
// The OUTBOX - finished runs waiting to be uploaded, saved in a small
// JSON file on the phone so they survive the app closing, the phone
// restarting, and the internet being down for days.
//
// Same idea as a Laravel queue: a job that fails is kept and retried
// later instead of being lost.
//
// ORDER MATTERS. Entries stay in the order they were added, and the
// uploader always sends the oldest first. Laravel numbers attempts in
// the order they arrive, so upload order has to match play order.
//
// WHERE THE FILE LIVES:
// Application.persistentDataPath - the app's private storage. On Android
// other apps cannot read it, and it is kept across app updates. Deleted
// only if the app is uninstalled or its data is cleared.
//
// SAFE AGAINST A CRASH MID-SAVE:
// Each save writes a temporary file first, then swaps it in. If the app
// dies during the write, the old file is still intact - and if it dies
// during the swap, the temporary file is used on the next load.
//
// FAILED FILE:
// An entry Laravel REJECTS (a 422 - the data itself is invalid) can never
// succeed by retrying. It is moved to a separate file so it stops blocking
// the queue, and is kept there for checking rather than silently deleted.
//
// This class only stores. OutboxUploader decides when to send.
// -------------------------------------------------------
public static class OfflineOutbox
{
    private const string OutboxFileName = "results_outbox.json";
    private const string FailedFileName = "results_outbox_failed.json";

    // Raised whenever the outbox changes, e.g. for a "2 results waiting" label.
    public static event Action Changed;

    // In-memory copy of the outbox file, loaded once.
    private static OutboxFile cache;

    // Editor safety: clear the static copy at the start of every Play, so a
    // previous test session's state never leaks into the next one.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache = null;
        Changed = null;
    }

    // -------------------------------------------------------
    // READING
    // -------------------------------------------------------

    /// <summary>How many runs are waiting to be uploaded.</summary>
    public static int Count => Load().entries.Count;

    /// <summary>A copy of the waiting runs, oldest first. Safe to loop over
    /// while entries are being removed.</summary>
    public static List<OutboxEntry> Snapshot()
    {
        return new List<OutboxEntry>(Load().entries);
    }

    public static bool Contains(string attemptId)
    {
        return Find(Load(), attemptId) != null;
    }

    // -------------------------------------------------------
    // WRITING
    // -------------------------------------------------------

    /// <summary>Adds a finished run to the END of the queue. Ignored if a run
    /// with the same ID is already waiting.</summary>
    public static void Add(OutboxEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.attemptId)) return;

        OutboxFile file = Load();
        if (Find(file, entry.attemptId) != null) return;

        file.entries.Add(entry);
        Save(file, OutboxFileName);

        Debug.Log($"[OfflineOutbox] Saved run {entry.attemptId} ({entry.environment}). Waiting: {file.entries.Count}.");
        Changed?.Invoke();
    }

    /// <summary>Removes a run once Laravel has it.</summary>
    public static void Remove(string attemptId)
    {
        OutboxFile file = Load();
        OutboxEntry entry = Find(file, attemptId);
        if (entry == null) return;

        file.entries.Remove(entry);
        Save(file, OutboxFileName);

        Debug.Log($"[OfflineOutbox] Uploaded and removed run {attemptId}. Waiting: {file.entries.Count}.");
        Changed?.Invoke();
    }

    /// <summary>Records a failed upload attempt on a run that stays queued.</summary>
    public static void MarkFailedTry(string attemptId, string error)
    {
        OutboxFile file = Load();
        OutboxEntry entry = Find(file, attemptId);
        if (entry == null) return;

        entry.tries++;
        entry.lastError = error;
        Save(file, OutboxFileName);
    }

    /// <summary>Moves a run Laravel rejected out of the queue, into the failed
    /// file, so it stops blocking the runs behind it.</summary>
    public static void MoveToFailed(string attemptId, string reason)
    {
        OutboxFile file = Load();
        OutboxEntry entry = Find(file, attemptId);
        if (entry == null) return;

        file.entries.Remove(entry);
        Save(file, OutboxFileName);

        entry.lastError = reason;
        OutboxFile failed = Read(FailedFileName);
        failed.entries.Add(entry);
        Save(failed, FailedFileName);

        Debug.LogWarning($"[OfflineOutbox] Run {attemptId} was rejected and moved to {FailedFileName}: {reason}");
        Changed?.Invoke();
    }

    // -------------------------------------------------------
    // FILE HANDLING
    // -------------------------------------------------------

    private static OutboxEntry Find(OutboxFile file, string attemptId)
    {
        foreach (OutboxEntry e in file.entries)
            if (e.attemptId == attemptId) return e;
        return null;
    }

    private static OutboxFile Load()
    {
        if (cache == null) cache = Read(OutboxFileName);
        return cache;
    }

    private static string PathFor(string fileName)
    {
        return Path.Combine(Application.persistentDataPath, fileName);
    }

    private static OutboxFile Read(string fileName)
    {
        string path = PathFor(fileName);
        string tmp = path + ".tmp";

        try
        {
            // Normal case: the real file. If it is missing but a temporary
            // file exists, the app died during the swap - use that instead.
            string source = File.Exists(path) ? path : (File.Exists(tmp) ? tmp : null);
            if (source == null) return new OutboxFile();

            OutboxFile file = JsonUtility.FromJson<OutboxFile>(File.ReadAllText(source));
            return file ?? new OutboxFile();
        }
        catch (Exception e)
        {
            // A damaged file must not crash the game or block every future
            // run. Keep a copy for checking, then start a fresh outbox.
            Debug.LogError($"[OfflineOutbox] Could not read {fileName}: {e.Message}. Keeping a copy and starting fresh.");
            try { if (File.Exists(path)) File.Copy(path, path + ".corrupt", true); } catch { }
            return new OutboxFile();
        }
    }

    private static void Save(OutboxFile file, string fileName)
    {
        string path = PathFor(fileName);
        string tmp = path + ".tmp";

        try
        {
            File.WriteAllText(tmp, JsonUtility.ToJson(file, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[OfflineOutbox] Could not save {fileName}: {e.Message}");
        }
    }
}

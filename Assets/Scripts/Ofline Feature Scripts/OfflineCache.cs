using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Remembers the LAST SUCCESSFUL answer Laravel gave a screen, so that
// screen still has something to show when there is no internet.
//
// Same idea as Facebook offline: you still see the posts it loaded last
// time, with a "No internet" notice, instead of a blank page.
//
// USED BY:
//   PerformanceResultsManager - the participant's results
//   EventSelectionManager     - the list of open events
//   PracticeGate              - "has this participant completed X?"
//
// EVERY COPY BELONGS TO ONE PARTICIPANT.
// The key includes a short fingerprint of the login token, so on a shared
// tablet participant B can never be shown participant A's saved results.
// The token itself is never written into a file name - only a hash of it.
//
// A saved copy is ONLY a fallback. Screens always ask Laravel first and
// use this only when that fails, and they say so on screen.
//
// FILES: Application.persistentDataPath/offline_cache/<key>.json
// The app's private storage - other apps cannot read it.
// -------------------------------------------------------
public static class OfflineCache
{
    [Serializable]
    private class Entry
    {
        public string savedAtUtc;   // ISO 8601
        public string json;         // Laravel's answer, exactly as received
    }

    private static string Folder => Path.Combine(Application.persistentDataPath, "offline_cache");

    /// <summary>
    /// Builds a key for one kind of data, for the CURRENT participant.
    /// e.g. KeyFor("results", "event1") -> "results_3fa9c2e17b04d6e8_event1"
    /// </summary>
    public static string KeyFor(string kind, string extra = "")
    {
        string token = PlayerPrefs.GetString("participant_token", "");
        string key = kind + "_" + ShortHash(token);
        if (!string.IsNullOrEmpty(extra)) key += "_" + extra;
        return Sanitize(key);
    }

    /// <summary>Saves Laravel's answer for this key, stamped with the time.</summary>
    public static void Save(string key, string json)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(json)) return;

        try
        {
            Directory.CreateDirectory(Folder);

            Entry entry = new Entry
            {
                savedAtUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                json = json
            };

            string path = Path.Combine(Folder, key + ".json");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(entry));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            // A cache that cannot save is not an error the player needs to see.
            Debug.LogWarning($"[OfflineCache] Could not save '{key}': {e.Message}");
        }
    }

    /// <summary>
    /// Loads the last saved answer for this key. Returns false if there is none.
    /// savedAtLocal is when it was saved, in the phone's own time zone.
    /// </summary>
    public static bool TryLoad(string key, out string json, out DateTime savedAtLocal)
    {
        json = null;
        savedAtLocal = DateTime.MinValue;

        try
        {
            string path = Path.Combine(Folder, key + ".json");
            if (!File.Exists(path)) return false;

            Entry entry = JsonUtility.FromJson<Entry>(File.ReadAllText(path));
            if (entry == null || string.IsNullOrEmpty(entry.json)) return false;

            json = entry.json;
            if (DateTime.TryParse(entry.savedAtUtc, CultureInfo.InvariantCulture,
                                  DateTimeStyles.RoundtripKind, out DateTime utc))
                savedAtLocal = utc.ToLocalTime();

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OfflineCache] Could not read '{key}': {e.Message}");
            return false;
        }
    }

    /// <summary>"Oct 1, 9:45 PM" - for the on-screen notice.</summary>
    public static string FriendlyTime(DateTime local)
    {
        if (local == DateTime.MinValue) return "earlier";
        return local.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture);
    }

    // A short, one-way fingerprint of the token. Enough to tell participants
    // apart; impossible to turn back into the token.
    private static string ShortHash(string text)
    {
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    // Keeps file names to letters, digits, _ and -.
    private static string Sanitize(string key)
    {
        StringBuilder sb = new StringBuilder(key.Length);
        foreach (char c in key)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return sb.ToString();
    }
}

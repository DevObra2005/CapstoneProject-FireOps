using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Short, controllable vibration on Android.
//
// WHY NOT Handheld.Vibrate():
// Unity's built-in call is a single fixed ~500ms full-strength buzz.
// Far too long for tap feedback, and there is no way to shorten it.
// So this talks to Android's real Vibrator service directly, which lets
// us set duration, strength AND patterns:
//
//   Light  — 20ms single pulse, soft  → reticle locks onto something
//   Medium — 35ms single pulse, firm  → a successful tap
//   Heavy  — DOUBLE pulse, full power → a wrong action ("bzt-bzt")
//
// WHY HEAVY IS A DOUBLE PULSE:
// Strength was already at the maximum (255), so the only ways to make a
// wrong action easier to feel are "longer" or "more than once". A single
// long buzz starts to feel like a phone notification. Two quick pulses
// with a short gap are felt much more clearly — the motor starts twice —
// and "bzt-bzt" is the pattern people already read as "error".
//
// Tune it with the four HEAVY_ numbers below:
//   HEAVY_PULSE_MS  how long each buzz lasts   (weak? go up, e.g. 120)
//   HEAVY_GAP_MS    silence between the buzzes (keep 50-100)
//   HEAVY_PULSES    how many buzzes            (2 is ideal, 3 max)
//
// History: v1 used a 35ms single pulse, v2 a 65ms one. Both were too
// weak on the test phone — many phones use a small spinning-weight motor
// that needs time to spin up, so short pulses end before they are felt.
//
// WHY THERE IS A Handheld.Vibrate() REFERENCE WE NEVER CALL:
// Unity scans scripts at build time. If it finds Handheld.Vibrate
// anywhere, it adds the VIBRATE permission to the APK automatically,
// so the permission can never go missing even if the custom manifest
// changes. EnsurePermissionIsAdded() is never called — do not delete it
// as "unused code".
//
// HOW IT DEGRADES:
//   Editor / PC     → does nothing, silently. Safe to call anywhere.
//   Android 8+      → patterns with strength control (the good path)
//   Android 7 and   → same patterns, always full strength.
//   older
//   No vibrator     → does nothing.
// -------------------------------------------------------

public static class Haptics
{
    private const string PREF_KEY = "haptics_enabled";

    // ---- WRONG-ACTION BUZZ — tune these ----
    private const long HEAVY_PULSE_MS = 90;
    private const long HEAVY_GAP_MS = 70;
    private const int HEAVY_PULSES = 2;

    // Wire this to a settings toggle later if you want one.
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(PREF_KEY, 1) == 1;
        set { PlayerPrefs.SetInt(PREF_KEY, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // -------------------------------------------------------
    // THE THREE YOU WILL ACTUALLY CALL
    // -------------------------------------------------------
    public static void Light() { Vibrate(20, 90); }
    public static void Medium() { Vibrate(35, 160); }
    public static void Heavy() { Pulses(HEAVY_PULSE_MS, HEAVY_GAP_MS, HEAVY_PULSES); }

    // -------------------------------------------------------
    // NEVER CALLED. Its only job is to contain Handheld.Vibrate so
    // Unity adds the VIBRATE permission at build time. See the header.
    // -------------------------------------------------------
#if UNITY_ANDROID
    private static void EnsurePermissionIsAdded()
    {
        Handheld.Vibrate();
    }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject vibrator;
    private static AndroidJavaClass vibrationEffectClass;
    private static int sdkInt;
    private static bool initialised;
    private static bool available;

    private static void Init()
    {
        if (initialised) return;
        initialised = true;

        try
        {
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkInt = version.GetStatic<int>("SDK_INT");

            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");

            if (vibrator == null) return;

            available = vibrator.Call<bool>("hasVibrator");

            // VibrationEffect (strength + patterns) arrived in Android 8 / API 26.
            if (sdkInt >= 26)
                vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
        }
        catch
        {
            // Some device or ROM refused. Never let feel-polish crash the run.
            vibrator = null;
            available = false;
        }
    }
#endif

    /// <summary>
    /// One buzz. duration in milliseconds, amplitude 1-255 (ignored below
    /// Android 8 and on phones without strength control).
    /// </summary>
    public static void Vibrate(long milliseconds, int amplitude)
    {
        if (!Enabled) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        Init();
        if (!available || vibrator == null) return;

        try
        {
            if (sdkInt >= 26 && vibrationEffectClass != null)
            {
                int amp = Mathf.Clamp(amplitude, 1, 255);
                using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                           "createOneShot", milliseconds, amp))
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                // Pre-Oreo: duration only, always full strength.
                vibrator.Call("vibrate", milliseconds);
            }
        }
        catch { /* never break gameplay over a buzz */ }
#endif
    }

    /// <summary>
    /// Several full-strength buzzes with silent gaps ("bzt-bzt").
    /// </summary>
    public static void Pulses(long pulseMs, long gapMs, int count)
    {
        if (!Enabled) return;
        count = Mathf.Clamp(count, 1, 5);

#if UNITY_ANDROID && !UNITY_EDITOR
        Init();
        if (!available || vibrator == null) return;

        // Android describes a pattern as alternating OFF / ON times,
        // starting with OFF: [wait, buzz, wait, buzz, ...]
        // Example for 2 pulses: [0, 90, 70, 90]
        long[] timings = new long[count * 2];
        int[] amplitudes = new int[count * 2];
        for (int i = 0; i < count; i++)
        {
            timings[i * 2] = (i == 0) ? 0 : gapMs;   // silence
            amplitudes[i * 2] = 0;
            timings[i * 2 + 1] = pulseMs;            // buzz
            amplitudes[i * 2 + 1] = 255;             // full strength
        }

        try
        {
            if (sdkInt >= 26 && vibrationEffectClass != null)
            {
                // -1 = play once, do not repeat.
                using (var effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                           "createWaveform", timings, amplitudes, -1))
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                // Pre-Oreo: same pattern, always full strength.
                vibrator.Call("vibrate", timings, -1);
            }
        }
        catch { /* never break gameplay over a buzz */ }
#endif
    }
}
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Short, controllable vibration on Android.
//
// WHY NOT Handheld.Vibrate():
// Unity's built-in call is a single fixed ~500ms full-strength buzz.
// Far too long for tap feedback, and there is no way to shorten it.
// So this talks to Android's real Vibrator service directly, which lets
// us set BOTH duration and strength:
//
//   Light  — 20ms, soft   → reticle locks onto something
//   Medium — 35ms, firm   → a successful tap
//   Heavy  — 65ms, strong → a wrong action
//
// WHY THESE LENGTHS:
// The first version used 12 / 20 / 35 ms. On the test phone the 35ms
// wrong-action buzz was too weak to notice. Many phones use a small
// spinning-weight motor that needs roughly 50-80ms to spin up, so very
// short pulses end before they can be felt. 65ms at full strength is
// clearly felt while still reading as a quick "bzt", not an alarm.
// To tune, change the numbers in the three methods below (keep Heavy
// under ~100ms or it starts to feel like a notification).
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
//   Android 8+      → duration AND amplitude (the good path)
//   Android 7 and   → duration only, full strength.
//   older
//   No vibrator     → does nothing.
// -------------------------------------------------------

public static class Haptics
{
    private const string PREF_KEY = "haptics_enabled";

    // Wire this to a settings toggle later if you want one.
    public static bool Enabled
    {
        get => PlayerPrefs.GetInt(PREF_KEY, 1) == 1;
        set { PlayerPrefs.SetInt(PREF_KEY, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // -------------------------------------------------------
    // THE THREE YOU WILL ACTUALLY CALL
    // (duration in ms, strength 1-255)
    // -------------------------------------------------------
    public static void Light() { Vibrate(20, 90); }
    public static void Medium() { Vibrate(35, 160); }
    public static void Heavy() { Vibrate(65, 255); }

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

            // VibrationEffect (amplitude control) arrived in Android 8 / API 26.
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
    /// duration in milliseconds, amplitude 1-255 (ignored below Android 8
    /// and on phones without amplitude control).
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
}
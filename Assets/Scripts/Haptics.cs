using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Short, precise vibration on Android.
//
// WHY NOT Handheld.Vibrate():
// Unity's built-in call is a single fixed 500ms full-strength buzz.
// That is roughly ten times too long for a tap. On a phone it reads as
// "something went wrong", not "you pressed a thing". There is no way to
// shorten it and no way to soften it.
//
// So this talks to Android's real Vibrator service directly, which lets
// us set BOTH duration and strength:
//
//   Light  — 12ms, soft   → reticle locks onto something
//   Medium — 20ms, firm   → a successful tap
//   Heavy  — 35ms, strong → a wrong action
//
// HOW IT DEGRADES:
//   Editor / PC     → does nothing, silently. Safe to call anywhere.
//   Android 8+      → duration AND amplitude (the good path)
//   Android 7 and   → duration only, full strength. Still much better
//   older              than a 500ms buzz.
//   No vibrator     → does nothing.
//
// PERMISSION REQUIRED:
// Because this does not use Handheld.Vibrate(), Unity does not know to
// add the VIBRATE permission for you. You must add it by hand — see the
// setup steps. Without it, this fails silently on device and you will
// think the code is broken when it is only unpermitted.
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
    // -------------------------------------------------------
    public static void Light() { Vibrate(12, 60); }
    public static void Medium() { Vibrate(20, 130); }
    public static void Heavy() { Vibrate(35, 210); }

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
    /// duration in milliseconds, amplitude 1-255 (ignored below Android 8).
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
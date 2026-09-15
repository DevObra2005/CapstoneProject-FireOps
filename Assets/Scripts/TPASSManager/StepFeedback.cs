using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// -------------------------------------------------------
// StepFeedback - the "was that right or wrong?" signal for step buttons.
//
// WHAT IT DOES
//   Wrong step   -> heavy buzz + error sound + RED glow on the screen edges
//   Correct step -> chime + GREEN glow on the screen edges
//
// HOW OTHER SCRIPTS USE IT (like toast.error() / toast.success() in React)
//   StepFeedback.Wrong();
//   StepFeedback.Correct();
//
//   Both are static, so callers need no Inspector reference. If the scene
//   has no StepFeedback, the call does nothing - Kitchen, Classroom or a
//   test scene can never break because this is missing.
//
// WHERE IT LIVES
//   On a full-screen RawImage inside Phase2UICanvas, placed as the FIRST
//   child so it draws BEHIND the buttons, timer and log. The glow tints
//   the 3D view around the edges but never covers a tile turning green.
//
// WHY THE GLOW IS PAINTED IN CODE (no PNG)
//   A glow picture stretched to fill the screen gets distorted: phones are
//   much wider than tall, so the left/right glow would come out thicker than
//   top/bottom. Instead this script paints its own white glow texture at
//   start-up, matched to THIS phone's screen shape, so the glow is the same
//   thickness on every edge and every device. The RawImage colour then
//   tints it red or green and controls how visible it is.
//
// WHY IT NEVER BLOCKS TAPS
//   A full-screen UI image normally catches every touch, which would make
//   all your buttons dead. raycastTarget is forced OFF in code, so touches
//   pass straight through even if someone ticks it by mistake.
//
// SEPARATE FROM THE WORLD-TAP FEEDBACK
//   SimulationInteractable already flashes and buzzes on wrong WORLD taps.
//   This script is only called by the button managers, so nothing fires
//   twice.
// -------------------------------------------------------

[RequireComponent(typeof(RawImage))]
public class StepFeedback : MonoBehaviour
{
    // The one StepFeedback in the current scene. NOT DontDestroyOnLoad:
    // each scene has its own, and it dies with the scene on reload.
    public static StepFeedback Instance { get; private set; }

    [Header("Glow colours (alpha = how strong at full flash)")]
    [SerializeField] private Color wrongColor = new Color32(235, 64, 52, 230);
    [Tooltip("Same green as the done-tile Disabled Color, so both read as one signal.")]
    [SerializeField] private Color correctColor = new Color32(110, 200, 155, 230);

    [Header("Glow shape")]
    [Tooltip("How far the glow reaches in from each edge, as a share of screen height. " +
             "0.16 = 16%. Can be tuned live in Play mode.")]
    [Range(0.05f, 0.4f)]
    [SerializeField] private float glowDepth = 0.16f;

    [Header("Glow timing (seconds, real time)")]
    [SerializeField] private float fadeIn = 0.06f;
    [SerializeField] private float hold = 0.13f;
    [SerializeField] private float fadeOut = 0.45f;

    [Header("Sounds")]
    [SerializeField] private AudioClip correctSound;
    [SerializeField] private AudioClip wrongSound;
    [Range(0f, 1f)]
    [SerializeField] private float soundVolume = 0.9f;

    [Header("Vibration (phone only - silent in the Editor)")]
    [SerializeField] private bool vibrateOnWrong = true;
    [Tooltip("Off by default: a buzz on every correct tap dilutes the wrong-step buzz.")]
    [SerializeField] private bool vibrateOnCorrect = false;

    private RawImage glow;
    private Texture2D glowTexture;
    private Coroutine flashRoutine;
    private int builtWidth;
    private int builtHeight;

    // -------------------------------------------------------
    // THE TWO CALLS OTHER SCRIPTS USE
    // -------------------------------------------------------
    public static void Correct()
    {
        if (Instance != null) Instance.Play(true);
    }

    public static void Wrong()
    {
        if (Instance != null) Instance.Play(false);
    }

    // -------------------------------------------------------
    // SETUP
    // -------------------------------------------------------

    // Runs in the Editor the moment the component is added. Without this the
    // RawImage shows as a solid white rectangle over your whole Game view.
    private void Reset()
    {
        RawImage img = GetComponent<RawImage>();
        img.color = new Color(1f, 1f, 1f, 0f);
        img.raycastTarget = false;
    }

    private void Awake()
    {
        Instance = this;

        glow = GetComponent<RawImage>();
        glow.raycastTarget = false;          // never eat taps, no matter what
        SetGlow(wrongColor, 0f);             // start invisible

        BuildTexture();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        // Textures made in code are not cleaned up automatically.
        if (glowTexture != null) Destroy(glowTexture);
    }

    // Unity calls this when the screen size changes (rotation, resize in
    // the Simulator). Rebuild so the glow keeps its even thickness.
    private void OnRectTransformDimensionsChange()
    {
        if (glow == null) return;
        if (Screen.width != builtWidth || Screen.height != builtHeight)
            BuildTexture();
    }

    // Lets you drag Glow Depth in Play mode and see the change immediately.
    private void OnValidate()
    {
        if (Application.isPlaying && glow != null)
            BuildTexture();
    }

    // -------------------------------------------------------
    // THE FEEDBACK
    // -------------------------------------------------------
    private void Play(bool correct)
    {
        // 1. Sound - through the AudioManager that survives scene loads.
        //    Null clip or no AudioManager = silent, never an error.
        AudioManager.Play(correct ? correctSound : wrongSound, soundVolume);

        // 2. Vibration - the helper already does nothing in the Editor.
        if (correct && vibrateOnCorrect) Haptics.Light();
        if (!correct && vibrateOnWrong) Haptics.Heavy();

        // 3. Glow. A coroutine cannot start on a disabled object.
        if (!isActiveAndEnabled) return;

        // A new tap RESTARTS the flash instead of stacking a second one,
        // so fast tapping never leaves the screen stuck half-red.
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(Flash(correct ? correctColor : wrongColor));
    }

    // Fade in fast, hold briefly, fade out gently.
    // Uses REAL time, so the flash still finishes if the game is paused.
    private IEnumerator Flash(Color color)
    {
        float t = 0f;
        float inTime = Mathf.Max(0.0001f, fadeIn);
        while (t < inTime)
        {
            t += Time.unscaledDeltaTime;
            SetGlow(color, Mathf.Clamp01(t / inTime));
            yield return null;
        }
        SetGlow(color, 1f);

        yield return new WaitForSecondsRealtime(hold);

        t = 0f;
        float outTime = Mathf.Max(0.0001f, fadeOut);
        while (t < outTime)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t / outTime);
            SetGlow(color, k * k);           // drops quickly, then trails off
            yield return null;
        }

        SetGlow(color, 0f);
        flashRoutine = null;
    }

    // amount 0..1 = invisible..full strength (scaled by the colour's alpha).
    private void SetGlow(Color color, float amount)
    {
        glow.color = new Color(color.r, color.g, color.b, color.a * amount);
    }

    // -------------------------------------------------------
    // PAINTING THE GLOW TEXTURE
    //
    // A small white picture: fully see-through in the middle, getting more
    // solid towards the edges. Its width:height matches the screen, so when
    // stretched full-screen nothing is distorted. 128px tall is plenty -
    // a soft glow has no fine detail, and bilinear filtering smooths it.
    // -------------------------------------------------------
    private void BuildTexture()
    {
        builtWidth = Screen.width;
        builtHeight = Screen.height;

        const int height = 128;
        float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        int width = Mathf.Clamp(Mathf.RoundToInt(height * aspect), 16, 512);

        if (glowTexture != null) Destroy(glowTexture);
        glowTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "StepFeedbackGlow"
        };

        // Depth is measured against HEIGHT for both directions, which is
        // what keeps side edges and top/bottom edges equally thick.
        float depth = glowDepth * height;
        Color32[] pixels = new Color32[width * height];

        for (int y = 0; y < height; y++)
        {
            float distY = Mathf.Min(y + 0.5f, height - y - 0.5f);
            float glowY = Falloff(distY, depth);

            for (int x = 0; x < width; x++)
            {
                float distX = Mathf.Min(x + 0.5f, width - x - 0.5f);
                float glowX = Falloff(distX, depth);

                // Combine both edges softly: corners come out a little
                // stronger, with no hard diagonal line.
                float alpha = 1f - (1f - glowX) * (1f - glowY);

                pixels[y * width + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        glowTexture.SetPixels32(pixels);
        glowTexture.Apply(false, true);      // upload to GPU, free the CPU copy
        glow.texture = glowTexture;
    }

    // 1 at the edge, 0 at "depth" pixels in, curved so it fades smoothly.
    private static float Falloff(float distance, float depth)
    {
        float k = 1f - Mathf.Clamp01(distance / depth);
        return k * k;
    }

    // -------------------------------------------------------
    // TEST BUTTONS - in Play mode, click the three dots on this component
    // (top-right of it in the Inspector) and pick one. Lets you check the
    // look without playing through the drill.
    // -------------------------------------------------------
    [ContextMenu("Test: wrong step")]
    private void TestWrong()
    {
        if (Application.isPlaying) Play(false);
    }

    [ContextMenu("Test: correct step")]
    private void TestCorrect()
    {
        if (Application.isPlaying) Play(true);
    }
}
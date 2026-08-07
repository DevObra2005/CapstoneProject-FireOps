using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;

public class LoadingScreen : MonoBehaviour
{
    // Singleton so any scene can call LoadingScreen.Instance.Show(...)
    public static LoadingScreen Instance;

    [Header("UI References")]
    public GameObject panel;
    public Image barFill;
    public TextMeshProUGUI percentText;
    public TextMeshProUGUI taglineText;

    [Header("Timing")]
    [Tooltip("Minimum seconds the bar takes to fill, even on a fast load")]
    public float minDuration = 2f;
    [Tooltip("Pause at 100% before swapping scenes")]
    public float holdAtFull = 0.25f;
    [Tooltip("Extra frames held after the scene activates, so it has rendered")]
    public int settleFrames = 2;

    private bool isLoading = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        if (panel != null) panel.SetActive(false);
    }

    // Optional custom tagline per use, e.g. "PREPARING SIMULATION"
    public void Show(string sceneName, string tagline = "LOADING")
    {
        // Guard against a double-click firing two loads at once
        if (isLoading)
        {
            Debug.LogWarning("[LoadingScreen] Already loading — ignoring duplicate call.");
            return;
        }
        isLoading = true;

        // Stop the persistent FRONT-END music (menu + selection scene) the
        // moment the loading screen appears. This is the "leaving the
        // front-end" point — the player has committed to loading a scene.
        // Null-guarded so it's safe when no front-end music exists (e.g.
        // the Phase 1 -> Phase 2 transition, where it's already stopped).
        if (BackgroundMusic.Instance != null)
            BackgroundMusic.Instance.Stop();

        if (taglineText != null) taglineText.text = tagline;
        if (barFill != null) barFill.fillAmount = 0f;
        if (percentText != null) percentText.text = "0%";
        if (panel != null) panel.SetActive(true);

        StartCoroutine(LoadRoutine(sceneName));
    }

    IEnumerator LoadRoutine(string sceneName)
    {
        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        op.allowSceneActivation = false;   // hold the swap until the bar is done

        float t = 0f;
        float shown = 0f;

        while (true)
        {
            t += Time.unscaledDeltaTime;

            // Unity stalls progress at 0.9 until activation is allowed,
            // so remap 0–0.9 into a full 0–1 range.
            float real = Mathf.Clamp01(op.progress / 0.9f);

            // Time-based pacing so a fast load still fills smoothly
            float paced = Mathf.Clamp01(t / minDuration);
            paced = 1f - (1f - paced) * (1f - paced);   // EaseOutQuad

            // Show whichever is SLOWER — the bar never claims more progress
            // than actually exists, and never finishes before minDuration.
            float target = Mathf.Min(real, paced);

            // Ratchet: the bar can only move forward, never jump back
            shown = Mathf.Max(shown, target);

            if (barFill != null) barFill.fillAmount = shown;
            if (percentText != null)
                percentText.text = Mathf.RoundToInt(shown * 100f) + "%";

            // Finished when the scene is ready AND the minimum time has passed
            if (real >= 1f && t >= minDuration)
                break;

            yield return null;
        }

        if (barFill != null) barFill.fillAmount = 1f;
        if (percentText != null) percentText.text = "100%";

        yield return new WaitForSecondsRealtime(holdAtFull);

        // Release the scene — it now opens automatically
        op.allowSceneActivation = true;

        // Wait until Unity confirms the new scene is genuinely active.
        // Without this the panel hides while the OLD scene is still on screen.
        while (!op.isDone)
            yield return null;

        // Hold a couple more frames so the new scene has rendered
        // before we uncover it.
        for (int i = 0; i < settleFrames; i++)
            yield return null;

        if (panel != null) panel.SetActive(false);
        if (barFill != null) barFill.fillAmount = 0f;
        if (percentText != null) percentText.text = "0%";
        isLoading = false;
    }
}
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class SplashManager : MonoBehaviour
{
    public Image logo;                  // Logo Image
    public CanvasGroup logoCanvasGroup; // CanvasGroup for logo
    public CanvasGroup fadePanel;       // Panel for background fade
    public float fadeDuration = 0.5f;
    public float splashTime = 2f;

    // -------------------------------------------------------
    // WHERE THE SPLASH GOES NEXT.
    //
    // This used to be a single nextSceneName that always pointed at
    // LoginScene, and LoginManager redirected from there when it found a
    // saved token.
    //
    // That redirect lives in Start(), which runs AFTER the first frame is
    // drawn — so the login form genuinely rendered for a frame or two
    // before vanishing. On a slow Android device that flash is long enough
    // to look like a bug.
    //
    // Deciding here means LoginScene is never loaded at all for a
    // signed-in participant. Nothing wrong renders, so there is nothing to
    // hide.
    //
    // LoginManager keeps its own auto-login check as a fallback for anyone
    // who reaches that scene another way.
    // -------------------------------------------------------
    [Header("Routing")]
    [Tooltip("Loaded when NO saved token exists.")]
    public string loginSceneName = "LoginScene";

    [Tooltip("Loaded when a saved token exists — skips the login form.")]
    public string signedInSceneName = "EventSelectionScene";

    [Header("Loading Screen")]
    [Tooltip("Route the first transition through the loading screen, so every " +
             "launch looks the same whether or not the participant is signed in.")]
    public bool useLoadingScreen = true;

    [Tooltip("Shown under the progress bar on launch.")]
    public string loadingTagline = "PREPARING FIREOPS";

    private void Start()
    {
        StartCoroutine(PlaySplash());
    }

    private IEnumerator PlaySplash()
    {
        // Start panel and logo fully black/invisible
        fadePanel.alpha = 1f;           // black background
        logoCanvasGroup.alpha = 0f;     // logo invisible

        // Fade in logo
        yield return StartCoroutine(FadeLogo(0f, 1f, fadeDuration));

        // Wait while logo is visible
        yield return new WaitForSeconds(splashTime);

        // Fade out logo
        yield return StartCoroutine(FadeLogo(1f, 0f, fadeDuration));

        // Fade panel to black. Starts from its CURRENT alpha rather than a
        // hardcoded 0 — the old version lerped from 0 while the panel was
        // already at 1, which snapped it transparent for a frame before
        // fading back in.
        yield return StartCoroutine(FadePanel(fadePanel.alpha, 1f, fadeDuration));

        LoadNextScene();
    }

    private void LoadNextScene()
    {
        // HasSavedSession only checks that a token STRING exists on this
        // device. It cannot know whether the server still accepts it — a
        // participant deleted by staff would pass this check and then get a
        // 401 on the first real request. EventSelectionManager handles that
        // case by clearing the session and returning to login.
        bool signedIn = LoginManager.HasSavedSession();

        string target = signedIn ? signedInSceneName : loginSceneName;

        Debug.Log($"[Splash] {(signedIn ? "Saved session found" : "No saved session")} -> loading {target}");

        // Same pattern as SceneLoader.Go. The fallback matters more here
        // than anywhere else: if LoadingCanvas is not in THIS scene, the
        // LoadingScreen singleton has never been created, and every later
        // transition in the whole session silently loses its loading screen
        // too. Put LoadingCanvas in SplashScene.
        if (useLoadingScreen && LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(target, loadingTagline);
        else
            SceneManager.LoadScene(target);
    }

    private IEnumerator FadeLogo(float start, float end, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            logoCanvasGroup.alpha = Mathf.Lerp(start, end, elapsed / duration);
            yield return null;
        }
        logoCanvasGroup.alpha = end;
    }

    private IEnumerator FadePanel(float start, float end, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            fadePanel.alpha = Mathf.Lerp(start, end, elapsed / duration);
            yield return null;
        }
        fadePanel.alpha = end;
    }
}
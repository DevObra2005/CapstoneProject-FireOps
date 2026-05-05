using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class SplashManager : MonoBehaviour
{
    public Image logo;               // Logo Image
    public CanvasGroup logoCanvasGroup; // CanvasGroup for logo
    public CanvasGroup fadePanel;    // Panel for background fade
    public float fadeDuration = 0.5f;
    public float splashTime = 2f;
    public string nextSceneName = "LoginScene";

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

        // Fade panel to black if needed
        yield return StartCoroutine(FadePanel(0f, 1f, fadeDuration));

        // Load next scene
        SceneManager.LoadScene(nextSceneName);
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
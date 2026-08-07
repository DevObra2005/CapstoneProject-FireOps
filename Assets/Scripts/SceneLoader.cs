using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    // Shared helper — uses the persistent loading screen when available,
    // otherwise falls back to a direct load (safe when testing a scene
    // in isolation where LoadingScreen hasn't been created yet).
    private void Go(string sceneName, string tagline)
    {
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(sceneName, tagline);
        else
            SceneManager.LoadScene(sceneName);
    }

    // --- Menu navigation ---

    // UPDATED: now shows the loading screen on the way back to the menu
    // (used by the results panel's "Back to Main Menu" button). The menu
    // scene's own background music restarts automatically when it loads.
    public void LoadMainMenu()
    {
        Go("MainMenuScene", "RETURNING TO MENU");
    }

    // Instant menu-to-menu navigation (no loading screen needed — these
    // are lightweight UI scenes).
    public void LoadPerformanceResults()
    {
        SceneManager.LoadScene("PerformanceResultsScene");
    }
    public void LoadResultsDetail()
    {
        SceneManager.LoadScene("ResultsDetailScene");
    }

    public void LoadScenarioSelection()
    {
        SceneManager.LoadScene("ScenarioSelectionScene");
    }

    public void GoBack()
    {
        SceneManager.LoadScene("MainMenuScene");
    }
    public void GoBackMyResult()
    {
        SceneManager.LoadScene("PerformanceResultsScene");
    }

    // --- Environment scenes: heavy, so show the loading screen ---

    public void LoadOfficeScene()
    {
        Go("Office3DScene", "LOADING OFFICE");
    }

    public void LoadClassroomScene()
    {
        Go("Classroom3DScene", "LOADING CLASSROOM");
    }

    public void LoadKitchenScene()
    {
        Go("Kitchen3DScene", "LOADING KITCHEN");
    }

    // --- Extra helpers ---

    // Reload the current scene through the loading screen (e.g. a full
    // "Try Again" that restarts the whole scene). If your Try Again should
    // only restart Phase 2, use your existing phase logic instead.
    public void ReloadCurrentScene()
    {
        string current = SceneManager.GetActiveScene().name;
        Go(current, "RESTARTING");
    }

    // Quit the app (ignored in the editor, works in a build).
    public void QuitGame()
    {
        Debug.Log("[SceneLoader] Quit requested.");
        Application.Quit();
    }
}
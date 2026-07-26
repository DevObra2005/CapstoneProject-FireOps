using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneLoader : MonoBehaviour
{
    // Shared helper — uses the persistent loading screen when available.
    private void Go(string sceneName, string tagline)
    {
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(sceneName, tagline);
        else
            SceneManager.LoadScene(sceneName);
    }

    // --- Menu navigation: instant, no loading screen needed ---

    public void LoadMainMenu()
    {
        SceneManager.LoadScene("MainMenuScene");
    }

    public void GoBackExit()
    {
        SceneManager.LoadScene("LoginScene");
    }

    public void ScenarioSelection()
    {
        SceneManager.LoadScene("ScenarioSelectionScene");
    }

    public void GoBack()
    {
        SceneManager.LoadScene("MainMenuScene");
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
}
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScenarioSelection : MonoBehaviour
{
    // Shared helper — uses the persistent loading screen when available.
    private void Go(string sceneName, string tagline)
    {
        if (LoadingScreen.Instance != null)
            LoadingScreen.Instance.Show(sceneName, tagline);
        else
            SceneManager.LoadScene(sceneName);
    }

    // Called when the user clicks the scenario button
    public void OnScenario1Selected()
    {
        Go("Office3DScene", "LOADING OFFICE");
    }
}
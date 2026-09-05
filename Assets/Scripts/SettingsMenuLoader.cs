using UnityEngine;
using UnityEngine.SceneManagement;

public class SettingsMenuLoader : MonoBehaviour
{
    public void OpenSettings()
    {
        SceneManager.LoadScene("SettingsScene", LoadSceneMode.Single);
    }

    public void OpenSettingsOverlay()
    {
        SceneManager.LoadScene("SettingsScene", LoadSceneMode.Additive);
    }

    public void GoBackToMainMenu()
    {
        SceneManager.LoadScene("MainMenuScene", LoadSceneMode.Single);
    }

    public void ResumeGame()
    {
        SceneManager.UnloadSceneAsync("SettingsScene");
    }
}
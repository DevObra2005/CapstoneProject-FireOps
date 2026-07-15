using UnityEngine;
using UnityEngine.SceneManagement;
public class SceneLoader : MonoBehaviour
{
    // Call this function from your button
    public void LoadMainMenu()
    {
        SceneManager.LoadScene("MainMenuScene"); // Replace with your MainMenu scene name
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
    // --- Add this for your office scenario ---
    public void LoadOfficeScene()
    {
        SceneManager.LoadScene("Office3DScene"); // Replace with your office scene name
    }
    // --- Add this for your classroom scenario ---
    public void LoadClassroomScene()
    {
        SceneManager.LoadScene("Classroom3DScene");
    }
    // --- Add this for your kitchen scenario ---
    public void LoadKitchenScene()
    {
        SceneManager.LoadScene("Kitchen3DScene");
    }
}
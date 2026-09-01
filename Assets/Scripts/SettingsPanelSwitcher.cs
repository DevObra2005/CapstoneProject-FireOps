using UnityEngine;
using UnityEngine.UI;

public class SettingsPanelSwitcher : MonoBehaviour
{
    public GameObject audioPanel;
    public GameObject accountPanel;
    public GameObject aboutPanel;

    public Image audioButtonBg;
    public Image accountButtonBg;
    public Image aboutButtonBg;

    public Color activeColor = new Color(0.3f, 0.3f, 0.35f, 1f);
    public Color inactiveColor = new Color(1f, 1f, 1f, 1f);

    public void ShowAudio()
    {
        audioPanel.SetActive(true);
        accountPanel.SetActive(false);
        aboutPanel.SetActive(false);

        audioButtonBg.color = activeColor;
        accountButtonBg.color = inactiveColor;
        aboutButtonBg.color = inactiveColor;
    }

    public void ShowAccount()
    {
        audioPanel.SetActive(false);
        accountPanel.SetActive(true);
        aboutPanel.SetActive(false);

        audioButtonBg.color = inactiveColor;
        accountButtonBg.color = activeColor;
        aboutButtonBg.color = inactiveColor;
    }

    public void ShowAbout()
    {
        audioPanel.SetActive(false);
        accountPanel.SetActive(false);
        aboutPanel.SetActive(true);

        audioButtonBg.color = inactiveColor;
        accountButtonBg.color = inactiveColor;
        aboutButtonBg.color = activeColor;
    }
}
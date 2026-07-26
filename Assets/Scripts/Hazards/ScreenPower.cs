using UnityEngine;

public class ScreenPower : MonoBehaviour
{
    public GameObject screenDisplay;
    private bool isOn = true;

    public void TurnOff()
    {
        if (!isOn) return;
        isOn = false;
        if (screenDisplay != null)
            screenDisplay.SetActive(false);
    }
}
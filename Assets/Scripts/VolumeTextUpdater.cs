using UnityEngine;
using TMPro;

public class VolumeTextUpdater : MonoBehaviour
{
    public TextMeshProUGUI percentText;

    public void UpdatePercentText(float value)
    {
        int percent = Mathf.RoundToInt(value * 100f);
        percentText.text = percent + "%";
    }

    public void UpdateActualVolume(float value)
    {
        if (GlobalAudioManager.Instance != null)
        {
            GlobalAudioManager.Instance.SetMasterVolume(value);
        }
    }
}
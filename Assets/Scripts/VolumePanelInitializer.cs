using UnityEngine;
using UnityEngine.UI;

public class VolumeSliderInitializer : MonoBehaviour
{
    public Slider slider;
    public VolumeTextUpdater textUpdater;

    void Start()
    {
        float saved = 0.8f;
        if (GlobalAudioManager.Instance != null)
        {
            saved = GlobalAudioManager.Instance.GetSavedVolume();
        }
        Debug.Log("Initializer setting slider to: " + saved);
        slider.SetValueWithoutNotify(saved);
        textUpdater.UpdatePercentText(saved);
    }
}
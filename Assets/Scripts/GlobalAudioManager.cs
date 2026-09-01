using UnityEngine;
using UnityEngine.Audio;

public class GlobalAudioManager : MonoBehaviour
{
    public static GlobalAudioManager Instance;
    public AudioMixer masterMixer;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        float savedVolume = PlayerPrefs.GetFloat("MasterVolume", 0.8f);
        Debug.Log("Loaded saved volume: " + savedVolume);
        SetMasterVolume(savedVolume);

        float savedSFX = PlayerPrefs.GetFloat("SFXVolume", 0.8f);
        Debug.Log("Loaded saved SFX volume: " + savedSFX);
        SetSFXVolume(savedSFX);
    }

    public void SetMasterVolume(float value)
    {
        Debug.Log("SetMasterVolume called with: " + value);
        float clamped = Mathf.Clamp(value, 0.0001f, 1f);
        float db = Mathf.Log10(clamped) * 20f;
        masterMixer.SetFloat("MasterVolume", db);
        PlayerPrefs.SetFloat("MasterVolume", value);
    }

    public float GetSavedVolume()
    {
        return PlayerPrefs.GetFloat("MasterVolume", 0.8f);
    }

    public void SetSFXVolume(float value)
    {
        Debug.Log("SetSFXVolume called with: " + value);
        float clamped = Mathf.Clamp(value, 0.0001f, 1f);
        float db = Mathf.Log10(clamped) * 20f;
        masterMixer.SetFloat("SFXVolume", db);
        PlayerPrefs.SetFloat("SFXVolume", value);
    }

    public float GetSavedSFXVolume()
    {
        return PlayerPrefs.GetFloat("SFXVolume", 0.8f);
    }
}
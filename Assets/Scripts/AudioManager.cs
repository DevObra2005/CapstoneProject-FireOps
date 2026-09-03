using UnityEngine;
using System.Collections;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("UI Click (existing setup)")]
    public AudioSource audioSource;
    public AudioClip clickSound;

    [Header("Volume (0 - 1)")]
    [Range(0f, 1f)] public float musicVolume = 0.35f;
    [Range(0f, 1f)] public float ambientVolume = 0.45f;
    [Range(0f, 1f)] public float sfxVolume = 1.0f;

    [Header("Fade")]
    public float fadeDuration = 1.0f;

    private AudioSource musicSource;
    private AudioSource ambientSource;
    private AudioSource sfxSource;

    private Coroutine musicFade;
    private Coroutine ambientFade;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        musicSource = MakeSource(true);
        ambientSource = MakeSource(true);
        sfxSource = MakeSource(false);
    }

    private AudioSource MakeSource(bool loop)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.loop = loop;
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    // ---- Existing method. Buttons still reference this. ----
    public void PlayClick()
    {
        if (clickSound == null) return;

        if (audioSource != null)
            audioSource.PlayOneShot(clickSound);
        else
            sfxSource.PlayOneShot(clickSound, sfxVolume);
    }

    // ---- New: music and ambient ----

    public void PlayMusic(AudioClip clip)
    {
        if (clip == null) return;
        if (musicSource.clip == clip && musicSource.isPlaying) return;

        if (musicFade != null) StopCoroutine(musicFade);
        musicFade = StartCoroutine(FadeTo(musicSource, clip, musicVolume));
    }

    public void PlayAmbient(AudioClip clip)
    {
        if (clip == null) return;
        if (ambientSource.clip == clip && ambientSource.isPlaying) return;

        if (ambientFade != null) StopCoroutine(ambientFade);
        ambientFade = StartCoroutine(FadeTo(ambientSource, clip, ambientVolume));
    }

    /// <summary>Fades the music out and stops it.</summary>
    public void StopMusic()
    {
        if (musicFade != null) StopCoroutine(musicFade);
        musicFade = StartCoroutine(FadeOutAndStop(musicSource));
    }

    /// <summary>Fades the ambient loop out and stops it.</summary>
    public void StopAmbient()
    {
        if (ambientFade != null) StopCoroutine(ambientFade);
        ambientFade = StartCoroutine(FadeOutAndStop(ambientSource));
    }

    // ---- New: one-line SFX from any script ----

    public static void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (Instance == null || clip == null) return;
        Instance.sfxSource.PlayOneShot(clip, Instance.sfxVolume * volumeScale);
    }

    private IEnumerator FadeTo(AudioSource src, AudioClip clip, float targetVol)
    {
        if (src.isPlaying)
        {
            float start = src.volume;
            for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
            {
                src.volume = Mathf.Lerp(start, 0f, t / fadeDuration);
                yield return null;
            }
            src.Stop();
        }

        src.clip = clip;
        src.volume = 0f;
        src.Play();

        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            src.volume = Mathf.Lerp(0f, targetVol, t / fadeDuration);
            yield return null;
        }
        src.volume = targetVol;
    }
    private IEnumerator FadeOutAndStop(AudioSource src)
    {
        if (!src.isPlaying) yield break;

        float start = src.volume;
        for (float t = 0f; t < fadeDuration; t += Time.unscaledDeltaTime)
        {
            src.volume = Mathf.Lerp(start, 0f, t / fadeDuration);
            yield return null;
        }

        src.Stop();
        src.clip = null;
    }
}
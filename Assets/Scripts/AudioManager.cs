using UnityEngine;
using System.Collections;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    // -------------------------------------------------------
    // UI CLICK
    //
    // clickSound must be assigned in EVERY scene that has an AudioManager.
    // Only ONE AudioManager survives — the first one loaded — and the guard
    // in Awake destroys the rest. If the survivor has an empty clip, no
    // button in the entire game makes a sound, with no error to follow.
    //
    // audioSource is the ORIGINAL setup and is optional now. When empty, the
    // click falls through to the internal SFX source, which is created in
    // code and therefore always exists.
    // -------------------------------------------------------
    [Header("UI Click")]
    [Tooltip("OPTIONAL. Leave empty to use the internal SFX source instead.")]
    public AudioSource audioSource;

    [Tooltip("Assign in EVERY scene. The surviving AudioManager's clip is the " +
             "one that plays, and there is no way to know in advance which " +
             "scene loads first.")]
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
            // A LATER scene's AudioManager is being destroyed here. If its
            // clickSound was assigned and the survivor's was not, inherit it —
            // otherwise the whole game goes silent because of load order,
            // which is not something the Inspector shows you.
            if (Instance.clickSound == null && clickSound != null)
                Instance.clickSound = clickSound;

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

    // ---- UI CLICK ----

    public void PlayClick()
    {
        if (clickSound == null) return;

        // PlayOneShot, never Play(). The assigned audioSource may have Loop
        // ticked from an earlier setup, and Play() would honour that — one
        // click and the sound repeats forever. PlayOneShot ignores loop.
        if (audioSource != null)
            audioSource.PlayOneShot(clickSound, sfxVolume);
        else
            sfxSource.PlayOneShot(clickSound, sfxVolume);
    }

    /// <summary>
    /// Static entry point. Any script can call AudioManager.Click() and it
    /// reaches whichever instance survived DontDestroyOnLoad.
    ///
    /// Unity's OnClick list cannot call static methods, which is what
    /// ButtonClickSound exists for — see that file.
    /// </summary>
    public static void Click()
    {
        Instance?.PlayClick();
    }

    // ---- MUSIC AND AMBIENT ----

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

    public void StopMusic()
    {
        if (musicFade != null) StopCoroutine(musicFade);
        musicFade = StartCoroutine(FadeOutAndStop(musicSource));
    }

    public void StopAmbient()
    {
        if (ambientFade != null) StopCoroutine(ambientFade);
        ambientFade = StartCoroutine(FadeOutAndStop(ambientSource));
    }

    // ---- SFX ----

    public static void Play(AudioClip clip, float volumeScale = 1f)
    {
        if (Instance == null || clip == null) return;
        Instance.sfxSource.PlayOneShot(clip, Instance.sfxVolume * volumeScale);
    }

    // ---- FADES ----

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
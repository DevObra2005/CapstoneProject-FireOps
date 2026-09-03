using UnityEngine;
using System.Collections;

// -------------------------------------------------------
// WHAT THIS DOES:
// A simple "remote control" for the nozzle spray — particles AND sound.
// This script doesn't decide WHEN to spray, it just knows HOW.
// Other scripts (SimulationManager) call Play() and Stop() at the right
// moments in the TPASS sequence.
//
// This separation is why adding audio needed no changes anywhere else:
// SimulationManager already called Play() on Squeeze and Stop() in
// OnFireIsOut(), so the sound rides along on calls that were already
// correctly placed.
//
// -------------------------------------------------------
// WHY THE SOUND IS NOT ROUTED THROUGH AudioManager.Play()
//
// Every other SFX in the project is a one-shot: it fires and finishes on
// its own. The spray is different — it runs for as long as the player is
// discharging, which might be two seconds or eight depending on how fast
// they reach Sweep. A one-shot has no way to be stopped early.
//
// So it needs its own looping AudioSource, created here rather than
// assigned in the Inspector. Sitting on the same object as the particle
// system means the two can never get out of sync.
// -------------------------------------------------------

public class ExtinguisherSprayVFX : MonoBehaviour
{
    [Tooltip("Drag the NozzleSprayVFX Particle System here.")]
    [SerializeField] private ParticleSystem sprayParticles;

    [Header("Audio")]
    [Tooltip("The discharge hiss. Must be a LOOPABLE clip — it plays for as " +
             "long as the player is spraying. Leave empty to skip the sound.")]
    [SerializeField] private AudioClip sprayClip;

    [Range(0f, 1f)]
    [SerializeField] private float volume = 0.7f;

    [Tooltip("Seconds to fade the sound out when the fire goes out. Matches " +
             "the particles finishing their lifetime — a hard cut would end " +
             "the hiss while spray is still visibly in the air.")]
    [SerializeField] private float fadeOutDuration = 0.4f;

    private AudioSource source;
    private Coroutine fadeRoutine;

    private void Awake()
    {
        if (sprayClip == null) return;

        // Built in code rather than assigned in the Inspector, so the sound
        // cannot end up on a different object than the particles.
        source = gameObject.AddComponent<AudioSource>();
        source.clip = sprayClip;
        source.loop = true;
        source.playOnAwake = false;
        source.volume = volume;

        // 2D. The extinguisher is in the player's own hands, so distance
        // falloff would be wrong — it is never far away.
        source.spatialBlend = 0f;
    }

    // ── Called to START the spray ─────────────────────────────────
    public void Play()
    {
        if (sprayParticles != null)
        {
            // Play() starts emitting particles continuously, following
            // whatever settings you configured in the Inspector.
            sprayParticles.Play();
        }

        if (source != null)
        {
            // Cancel a fade still running from a previous discharge, or the
            // new spray would start quiet and keep fading toward zero.
            if (fadeRoutine != null)
            {
                StopCoroutine(fadeRoutine);
                fadeRoutine = null;
            }

            source.volume = volume;
            source.Play();
        }
    }

    // ── Called to STOP the spray ──────────────────────────────────
    public void Stop()
    {
        if (sprayParticles != null)
        {
            // Stop() halts NEW particles from being created, but lets any
            // particles already in the air finish their lifetime naturally —
            // looks more natural than particles vanishing instantly mid-air.
            sprayParticles.Stop();
        }

        if (source != null && source.isPlaying)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadeOut());
        }
    }

    // The audio equivalent of letting the particles finish their lifetime.
    private IEnumerator FadeOut()
    {
        float start = source.volume;

        for (float t = 0f; t < fadeOutDuration; t += Time.deltaTime)
        {
            source.volume = Mathf.Lerp(start, 0f, t / fadeOutDuration);
            yield return null;
        }

        source.Stop();
        source.volume = volume;
        fadeRoutine = null;
    }
}
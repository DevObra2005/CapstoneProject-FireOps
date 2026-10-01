using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// Plays a looping fire-alarm sound during Phase 2, starting
// once the player has sounded the alarm (completed step 1),
// and STOPS it the moment the run ends.
//
// Attach this to the wall fire alarm panel (or any object).
// Add an AudioSource with the alarm clip; this script starts
// it at the right moment and keeps it looping until the run ends.
//
// "Alarm sounded" = SimulationManager.CurrentStep has advanced
// past step 1. Step 1 is Sound Alarm, so CurrentStep >= 2 means
// the alarm has been triggered.
//
// WHY IT NOW STOPS ITSELF:
// The siren used to check only "should I START?". Once it started,
// Update() returned on its first line and never looked again - so the
// siren kept looping behind the Win and Lose panels until the scene
// unloaded.
//
// It now also watches SimulationManager.IsSimActive. EndSimulation sets
// that to false the instant a run ends - win, lose, timeout, the Office
// wrong-fire ending, and a practice run finishing - so one check covers
// every way a run can end, without touching SimulationManager.
//
// The siren FADES rather than cutting dead: a looping alarm that stops
// mid-wave reads as a glitch; half a second of fade reads as deliberate.
// -------------------------------------------------------
[RequireComponent(typeof(AudioSource))]
public class FireAlarmSound : MonoBehaviour
{
    [Tooltip("Alarm starts once SimulationManager.CurrentStep reaches this. " +
             "Step 1 = Sound Alarm, so 2 means 'alarm has been sounded'.")]
    public int startAtStep = 2;

    [Tooltip("Seconds to fade the siren out when the run ends. " +
             "0 = stop instantly.")]
    public float fadeOutDuration = 0.5f;

    private AudioSource source;
    private float originalVolume = 1f;

    // started = the siren has been triggered this run
    // stopped = the run ended and the siren was silenced
    private bool started = false;
    private bool stopped = false;

    private Coroutine fadeRoutine;

    private void Start()
    {
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        // 2D so it plays at full volume regardless of distance.
        source.spatialBlend = 0f;
        originalVolume = source.volume;
    }

    private void Update()
    {
        if (SimulationManager.Instance == null) return;

        bool runActive = SimulationManager.Instance.IsSimActive;
        int step = SimulationManager.Instance.CurrentStep;

        // ---- 1. Not started yet: wait for the alarm step ----
        if (!started)
        {
            // Only during Phase 2, and only once the sim is actually running.
            if (!runActive) return;

            if (step >= startAtStep)
            {
                StopFade();
                source.volume = originalVolume;
                source.Play();
                started = true;
                Debug.Log("[FireAlarmSound] Alarm sounded - siren playing.");
            }
            return;
        }

        // ---- 2. Playing: stop once the run is over ----
        if (!stopped && !runActive)
        {
            stopped = true;
            Debug.Log("[FireAlarmSound] Run ended - silencing the siren.");
            SilenceSiren();
            return;
        }

        // ---- 3. Silenced, and a NEW run started without a scene reload ----
        // Reset so the siren can ring again on that run's alarm step.
        if (stopped && runActive && step < startAtStep)
        {
            started = false;
            stopped = false;
        }
    }

    private void SilenceSiren()
    {
        if (source == null || !source.isPlaying) return;

        StopFade();

        if (fadeOutDuration <= 0f)
        {
            source.Stop();
            source.volume = originalVolume;
            return;
        }

        fadeRoutine = StartCoroutine(FadeOut());
    }

    private IEnumerator FadeOut()
    {
        float startVolume = source.volume;
        float elapsed = 0f;

        while (elapsed < fadeOutDuration)
        {
            // Unscaled, so a paused game (timeScale 0) still finishes the fade.
            elapsed += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeOutDuration);
            yield return null;
        }

        source.Stop();
        source.volume = originalVolume;   // ready for the next time it plays
        fadeRoutine = null;
    }

    private void StopFade()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
    }

    private void OnDisable()
    {
        // Stop the siren if the object/scene is torn down.
        StopFade();
        if (source != null && source.isPlaying)
            source.Stop();
    }
}
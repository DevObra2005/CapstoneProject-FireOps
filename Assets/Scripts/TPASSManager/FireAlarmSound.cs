using UnityEngine;

// -------------------------------------------------------
// Plays a looping fire-alarm sound during Phase 2, starting
// once the player has sounded the alarm (completed step 1).
//
// Attach this to the wall fire alarm panel (or any object).
// Add an AudioSource with the alarm clip; this script starts
// it at the right moment and keeps it looping until Phase 2 ends.
//
// "Alarm sounded" = SimulationManager.CurrentStep has advanced
// past step 1. Step 1 is Sound Alarm, so CurrentStep >= 2 means
// the alarm has been triggered.
// -------------------------------------------------------
[RequireComponent(typeof(AudioSource))]
public class FireAlarmSound : MonoBehaviour
{
    [Tooltip("Alarm starts once SimulationManager.CurrentStep reaches this. " +
             "Step 1 = Sound Alarm, so 2 means 'alarm has been sounded'.")]
    public int startAtStep = 2;

    private AudioSource source;
    private bool started = false;

    private void Start()
    {
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        // 2D so it plays at full volume regardless of distance.
        source.spatialBlend = 0f;
    }

    private void Update()
    {
        if (started) return;
        if (SimulationManager.Instance == null) return;

        // Only during Phase 2, and only once the sim is actually running.
        if (!SimulationManager.Instance.IsSimActive) return;

        if (SimulationManager.Instance.CurrentStep >= startAtStep)
        {
            source.Play();
            started = true;
            Debug.Log("[FireAlarmSound] Alarm sounded — siren playing.");
        }
    }

    private void OnDisable()
    {
        // Stop the siren if the object/scene is torn down.
        if (source != null && source.isPlaying)
            source.Stop();
    }
}
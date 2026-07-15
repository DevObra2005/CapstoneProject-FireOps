using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Controls the office fire so it reacts to the player's TPASS
// actions:
//   - On SQUEEZE (step 6): the fire SHRINKS partway (weakens).
//   - On SWEEP  (step 7): the fire DIES completely (goes out).
//
// It works by turning down the particle systems' emission rate
// and start size over time (a smooth lerp), so the fire looks
// like it's actually being extinguished, not just switched off.
//
// This script goes on the fire object (VFX_Fire_01_Big_Smoke).
// It automatically finds ALL child particle systems (flames,
// smoke, embers, etc.) and dims them together.
//
// ANALOGY: think of it like a dimmer switch for the fire, not an
// on/off switch. Squeeze = dim halfway. Sweep = dim to zero.
// -------------------------------------------------------

public class FireController : MonoBehaviour
{
    [Header("Shrink Settings")]
    // How small the fire gets on SQUEEZE (0.5 = half size/strength).
    [SerializeField] private float squeezeShrinkTo = 0.5f;
    // How long the shrink animation takes (seconds).
    [SerializeField] private float squeezeDuration = 1f;

    [Header("Die Settings")]
    // How long the fire takes to fully die on SWEEP (seconds).
    [SerializeField] private float dieDuration = 2f;
    // Should the whole fire object turn off after it dies?
    [SerializeField] private bool deactivateAfterDeath = true;

    // -------------------------------------------------------
    // All the particle systems that make up this fire (found at Start).
    // We remember each one's ORIGINAL emission rate and start size so
    // we can scale them down relative to their starting values.
    // -------------------------------------------------------
    private ParticleSystem[] allSystems;
    private float[] originalEmission;
    private float[] originalStartSize;

    private void Start()
    {
        // Grab every particle system on this object AND its children.
        // (A fire prefab is usually many systems: flames, smoke, sparks.)
        allSystems = GetComponentsInChildren<ParticleSystem>();

        originalEmission = new float[allSystems.Length];
        originalStartSize = new float[allSystems.Length];

        // Remember each system's starting emission rate and size.
        for (int i = 0; i < allSystems.Length; i++)
        {
            var emission = allSystems[i].emission;
            originalEmission[i] = emission.rateOverTime.constant;

            var main = allSystems[i].main;
            originalStartSize[i] = main.startSize.constant;
        }
    }

    // -------------------------------------------------------
    // PUBLIC: called when the player SQUEEZES (step 6).
    // Shrinks the fire partway.
    // -------------------------------------------------------
    public void WeakenFire()
    {
        StopAllCoroutines();
        StartCoroutine(ScaleFireRoutine(squeezeShrinkTo, squeezeDuration, false));
        Debug.Log("[FireController] Fire weakened (squeeze).");
    }

    // -------------------------------------------------------
    // PUBLIC: called when the player SWEEPS (step 7).
    // Kills the fire completely.
    // -------------------------------------------------------
    public void ExtinguishFire()
    {
        StopAllCoroutines();
        StartCoroutine(ScaleFireRoutine(0f, dieDuration, true));
        Debug.Log("[FireController] Fire extinguished (sweep).");
    }

    // -------------------------------------------------------
    // Smoothly scales every particle system's emission + size
    // from its CURRENT value down to (original * targetFraction)
    // over the given duration.
    //   targetFraction 0.5 = half strength (weaken)
    //   targetFraction 0   = fully out (die)
    // -------------------------------------------------------
    private IEnumerator ScaleFireRoutine(float targetFraction, float duration, bool isDeath)
    {
        // Capture where each system is RIGHT NOW (so weaken-then-die
        // continues smoothly from the weakened state).
        float[] startEmission = new float[allSystems.Length];
        float[] startSize = new float[allSystems.Length];

        for (int i = 0; i < allSystems.Length; i++)
        {
            startEmission[i] = allSystems[i].emission.rateOverTime.constant;
            startSize[i] = allSystems[i].main.startSize.constant;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < allSystems.Length; i++)
            {
                // Target = original value * fraction (e.g. half, or zero).
                float targetEmission = originalEmission[i] * targetFraction;
                float targetSize = originalStartSize[i] * targetFraction;

                // Lerp from current-at-start toward the target.
                float newEmission = Mathf.Lerp(startEmission[i], targetEmission, eased);
                float newSize = Mathf.Lerp(startSize[i], targetSize, eased);

                // Apply to the emission module.
                var emission = allSystems[i].emission;
                emission.rateOverTime = newEmission;

                // Apply to the main module (start size).
                var main = allSystems[i].main;
                main.startSize = newSize;
            }

            yield return null;
        }

        // If this was the death pass, stop the systems and optionally
        // turn the whole fire object off.
        if (isDeath)
        {
            foreach (var ps in allSystems)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            if (deactivateAfterDeath)
            {
                // Give lingering particles a moment to fade, then hide.
                yield return new WaitForSeconds(2f);
                gameObject.SetActive(false);
            }
        }
    }
}
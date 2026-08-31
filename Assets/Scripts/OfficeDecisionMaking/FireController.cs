using System;
using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Controls the office fire so it reacts to the player's TPASS actions:
//   - On SQUEEZE (step 6): the fire SHRINKS partway (weakens).
//   - On SWEEP  (step 7): the fire DIES completely (goes out).
//
// It works by turning down the particle systems' emission rate and start
// size over time (a smooth lerp), so the fire looks like it is actually
// being extinguished, not just switched off.
//
// This script goes on the fire object. It automatically finds ALL child
// particle systems (flames, smoke, embers) and dims them together.
//
// ANALOGY: a dimmer switch for the fire, not an on/off switch.
// Squeeze = dim halfway. Sweep = dim to zero.
//
// -------------------------------------------------------
// COMPLETION CALLBACKS
//
// BOTH WeakenFire() and ExtinguishFire() take an optional callback that
// fires when their fade finishes. SimulationManager uses them for two
// different jobs:
//
//   WeakenFire(onWeakened)  -> unlocks the TPASS buttons once the fire has
//                              VISIBLY shrunk. Without this the player can
//                              tap Sweep 100ms after Squeeze, and because
//                              ExtinguishFire calls StopAllCoroutines the
//                              weaken is cancelled mid-shrink. The fire
//                              just dies, and the whole teaching point of
//                              the two-stage design - one burst is not
//                              enough - never reaches the screen.
//
//   ExtinguishFire(onFireOut) -> stops the spray, lifts the thumb and
//                              relaxes the hose. The spray is what is
//                              KILLING the fire, so it has to still be
//                              running while the flames fade. Cutting it
//                              on the button press showed the fire dying
//                              with nothing hitting it.
//
// WHY CALLBACKS AND NOT DELAY FIELDS ON SimulationManager:
// The two fade lengths are squeezeDuration and dieDuration, and they live
// HERE. If SimulationManager also held "wait this long before unlocking"
// and "wait this long before stopping the spray", those would be second
// copies of the same numbers. The first time either was retuned they would
// drift apart, and nothing would warn you - the timing would just quietly
// start being wrong.
//
// Same pattern as the onArrived callbacks on LeftHandIKController: one
// owner per number, everyone else asks to be told when it is done.
//
// -------------------------------------------------------
// THREE ADDITIONS FOR THE OFFICE TWO-FIRE DECISION.
//
//   GrowFire()            makes a fire BIGGER instead of smaller
//   ResetToFullStrength() puts a fire back to its starting size
//   IsOut                 has this fire finished dying?
//
// ALL THREE ARE PURELY ADDITIVE. Not one existing line changed, so KITCHEN
// AND CLASSROOM BEHAVE EXACTLY AS BEFORE. Kitchen calls only
// ExtinguishFire(); it has no way to reach any of them.
//
// GrowFire reuses ScaleFireRoutine unchanged. That routine already scales
// toward (original * targetFraction), and nothing in it assumed the
// fraction was below 1 - so growth needed no new fade logic at all, only a
// fraction above 1 and isDeath left false.
//
// WHY IsOut EXISTS RATHER THAN CHECKING activeInHierarchy.
// A dead fire hides itself, but only after hideDelay - about two seconds
// later, so drifting particles can fade instead of popping out of
// existence. Anything asking "is this fire still burning?" by checking
// whether the object is active would get YES for those two seconds, long
// after the flames are gone.
//
// Door uses this to decide whether the exit is blocked. Without IsOut, a
// player who correctly cleared the doorway fire would be refused at the
// door for two seconds with the fire visibly out - which reads as a bug,
// and is exactly the kind of thing that gets found during a defense demo.
// -------------------------------------------------------

public class FireController : MonoBehaviour
{
    [Header("Shrink Settings")]
    [Tooltip("How small the fire gets on SQUEEZE. 0.5 = half size and half " +
             "emission rate.")]
    [SerializeField] private float squeezeShrinkTo = 0.5f;

    [Tooltip("How long the shrink takes, in seconds.\n\n" +
             "This is ALSO how long the TPASS buttons stay locked after " +
             "Squeeze, because SimulationManager unlocks them from the " +
             "onWeakened callback rather than from a timer of its own.\n\n" +
             "Raising it makes the shrink more readable but delays Sweep by " +
             "the same amount. Around 1 second reads well: long enough to " +
             "see the fire drop, short enough not to feel like a wait.")]
    [SerializeField] private float squeezeDuration = 1f;

    [Header("Die Settings")]
    [Tooltip("How long the fire takes to fully die on SWEEP, in seconds.\n\n" +
             "This is ALSO how long the spray keeps running after Sweep, " +
             "because the onFireOut callback fires at the end of this fade.\n\n" +
             "SET TO 0 AND THE FIRE DIES INSTANTLY, which makes the hose look " +
             "like it never sweeps: the relax fade starts on the same frame " +
             "as the Sweep clip and pulls the layer to zero before the blend " +
             "finishes. Around 2 is right.")]
    [SerializeField] private float dieDuration = 2f;

    [Tooltip("Should the whole fire object turn off after it dies?\n\n" +
             "KEEP THIS TICKED on the exit fire in Office. Its blocker " +
             "collider is a CHILD of this object, so deactivating is what " +
             "clears the doorway. Untick it and the player can never leave, " +
             "even after doing everything right.")]
    [SerializeField] private bool deactivateAfterDeath = true;

    [Tooltip("Seconds to wait after the flames are out before hiding the " +
             "object, so lingering particles can drift away rather than pop " +
             "out of existence.\n\n" +
             "Nothing should test 'is the fire out' by checking whether this " +
             "object is active — that answer is wrong for this many seconds. " +
             "Use IsOut instead.")]
    [SerializeField] private float hideDelay = 2f;

    // -------------------------------------------------------
    // All the particle systems that make up this fire (found at Start).
    // We remember each one's ORIGINAL emission rate and start size so we can
    // scale them down relative to their starting values.
    // -------------------------------------------------------
    private ParticleSystem[] allSystems;
    private float[] originalEmission;
    private float[] originalStartSize;

    // TRUE from the moment the death fade FINISHES — not when Sweep was
    // tapped, and not when the object is hidden. See the header note.
    private bool isOut = false;

    /// <summary>
    /// TRUE once this fire has finished dying. Read by Door to decide whether
    /// the exit is blocked.
    ///
    /// Deliberately not "is the GameObject active": the object stays active
    /// for hideDelay seconds after the flames are gone, so that test would
    /// report a dead fire as still burning.
    /// </summary>
    public bool IsOut => isOut;

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

        isOut = false;
    }

    // -------------------------------------------------------
    // PUBLIC: called when the player SQUEEZES (step 6).
    // Shrinks the fire partway.
    //
    // onWeakened fires when the shrink has finished, so the caller knows the
    // reduction is actually on screen. SimulationManager uses it to unlock
    // the TPASS buttons - see the note at the top of this file.
    // -------------------------------------------------------
    public void WeakenFire(Action onWeakened = null)
    {
        StopAllCoroutines();
        StartCoroutine(ScaleFireRoutine(squeezeShrinkTo, squeezeDuration, false, onWeakened));
        Debug.Log("[FireController] Fire weakened (squeeze).");
    }

    // -------------------------------------------------------
    // PUBLIC: called when the player SWEEPS (step 7).
    // Kills the fire completely.
    //
    // onFireOut runs the moment the flames have finished fading - BEFORE the
    // object is hidden, because hiding is just cleanup. SimulationManager
    // passes the spray stop and the return to rest in here.
    // -------------------------------------------------------
    public void ExtinguishFire(Action onFireOut = null)
    {
        StopAllCoroutines();
        StartCoroutine(ScaleFireRoutine(0f, dieDuration, true, onFireOut));
        Debug.Log("[FireController] Fire extinguished (sweep).");
    }

    // -------------------------------------------------------
    // OFFICE TWO-FIRE DECISION ONLY: make the fire BIGGER.
    //
    // Called by TwoFireDecision once the first fire is out, so the remaining
    // one visibly takes hold. NOTHING ELSE CALLS THIS.
    //
    //   targetFraction  size relative to the fire's ORIGINAL size, not its
    //                   current one. 1.6 = 60% bigger than it started.
    //                   Values at or below 1 would SHRINK the fire, which
    //                   teaches the opposite lesson - clamped below so a
    //                   mistyped Inspector value cannot silently reverse it.
    //
    // isDeath is FALSE, so nothing is stopped and the object is never
    // hidden - this fire is meant to keep burning behind the player.
    // -------------------------------------------------------
    public void GrowFire(float targetFraction, float duration, Action onGrown = null)
    {
        if (allSystems == null || allSystems.Length == 0)
        {
            Debug.LogWarning("[FireController] GrowFire called before Start() had found " +
                             "any particle systems - ignored.");
            onGrown?.Invoke();
            return;
        }

        if (targetFraction <= 1f)
        {
            Debug.LogWarning($"[FireController] GrowFire target {targetFraction} is not " +
                             "larger than 1 - clamping to 1.5. Set Grow To above 1 on " +
                             "TwoFireDecision.");
            targetFraction = 1.5f;
        }

        StopAllCoroutines();
        StartCoroutine(ScaleFireRoutine(targetFraction, Mathf.Max(0.01f, duration), false, onGrown));
        Debug.Log($"[FireController] Fire grew to {targetFraction}x (spread).");
    }

    // -------------------------------------------------------
    // OFFICE TWO-FIRE DECISION ONLY: put the fire back to how it started.
    //
    // Insurance, mostly. The Office scene reloads between attempts, which
    // re-runs Start() and restores everything anyway - so in normal play this
    // does nothing that was not already done.
    //
    // It exists because TwoFireDecision.ResetForReplay is wired into
    // SimulationManager.ResetRuntimeState, and a run restarted WITHOUT a
    // scene reload would otherwise start with a dead or oversized fire.
    //
    // Instant, not a fade. This runs during setup, where nobody is watching.
    // -------------------------------------------------------
    public void ResetToFullStrength()
    {
        // Called before Start() has run - nothing captured yet, and nothing
        // to restore. Start() will set the correct values momentarily.
        if (allSystems == null || allSystems.Length == 0) return;

        StopAllCoroutines();

        for (int i = 0; i < allSystems.Length; i++)
        {
            if (allSystems[i] == null) continue;

            var emission = allSystems[i].emission;
            emission.rateOverTime = originalEmission[i];

            var main = allSystems[i].main;
            main.startSize = originalStartSize[i];

            // A dead fire was stopped with StopEmitting. Restoring the values
            // alone would not restart it - the system has to be told to play
            // again, and cleared of whatever particles were still drifting.
            allSystems[i].Clear();
            allSystems[i].Play();
        }

        // Burning again, so the exit is blocked again.
        isOut = false;

        Debug.Log("[FireController] Fire restored to full strength.");
    }

    // -------------------------------------------------------
    // Smoothly scales every particle system's emission + size from its
    // CURRENT value down to (original * targetFraction) over the duration.
    //   targetFraction 0.5 = half strength (weaken)
    //   targetFraction 0   = fully out (die)
    //   targetFraction 1.6 = 60% larger than it started (spread)
    // -------------------------------------------------------
    private IEnumerator ScaleFireRoutine(
        float targetFraction,
        float duration,
        bool isDeath,
        Action onComplete)
    {
        // Capture where each system is RIGHT NOW, so weaken-then-die
        // continues smoothly from the weakened state.
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

                var emission = allSystems[i].emission;
                emission.rateOverTime = newEmission;

                var main = allSystems[i].main;
                main.startSize = newSize;
            }

            yield return null;
        }

        // On the death pass, stop the systems emitting entirely.
        if (isDeath)
        {
            foreach (var ps in allSystems)
                ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);

            // THE FLAMES ARE GONE. Set this BEFORE the callback, so anything
            // the callback triggers already sees the fire as out — and well
            // before the object is hidden, which is hideDelay seconds away.
            isOut = true;
        }

        // -----------------------------------------------
        // THE FADE IS DONE.
        //
        // For a WEAKEN this means the fire has visibly shrunk and the player
        // has had a chance to see it - so the buttons can unlock.
        //
        // For a DEATH this means the flames are gone - so the spray can
        // stop, the thumb can lift, and the hose can relax.
        //
        // Fires BEFORE the hide delay below, because hiding is only
        // housekeeping. The flames are already gone by this line, and the
        // player should stop spraying a fire that is OUT, not one that has
        // been made invisible.
        // -----------------------------------------------
        onComplete?.Invoke();

        if (isDeath && deactivateAfterDeath)
        {
            // Give lingering particles a moment to fade, then hide.
            //
            // In Office this is ALSO what clears the doorway: the blocker
            // collider is a child of this object, so it goes with it.
            yield return new WaitForSeconds(hideDelay);
            gameObject.SetActive(false);
        }
    }
}
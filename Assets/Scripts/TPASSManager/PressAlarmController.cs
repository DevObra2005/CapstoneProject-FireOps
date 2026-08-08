using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// -------------------------------------------------------
// WHAT THIS DOES:
// Plays the right hand reaching out and pressing the fire
// alarm button (Step 1), then returns the arm to rest so the
// extinguisher grab can take over normally.
//
// THE ONE RULE THIS SCRIPT ENFORCES:
// ONE OWNER PER TRANSFORM.
//
//   RightHandTarget  - this script is its ONLY writer, every
//                      frame, forever. During the press it
//                      follows PressTarget. At all other times
//                      it follows Grip_Handle, which is what
//                      restores your sculpted extinguisher grip.
//
//   rightArmIK.weight - time-sliced. This script ramps it
//                      0 -> 1 -> 0 during the press. Afterwards
//                      ExtinguisherGrab does its own 0 -> 1 fade
//                      for the grab. They are never both driving
//                      it at the same moment, so they never fight.
//
// WHY CAMERA-SPACE:
// PressTarget is parented under PlayerCamera, not to the alarm
// button on the wall. The distance from shoulder to target is
// therefore FIXED - it can never fall outside the arm's reach,
// no matter where the player stands or which environment they
// are in. Because the tap raycast fires from screen centre, the
// player is always looking straight at the button when they tap,
// so a hand reaching toward screen centre reads as pressing it.
// Sculpt once, works in Office, Kitchen and Classroom alike.
//
// WHY LateUpdate:
// Grip_Handle sits on LeverDown_Bone, which the Animator drives.
// Animation evaluates between Update and LateUpdate, so reading
// it in Update returns LAST frame's position and shows as jitter.
// Same reason LeftHandIKController uses LateUpdate.
// -------------------------------------------------------

public class PressAlarmController : MonoBehaviour
{
    [Header("Rig References")]
    [Tooltip("The Two Bone IK Constraint on RightArmIK.")]
    [SerializeField] private TwoBoneIKConstraint rightArmIK;

    [Tooltip("The empty under Rig_RightArm that RightArmIK targets. " +
             "This script is the only thing that moves it.")]
    [SerializeField] private Transform rightHandTarget;

    [Tooltip("The empty under PlayerCamera marking where the hand " +
             "reaches to press the alarm. Sculpt this in Play mode.")]
    [SerializeField] private Transform pressTarget;

    [Tooltip("Grip_Handle on the extinguisher. The hand returns here " +
             "whenever it is not pressing, preserving your sculpt.")]
    [SerializeField] private Transform gripHandle;

    [Tooltip("The FingerGripController on hand.R.")]
    [SerializeField] private FingerGripController fingerGrip;

    [Header("Timing")]
    [Tooltip("Seconds for the arm to reach out to the button.")]
    [SerializeField] private float reachDuration = 0.35f;

    [Tooltip("Seconds the finger stays held on the button.")]
    [SerializeField] private float pressHold = 0.15f;

    [Tooltip("Seconds for the arm to withdraw back to rest.")]
    [SerializeField] private float retractDuration = 0.30f;

    [Header("Press Motion")]
    [Tooltip("How far the hand pushes forward at the moment of " +
             "contact, in metres. Small values look best.")]
    [SerializeField] private float pressDepth = 0.05f;

    [Tooltip("Seconds for the forward push (and again for the release).")]
    [SerializeField] private float punchDuration = 0.12f;

#if UNITY_EDITOR
    [Header("Editor Testing (stripped from builds)")]
    [Tooltip("Tick this DURING Play mode to hold the arm out at " +
             "PressTarget indefinitely, so you can sculpt PressTarget " +
             "with the Move gizmo and watch the hand follow live. " +
             "Untick to send the arm back to rest.")]
    [SerializeField] private bool sculptMode = false;

    // Remembers the last sculptMode value so we only act on CHANGES,
    // not every single frame.
    private bool sculptModeWasOn = false;
#endif

    // -------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------

    // While true, RightHandTarget follows PressTarget instead of
    // Grip_Handle, and this script owns the IK weight.
    private bool isPressing = false;

    // Extra forward offset applied during the contact push.
    private float punchOffset = 0f;

    private Coroutine pressRoutine;

    /// <summary>True while the press animation is playing.</summary>
    public bool IsPressing => isPressing;

    private void Start()
    {
        // The arm must start invisible/unposed. ExtinguisherGrab also
        // zeroes this in its own Start(); setting it here too means the
        // press works correctly even if script execution order changes.
        if (rightArmIK != null)
            rightArmIK.weight = 0f;
    }

    private void LateUpdate()
    {
#if UNITY_EDITOR
        HandleSculptMode();
#endif

        if (rightHandTarget == null) return;

        // Choose what the target follows this frame.
        Transform follow = isPressing ? pressTarget : gripHandle;
        if (follow == null) return;

        // During the press, push forward along the target's own
        // forward axis to create the contact motion.
        Vector3 offset = isPressing ? follow.forward * punchOffset : Vector3.zero;

        rightHandTarget.position = follow.position + offset;
        rightHandTarget.rotation = follow.rotation;
    }

#if UNITY_EDITOR
    // -------------------------------------------------------
    // SCULPT MODE (editor only)
    //
    // Holds the arm at PressTarget so you can position it with the
    // Move gizmo instead of trying to judge a 1-second animation.
    //
    // It deliberately does nothing while the real press coroutine is
    // running - two systems writing one IK weight is exactly the bug
    // this whole architecture is designed to avoid.
    // -------------------------------------------------------
    private void HandleSculptMode()
    {
        if (!Application.isPlaying) return;
        if (pressRoutine != null) return;   // real press wins

        // Only react when the toggle actually changes.
        if (sculptMode == sculptModeWasOn) return;
        sculptModeWasOn = sculptMode;

        isPressing = sculptMode;
        punchOffset = 0f;

        if (rightArmIK != null)
            rightArmIK.weight = sculptMode ? 1f : 0f;

        if (fingerGrip != null)
            fingerGrip.SetPoint(sculptMode);
    }
#endif

    // -------------------------------------------------------
    // PUBLIC API - called by SimulationManager on Step 1
    // -------------------------------------------------------

    /// <summary>
    /// Plays the full press animation. onComplete fires once the arm
    /// has fully withdrawn, so the simulation can advance.
    /// </summary>
    public void PlayAlarmPress(System.Action onComplete = null)
    {
        if (isPressing)
        {
            // Already playing - ignore the double-tap rather than
            // stacking two coroutines onto the same arm.
            return;
        }

        pressRoutine = StartCoroutine(PressRoutine(onComplete));
    }

    /// <summary>
    /// Snaps everything back to its starting state.
    /// Call this from SimulationManager.ResetRuntimeState().
    /// </summary>
    public void ResetState()
    {
        if (pressRoutine != null)
        {
            StopCoroutine(pressRoutine);
            pressRoutine = null;
        }

        isPressing = false;
        punchOffset = 0f;

        if (rightArmIK != null)
            rightArmIK.weight = 0f;

        if (fingerGrip != null)
            fingerGrip.SetPoint(false);
    }

    /// <summary>
    /// Runs the press immediately from the component's right-click menu.
    /// Handy for checking the timing without playing through Step 1.
    /// </summary>
    [ContextMenu("Test Press")]
    private void TestPress()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("PressAlarmController: enter Play mode first - " +
                             "Animation Rigging does not evaluate in edit mode.");
            return;
        }

        PlayAlarmPress(() => Debug.Log("PressAlarmController: press complete."));
    }

    // -------------------------------------------------------
    // The sequence
    // -------------------------------------------------------

    private IEnumerator PressRoutine(System.Action onComplete)
    {
        isPressing = true;
        punchOffset = 0f;

        // Fold the fingers into the pointing pose as the arm rises,
        // so the hand is already shaped by the time it arrives.
        if (fingerGrip != null)
            fingerGrip.SetPoint(true);

        // 1. Reach - fade the arm in toward the button.
        yield return RampWeight(0f, 1f, reachDuration);

        // 2. Contact - push forward, hold, release.
        yield return RampPunch(0f, pressDepth, punchDuration);
        yield return new WaitForSeconds(pressHold);
        yield return RampPunch(pressDepth, 0f, punchDuration);

        // 3. Relax the fingers as the arm withdraws.
        if (fingerGrip != null)
            fingerGrip.SetPoint(false);

        // 4. Withdraw - fade the arm back out.
        yield return RampWeight(1f, 0f, retractDuration);

        isPressing = false;
        punchOffset = 0f;
        pressRoutine = null;

        onComplete?.Invoke();
    }

    // -------------------------------------------------------
    // Helpers
    //
    // Both use Mathf.SmoothStep - the same easing curve as the
    // extinguisher flight in ExtinguisherGrab, so the two motions
    // feel like they belong to the same pair of hands.
    // -------------------------------------------------------

    private IEnumerator RampWeight(float from, float to, float duration)
    {
        if (rightArmIK == null || duration <= 0f)
        {
            if (rightArmIK != null) rightArmIK.weight = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rightArmIK.weight = Mathf.SmoothStep(from, to, t);
            yield return null;
        }

        rightArmIK.weight = to;
    }

    private IEnumerator RampPunch(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            punchOffset = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            punchOffset = Mathf.SmoothStep(from, to, t);
            yield return null;
        }

        punchOffset = to;
    }
}
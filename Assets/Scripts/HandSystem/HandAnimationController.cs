using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES NOW (rewritten for the IK architecture):
//
// Previously this script rotated the arm BONES directly to fake
// TPASS motions. That job now belongs to Animation Rigging:
// RightArmIK and LeftArmIK move the arms by chasing targets on
// the extinguisher.
//
// So this script's remaining job is much smaller:
//
//   1. STAY OUT OF THE WAY. If an arm is IK-controlled, this
//      script must not write to its bone, or the two systems
//      fight over the same transform every frame and flicker.
//
//   2. NOTHING ELSE, in the normal setup. See the sweep note below.
//
// -------------------------------------------------------
// NEW IN THIS VERSION - THE SWEEP NO LONGER ROTATES THE ANCHOR
//
// SYMPTOM: tapping Sweep swung the RIGHT hand and the whole
// extinguisher body, when only the hose and the left hand should
// have moved.
//
// CAUSE: SweepRoutine() rotated sweepAnchor, which is
// ExtinguisherAnchor - the PARENT of the entire extinguisher. That
// carries the tank, the body, and Grip_Handle. The right hand IK is
// glued to Grip_Handle, so the right arm came along for the ride.
//
// WHY IT USED TO BE CORRECT: this code predates the masked Animator
// layers. Back then there was no way to move the hose on its own,
// so rotating the whole prop was the ONLY way to produce a sweep,
// and dragging both arms with it was the intended behaviour.
//
// WHY IT IS WRONG NOW: the Sweep clip on HoseLayer already does the
// job properly. HoseMask limits it to Bone.012 and below, so it
// moves the hose alone. Grip_Nozzle is parented to a hose bone, so
// it travels with the hose, and the left hand IK follows it for
// free. The right hand stays put on the handle, which is exactly
// how a person actually sweeps an extinguisher.
//
// So the anchor rotation is not just redundant - it is a second
// system fighting the correct one. The sweepControlledByAnimation
// flag below switches it off, using the same "IK owns this, so I
// keep my hands off it" pattern as the two arm flags.
//
// The flag is left in the Inspector rather than the code being
// deleted, so the old behaviour can be restored in one tick if a
// future environment (Kitchen, Classroom) ever needs a prop-level
// sweep that has no Blender clip behind it.
// -------------------------------------------------------

public class HandAnimationController : MonoBehaviour
{
    public static HandAnimationController Instance { get; private set; }

    [Header("Bone References (legacy, non-IK fallback)")]
    [Tooltip("upper_arm.R - ONLY used if Right Hand Controlled By IK is unticked.")]
    [SerializeField] private Transform rightArmBone;

    [Tooltip("upper_arm.L - ONLY used if Left Hand Controlled By IK is unticked.")]
    [SerializeField] private Transform leftArmBone;

    [Header("Sweep Target (legacy, non-clip fallback)")]
    [Tooltip("ExtinguisherAnchor. ONLY used if Sweep Controlled By Animation " +
             "is UNTICKED. In the normal setup the Sweep clip on HoseLayer " +
             "produces the sweep instead, and this is never touched.")]
    [SerializeField] private Transform sweepAnchor;

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.35f;
    [SerializeField] private float holdDuration = 0.15f;

    [Header("Sweep Settings (legacy, non-clip fallback)")]
    [SerializeField] private float sweepAngle = 25f;      // how far left/right (degrees)
    [SerializeField] private float sweepDuration = 0.6f;  // one side-to-side swing
    [SerializeField] private int sweepCount = 2;          // how many full sweeps

    [Header("Hand-off Flags")]
    [Tooltip("Tick when RightArmIK controls the right arm. This script then " +
             "never writes to rightArmBone.")]
    public bool rightHandControlledByIK = true;

    [Tooltip("Tick when LeftArmIK controls the left arm. This script then " +
             "never writes to leftArmBone.")]
    public bool leftHandControlledByIK = true;

    [Tooltip("TICK THIS in the normal setup. The Sweep motion comes from the " +
             "Sweep clip on the masked HoseLayer, which moves ONLY the hose. " +
             "This script then never rotates sweepAnchor.\n\n" +
             "Untick ONLY for an environment that needs a whole-prop sweep " +
             "with no Blender clip behind it - and be aware that rotating the " +
             "anchor drags the right hand and the extinguisher body with it, " +
             "because Grip_Handle lives under the anchor.")]
    public bool sweepControlledByAnimation = true;

    public bool IsAnimating { get; private set; }
    public System.Action OnAnimationComplete;

    // -------------------------------------------------------
    // RESTING POSES - captured once at Start. All motions are
    // offsets FROM these and always return TO these, so poses
    // can never drift over many animations.
    // -------------------------------------------------------
    private Vector3 rightArmRestRot;
    private Vector3 leftArmRestRot;
    private Vector3 sweepAnchorRestRot;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (rightArmBone != null) rightArmRestRot = rightArmBone.localEulerAngles;
        if (leftArmBone != null) leftArmRestRot = leftArmBone.localEulerAngles;
        if (sweepAnchor != null) sweepAnchorRestRot = sweepAnchor.localEulerAngles;
    }


    // -------------------------------------------------------
    // PUBLIC TRIGGERS - one per TPASS sub-step.
    //
    // With all three hand-off flags ticked (the normal setup), every
    // one of these is a no-op: they fire OnAnimationComplete so any
    // waiting code still proceeds, but they touch nothing. The IK
    // targets and the masked clips produce the real motion.
    // -------------------------------------------------------

    public void PlayTwist()
    {
        PlayRightDominant(
            rightRotOffset: new Vector3(0f, 0f, 30f),
            leftRotOffset: new Vector3(15f, 0f, 0f)
        );
    }

    public void PlayPull()
    {
        PlayRightDominant(
            rightRotOffset: new Vector3(-25f, 0f, 0f),
            leftRotOffset: new Vector3(-15f, 0f, 0f)
        );
    }

    public void PlayAim()
    {
        PlayRightDominant(
            rightRotOffset: new Vector3(20f, 0f, -10f),
            leftRotOffset: new Vector3(15f, 0f, -5f)
        );
    }

    public void PlaySqueeze()
    {
        PlayRightDominant(
            rightRotOffset: new Vector3(15f, 0f, -5f),
            leftRotOffset: new Vector3(12f, 0f, 0f)
        );
    }

    // -------------------------------------------------------
    // SWEEP
    //
    // In the normal setup this does NOTHING except report completion.
    // The Sweep clip on HoseLayer owns the motion.
    //
    // Reporting completion still matters: if any caller is waiting on
    // OnAnimationComplete before doing its next thing, returning
    // silently would leave it waiting forever.
    // -------------------------------------------------------
    public void PlaySweep()
    {
        if (IsAnimating) return;

        if (sweepControlledByAnimation)
        {
            // The masked Sweep clip owns this. Do not rotate the anchor -
            // that would drag the right hand and the extinguisher body.
            OnAnimationComplete?.Invoke();
            return;
        }

        StartCoroutine(SweepRoutine());
    }

    // -------------------------------------------------------
    // RIGHT-DOMINANT MOTION
    // Each arm's offset is zeroed if IK owns that arm. If BOTH
    // are IK-owned there is nothing left to animate, so we skip
    // the coroutine entirely and just report completion.
    // -------------------------------------------------------
    private void PlayRightDominant(Vector3 rightRotOffset, Vector3 leftRotOffset)
    {
        if (IsAnimating) return;

        if (rightHandControlledByIK) rightRotOffset = Vector3.zero;
        if (leftHandControlledByIK) leftRotOffset = Vector3.zero;

        // Nothing to animate - IK owns both arms. Report done so
        // any listener waiting on OnAnimationComplete still fires.
        if (rightRotOffset == Vector3.zero && leftRotOffset == Vector3.zero)
        {
            OnAnimationComplete?.Invoke();
            return;
        }

        StartCoroutine(RightDominantRoutine(rightRotOffset, leftRotOffset));
    }

    private IEnumerator RightDominantRoutine(Vector3 rightRotOffset, Vector3 leftRotOffset)
    {
        IsAnimating = true;

        Vector3 rightStart = rightArmRestRot;
        Vector3 rightTarget = rightArmRestRot + rightRotOffset;
        Vector3 leftStart = leftArmRestRot;
        Vector3 leftTarget = leftArmRestRot + leftRotOffset;

        // Out to the action pose (eased)...
        yield return TweenTwoBones(rightStart, rightTarget, leftStart, leftTarget, moveDuration);

        // ...hold for a beat so the pose reads visually...
        yield return new WaitForSeconds(holdDuration);

        // ...and back to rest (eased).
        yield return TweenTwoBones(rightTarget, rightStart, leftTarget, leftStart, moveDuration);

        IsAnimating = false;
        OnAnimationComplete?.Invoke();
    }

    // -------------------------------------------------------
    // TWEEN - rotates arm bones from A to B with SmoothStep
    // easing (slow start, fast middle, slow stop).
    // Skips either bone while IK owns it.
    // -------------------------------------------------------
    private IEnumerator TweenTwoBones(
        Vector3 rightFrom, Vector3 rightTo,
        Vector3 leftFrom, Vector3 leftTo,
        float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            if (rightArmBone != null && !rightHandControlledByIK)
                rightArmBone.localEulerAngles = Vector3.Lerp(rightFrom, rightTo, t);

            if (leftArmBone != null && !leftHandControlledByIK)
                leftArmBone.localEulerAngles = Vector3.Lerp(leftFrom, leftTo, t);

            yield return null;
        }

        // Land exactly on the end pose.
        if (rightArmBone != null && !rightHandControlledByIK)
            rightArmBone.localEulerAngles = rightTo;

        if (leftArmBone != null && !leftHandControlledByIK)
            leftArmBone.localEulerAngles = leftTo;
    }

    // -------------------------------------------------------
    // LEGACY SWEEP - swings the EXTINGUISHER ANCHOR left and right.
    //
    // ONLY reachable when sweepControlledByAnimation is UNTICKED.
    //
    // Kept for an environment that might need a whole-prop sweep with
    // no Blender clip behind it. Be aware of what it costs: rotating
    // the anchor moves the tank, the body, and Grip_Handle - so the
    // right hand travels with it whether you want that or not.
    // -------------------------------------------------------
    private IEnumerator SweepRoutine()
    {
        if (sweepAnchor == null)
        {
            Debug.LogWarning("[HandAnimationController] No sweepAnchor assigned - " +
                             "drag ExtinguisherAnchor into the Sweep Anchor slot, " +
                             "or tick Sweep Controlled By Animation to use the " +
                             "masked Sweep clip instead.");
            OnAnimationComplete?.Invoke();
            yield break;
        }

        IsAnimating = true;

        Vector3 centerRot = sweepAnchorRestRot;
        Vector3 leftRot = centerRot + new Vector3(0f, -sweepAngle, 0f);
        Vector3 rightRot = centerRot + new Vector3(0f, sweepAngle, 0f);

        float half = sweepDuration * 0.5f;

        // Ease out to the left side first...
        yield return TweenAnchor(centerRot, leftRot, half);

        // ...then full swings: left -> right -> left...
        for (int i = 0; i < sweepCount; i++)
        {
            yield return TweenAnchor(leftRot, rightRot, sweepDuration);
            yield return TweenAnchor(rightRot, leftRot, sweepDuration);
        }

        // ...and settle back to center.
        yield return TweenAnchor(leftRot, centerRot, half);

        IsAnimating = false;
        OnAnimationComplete?.Invoke();
    }

    private IEnumerator TweenAnchor(Vector3 from, Vector3 to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            sweepAnchor.localEulerAngles = Vector3.Lerp(from, to, t);

            yield return null;
        }

        sweepAnchor.localEulerAngles = to;
    }
}
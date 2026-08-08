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
//   2. SWEEP. The sweep is the one motion IK cannot produce on
//      its own, because it moves the whole extinguisher rather
//      than one hand. So sweep now rotates the EXTINGUISHER
//      ANCHOR - and because both arms are IK'd onto the
//      extinguisher, both hands follow it automatically.
//      One rotation, two arms, no desync.
//
// WHY THE OLD SWEEP BROKE:
// It rotated the hand rig. Under the old setup the extinguisher
// was parented to the hands, so it came along. Now the
// extinguisher is anchored to the camera instead - rotating the
// hands would slide them straight off a stationary extinguisher.
// -------------------------------------------------------

public class HandAnimationController : MonoBehaviour
{
    public static HandAnimationController Instance { get; private set; }

    [Header("Bone References (legacy, non-IK fallback)")]
    [Tooltip("upper_arm.R - ONLY used if Right Hand Controlled By IK is unticked.")]
    [SerializeField] private Transform rightArmBone;

    [Tooltip("upper_arm.L - ONLY used if Left Hand Controlled By IK is unticked.")]
    [SerializeField] private Transform leftArmBone;

    [Header("Sweep Target")]
    [Tooltip("Drag ExtinguisherAnchor here. Rotating it sweeps the extinguisher, " +
             "and both IK arms follow it automatically.")]
    [SerializeField] private Transform sweepAnchor;

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.35f;
    [SerializeField] private float holdDuration = 0.15f;

    [Header("Sweep Settings")]
    [SerializeField] private float sweepAngle = 25f;      // how far left/right (degrees)
    [SerializeField] private float sweepDuration = 0.6f;  // one side-to-side swing
    [SerializeField] private int sweepCount = 2;          // how many full sweeps

    [Header("IK Hand-off")]
    [Tooltip("Tick when RightArmIK controls the right arm. This script then " +
             "never writes to rightArmBone.")]
    public bool rightHandControlledByIK = true;

    [Tooltip("Tick when LeftArmIK controls the left arm. This script then " +
             "never writes to leftArmBone.")]
    public bool leftHandControlledByIK = true;

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

#if UNITY_EDITOR
    private void Update()
    {
        // Editor-only test keys (Game tab, not Simulator tab).
        if (Input.GetKeyDown(KeyCode.Alpha1)) { Debug.Log("Key 1 - Twist"); PlayTwist(); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { Debug.Log("Key 2 - Pull"); PlayPull(); }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { Debug.Log("Key 3 - Aim"); PlayAim(); }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { Debug.Log("Key 4 - Squeeze"); PlaySqueeze(); }
        if (Input.GetKeyDown(KeyCode.Alpha5)) { Debug.Log("Key 5 - Sweep"); PlaySweep(); }
    }
#endif

    // -------------------------------------------------------
    // PUBLIC TRIGGERS - one per TPASS sub-step.
    //
    // With both IK flags ticked (the normal setup), these become
    // no-ops: they fire OnAnimationComplete so any waiting code
    // still proceeds, but they do not touch the arm bones.
    // The IK targets on the extinguisher produce the real motion.
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

    public void PlaySweep()
    {
        if (IsAnimating) return;
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
    // SWEEP - swings the EXTINGUISHER ANCHOR left and right.
    // Both IK arms chase targets on the extinguisher, so both
    // hands travel with it and stay glued to their grips.
    // -------------------------------------------------------
    private IEnumerator SweepRoutine()
    {
        if (sweepAnchor == null)
        {
            Debug.LogWarning("[HandAnimationController] No sweepAnchor assigned - " +
                             "drag ExtinguisherAnchor into the Sweep Anchor slot.");
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
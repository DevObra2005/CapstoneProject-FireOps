using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Two-handed TPASS animation system. The RIGHT arm bone does
// the active motions (twist, pull, aim, squeeze). The LEFT arm
// bone acts as the "support hand" — UNLESS Animation Rigging IK
// has taken control of it (leftHandControlledByIK = true), in
// which case this script leaves the left arm completely alone
// and only animates the right arm.
//
// For Sweep, the whole HandAnchor moves so both hands swing
// together as one unit.
//
// REALISM UPGRADE: every motion now uses SmoothStep easing —
// arms accelerate at the start and decelerate at the end,
// like real limbs, instead of moving at robotic constant speed.
// -------------------------------------------------------

public class HandAnimationController : MonoBehaviour
{
    public static HandAnimationController Instance { get; private set; }

    [Header("Bone References")]
    // Drag the actual arm bones here — NOT the root fps-hands object.
    // upper_arm.R = the active/working hand
    // upper_arm.L = the support/holding hand (ignored while IK owns it)
    [SerializeField] private Transform rightArmBone;
    [SerializeField] private Transform leftArmBone;

    [Header("Whole-Hand Anchor (for Sweep)")]
    // The parent object that moves BOTH hands together.
    [SerializeField] private Transform handAnchor;

    [Header("Animation Settings")]
    [SerializeField] private float moveDuration = 0.35f;
    [SerializeField] private float holdDuration = 0.15f;

    [Header("Sweep Settings")]
    [SerializeField] private float sweepAngle = 25f;      // how far left/right (degrees)
    [SerializeField] private float sweepDuration = 0.6f;  // one side-to-side swing
    [SerializeField] private int sweepCount = 2;          // how many full sweeps

    [Header("IK Hand-off")]
    // When TRUE, Animation Rigging IK owns the left arm — this
    // script stops touching leftArmBone entirely. The right arm
    // keeps animating as normal. Set from TPASSButtonManager at
    // the Twist step.
    public bool leftHandControlledByIK = false;

    public bool IsAnimating { get; private set; }
    public System.Action OnAnimationComplete;

    // -------------------------------------------------------
    // RESTING POSES — captured once at Start, for each bone.
    // All motions are offsets FROM these, and always return TO
    // these, so poses can never drift over many animations.
    // -------------------------------------------------------
    private Vector3 rightArmRestRot;
    private Vector3 leftArmRestRot;
    private Vector3 handAnchorRestPos;
    private Vector3 handAnchorRestRot;

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
        if (handAnchor != null)
        {
            handAnchorRestPos = handAnchor.localPosition;
            handAnchorRestRot = handAnchor.localEulerAngles;
        }
    }

#if UNITY_EDITOR
    private void Update()
    {
        // Editor-only test keys (Game tab, not Simulator tab!).
        if (Input.GetKeyDown(KeyCode.Alpha1)) { Debug.Log("Key 1 pressed - Twist"); PlayTwist(); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { Debug.Log("Key 2 pressed - Pull"); PlayPull(); }
        if (Input.GetKeyDown(KeyCode.Alpha3)) { Debug.Log("Key 3 pressed - Aim"); PlayAim(); }
        if (Input.GetKeyDown(KeyCode.Alpha4)) { Debug.Log("Key 4 pressed - Squeeze"); PlaySqueeze(); }
        if (Input.GetKeyDown(KeyCode.Alpha5)) { Debug.Log("Key 5 pressed - Sweep"); PlaySweep(); }
    }
#endif

    // -------------------------------------------------------
    // PUBLIC TRIGGERS — one per TPASS sub-step.
    // (Same offsets as before — the realism comes from IK on the
    // left hand + smooth easing, not from bigger swings.)
    // -------------------------------------------------------

    public void PlayTwist()
    {
        // RIGHT hand: twists — rotates on the Z axis (30°).
        // LEFT hand: (only if IK is off) grips tighter on X axis.
        PlayRightDominant(
            rightRotOffset: new Vector3(0f, 0f, 30f),
            leftRotOffset: new Vector3(15f, 0f, 0f)
        );
    }

    public void PlayPull()
    {
        // RIGHT hand: pulls back toward the player.
        // LEFT hand: (only if IK is off) braces against the pull.
        PlayRightDominant(
            rightRotOffset: new Vector3(-25f, 0f, 0f),
            leftRotOffset: new Vector3(-15f, 0f, 0f)
        );
    }

    public void PlayAim()
    {
        // RIGHT hand: tilts down/forward as if aiming.
        // LEFT hand: (only if IK is off) tilts the same way.
        // NOTE: when IK is ON, the left hand is busy grabbing the
        // nozzle at this exact step — which looks far better than
        // this generic tilt ever did.
        PlayRightDominant(
            rightRotOffset: new Vector3(20f, 0f, -10f),
            leftRotOffset: new Vector3(15f, 0f, -5f)
        );
    }

    public void PlaySqueeze()
    {
        // RIGHT hand: sharp downward press of the handle.
        // LEFT hand: (only if IK is off) braces the tank.
        PlayRightDominant(
            rightRotOffset: new Vector3(15f, 0f, -5f),
            leftRotOffset: new Vector3(12f, 0f, 0f)
        );
    }

    public void PlaySweep()
    {
        // The whole HandAnchor swings side to side — both hands
        // (and, visually, the extinguisher) sweeping across the
        // base of the fire. If IK owns the left hand, it stays
        // glued to the nozzle throughout — automatically correct.
        if (IsAnimating) return;
        StartCoroutine(SweepRoutine());
    }

    // -------------------------------------------------------
    // RIGHT-DOMINANT MOTION — right hand does the main movement;
    // left hand supports it ONLY while IK hasn't taken over.
    // -------------------------------------------------------
    private void PlayRightDominant(Vector3 rightRotOffset, Vector3 leftRotOffset)
    {
        if (IsAnimating) return;

        // THE IK HAND-OFF: if IK owns the left arm, we zero out the
        // left offset — the tween below then leaves that bone at its
        // current pose and IK stays in full control. Right arm is
        // unaffected either way.
        if (leftHandControlledByIK)
            leftRotOffset = Vector3.zero;

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

        // ...hold for a beat so the pose "reads" visually...
        yield return new WaitForSeconds(holdDuration);

        // ...and back to rest (eased).
        yield return TweenTwoBones(rightTarget, rightStart, leftTarget, leftStart, moveDuration);

        IsAnimating = false;
        OnAnimationComplete?.Invoke();
    }

    // -------------------------------------------------------
    // TWEEN — rotates both arm bones from A to B over `duration`,
    // with SmoothStep easing (slow start, fast middle, slow stop).
    // Skips the left bone entirely while IK owns it.
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

            // SmoothStep turns a straight 0→1 ramp into an S-curve:
            // gentle acceleration in, gentle deceleration out.
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            if (rightArmBone != null)
                rightArmBone.localEulerAngles = Vector3.Lerp(rightFrom, rightTo, t);

            if (leftArmBone != null && !leftHandControlledByIK)
                leftArmBone.localEulerAngles = Vector3.Lerp(leftFrom, leftTo, t);

            yield return null; // wait one frame
        }

        // Land exactly on the end pose (never trust the last frame's math).
        if (rightArmBone != null)
            rightArmBone.localEulerAngles = rightTo;
        if (leftArmBone != null && !leftHandControlledByIK)
            leftArmBone.localEulerAngles = leftTo;
    }

    // -------------------------------------------------------
    // SWEEP — swings the whole HandAnchor left ↔ right, easing
    // into each turn like an object with real mass, then returns
    // to center.
    // -------------------------------------------------------
    private IEnumerator SweepRoutine()
    {
        IsAnimating = true;

        Vector3 centerRot = handAnchorRestRot;
        Vector3 leftRot = centerRot + new Vector3(0f, -sweepAngle, 0f);
        Vector3 rightRot = centerRot + new Vector3(0f, sweepAngle, 0f);

        float half = sweepDuration * 0.5f;

        // Ease out to the left side first...
        yield return TweenAnchor(centerRot, leftRot, half);

        // ...then full swings: left → right → left...
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

            if (handAnchor != null)
                handAnchor.localEulerAngles = Vector3.Lerp(from, to, t);

            yield return null;
        }

        if (handAnchor != null)
            handAnchor.localEulerAngles = to;
    }
}
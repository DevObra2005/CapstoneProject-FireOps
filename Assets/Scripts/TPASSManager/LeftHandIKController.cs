using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// v15 — Controls the LEFT hand via Animation Rigging (Two Bone IK).
///
/// WHAT CHANGED FROM v14, AND WHY:
///
/// v14 was written when the hand had to DRIVE the pin: it moved through
/// three sculpted keyframes (pinTarget -> pinTwistTarget -> pinPullTarget)
/// and a NozzleController made the hose CHASE the hand.
///
/// That is no longer true. The new FireExtinguisher_ABC has six baked
/// Blender clips. The PIN is animated by the Twist and Pull clips, and
/// the HOSE is animated by Aim/Squeeze/Sweep. Grip markers are parented
/// to those animated parts:
///
///     Grip_Pin     -> child of Pin
///     Grip_Nozzle  -> child of Bone.025_end (the hose tip bone)
///
/// So the hand no longer has to produce the motion. It only has to HOLD
/// ON. Because the marker is welded to the animated part, the clip
/// carries the hand along for free - one sculpt, correct in every frame
/// of every clip.
///
/// CLIP-DRIVEN MODE (the new default):
///   Twist -> hand reaches Grip_Pin and stays. Clip does the twisting.
///   Pull  -> hand does nothing. Clip pulls the pin, hand follows.
///   Aim   -> hand travels from Grip_Pin to Grip_Nozzle.
///
/// LEGACY MODE (useClipDrivenPin = false):
///   Falls back to v14 behaviour using your sculpted keyframes, in case
///   the clips are not wired up yet or you want to compare.
///
/// Everything public that TPASSButtonManager calls is unchanged:
///   ReachPinAndTwist()  PullPin()  GrabNozzle()  ReleaseAll()
///
/// TEST KEYS (editor only, GAME tab):
///   8 = Twist   9 = Pull   0 = Aim   7 = FULL RESET
/// </summary>
public class LeftHandIKController : MonoBehaviour
{
    [Header("Rig References")]
    [Tooltip("The Two Bone IK Constraint on the LeftArmIK object.")]
    public TwoBoneIKConstraint leftArmIK;

    [Tooltip("The IK_Target transform that the constraint follows.")]
    public Transform ikTarget;

    // ─────────────────────────────────────────────────────────────
    [Header("MODE")]
    [Tooltip("ON  = Blender clips animate the pin and hose; the hand just holds the grip markers and is carried along.\n" +
             "OFF = legacy v14 behaviour using the sculpted pin keyframes below.")]
    public bool useClipDrivenPin = true;

    // ─────────────────────────────────────────────────────────────
    [Header("Clip-Driven Grip Markers (new architecture)")]
    [Tooltip("Grip_Pin — an empty parented to the Pin object. The Twist and Pull clips move Pin, so this marker carries the hand.")]
    public Transform gripPin;

    [Tooltip("Grip_Nozzle — an empty parented to Bone.025_end (hose tip). The Aim/Squeeze/Sweep clips move the hose, so this marker carries the hand.")]
    public Transform gripNozzle;

    [Tooltip("Seconds for the hand to travel from the pin across to the nozzle.")]
    public float travelDuration = 0.5f;

    // ─────────────────────────────────────────────────────────────
    [Header("Legacy Pin Keyframes (used only when Use Clip Driven Pin is OFF)")]
    public Transform pinTarget;
    public Transform pinTwistTarget;
    public Transform pinPullTarget;

    [Header("Legacy Aim")]
    [Tooltip("The AimTarget marker. Only used in legacy mode.")]
    public Transform aimTarget;

    [Tooltip("If ON, the wrist matches AimTarget's rotation at the aim pose.")]
    public bool matchAimRotation = true;

    [Tooltip("Seconds for the hand to glide from the pull pose to the AimTarget.")]
    public float aimDuration = 0.5f;

    // ─────────────────────────────────────────────────────────────
    [Header("Rest")]
    [Tooltip("Idle marker (child of PlayerCamera).")]
    public Transform restTarget;

    [Header("Movement Settings")]
    [Tooltip("Seconds for the hand to travel to the pin.")]
    public float moveDuration = 0.35f;

    [Tooltip("How fast the IK weight fades in/out. Higher = snappier.")]
    public float weightBlendSpeed = 4f;

    [Header("Twist / Pull Settings (legacy mode)")]
    public float twistDuration = 0.5f;
    public float pullDuration = 0.4f;

    [Tooltip("FALLBACK ONLY (if Pin Pull Target is empty): straight-back pull distance, meters.")]
    public float pullDistance = 0.12f;

    // ─────────────────────────────────────────────────────────────
    [Header("Pin & Nozzle Controllers (legacy / optional)")]
    [Tooltip("PinController on the Pin object. Not needed in clip-driven mode — the Blender clip handles the pin.")]
    public PinController pinControllerForTests;

    [Tooltip("NozzleController. Not needed in clip-driven mode — the hose is animated by the clips.")]
    public NozzleController nozzleControllerForTests;

    [Header("Fingers (optional)")]
    [Tooltip("Optional FingerGripController on hand.L. If assigned, fingers curl on grip and relax on reset.")]
    public FingerGripController fingerGrip;

#if UNITY_EDITOR
    [Header("Editor Test Keys (never in phone builds)")]
    [Tooltip("Play mode, GAME tab focused: 8 = Twist, 9 = Pull, 0 = Aim, 7 = FULL RESET.")]
    public bool enableTestKeys = true;
#endif

    private float targetWeight = 0f;
    private Coroutine moveRoutine;
    private Transform ikTargetOriginalParent;

    // In clip-driven mode: which marker the hand is currently glued to.
    // LateUpdate keeps ikTarget pinned to it every frame, because the
    // marker is being moved by the animation.
    private Transform currentGrip;

    // True while a travel/reach coroutine owns ikTarget, so LateUpdate
    // does not fight it.
    private bool isTransitioning = false;

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (leftArmIK != null)
            leftArmIK.weight = 0f;              // FK owns the arm until Twist

        if (ikTarget != null)
            ikTargetOriginalParent = ikTarget.parent;

        currentGrip = null;
    }

    void Update()
    {
        if (leftArmIK != null)
        {
            leftArmIK.weight = Mathf.MoveTowards(
                leftArmIK.weight, targetWeight, weightBlendSpeed * Time.deltaTime);
        }

#if UNITY_EDITOR
        if (enableTestKeys)
        {
            if (Input.GetKeyDown(KeyCode.Alpha8)) { TakeOverLeftHand(); ReachPinAndTwist(); }
            if (Input.GetKeyDown(KeyCode.Alpha9)) { TakeOverLeftHand(); PullPin(); }
            if (Input.GetKeyDown(KeyCode.Alpha0)) { TakeOverLeftHand(); GrabNozzle(); }
            if (Input.GetKeyDown(KeyCode.Alpha7)) FullTestReset();
        }
#endif
    }

    void LateUpdate()
    {
        // CLIP-DRIVEN MODE: keep the IK target welded to the current grip
        // marker. The marker is a child of an animated part, so it moves
        // every frame - this is what makes the hand ride the animation
        // instead of sitting still while the pin twists out from under it.
        //
        // LateUpdate specifically, because animation is evaluated between
        // Update and LateUpdate. Reading the marker in Update would give
        // last frame's position and show up as jitter.
        if (useClipDrivenPin && !isTransitioning && currentGrip != null && ikTarget != null)
        {
            ikTarget.position = currentGrip.position;
            ikTarget.rotation = currentGrip.rotation;
        }
    }

#if UNITY_EDITOR
    private void TakeOverLeftHand()
    {
        if (HandAnimationController.Instance != null)
            HandAnimationController.Instance.leftHandControlledByIK = true;
    }

    private void FullTestReset()
    {
        if (nozzleControllerForTests != null) nozzleControllerForTests.Release();
        if (pinControllerForTests != null) pinControllerForTests.ResetPin();
        if (fingerGrip != null) fingerGrip.SetGrip(false);

        ReleaseAll();

        if (HandAnimationController.Instance != null)
            HandAnimationController.Instance.leftHandControlledByIK = false;

        Debug.Log("[LeftHandIK] FULL RESET — hand returning to rest.");
    }
#endif

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API — called by TPASSButtonManager (unchanged names)
    // ─────────────────────────────────────────────────────────────

    public void ReachPinAndTwist() { StartExclusive(ReachAndTwistRoutine()); }
    public void PullPin() { StartExclusive(PullRoutine()); }
    public void GrabNozzle() { StartExclusive(GrabNozzleRoutine()); }
    public void ReleaseAll() { StartExclusive(ReturnToRestRoutine()); }

    // ─────────────────────────────────────────────────────────────
    //  ROUTINES
    // ─────────────────────────────────────────────────────────────

    private IEnumerator ReachAndTwistRoutine()
    {
        targetWeight = 1f;

        if (useClipDrivenPin)
        {
            // Reach out and take hold of Grip_Pin. Once we arrive, set
            // currentGrip so LateUpdate keeps us glued to it. The Blender
            // Twist clip then rotates Pin, and the hand rotates with it.
            if (gripPin == null)
            {
                Debug.LogWarning("[LeftHandIK] Grip Pin is not assigned.");
                yield break;
            }

            isTransitioning = true;
            yield return MoveTargetTo(gripPin, moveDuration, matchRotation: true);
            isTransitioning = false;

            currentGrip = gripPin;

            if (fingerGrip != null) fingerGrip.SetGrip(true);
            yield break;
        }

        // ---- LEGACY MODE ----
        yield return MoveTargetTo(pinTarget, moveDuration, matchRotation: true);

        if (fingerGrip != null) fingerGrip.SetGrip(true);
        if (pinControllerForTests != null) pinControllerForTests.AttachNow();

        yield return new WaitForSeconds(0.1f);

        if (pinTwistTarget != null)
            yield return MoveTargetTo(pinTwistTarget, twistDuration, matchRotation: true);
    }

    private IEnumerator PullRoutine()
    {
        targetWeight = 1f;

        if (useClipDrivenPin)
        {
            // Nothing for the hand to do. Grip_Pin is parented to Pin, and
            // the Pull clip moves Pin - so LateUpdate carries the hand out
            // with it automatically. This routine exists only so the call
            // from TPASSButtonManager still resolves.
            yield break;
        }

        // ---- LEGACY MODE ----
        if (pinPullTarget != null)
        {
            yield return MoveTargetTo(pinPullTarget, pullDuration, matchRotation: true);
        }
        else
        {
            Vector3 startPos = ikTarget.position;
            Vector3 endPos = startPos - ikTarget.forward * pullDistance;
            float elapsed = 0f;
            while (elapsed < pullDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / pullDuration));
                ikTarget.position = Vector3.Lerp(startPos, endPos, t);
                yield return null;
            }
            ikTarget.position = endPos;
        }

        yield return new WaitForSeconds(0.15f);
        if (pinControllerForTests != null) pinControllerForTests.HideNow();
    }

    private IEnumerator GrabNozzleRoutine()
    {
        targetWeight = 1f;

        if (fingerGrip != null) fingerGrip.SetGrip(true);

        if (useClipDrivenPin)
        {
            if (gripNozzle == null)
            {
                Debug.LogWarning("[LeftHandIK] Grip Nozzle is not assigned.");
                yield break;
            }

            // TRAVEL: slide the IK target from wherever it is now across to
            // the nozzle. Both endpoints are read fresh every frame because
            // the extinguisher (and the hose) may be moving during the move.
            isTransitioning = true;

            Transform from = currentGrip;
            Vector3 startPos = ikTarget.position;
            Quaternion startRot = ikTarget.rotation;

            float elapsed = 0f;
            while (elapsed < travelDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / travelDuration));

                Vector3 fromPos = from != null ? from.position : startPos;
                Quaternion fromRot = from != null ? from.rotation : startRot;

                ikTarget.position = Vector3.Lerp(fromPos, gripNozzle.position, t);
                ikTarget.rotation = Quaternion.Slerp(fromRot, gripNozzle.rotation, t);

                yield return null;
            }

            isTransitioning = false;
            currentGrip = gripNozzle;
            yield break;
        }

        // ---- LEGACY MODE ----
        if (nozzleControllerForTests != null) nozzleControllerForTests.StartFollow();

        if (aimTarget != null)
        {
            Quaternion endRot = matchAimRotation ? aimTarget.rotation : ikTarget.rotation;
            yield return MoveTargetToPose(aimTarget.position, endRot, aimDuration);
            ikTarget.SetParent(aimTarget, worldPositionStays: true);
        }
        else
        {
            Debug.LogWarning("[LeftHandIK] AimTarget is not assigned — hand cannot move to the aim pose.");
        }
    }

    private IEnumerator ReturnToRestRoutine()
    {
        // Stop tracking any grip and unparent if legacy mode parented us.
        currentGrip = null;
        isTransitioning = true;

        if (ikTarget != null && ikTarget.parent != ikTargetOriginalParent)
            ikTarget.SetParent(ikTargetOriginalParent, worldPositionStays: true);

        if (restTarget != null)
            yield return MoveTargetTo(restTarget, moveDuration, matchRotation: true);

        isTransitioning = false;
        targetWeight = 0f;

        if (fingerGrip != null) fingerGrip.SetGrip(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    private IEnumerator MoveTargetTo(Transform destination, float duration, bool matchRotation)
    {
        if (destination == null || ikTarget == null) yield break;

        Vector3 startPos = ikTarget.position;
        Quaternion startRot = ikTarget.rotation;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));

            // Read the destination fresh each frame - it may be animated.
            ikTarget.position = Vector3.Lerp(startPos, destination.position, t);
            if (matchRotation)
                ikTarget.rotation = Quaternion.Slerp(startRot, destination.rotation, t);

            yield return null;
        }

        ikTarget.position = destination.position;
        if (matchRotation) ikTarget.rotation = destination.rotation;
    }

    private IEnumerator MoveTargetToPose(Vector3 worldPos, Quaternion worldRot, float duration)
    {
        if (ikTarget == null) yield break;

        Vector3 startPos = ikTarget.position;
        Quaternion startRot = ikTarget.rotation;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            ikTarget.position = Vector3.Lerp(startPos, worldPos, t);
            ikTarget.rotation = Quaternion.Slerp(startRot, worldRot, t);
            yield return null;
        }

        ikTarget.position = worldPos;
        ikTarget.rotation = worldRot;
    }

    private void StartExclusive(IEnumerator routine)
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(routine);
    }
}
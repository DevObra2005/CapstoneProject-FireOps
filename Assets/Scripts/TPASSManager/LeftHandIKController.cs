using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// FINAL (v14) — Controls the LEFT hand via Animation Rigging (Two Bone IK).
///
/// v14 CHANGE (one line, big consequence):
///  - The Aim move now matches AimTarget's ROTATION as well as its
///    position. AimTarget is a fresh marker YOU sculpt — safe, unlike
///    hose-bone markers with Blender bone-roll. Because the hand is
///    PARENTED to AimTarget after Aim, you can drag/rotate AimTarget
///    while the game runs and the hand follows LIVE in the Game view.
///    Sculpt the final pose with your eyes, write the numbers, done.
///
/// Everything else identical to v13:
///  - Twist/Pull via tuned pin keyframes, exact-frame pin attach/hide.
///  - Aim: hand moves first, nozzle CHASES and attaches on contact
///    (snapping structurally impossible).
///  - AimTarget is a child of HandRig, so Sweep carries hand + hose.
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

    [Header("Pin Keyframes (already tuned — do not re-sculpt)")]
    public Transform pinTarget;
    public Transform pinTwistTarget;
    public Transform pinPullTarget;

    [Header("Aim")]
    [Tooltip("The AimTarget marker — child of HandRig. Sculpt its POSITION (W) and ROTATION (E) live during Play; the parked hand follows in real time.")]
    public Transform aimTarget;

    [Tooltip("If ON, the wrist matches AimTarget's rotation at the aim pose — giving you full sculpt control of the final wrist angle.")]
    public bool matchAimRotation = true;

    [Tooltip("Seconds for the hand to glide from the pull pose to the AimTarget.")]
    public float aimDuration = 0.5f;

    [Header("Rest")]
    [Tooltip("Idle marker (child of PlayerCamera).")]
    public Transform restTarget;

    [Header("Movement Settings")]
    [Tooltip("Seconds for the hand to travel to the pin.")]
    public float moveDuration = 0.35f;

    [Tooltip("How fast the IK weight fades in/out. Higher = snappier.")]
    public float weightBlendSpeed = 4f;

    [Header("Twist / Pull Settings")]
    public float twistDuration = 0.5f;
    public float pullDuration = 0.4f;

    [Tooltip("FALLBACK ONLY (if Pin Pull Target is empty): straight-back pull distance, meters.")]
    public float pullDistance = 0.12f;

    [Header("Pin & Nozzle (exact-frame choreography)")]
    [Tooltip("PinController on the Pin object.")]
    public PinController pinControllerForTests;

    [Tooltip("NozzleController on the FireExtinguisherWrapper — its chase starts the moment the Aim move begins.")]
    public NozzleController nozzleControllerForTests;

    [Header("Fingers (optional — leave EMPTY if you posed a static curl)")]
    [Tooltip("Optional FingerGripController. If assigned, fingers curl on grip and relax on reset.")]
    public FingerGripController fingerGrip;

#if UNITY_EDITOR
    [Header("Editor Test Keys (never in phone builds)")]
    [Tooltip("Play mode, GAME tab focused: 8 = Twist, 9 = Pull, 0 = Aim, 7 = FULL RESET.")]
    public bool enableTestKeys = true;
#endif

    private float targetWeight = 0f;
    private Coroutine moveRoutine;
    private Transform ikTargetOriginalParent;

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (leftArmIK != null)
            leftArmIK.weight = 0f;              // FK owns the arm until Twist

        if (ikTarget != null)
            ikTargetOriginalParent = ikTarget.parent;
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

        Debug.Log("[LeftHandIK] FULL RESET — pin restored, hose released, hand returning to rest.");
    }
#endif

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API — called by TPASSButtonManager
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

        yield return MoveTargetTo(pinTarget, moveDuration, matchRotation: true);

        // ARRIVED — curl fingers, glue pin THIS frame (zero offset).
        if (fingerGrip != null) fingerGrip.SetGrip(true);
        if (pinControllerForTests != null) pinControllerForTests.AttachNow();

        yield return new WaitForSeconds(0.1f);

        if (pinTwistTarget != null)
            yield return MoveTargetTo(pinTwistTarget, twistDuration, matchRotation: true);
    }

    private IEnumerator PullRoutine()
    {
        targetWeight = 1f;

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

        // Fingers into the grip shape (if the optional controller exists).
        if (fingerGrip != null) fingerGrip.SetGrip(true);

        // HAND MOVES FIRST — nozzle starts CHASING the same instant.
        if (nozzleControllerForTests != null) nozzleControllerForTests.StartFollow();

        if (aimTarget != null)
        {
            // v14: the wrist can now match AimTarget's sculpted rotation —
            // your one control point for the ENTIRE final pose.
            Quaternion endRot = matchAimRotation ? aimTarget.rotation
                                                 : ikTarget.rotation;

            yield return MoveTargetToPose(aimTarget.position, endRot, aimDuration);

            // PARK on AimTarget: the hand is now its child, so
            //  (a) Sweep (HandRig) carries hand + hose automatically, and
            //  (b) dragging/rotating AimTarget during Play moves the
            //      hand LIVE — your real-time sculpting handle.
            ikTarget.SetParent(aimTarget, worldPositionStays: true);
        }
        else
        {
            Debug.LogWarning("[LeftHandIK] AimTarget is not assigned — hand cannot move to the aim pose.");
        }
    }

    private IEnumerator ReturnToRestRoutine()
    {
        ikTarget.SetParent(ikTargetOriginalParent, worldPositionStays: true);

        if (restTarget != null)
            yield return MoveTargetTo(restTarget, moveDuration, matchRotation: true);

        targetWeight = 0f;
    }

    // ─────────────────────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────────────────────

    private IEnumerator MoveTargetTo(Transform destination, float duration, bool matchRotation)
    {
        if (destination == null) yield break;

        Vector3 startPos = ikTarget.position;
        Quaternion startRot = ikTarget.rotation;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
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
using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// v16 — Controls the LEFT hand via Animation Rigging (Two Bone IK).
///
/// CLEANED: the v14 legacy path is gone. Twist, Pull and PinDrop are all
/// proven working in clip-driven mode, so the sculpted-keyframe fallback
/// and its fields were dead weight that still had to be read and reasoned
/// about every time this file was opened.
///
/// HOW IT WORKS — THE WHOLE IDEA IN ONE PARAGRAPH:
/// The hand does not produce the motion. The six baked Blender clips on
/// FireExtinguisher_ABC animate the PIN (Twist, Pull, PinDrop) and the
/// HOSE (Aim, Squeeze, Sweep). Grip markers are parented to those animated
/// parts:
///
///     Grip_Pin     -> child of Pin
///     Grip_Nozzle  -> child of the hose bone
///
/// So the hand only has to HOLD ON. Because the marker is welded to the
/// animated part, the clip carries the hand along for free — one sculpt,
/// correct in every frame of every clip.
///
/// THE SEQUENCE:
///   Twist -> hand reaches Grip_Pin and stays. The clip does the twisting.
///   Pull  -> hand does nothing. The clip pulls the pin, the hand follows.
///   Aim   -> hand travels from Grip_Pin across to Grip_Nozzle.
///
/// Everything public that TPASSButtonManager and SimulationManager call is
/// unchanged:
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
    [Header("Grip Markers")]
    [Tooltip("Grip_Pin — an empty parented to the Pin object. The Twist and " +
             "Pull clips move Pin, so this marker carries the hand.")]
    public Transform gripPin;

    [Tooltip("Grip_Nozzle — an empty parented to a hose bone. The Aim, " +
             "Squeeze and Sweep clips move the hose, so this marker carries " +
             "the hand. Parent it a few joints back from the tip if the arm " +
             "cannot reach.")]
    public Transform gripNozzle;

    [Tooltip("Idle marker (child of PlayerCamera). Where the hand returns " +
             "when it has nothing to hold.")]
    public Transform restTarget;

    // ─────────────────────────────────────────────────────────────
    [Header("Movement Settings")]
    [Tooltip("Seconds for the hand to reach out to a grip marker.")]
    public float moveDuration = 0.35f;

    [Tooltip("Seconds for the hand to travel from the pin across to the nozzle.")]
    public float travelDuration = 0.5f;

    [Tooltip("How fast the IK weight fades in/out. Higher = snappier.")]
    public float weightBlendSpeed = 4f;

    [Header("Fingers (optional)")]
    [Tooltip("Optional FingerGripController on hand.L. If assigned, fingers " +
             "curl on grip and relax on reset.")]
    public FingerGripController fingerGrip;

#if UNITY_EDITOR
    // =========================================================
    // SCULPT MODE (editor only — stripped from builds)
    //
    // Animation Rigging constraints only evaluate in Play mode, so grip
    // markers can only be positioned there. But a reach animation lasts
    // well under a second, which is not long enough to judge, let alone
    // drag a gizmo against.
    //
    // Ticking sculptMode holds the hand on the chosen marker INDEFINITELY,
    // so you can move and rotate that marker in the Scene view and watch
    // the hand follow live. When it looks right:
    //
    //   right-click the marker's Transform header -> Copy Component
    //   -> Stop Play -> right-click -> Paste Component Values
    //
    // Local position and rotation are what get saved, so the bone snapping
    // back to rest afterwards does not matter — the offset is what rides
    // the animation.
    // =========================================================
    public enum SculptGrip { Pin, Nozzle }

    [Header("Editor Testing (never in phone builds)")]
    [Tooltip("Play mode, GAME tab focused: 8 = Twist, 9 = Pull, 0 = Aim, 7 = FULL RESET.")]
    public bool enableTestKeys = true;

    [Tooltip("Tick DURING Play mode to hold the hand on the chosen grip " +
             "marker so you can sculpt it. Untick to send the hand to rest.")]
    public bool sculptMode = false;

    [Tooltip("Which marker sculptMode holds the hand on.")]
    public SculptGrip sculptGrip = SculptGrip.Nozzle;

    // Remembers the last sculptMode value so we only act on CHANGES.
    private bool sculptModeWasOn = false;
#endif

    private float targetWeight = 0f;
    private Coroutine moveRoutine;

    // Which marker the hand is currently glued to. LateUpdate keeps
    // ikTarget pinned to it every frame, because the marker is being
    // moved by the animation.
    private Transform currentGrip;

    // True while a travel/reach coroutine owns ikTarget, so LateUpdate
    // does not fight it. One owner per Transform, always.
    private bool isTransitioning = false;

    // ─────────────────────────────────────────────────────────────
    //  LIFECYCLE
    // ─────────────────────────────────────────────────────────────

    void Start()
    {
        if (leftArmIK != null)
            leftArmIK.weight = 0f;              // FK owns the arm until Twist

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
        HandleSculptMode();

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
        // Keep the IK target welded to the current grip marker. The marker
        // is a child of an animated part, so it moves every frame — this is
        // what makes the hand ride the animation instead of sitting still
        // while the pin twists out from under it.
        //
        // LateUpdate specifically, because animation is evaluated between
        // Update and LateUpdate. Reading the marker in Update would give
        // last frame's position and show up as jitter.
        if (!isTransitioning && currentGrip != null && ikTarget != null)
        {
            ikTarget.position = currentGrip.position;
            ikTarget.rotation = currentGrip.rotation;
        }
    }

#if UNITY_EDITOR
    // Holds the hand on a marker so it can be sculpted. Does nothing while
    // a real routine is running — two systems driving one IK target is the
    // bug this whole architecture exists to avoid.
    private void HandleSculptMode()
    {
        if (!Application.isPlaying) return;

        if (sculptMode == sculptModeWasOn) return;
        sculptModeWasOn = sculptMode;

        if (sculptMode)
        {
            if (moveRoutine != null) { StopCoroutine(moveRoutine); moveRoutine = null; }

            Transform marker = (sculptGrip == SculptGrip.Pin) ? gripPin : gripNozzle;

            if (marker == null)
            {
                Debug.LogWarning($"[LeftHandIK] Sculpt mode: {sculptGrip} marker is not assigned.");
                sculptMode = false;
                sculptModeWasOn = false;
                return;
            }

            isTransitioning = false;
            currentGrip = marker;
            targetWeight = 1f;

            if (fingerGrip != null) fingerGrip.SetGrip(true);

            Debug.Log($"[LeftHandIK] Sculpt mode ON — holding {marker.name}. " +
                      "Move it in the Scene view, then Copy Component on its Transform.");
        }
        else
        {
            ReleaseAll();
            Debug.Log("[LeftHandIK] Sculpt mode OFF — hand returning to rest.");
        }
    }

    private void TakeOverLeftHand()
    {
        if (HandAnimationController.Instance != null)
            HandAnimationController.Instance.leftHandControlledByIK = true;
    }

    private void FullTestReset()
    {
        if (fingerGrip != null) fingerGrip.SetGrip(false);

        ReleaseAll();

        if (HandAnimationController.Instance != null)
            HandAnimationController.Instance.leftHandControlledByIK = false;

        Debug.Log("[LeftHandIK] FULL RESET — hand returning to rest.");
    }
#endif

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API — called by TPASSButtonManager and SimulationManager
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
    }

    private IEnumerator PullRoutine()
    {
        targetWeight = 1f;

        // Nothing for the hand to do. Grip_Pin is parented to Pin, and the
        // Pull clip moves Pin — so LateUpdate carries the hand out with it
        // automatically. This routine exists only so the call from
        // TPASSButtonManager still resolves.
        yield break;
    }

    private IEnumerator GrabNozzleRoutine()
    {
        targetWeight = 1f;

        if (fingerGrip != null) fingerGrip.SetGrip(true);

        if (gripNozzle == null)
        {
            Debug.LogWarning("[LeftHandIK] Grip Nozzle is not assigned.");
            yield break;
        }

        // TRAVEL: slide the IK target from wherever it is now across to the
        // nozzle. Both endpoints are read fresh every frame because the
        // extinguisher (and the hose) may be moving during the move.
        //
        // Note the hand may start from REST rather than from the pin, since
        // ReleaseAll() fires after PinDrop. `from` being null is handled.
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
    }

    private IEnumerator ReturnToRestRoutine()
    {
        // Stop tracking any grip so LateUpdate lets go.
        currentGrip = null;
        isTransitioning = true;

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

            // Read the destination fresh each frame — it may be animated.
            ikTarget.position = Vector3.Lerp(startPos, destination.position, t);
            if (matchRotation)
                ikTarget.rotation = Quaternion.Slerp(startRot, destination.rotation, t);

            yield return null;
        }

        ikTarget.position = destination.position;
        if (matchRotation) ikTarget.rotation = destination.rotation;
    }

    private void StartExclusive(IEnumerator routine)
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine);
        moveRoutine = StartCoroutine(routine);
    }
}
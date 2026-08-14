using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// v18 — Controls the LEFT hand via Animation Rigging (Two Bone IK).
///
/// CHANGED IN v18 — restTarget AND fingerGrip REMOVED:
///
/// Both fields were unassigned in the scene and both were null-guarded at
/// every call site, so nothing broke — but "does nothing" and "is not
/// needed" are different claims, and it was worth checking which applied.
///
/// restTarget: the real release in ReturnToRestRoutine was always
/// `targetWeight = 0f`, which fades the IK constraint out so the arm
/// returns to its FK rig pose on its own. That happens with or without a
/// rest marker. All restTarget added was an optional 0.4-second travel to a
/// parking spot BEFORE the weight faded — a scenic route to the same
/// destination. Removing it makes the release immediate rather than
/// delayed, which reads no differently.
///
/// The same applied in Start(): the parking was skipped when null, and the
/// weight is 0 at that point anyway, so the arm is in its FK pose and
/// nothing was visible either way.
///
/// fingerGrip: the LEFT hand grabs a pin and a nozzle. It does not wrap a
/// handle the way the right hand does, so it never needed sculpted curls.
/// The right hand keeps its FingerGripController — that one is doing real
/// work (grip, point, and the thumb press on Squeeze).
///
/// Editor testing and sculpt mode are DELIBERATELY KEPT. They live inside
/// #if UNITY_EDITOR so they cost nothing in the APK, and Kitchen and
/// Classroom Phase 2 still need their grip markers positioned. Animation
/// Rigging constraints only evaluate in Play mode, so sculpt mode is the
/// only way to do that. Deleting it would mean rebuilding it.
///
/// HOW IT WORKS — THE WHOLE IDEA IN ONE PARAGRAPH:
/// The hand does not produce the motion. The six baked Blender clips on
/// FireExtinguisher_ABC animate the PIN (Twist, Pull, PinDrop) and the
/// HOSE (Aim, Squeeze, Sweep). Grip markers are parented to those animated
/// parts:
///
///     Grip_Pin     -> child of Pin
///     Grip_Nozzle  -> child of a hose bone
///
/// So the hand only has to HOLD ON. Because the marker is welded to the
/// animated part, the clip carries the hand along for free — one sculpt,
/// correct in every frame of every clip.
///
/// ARRIVAL CALLBACKS:
/// The reach routines accept an optional onArrived callback that fires the
/// moment the hand actually reaches its grip marker.
///
/// WHY THAT MATTERS:
/// Tapping Twist used to start the pin turning INSTANTLY while the hand was
/// still travelling toward it, so the pin twisted in empty air. The obvious
/// fix — a delay field in SimulationManager — would have duplicated
/// moveDuration in a second file. Two numbers meaning the same thing drift
/// apart the first time one is retuned, and nothing warns you.
///
/// A callback has nothing to keep in sync. The hand finishes moving, says
/// so, and SimulationManager decides what happens next. Retune moveDuration
/// freely; the timing stays correct by construction.
///
/// Note the callback does NOT play the clip itself. This script never
/// touches the Animator — it only reports arrival. SimulationManager still
/// owns all clip playback.
///
/// THE SEQUENCE:
///   Twist -> hand reaches Grip_Pin, THEN the clip twists it.
///   Pull  -> hand does nothing. The clip pulls the pin, the hand follows.
///   Aim   -> hand travels from Grip_Pin across to Grip_Nozzle, THEN aims.
///
/// Every public method is unchanged in name and signature, so all existing
/// call sites in TPASSButtonManager and SimulationManager compile untouched:
///   ReachPinAndTwist()  PullPin()  GrabNozzle()  ReleaseAll()
///
/// One timing note: ReleaseAll's onArrived now fires immediately, because
/// there is no longer a travel to wait for. Nothing in the project passes a
/// callback to ReleaseAll, so this changes nothing today — but if you ever
/// chain something off it, know that it means "release started", not
/// "hand has finished lowering".
///
/// TEST KEYS (editor only, GAME tab):
///   8 = Twist   9 = Pull   0 = Aim   7 = FULL RESET
/// </summary>
public class LeftHandIKController : MonoBehaviour
{
    [Header("Rig References")]
    [Tooltip("The Two Bone IK Constraint on the LeftArmIK object.")]
    public TwoBoneIKConstraint leftArmIK;

    [Tooltip("The IK_Target transform that the constraint follows. This must " +
             "be LeftHandTarget - NOT a grip marker. A constraint can only " +
             "follow one object, so if it pointed straight at Grip_Pin the " +
             "hand could never reach the nozzle.")]
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

    // ─────────────────────────────────────────────────────────────
    [Header("Movement Settings")]
    [Tooltip("Seconds for the hand to reach out to a grip marker. The Twist " +
             "clip waits for this to finish, so raising it delays the pin " +
             "turning too - nothing to keep in sync by hand.")]
    public float moveDuration = 0.35f;

    [Tooltip("Seconds for the hand to travel from the pin across to the nozzle.")]
    public float travelDuration = 0.5f;

    [Tooltip("How fast the IK weight fades in/out. Higher = snappier.\n\n" +
             "This is also what RELEASES the hand: ReleaseAll() drops the " +
             "target weight to 0 and the arm returns to its FK rig pose over " +
             "roughly 1/weightBlendSpeed seconds. Lower this if the hand " +
             "snaps away too abruptly after the pin drops or the fire goes " +
             "out; raise it if the release drags.")]
    public float weightBlendSpeed = 4f;

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
    //
    // KEEP THIS. Kitchen and Classroom Phase 2 still need their grip
    // markers positioned, and this is the only way to do it. It is inside
    // #if UNITY_EDITOR, so it adds nothing to the APK. Collapse the section
    // in the Inspector if it is in the way.
    //
    // NOTE: the 90-second timer will end the run and release the hand while
    // you sculpt. Raise Total Time on SimulationManager first, and put it
    // back to 90 when you are done.
    // =========================================================
    public enum SculptGrip { Pin, Nozzle }

    [Header("Editor Testing (never in phone builds)")]
    [Tooltip("Play mode, GAME tab focused: 8 = Twist, 9 = Pull, 0 = Aim, 7 = FULL RESET.")]
    public bool enableTestKeys = true;

    [Tooltip("Tick DURING Play mode to hold the hand on the chosen grip " +
             "marker so you can sculpt it. Untick to release the hand.")]
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
        // FK owns the arm until Twist hands it over. At weight 0 the
        // constraint writes nothing, so wherever ikTarget happens to sit is
        // invisible — which is why there is no longer any need to park it
        // somewhere sensible first.
        if (leftArmIK != null)
            leftArmIK.weight = 0f;

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

            Debug.Log($"[LeftHandIK] Sculpt mode ON — holding {marker.name}. " +
                      "Move it in the Scene view, then Copy Component on its Transform.");
        }
        else
        {
            ReleaseAll();
            Debug.Log("[LeftHandIK] Sculpt mode OFF — hand released.");
        }
    }

    private void TakeOverLeftHand()
    {
        if (HandAnimationController.Instance != null)
            HandAnimationController.Instance.leftHandControlledByIK = true;
    }

    private void FullTestReset()
    {
        ReleaseAll();

        if (HandAnimationController.Instance != null)
            HandAnimationController.Instance.leftHandControlledByIK = false;

        Debug.Log("[LeftHandIK] FULL RESET — hand released, FK back in charge.");
    }
#endif

    // ─────────────────────────────────────────────────────────────
    //  PUBLIC API — called by TPASSButtonManager and SimulationManager
    //
    //  onArrived is OPTIONAL. Existing calls with no argument still
    //  compile and behave exactly as before.
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reaches the hand out to Grip_Pin. onArrived fires once the hand is
    /// actually holding the pin — play the Twist clip from there so the pin
    /// does not turn in mid-air while the hand is still travelling.
    /// </summary>
    public void ReachPinAndTwist(System.Action onArrived = null)
    {
        StartExclusive(ReachAndTwistRoutine(onArrived));
    }

    public void PullPin(System.Action onArrived = null)
    {
        StartExclusive(PullRoutine(onArrived));
    }

    /// <summary>
    /// Travels the hand from wherever it is across to Grip_Nozzle.
    /// onArrived fires once it is holding the nozzle.
    /// </summary>
    public void GrabNozzle(System.Action onArrived = null)
    {
        StartExclusive(GrabNozzleRoutine(onArrived));
    }

    /// <summary>
    /// Lets go of whatever the hand is holding. The IK weight fades to 0 and
    /// the arm returns to its FK rig pose — that fade IS the release.
    ///
    /// onArrived fires immediately, since there is nothing to wait for. It
    /// means "release started", not "hand has finished lowering".
    /// </summary>
    public void ReleaseAll(System.Action onArrived = null)
    {
        StartExclusive(ReleaseRoutine(onArrived));
    }

    // ─────────────────────────────────────────────────────────────
    //  ROUTINES
    // ─────────────────────────────────────────────────────────────

    private IEnumerator ReachAndTwistRoutine(System.Action onArrived)
    {
        targetWeight = 1f;

        // Reach out and take hold of Grip_Pin. Once we arrive, set
        // currentGrip so LateUpdate keeps us glued to it. The Blender
        // Twist clip then rotates Pin, and the hand rotates with it.
        if (gripPin == null)
        {
            Debug.LogWarning("[LeftHandIK] Grip Pin is not assigned.");
            // Still fire the callback so the caller is not left waiting
            // forever on a step that can never complete.
            onArrived?.Invoke();
            yield break;
        }

        isTransitioning = true;
        yield return MoveTargetTo(gripPin, moveDuration, matchRotation: true);
        isTransitioning = false;

        currentGrip = gripPin;

        // The hand is now ON the pin. Safe to start twisting it.
        onArrived?.Invoke();
    }

    private IEnumerator PullRoutine(System.Action onArrived)
    {
        targetWeight = 1f;

        // Nothing for the hand to do. Grip_Pin is parented to Pin, and the
        // Pull clip moves Pin — so LateUpdate carries the hand out with it
        // automatically. The hand is already in place, so the callback can
        // fire immediately.
        onArrived?.Invoke();
        yield break;
    }

    private IEnumerator GrabNozzleRoutine(System.Action onArrived)
    {
        targetWeight = 1f;

        if (gripNozzle == null)
        {
            Debug.LogWarning("[LeftHandIK] Grip Nozzle is not assigned.");
            onArrived?.Invoke();
            yield break;
        }

        // TRAVEL: slide the IK target from wherever it is now across to the
        // nozzle. Both endpoints are read fresh every frame because the
        // extinguisher (and the hose) may be moving during the move.
        //
        // Note the hand may start from nowhere in particular rather than
        // from the pin, since ReleaseAll() fires after PinDrop and clears
        // currentGrip. `from` being null is handled below.
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

        // The hand is now ON the nozzle. Safe to start the hose clip.
        onArrived?.Invoke();
    }

    private IEnumerator ReleaseRoutine(System.Action onArrived)
    {
        // Stop tracking any grip so LateUpdate lets go of ikTarget.
        currentGrip = null;

        // Clear the transition flag explicitly. StartExclusive stops
        // whatever routine was running, and if that routine happened to be
        // halfway through a MoveTargetTo, this flag would be stuck true
        // forever — and LateUpdate would silently never glue the hand to a
        // marker again. Cheap insurance against a bug with no error message.
        isTransitioning = false;

        // THIS is the release. Update() fades leftArmIK.weight toward 0 at
        // weightBlendSpeed, and once the constraint stops writing, the arm
        // returns to its FK rig pose on its own.
        //
        // There used to be a travel to a restTarget marker before this line.
        // Removed in v18: the marker was never assigned, the arm ends in the
        // same FK pose either way, and all it added was a delay before the
        // fade started. If the release ever needs to be gentler, lower
        // weightBlendSpeed rather than reintroducing a parking marker.
        targetWeight = 0f;

        // Nothing to wait for, so report immediately.
        onArrived?.Invoke();
        yield break;
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
using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// -------------------------------------------------------
// KitchenValveController — the Turn Off step's choreography.
//
// Reaches the RIGHT hand to the LPG regulator valve, turns it shut, and
// releases.
//
// Read LeftHandIKController.cs and TowelGrab.cs first. This borrows the core
// idea from one and the mechanism from the other, and neither is repeated
// here in full.
//
// -------------------------------------------------------
// THE ONE IDEA THAT MAKES THIS CHEAP
//
// The hand does not produce the motion. THIS FILE rotates the valve, and the
// hand follows because ValveGrip is PARENTED TO THE VALVE:
//
//     Regulator_Valve
//       └── ValveGrip        <- the IK target
//
// Rotate the valve and the marker orbits with it. The constraint drags the
// hand along, and the wrist turns for free. One sculpt, correct through every
// frame of the turn.
//
// Exactly the same trick as Grip_Pin under Pin in Office, and GripPoint_R
// under Towel_A1 in Kitchen. Three different props, one pattern.
//
// -------------------------------------------------------
// WHY THE TARGET IS REDIRECTED RATHER THAN TRAVELLED TO
//
// LeftHandIKController moves an intermediate transform (LeftHandTarget)
// between two markers, because the left hand has to TRAVEL from the pin
// across to the nozzle. You cannot lerp a constraint's target field, so it
// lerps a transform instead.
//
// The right hand has nowhere to travel from. By the Turn Off step the towel
// has been thrown and TowelCoverController has already faded both arm IKs to
// zero, so the arm is sitting in its FK rest pose. The reach IS the weight
// fade: at 0 the arm is where the rig puts it, at 1 it is on the valve, and
// blending between the two produces the reach.
//
// So this file just repoints the constraint and fades in — the same thing
// TowelGrab and ExtinguisherGrab both do. Simpler, and consistent with the
// two grabs already in this scene.
//
// -------------------------------------------------------
// THE STRUCT DANCE — DO NOT "SIMPLIFY" THIS
//
// TwoBoneIKConstraintData is a STRUCT and .data is a property. Writing
// rightArmIK.data.target = x modifies a temporary COPY that is discarded at
// the end of the statement, and the change never reaches the constraint.
//
// It has to be pulled into a local, modified, and assigned back. Written out
// longhand in RedirectIK below for exactly that reason.
//
// -------------------------------------------------------
// FINDING THE ROTATION AXIS
//
// rotationAxis is in the valve's LOCAL space, and which axis is correct
// depends on how the model was built and how the FBX importer converted it.
//
// To find it: select Regulator_Valve in the Scene view, switch the gizmo to
// LOCAL (the toggle next to Pivot/Center in the toolbar), and rotate each
// ring in turn. The one that spins the wheel in place — rather than swinging
// it off its stem — is the axis. Type that as a 1 with zeroes in the others.
//
// If NO axis spins it in place, the mesh origin is off-centre. Fix that by
// nesting the valve under an empty positioned at the stem and rotating the
// empty instead. Do not try to compensate with an offset here.
// -------------------------------------------------------

public class KitchenValveController : MonoBehaviour
{
    [Header("What Rotates")]
    [Tooltip("Regulator_Valve — the wheel on top of the LPG tank.")]
    [SerializeField] private Transform valve;

    [Tooltip("Which of the valve's LOCAL axes it spins around. See the header " +
             "for how to find it — the wrong axis swings the wheel off its " +
             "stem instead of turning it.\n\n" +
             "Y (0,1,0) is the usual answer for a wheel lying flat on top of " +
             "a tank.")]
    [SerializeField] private Vector3 rotationAxis = Vector3.up;

    [Tooltip("How far the valve turns, in degrees.\n\n" +
             "A real LPG regulator shuts in well under a full turn, but this " +
             "is a training tool and the player needs to SEE that something " +
             "happened. 180 reads as a decisive shut without looking like the " +
             "valve is being unscrewed.")]
    [SerializeField] private float turnAngle = 180f;

    [Header("Hand IK")]
    [Tooltip("RightArmIk — the SAME constraint TowelGrab uses. Its target is " +
             "redirected to ValveGrip for this step and restored on reset.")]
    [SerializeField] private TwoBoneIKConstraint rightArmIK;

    [Tooltip("ValveGrip — an empty PARENTED TO THE VALVE, positioned where a " +
             "hand would close around the wheel.\n\n" +
             "The parenting is the whole mechanism: rotate the valve and this " +
             "marker orbits with it, dragging the hand along. Use sculptMode " +
             "below to position it.")]
    [SerializeField] private Transform valveGrip;

    [Tooltip("hand.R's FingerGripController. Leave empty to skip finger " +
             "curling — the IK still puts the hand on the wheel.")]
    [SerializeField] private FingerGripController rightHandGrip;

    [Tooltip("How closed the fingers get on the wheel. Tighter than the " +
             "towel's 0.65: a valve is hard metal and does not compress, so a " +
             "loose grip reads as the hand hovering rather than gripping.")]
    [Range(0f, 1f)]
    [SerializeField] private float holdGripAmount = 0.8f;

    [Tooltip("The RigBuilder on fps-hands-v5.\n\n" +
             "Animation Rigging compiles its constraint graph once, in " +
             "RigBuilder's own Awake, and on some versions a target swapped " +
             "after that is ignored until the graph is rebuilt.\n\n" +
             "TEST WITHOUT THIS FIRST. Assign it only if the hand reaches for " +
             "the towel's old grip point instead of the valve.")]
    [SerializeField] private RigBuilder rigBuilder;

    [Header("Timing (seconds)")]
    [Tooltip("The reach. This is a WEIGHT FADE, not a travel — the arm blends " +
             "from its FK rest pose to the IK solution on the valve. Around " +
             "0.45 reads as reaching out; much faster reads as a snap.")]
    [SerializeField] private float reachDuration = 0.45f;

    [Tooltip("The turn itself. Deliberately unhurried — this is the step where " +
             "the player is meant to understand that shutting the gas is what " +
             "actually ends the emergency, not the smothering.")]
    [SerializeField] private float turnDuration = 1.0f;

    [Tooltip("Letting go. The weight fades back to 0 and the arm returns to " +
             "its FK pose — that fade IS the release.")]
    [SerializeField] private float releaseDuration = 0.3f;

    [Tooltip("How far AHEAD of the reach the fingers start closing, 0-1. Real " +
             "hands are already forming the grip before they arrive. 0.6 means " +
             "the curl starts 60% of the way through the reach.")]
    [Range(0f, 1f)]
    [SerializeField] private float curlStartPoint = 0.6f;

    [Header("Effects (optional)")]
    [Tooltip("A hiss dying away as the gas is shut off. Optional, but it is " +
             "the clearest signal that the valve did something.")]
    [SerializeField] private ParticleSystem gasHiss;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip valveTurnClip;

#if UNITY_EDITOR
    // =========================================================
    // SCULPT MODE (editor only — stripped from builds)
    //
    // Animation Rigging constraints only evaluate in Play mode, so ValveGrip
    // can only be positioned there. But the reach lasts under half a second,
    // which is not long enough to drag a gizmo against.
    //
    // Ticking sculptMode holds the hand on the valve INDEFINITELY, so the
    // marker can be moved and rotated in the Scene view with the hand
    // following live. When it looks right:
    //
    //   right-click ValveGrip's Transform header -> Copy Component
    //   -> Stop Play -> right-click -> Paste Component Values
    //
    // Local position and rotation are what get saved, so the hand snapping
    // back to rest afterwards does not matter — the offset is what rides the
    // rotation.
    //
    // Same tool, same reasoning, same keystrokes as LeftHandIKController's.
    //
    // NOTE: the 90-second timer will end the run and release the hand while
    // you sculpt. Raise Total Time on SimulationManager first, and put it
    // back to 90 when you are done.
    // =========================================================
    [Header("Editor Testing (never in phone builds)")]
    [Tooltip("Tick DURING Play mode to hold the hand on ValveGrip so it can " +
             "be sculpted. Untick to release.")]
    [SerializeField] private bool sculptMode = false;

    private bool sculptModeWasOn = false;
#endif

    // Cached in Awake so a replay can restore both the valve and the
    // constraint exactly. Awake rather than Start because this object is
    // never disabled — Awake is simply the earliest guaranteed point.
    private Quaternion valveStartRotation;
    private Transform originalIKTarget;

    private Coroutine turnRoutine;
    private bool hasTurned = false;

    /// <summary>
    /// TRUE once the valve is shut. Read this from anything that needs to know
    /// the gas is off — an exit trigger that should refuse to end the run
    /// while the tank is still live, for instance.
    /// </summary>
    public bool HasTurned => hasTurned;

    public bool IsTurning => turnRoutine != null;

    private void Awake()
    {
        if (valve != null) valveStartRotation = valve.localRotation;
        if (rightArmIK != null) originalIKTarget = rightArmIK.data.target;
    }

#if UNITY_EDITOR
    private void Update()
    {
        HandleSculptMode();
    }

    // Holds the hand on the valve so ValveGrip can be sculpted. Does nothing
    // while the real routine is running — two systems driving one IK weight
    // is the bug this whole architecture exists to avoid.
    private void HandleSculptMode()
    {
        if (!Application.isPlaying) return;
        if (sculptMode == sculptModeWasOn) return;
        sculptModeWasOn = sculptMode;

        if (sculptMode)
        {
            if (turnRoutine != null)
            {
                StopCoroutine(turnRoutine);
                turnRoutine = null;
            }

            if (valveGrip == null)
            {
                Debug.LogWarning("[KitchenValve] Sculpt mode: ValveGrip is not assigned.");
                sculptMode = false;
                sculptModeWasOn = false;
                return;
            }

            RedirectIK(valveGrip);

            if (rightArmIK != null) rightArmIK.weight = 1f;
            if (rightHandGrip != null) rightHandGrip.SetGripAmount(holdGripAmount);

            Debug.Log("[KitchenValve] Sculpt mode ON — holding ValveGrip. " +
                      "Move it in the Scene view, then Copy Component on its Transform.");
        }
        else
        {
            if (rightArmIK != null) rightArmIK.weight = 0f;
            if (rightHandGrip != null) rightHandGrip.SetGripAmount(0f);
            RedirectIK(originalIKTarget);

            Debug.Log("[KitchenValve] Sculpt mode OFF — hand released.");
        }
    }
#endif

    // -------------------------------------------------------
    // PLAY THE TURN
    //
    // onComplete is handed EndStepLockout by SimulationManager, so the buttons
    // stay dead for exactly as long as the choreography actually runs. No
    // duration is duplicated anywhere else — the routine that owns the timing
    // owns the unlock. Same rule that fixed the Pull -> Aim bug.
    // -------------------------------------------------------
    public void PlayTurn(System.Action onComplete = null)
    {
        if (turnRoutine != null)
        {
            Debug.Log("[KitchenValve] Turn already running — ignored.");
            return;
        }

        if (valve == null || valveGrip == null)
        {
            // Fail loudly but do not hang. A silent return would leave the
            // step lockout raised until the safety valve fired, and the player
            // staring at dead buttons.
            Debug.LogError("[KitchenValve] Valve or ValveGrip not assigned — " +
                           "skipping the animation. The step still registers.");
            hasTurned = true;
            onComplete?.Invoke();
            return;
        }

#if UNITY_EDITOR
        sculptMode = false;
        sculptModeWasOn = false;
#endif

        turnRoutine = StartCoroutine(TurnRoutine(onComplete));
    }

    private IEnumerator TurnRoutine(System.Action onComplete)
    {
        // Point the arm at the valve BEFORE the weight starts climbing. At
        // weight 0 the redirect is invisible; do it after the fade begins and
        // the hand visibly snaps from one target to the other mid-reach.
        RedirectIK(valveGrip);

        // ---- 1. REACH ----
        // A weight fade, not a travel. At 0 the arm is in its FK rest pose, at
        // 1 it is solved onto the valve, and blending between the two IS the
        // reach. Same mechanism as ExtinguisherGrab and TowelGrab.
        float elapsed = 0f;
        bool curlTriggered = false;

        while (elapsed < reachDuration)
        {
            elapsed += Time.deltaTime;
            float raw = Mathf.Clamp01(elapsed / reachDuration);
            float eased = Mathf.SmoothStep(0f, 1f, raw);

            if (rightArmIK != null) rightArmIK.weight = eased;

            // Fingers start closing before the hand arrives — real hands form
            // the grip on the way in.
            if (!curlTriggered && raw >= curlStartPoint)
            {
                curlTriggered = true;
                if (rightHandGrip != null) rightHandGrip.SetGripAmount(holdGripAmount);
            }

            yield return null;
        }

        if (rightArmIK != null) rightArmIK.weight = 1f;
        if (rightHandGrip != null) rightHandGrip.SetGripAmount(holdGripAmount);

        // ---- 2. TURN ----
        // The hand needs no separate animation. ValveGrip is a CHILD of the
        // valve, so rotating the valve orbits the marker, and the constraint
        // drags the wrist around with it.
        //
        // Slerped from a cached start to a computed end rather than
        // incremented with Rotate(), so the pose is deterministic and a reset
        // can restore it exactly. An incremental rotate accumulates float
        // drift across replays.
        Quaternion from = valveStartRotation;
        Quaternion to = valveStartRotation * Quaternion.AngleAxis(turnAngle, rotationAxis.normalized);

        if (audioSource != null && valveTurnClip != null)
            audioSource.PlayOneShot(valveTurnClip);

        elapsed = 0f;

        while (elapsed < turnDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / turnDuration));

            valve.localRotation = Quaternion.Slerp(from, to, t);

            yield return null;
        }

        valve.localRotation = to;
        hasTurned = true;

        // The gas is off. Anything still hissing should stop HERE, not when
        // the button was tapped — the tank was live until this frame.
        if (gasHiss != null) gasHiss.Stop();

        Debug.Log("[KitchenValve] Valve shut — gas is off.");

        // ---- 3. RELEASE ----
        // The weight fade IS the release: once the constraint stops writing,
        // the arm returns to its FK rig pose on its own.
        elapsed = 0f;

        while (elapsed < releaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / releaseDuration));

            if (rightArmIK != null) rightArmIK.weight = Mathf.Lerp(1f, 0f, t);

            yield return null;
        }

        if (rightArmIK != null) rightArmIK.weight = 0f;
        if (rightHandGrip != null) rightHandGrip.SetGripAmount(0f);

        // Put the constraint back where it was, so nothing downstream inherits
        // an arm still pointed at a valve on the far side of the kitchen.
        RedirectIK(originalIKTarget);

        turnRoutine = null;
        onComplete?.Invoke();
    }

    // -------------------------------------------------------
    // IK TARGET SWAP
    //
    // See the header. TwoBoneIKConstraintData is a struct and .data is a
    // property, so this cannot be shortened to rightArmIK.data.target = x —
    // that writes to a temporary copy and is silently lost.
    // -------------------------------------------------------
    private void RedirectIK(Transform target)
    {
        if (rightArmIK == null || target == null) return;

        var data = rightArmIK.data;
        data.target = target;
        rightArmIK.data = data;

        if (rigBuilder != null) rigBuilder.Build();
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // Call from wherever the Kitchen run resets, alongside the five towel
    // resets and WCTLButtonManager.ResetForReplay().
    //
    // Without it, a second attempt starts with the valve ALREADY SHUT. The
    // step still registers and the log still says correct — but the player
    // turns a valve that is visibly already closed, and the one action that
    // actually ends a gas fire stops being demonstrated.
    //
    // The IK restore matters as much. Leave the constraint pointed at
    // ValveGrip and the next run's towel grab would fade the right arm in
    // toward the LPG tank instead of the towel.
    // -------------------------------------------------------
    public void ResetToStart()
    {
        if (turnRoutine != null)
        {
            StopCoroutine(turnRoutine);
            turnRoutine = null;
        }

#if UNITY_EDITOR
        sculptMode = false;
        sculptModeWasOn = false;
#endif

        hasTurned = false;

        if (valve != null) valve.localRotation = valveStartRotation;

        if (rightArmIK != null) rightArmIK.weight = 0f;
        if (rightHandGrip != null) rightHandGrip.SetGripAmount(0f);

        RedirectIK(originalIKTarget);

        Debug.Log("[KitchenValve] Reset — valve open, hand released.");
    }
}
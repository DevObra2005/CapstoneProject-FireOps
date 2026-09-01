using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// -------------------------------------------------------
// TowelGrab — the Kitchen counterpart to ExtinguisherGrab.
//
// When the player correctly taps the towel draped over the timba during
// Step 1 (GrabTowel), this flies it from the bucket to TowelAnchor (a child
// of PlayerCamera) and parents it there, so it follows the player's view from
// then on.
//
// Read ExtinguisherGrab.cs first. The flight, the IK weight fade and the
// finger curl all work identically and the reasoning is not repeated here.
//
// -------------------------------------------------------
// THE THREE DIFFERENCES FROM THE EXTINGUISHER
//
// 1. TWO HANDS, NOT ONE.
//    An extinguisher is carried in one hand by a moulded handle. A towel is
//    held spread between both hands, so there are two IK constraints, two
//    grip targets and two sets of fingers to curl.
//
// 2. THE SHAPE CHANGES ON PICKUP.
//    The extinguisher is rigid — the same object on the wall and in the hand.
//    A towel is not. Draped over a rim it hangs in an inverted V; gathered in
//    a fist it bunches tighter.
//
//    So the grab fires a Grab trigger on the Animator, blending Towel_Draped
//    into Towel_Idle across the flight. The two poses are close by design —
//    Draped is A1 -30 / A2 -60, Idle is A1 -45 / A2 -70 — so the blend has
//    little distance to cover and reads as the cloth being gathered rather
//    than as a pop.
//
// 3. THE IK TARGETS ARE SWAPPED AT RUNTIME.  <-- see below
//
// -------------------------------------------------------
// WHY THE TARGETS ARE SWAPPED RATHER THAN REPARENTED
//
// There is ONE pair of arm constraints on the player, and TWO props that want
// them. The extinguisher's constraints already point at its own grip markers.
// The towel needs them pointing at GripPoint_R and GripPoint_L instead.
//
// The obvious fix — move the extinguisher's target objects under the towel's
// bones — would break the extinguisher. The two props are never held at once,
// but they are both in the same scene and both expect their targets intact.
//
// So this script REMEMBERS what each constraint pointed at, redirects it to
// the towel's grip markers on grab, and puts it back on reset. Nothing is
// moved, nothing is reparented, and Office's rig is untouched.
//
// NOTE ON THE STRUCT DANCE. TwoBoneIKConstraintData is a STRUCT, and .data is
// a property. Writing rightArmIK.data.target = x modifies a temporary COPY
// that is discarded the moment the statement ends — the change never reaches
// the constraint. It has to be pulled into a local, modified, and assigned
// back. The compiler catches this in most forms but not all, so the pattern
// is written out explicitly at every site below.
//
// -------------------------------------------------------
// WHERE THE GRIP MARKERS GO, AND WHY IT MATTERS
//
// GripPoint_R and GripPoint_L are parented to Towel_A1 and Towel_B1 — the two
// INNER bones. That placement is deliberate: those bones are the towel's
// centre-left and centre-right, exactly where hands close around cloth.
//
// Because the markers ride the BONES rather than the object, they inherit
// BOTH the transform lerps in TowelDipController AND the bone animation from
// the Animator. The hands stay welded to the fabric whether it is swinging
// into the bucket or unfurling mid-throw, with no extra clips and nothing to
// keep in sync.
//
// -------------------------------------------------------
// ONE OBJECT, NOT TWO
//
// An earlier version used a draped prop plus a hidden held towel, swapped on
// tap. This replaces it. The single-object version needs TowelDipController
// and TowelCoverController to capture their rest pose LAZILY rather than in
// Start() — both now do. See their headers.
// -------------------------------------------------------

public class TowelGrab : MonoBehaviour
{
    [Header("Where the towel ends up (in the hands)")]
    [Tooltip("Drag TowelAnchor here.")]
    [SerializeField] private Transform holdPoint;

    [Header("Grab Motion")]
    [SerializeField] private float grabDuration = 0.5f;

    [Header("Bone Animation")]
    [Tooltip("The towel's Animator, running AC_Towel. Leave empty to search " +
             "this object and its children.")]
    [SerializeField] private Animator animator;

    [Tooltip("Trigger on AC_Towel that blends Towel_Draped into Towel_Idle. " +
             "Must match the parameter name EXACTLY — a mismatch fails " +
             "silently and Unity logs nothing.\n\n" +
             "Set that transition's duration to roughly grabDuration so the " +
             "shape change finishes as the towel arrives.")]
    [SerializeField] private string grabTriggerName = "Grab";

    [Header("Hand Grip")]
    [Tooltip("Drag hand.R here — the same FingerGripController the " +
             "extinguisher uses. Leave empty to skip finger curling.")]
    [SerializeField] private FingerGripController rightHandGrip;

    [Tooltip("Drag hand.L here. Leave empty to skip finger curling.")]
    [SerializeField] private FingerGripController leftHandGrip;

    [Tooltip("How closed the fingers become when holding the towel.\n\n" +
             "Lower than the extinguisher's 0.8 on purpose. Cloth compresses " +
             "under the fingers, so a full curl reads as gripping nothing; a " +
             "looser hold reads as fabric bunching in the palm.")]
    [Range(0f, 1f)]
    [SerializeField] private float holdGripAmount = 0.65f;

    [Tooltip("Start curling BEFORE the towel lands, so the hands look like " +
             "they are reaching to take it. 0 = curl only on arrival.")]
    [Range(0f, 1f)]
    [SerializeField] private float curlStartPoint = 0.6f;

    [Header("Arm IK")]
    [Tooltip("Drag RightArmIk here — the SAME constraint the extinguisher " +
             "uses. Its target is swapped to the towel's grip marker on grab " +
             "and restored on reset.")]
    [SerializeField] private TwoBoneIKConstraint rightArmIK;

    [Tooltip("Drag LeftArmIk here.")]
    [SerializeField] private TwoBoneIKConstraint leftArmIK;

    [Tooltip("Leave ticked so the arms keep their natural rest pose until the " +
             "towel is actually within reach. Without it, both arms stretch " +
             "toward the timba from across the kitchen and look broken.")]
    [SerializeField] private bool forceIKOffBeforeGrab = true;

    [Header("Grip Targets (on the towel's bones)")]
    [Tooltip("GripPoint_R — the empty parented under Towel_A1. RightArmIk is " +
             "redirected here for the duration of the run.\n\n" +
             "Leave empty to skip the swap entirely, in which case the arms " +
             "will reach for whatever the constraint already points at.")]
    [SerializeField] private Transform gripTargetRight;

    [Tooltip("GripPoint_L — the empty parented under Towel_B1.")]
    [SerializeField] private Transform gripTargetLeft;

    [Tooltip("The RigBuilder on the player's hand rig.\n\n" +
             "Animation Rigging compiles its constraint graph once, in " +
             "RigBuilder's own Awake. On some versions a target swapped after " +
             "that point is ignored until the graph is rebuilt.\n\n" +
             "TEST WITHOUT THIS FIRST. If the hands reach correctly, leave it " +
             "empty — Build() is not free and calling it needlessly costs a " +
             "frame hitch at the exact moment the player is watching. Assign " +
             "it only if the arms ignore the towel and keep pointing at the " +
             "extinguisher's markers.")]
    [SerializeField] private RigBuilder rigBuilder;

    private int grabTriggerHash;
    private bool grabbed = false;

    // What the constraints pointed at before the towel took them. Captured in
    // Awake, restored on reset, so Office's extinguisher rig is untouched.
    private Transform originalTargetRight;
    private Transform originalTargetLeft;

    // Where the towel started, so a replay can put it back on the timba.
    private Transform originalParent;
    private Vector3 originalPosition;
    private Quaternion originalRotation;

    /// <summary>
    /// TRUE once the towel is in the player's hands. Read this before allowing
    /// any step that assumes the player is holding something.
    /// </summary>
    public bool IsGrabbed => grabbed;

    private void Awake()
    {
        originalParent = transform.parent;
        originalPosition = transform.position;
        originalRotation = transform.rotation;

        if (rightArmIK != null) originalTargetRight = rightArmIK.data.target;
        if (leftArmIK != null) originalTargetLeft = leftArmIK.data.target;
    }

    private void Start()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        grabTriggerHash = Animator.StringToHash(grabTriggerName);

        // Make sure IK is OFF at the start of Phase 2. Without this, both arms
        // reach for the towel while it is still on the timba, which looks
        // stretched and broken.
        if (forceIKOffBeforeGrab)
        {
            if (rightArmIK != null) rightArmIK.weight = 0f;
            if (leftArmIK != null) leftArmIK.weight = 0f;
        }
    }

    // -------------------------------------------------------
    // PUBLIC: called when the player correctly grabs (Step 1).
    // KitchenInteractable calls this.
    // -------------------------------------------------------
    public void Grab()
    {
        if (grabbed) return;

        if (holdPoint == null)
        {
            Debug.LogWarning("[TowelGrab] No holdPoint assigned!");
            return;
        }

        grabbed = true;
        StartCoroutine(GrabRoutine());
    }

    private IEnumerator GrabRoutine()
    {
        // Point the arms at the towel BEFORE anything fades in. The weight is
        // still 0 at this instant, so redirecting is invisible — do it after
        // the fade starts and the hands visibly snap from one target to the
        // other mid-reach.
        RedirectIKToTowel();

        // Start the shape change immediately, so the cloth is already
        // gathering as it leaves the rim rather than arriving draped and
        // popping into the held pose.
        if (animator != null)
            animator.SetTrigger(grabTriggerHash);

        // Remember where the towel starts (on the timba).
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        float elapsed = 0f;
        bool curlTriggered = false;

        while (elapsed < grabDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / grabDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            // Fly toward the hold point's CURRENT world pose (the anchor moves
            // with the camera).
            transform.position = Vector3.Lerp(startPos, holdPoint.position, eased);
            transform.rotation = Quaternion.Slerp(startRot, holdPoint.rotation, eased);

            // Fade both arms in over the same curve. They lift toward the
            // towel as it approaches rather than snapping onto it.
            if (rightArmIK != null) rightArmIK.weight = eased;
            if (leftArmIK != null) leftArmIK.weight = eased;

            // Begin curling partway through, so the hands are already closing
            // as the towel arrives.
            if (!curlTriggered && t >= curlStartPoint)
            {
                curlTriggered = true;
                if (rightHandGrip != null) rightHandGrip.SetGripAmount(holdGripAmount);
                if (leftHandGrip != null) leftHandGrip.SetGripAmount(holdGripAmount);
            }

            yield return null;
        }

        // Snap exactly to the hold point, then parent to it.
        transform.position = holdPoint.position;
        transform.rotation = holdPoint.rotation;
        transform.SetParent(holdPoint);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // Safety net: guarantee the final state regardless of how the loop
        // ended (e.g. a very short grabDuration).
        if (rightArmIK != null) rightArmIK.weight = 1f;
        if (leftArmIK != null) leftArmIK.weight = 1f;

        if (rightHandGrip != null) rightHandGrip.SetGripAmount(holdGripAmount);
        if (leftHandGrip != null) leftHandGrip.SetGripAmount(holdGripAmount);

        Debug.Log("[TowelGrab] Towel is now in hand. Both arm IKs active.");
    }

    // -------------------------------------------------------
    // IK TARGET SWAP
    //
    // TwoBoneIKConstraintData is a STRUCT and .data is a property, so
    // constraint.data.target = x writes to a temporary copy and is lost. Pull
    // it out, change it, assign it back. Written out longhand deliberately —
    // this is the single easiest thing in the file to "simplify" into
    // something that silently does nothing.
    // -------------------------------------------------------
    private void RedirectIKToTowel()
    {
        bool changed = false;

        if (rightArmIK != null && gripTargetRight != null)
        {
            var data = rightArmIK.data;
            data.target = gripTargetRight;
            rightArmIK.data = data;
            changed = true;
        }

        if (leftArmIK != null && gripTargetLeft != null)
        {
            var data = leftArmIK.data;
            data.target = gripTargetLeft;
            leftArmIK.data = data;
            changed = true;
        }

        if (changed) RebuildRigIfNeeded();
    }

    private void RestoreOriginalIKTargets()
    {
        bool changed = false;

        if (rightArmIK != null && originalTargetRight != null)
        {
            var data = rightArmIK.data;
            data.target = originalTargetRight;
            rightArmIK.data = data;
            changed = true;
        }

        if (leftArmIK != null && originalTargetLeft != null)
        {
            var data = leftArmIK.data;
            data.target = originalTargetLeft;
            leftArmIK.data = data;
            changed = true;
        }

        if (changed) RebuildRigIfNeeded();
    }

    // Only fires if a RigBuilder was assigned. See that field's tooltip —
    // most setups do not need this, and Build() is expensive enough that it
    // should not run on spec.
    private void RebuildRigIfNeeded()
    {
        if (rigBuilder == null) return;

        rigBuilder.Build();
        Debug.Log("[TowelGrab] Rig graph rebuilt after target swap.");
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // The towel has been REPARENTED to the anchor and its world pose
    // overwritten, so a reset has to put it back on the timba — not just
    // restore a local position.
    //
    // Restoring the IK targets matters as much as the towel. Leave them
    // pointing at GripPoint_R and GripPoint_L and the Office extinguisher's
    // grab would reach for a towel that is no longer in the player's hands.
    //
    // Call alongside TowelWetnessController.ResetToDry(),
    // TowelDipController.ResetToRest(), TowelCoverController.ResetToRest(),
    // KitchenInteractable.ResetForReplay() and
    // WCTLButtonManager.ResetForReplay().
    // -------------------------------------------------------
    public void ResetToTimba()
    {
        StopAllCoroutines();

        grabbed = false;

        transform.SetParent(originalParent, true);
        transform.position = originalPosition;
        transform.rotation = originalRotation;

        if (rightArmIK != null) rightArmIK.weight = 0f;
        if (leftArmIK != null) leftArmIK.weight = 0f;

        if (rightHandGrip != null) rightHandGrip.SetGripAmount(0f);
        if (leftHandGrip != null) leftHandGrip.SetGripAmount(0f);

        RestoreOriginalIKTargets();

        if (animator != null)
        {
            animator.ResetTrigger(grabTriggerHash);
            animator.Play("Armature|Towel_Draped", 0, 0f);
        }

        Debug.Log("[TowelGrab] Towel reset to the timba.");
    }
}
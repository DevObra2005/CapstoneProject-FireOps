using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// -------------------------------------------------------
// TowelCoverController — the Cover step's choreography.
//
// Arcs the wet towel out of the player's hands and down onto the burning
// regulator, then LEAVES IT THERE.
//
// Same transform-lerp approach as TowelDipController. Read that file's header
// first — the four realism rules apply here unchanged and are not repeated.
//
// -------------------------------------------------------
// THE SPLIT: THIS FILE MOVES THE TOWEL, THE BONES CHANGE ITS SHAPE
//
//   THIS FILE owns TRAVEL:  where the towel is in the world, its rotation,
//                           the arc, the unparenting, the landing position.
//   THE ANIMATOR owns SHAPE: unfurl, contact overshoot, edges wrapping down
//                           over the tank, the settle ripple.
//
// WHY NOT BAKE THE TRAVEL INTO THE CLIP TOO
//
// 1. OVERLAP. Baked together, travel and shape share one timeline and stop at
//    the same instant. Frozen-on-contact is the single clearest tell that
//    something is animated rather than simulated. Split, the travel finishes
//    while the cloth is still settling — and that lag is what sells it.
//
// 2. WORLD COORDINATES. A clip carrying travel carries the tank's POSITION.
//    Move the LPG assembly two feet during scene dressing and the animation
//    throws the towel at empty air. This file computes travel at runtime from
//    coverTarget's actual transform, so it survives any layout change.
//
// -------------------------------------------------------
// WHAT WAS REMOVED, AND WHY
//
// The old landingSquash / landingSpread fields scaled the whole transform to
// fake a drape. That is gone.
//
// Scaling compresses every part of the mesh by the same factor, which is how
// RUBBER behaves. Cloth does the opposite: the centre stops on contact while
// the edges keep travelling and fall past it. Towel_Cover frames 20 to 40
// already animate exactly that — the +5 degree overshoot at contact, the
// edges rotating past flat at 32, the recovery at 40.
//
// Running both at once meant the bones deforming a mesh that was
// simultaneously being squashed. Neither read clearly.
//
// The settle PHASE stayed, because something has to hold the step lockout
// while the bone animation finishes. It is now a wait rather than a lerp.
//
// -------------------------------------------------------
// THE REST POSE IS CAPTURED LAZILY — SAME REASON AS THE DIP
//
// The towel is now ONE object that starts draped on the timba. Start() runs
// while it is still on the bucket, so caching the "rest" pose there would
// store the timba's parent and coordinates — and a replay would put the towel
// back into the player's hands using numbers that mean nothing.
//
// Captured on the first PlayCover call instead, by which point TowelGrab has
// parented it to TowelAnchor at (0,0,0).
// -------------------------------------------------------

public class TowelCoverController : MonoBehaviour
{
    [Header("What Moves")]
    [Tooltip("The towel transform. By the time the Cover step runs it will be " +
             "parented under TowelAnchor on the PlayerCamera.\n\n" +
             "Leave empty to move THIS object.")]
    [SerializeField] private Transform towel;

    [Header("Bone Animation")]
    [Tooltip("The towel's Animator, running AC_Towel. Leave empty to search " +
             "this object and its children.\n\n" +
             "Null is survivable: the towel still travels and lands, it just " +
             "keeps its idle shape the whole way. Worth knowing when debugging " +
             "a throw that looks stiff.")]
    [SerializeField] private Animator animator;

    [Tooltip("Trigger on AC_Towel that starts Towel_Cover. Must match the " +
             "parameter name EXACTLY — a mismatch fails silently and Unity " +
             "logs nothing.")]
    [SerializeField] private string coverTriggerName = "Cover";

    [Header("Where It Lands")]
    [Tooltip("An empty GameObject positioned and rotated where the towel " +
             "should come to rest on the tank collar.\n\n" +
             "Place it BY EYE in the Scene view — the regulator is a lumpy " +
             "target and no set of typed numbers will beat dragging it into " +
             "position. Sit it a centimetre or two proud of the geometry so " +
             "the cloth does not clip through the regulator body.")]
    [SerializeField] private Transform coverTarget;

    [Tooltip("What the towel is parented to after landing. Usually the LPG " +
             "assembly root, so the cover moves with the tank if it is ever " +
             "repositioned.\n\n" +
             "Leave empty to unparent into world space instead.")]
    [SerializeField] private Transform parentAfterLanding;

    [Header("Motion Shape")]
    [Tooltip("How far the towel bows UP on its way across. You lift a cloth " +
             "over a flame and lower it, rather than sliding it in flat — the " +
             "arc is what makes that read.\n\n" +
             "Larger than the dip's arc on purpose: this is a bigger, more " +
             "deliberate movement.")]
    [SerializeField] private float arcHeight = 0.25f;

    [Tooltip("Which way the arc bows, in WORLD space. Up (0,1,0) is almost " +
             "always right here.")]
    [SerializeField] private Vector3 arcDirection = Vector3.up;

    [Tooltip("How far AHEAD of the position the rotation runs, 0-0.4. The " +
             "cloth turns flat before it arrives, which is what hands do when " +
             "laying something down.")]
    [Range(0f, 0.4f)]
    [SerializeField] private float rotationLead = 0.2f;

    [Header("Timing (seconds)")]
    [Tooltip("The throw. Deliberately not fast — this is a careful placement " +
             "over a flame, not a toss.\n\n" +
             "SHOULD MATCH THE CLIP'S CONTACT BEAT. Towel_Cover is 40 frames " +
             "at 24fps and its contact pose is frame 20, so the bones reach " +
             "contact at 0.83s. 0.8 here lands the towel as the cloth arrives. " +
             "Change one and check the other.")]
    [SerializeField] private float travelDuration = 0.8f;

    [Tooltip("How long the lockout is held AFTER contact while the bone " +
             "animation finishes its wrap and settle.\n\n" +
             "Towel_Cover runs frames 20 to 40 after contact — 20 frames at " +
             "24fps = 0.83s. Set shorter and the buttons unlock while the " +
             "cloth is still visibly moving; set longer and the player waits " +
             "on nothing.")]
    [SerializeField] private float postContactHold = 0.85f;

    [Header("Hand IK Release")]
    [Tooltip("The same TwoBoneIKConstraint assigned to TowelGrab's rightArmIK. " +
             "Its weight is eased to zero as the towel leaves the hands.\n\n" +
             "WITHOUT THIS, GripPoint_R travels with the towel and drags the " +
             "player's right arm across the room after it.")]
    [SerializeField] private TwoBoneIKConstraint rightArmIK;

    [Tooltip("The same TwoBoneIKConstraint assigned to TowelGrab's leftArmIK.")]
    [SerializeField] private TwoBoneIKConstraint leftArmIK;

    [Tooltip("Both hands, opened as the towel is released. Leave empty to skip.")]
    [SerializeField] private FingerGripController rightHandGrip;
    [SerializeField] private FingerGripController leftHandGrip;

    [Tooltip("Seconds to ease the grip off. A snap to zero teleports the arms " +
             "back to idle; a short fade reads as letting go.")]
    [SerializeField] private float gripReleaseDuration = 0.25f;

    [Header("Effects")]
    [Tooltip("Smoke or steam puffing out from under the cloth on contact. " +
             "Optional, but it is the clearest signal that the flame is being " +
             "starved rather than simply hidden.")]
    [SerializeField] private ParticleSystem smotherPuff;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip coverClip;

    // Captured on the FIRST PlayCover call. See the header.
    private Transform restParent;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation;
    private Vector3 restLocalScale;
    private bool restCaptured = false;

    private int coverTriggerHash;

    private Coroutine coverRoutine;
    private Coroutine gripRoutine;
    private bool hasCovered = false;

    /// <summary>
    /// TRUE once the towel is lying on the tank. Useful for anything that
    /// needs to know the player's hands are empty — the valve reach, for
    /// instance.
    /// </summary>
    public bool HasCovered => hasCovered;

    public bool IsCovering => coverRoutine != null;

    private void Start()
    {
        if (towel == null) towel = transform;
        if (animator == null) animator = GetComponentInChildren<Animator>();

        coverTriggerHash = Animator.StringToHash(coverTriggerName);
        // NO rest-pose caching here. See the header.
    }

    private void CaptureRestIfNeeded()
    {
        if (restCaptured) return;

        restParent = towel.parent;
        restLocalPosition = towel.localPosition;
        restLocalRotation = towel.localRotation;
        restLocalScale = towel.localScale;
        restCaptured = true;

        Debug.Log($"[TowelCover] Rest pose captured under {restParent?.name}.");
    }

    // -------------------------------------------------------
    // PLAY THE COVER
    //
    // onContact — the instant the cloth reaches the flame. Fire dies here.
    // onComplete — the settle has finished and the buttons should unlock.
    //
    // Two callbacks rather than one, because the extinguish must fire at the
    // exact frame the cloth touches the flame — not when the button was
    // tapped, and not when the whole animation finishes. Either of those reads
    // wrong: one kills the fire before anything reaches it, the other leaves
    // it burning through a cloth already lying on top of it.
    // -------------------------------------------------------
    public void PlayCover(System.Action onContact = null, System.Action onComplete = null)
    {
        if (coverRoutine != null)
        {
            Debug.Log("[TowelCover] Cover already running — ignored.");
            return;
        }

        if (coverTarget == null)
        {
            // Fail loudly but do not hang. A missing target with a silent
            // return would leave the step lockout raised until the safety
            // valve fired, and the player staring at dead buttons.
            Debug.LogError("[TowelCover] No coverTarget assigned — skipping the " +
                           "animation. The fire will still go out, but the towel " +
                           "will not move.");
            onContact?.Invoke();
            onComplete?.Invoke();
            return;
        }

        if (towel == null) towel = transform;

        CaptureRestIfNeeded();

        coverRoutine = StartCoroutine(CoverRoutine(onContact, onComplete));
    }

    private IEnumerator CoverRoutine(System.Action onContact, System.Action onComplete)
    {
        // ---- START THE BONE ANIMATION ----
        // Fired BEFORE the unparent, so the cloth is already unfurling as it
        // leaves the hands rather than starting flat and popping into shape.
        //
        // The Idle -> Cover transition in AC_Towel has Has Exit Time OFF, so
        // this takes effect on the next frame rather than waiting for the idle
        // loop to finish. If the towel visibly delays before moving, that
        // checkbox is the first thing to look at.
        if (animator != null)
            animator.SetTrigger(coverTriggerHash);

        // ---- RELEASE THE GRIP ----
        // Started here, not after landing. The IK targets are parented to the
        // towel's bones, so the instant it unparents they travel with it — and
        // the arms would be dragged along behind.
        if (gripRoutine != null) StopCoroutine(gripRoutine);
        gripRoutine = StartCoroutine(ReleaseGripRoutine());

        // UNPARENT. From here the towel travels in world space, so turning the
        // camera mid-throw no longer drags it sideways.
        //
        // worldPositionStays: true keeps it exactly where it appears right now
        // rather than snapping to the origin.
        towel.SetParent(null, true);

        Vector3 startPos = towel.position;
        Quaternion startRot = towel.rotation;

        Vector3 endPos = coverTarget.position;
        Quaternion endRot = coverTarget.rotation;

        Vector3 arc = arcDirection.normalized * arcHeight;

        if (audioSource != null && coverClip != null)
            audioSource.PlayOneShot(coverClip);

        // ---- TRAVEL ----
        float elapsed = 0f;

        while (elapsed < travelDuration)
        {
            elapsed += Time.deltaTime;
            float raw = Mathf.Clamp01(elapsed / travelDuration);

            float t = Mathf.SmoothStep(0f, 1f, raw);

            // Rotation leads, so the cloth is already flat when it arrives.
            float rotT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(raw + rotationLead));

            // Sine bulge peaks mid-travel and returns to zero at both ends, so
            // the towel rises over the flame and comes down onto it.
            float bulge = Mathf.Sin(raw * Mathf.PI);

            towel.position = Vector3.Lerp(startPos, endPos, t) + arc * bulge;
            towel.rotation = Quaternion.Slerp(startRot, endRot, rotT);

            yield return null;
        }

        towel.position = endPos;
        towel.rotation = endRot;

        // ---- CONTACT ----
        // The cloth is on the flame. Everything that depends on that moment
        // happens on this line and not before it.
        hasCovered = true;

        if (smotherPuff != null) smotherPuff.Play();

        onContact?.Invoke();

        Debug.Log("[TowelCover] Contact — cloth is on the flame.");

        // Parent to the tank AFTER landing, so the cover travels with the
        // assembly if it is ever repositioned. Done here rather than before
        // the travel so a moving tank cannot drag the towel through its throw.
        if (parentAfterLanding != null)
            towel.SetParent(parentAfterLanding, true);

        // ---- SETTLE ----
        // The towel has physically arrived. The BONES are still working: edges
        // rotating past flat, overshooting, easing back. That is the whole
        // point of splitting travel from shape — the cloth keeps moving after
        // the object has stopped, which is what real fabric does.
        //
        // A plain wait, because this file no longer has anything to animate
        // here. It is holding the step lockout, nothing more.
        yield return new WaitForSeconds(postContactHold);

        coverRoutine = null;
        onComplete?.Invoke();

        Debug.Log("[TowelCover] Cover complete — hands are free for the valve.");
    }

    // -------------------------------------------------------
    // GRIP RELEASE
    //
    // Eases both IK constraints' weight to zero. Weight is a 0-1 blend between
    // "the hand follows its target" and "the hand does whatever the base
    // animation says", so fading it is exactly the same shape as a crossfade.
    //
    // The fingers open at the same time. A hand that stays clenched around
    // nothing after letting go is a small thing, but it is visible whenever
    // the player looks down during the Turn Off step.
    // -------------------------------------------------------
    private IEnumerator ReleaseGripRoutine()
    {
        float rightStart = rightArmIK != null ? rightArmIK.weight : 0f;
        float leftStart = leftArmIK != null ? leftArmIK.weight : 0f;

        float elapsed = 0f;

        while (elapsed < gripReleaseDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / gripReleaseDuration));

            if (rightArmIK != null) rightArmIK.weight = Mathf.Lerp(rightStart, 0f, t);
            if (leftArmIK != null) leftArmIK.weight = Mathf.Lerp(leftStart, 0f, t);

            yield return null;
        }

        if (rightArmIK != null) rightArmIK.weight = 0f;
        if (leftArmIK != null) leftArmIK.weight = 0f;

        if (rightHandGrip != null) rightHandGrip.SetGripAmount(0f);
        if (leftHandGrip != null) leftHandGrip.SetGripAmount(0f);

        gripRoutine = null;
    }

    // -------------------------------------------------------
    // RESET FOR A REPLAY
    //
    // Note what this does NOT do: it does not put the towel back on the timba.
    // That belongs to TowelGrab.ResetToTimba(), which is what moved it there
    // in the first place. This only undoes what THIS file changed — the
    // reparent to the tank and the pose it left behind.
    //
    // Call both. Order does not matter, because TowelGrab writes a WORLD pose
    // and overrides whatever this restores.
    //
    // The Animator is reset too. A Trigger auto-clears once consumed, but a
    // run abandoned mid-throw leaves the state machine sitting in Towel_Cover,
    // and the next run would start with the towel already in its landed shape.
    // -------------------------------------------------------
    public void ResetToRest()
    {
        if (coverRoutine != null)
        {
            StopCoroutine(coverRoutine);
            coverRoutine = null;
        }

        if (gripRoutine != null)
        {
            StopCoroutine(gripRoutine);
            gripRoutine = null;
        }

        if (smotherPuff != null) smotherPuff.Stop();

        hasCovered = false;

        if (animator != null)
        {
            animator.ResetTrigger(coverTriggerHash);
            animator.Play("Armature|Towel_Draped", 0, 0f);
        }

        if (towel != null && restCaptured)
        {
            towel.SetParent(restParent, false);
            towel.localPosition = restLocalPosition;
            towel.localRotation = restLocalRotation;
            towel.localScale = restLocalScale;
        }

        restCaptured = false;

        Debug.Log("[TowelCover] Reset.");
    }
}
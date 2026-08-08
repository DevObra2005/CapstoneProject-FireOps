using System.Collections;
using UnityEngine;
using UnityEngine.Animations.Rigging;

// -------------------------------------------------------
// WHAT THIS DOES:
// When the player correctly taps the wall extinguisher during
// Step 2 (Grab), this flies it from the wall to the hold point
// (ExtinguisherAnchor, a child of PlayerCamera) and parents it
// there, so it follows the player's view from then on.
//
// It also handles two things that must happen at the same moment:
//
//   1. FINGER CURL - the right hand closes as the extinguisher
//      arrives, instead of being clenched around nothing.
//
//   2. IK WEIGHT FADE - this is the new part.
//
// WHY THE IK WEIGHT MATTERS:
// RightArmIK makes the arm chase Grip_Handle, which lives on the
// extinguisher. Before the grab, the extinguisher is across the
// room, so the arm would stretch out toward the wall and look
// deformed. Setting weight to 0 turns IK off and lets the arm
// keep its natural rest pose.
//
// So the weight starts at 0 and fades to 1 during the flight.
// By the time the extinguisher arrives, IK is fully in control
// and the hand is locked to the handle. Fading rather than
// snapping means the arm REACHES for the extinguisher instead
// of teleporting onto it.
//
// This script goes on the Phase 2 extinguisher (FireExtinguisher_ABC).
// It is triggered by SimulationInteractable on a correct Step 2 tap.
// -------------------------------------------------------

public class ExtinguisherGrab : MonoBehaviour
{
    [Header("Where the extinguisher ends up (in the hands)")]
    [Tooltip("Drag ExtinguisherAnchor here.")]
    [SerializeField] private Transform holdPoint;

    [Header("Grab Motion")]
    [SerializeField] private float grabDuration = 0.5f;

    [Header("Right Hand Grip")]
    [Tooltip("Drag hand.R here. Leave empty to skip finger curling.")]
    [SerializeField] private FingerGripController rightHandGrip;

    [Tooltip("How closed the fingers become when holding the extinguisher.")]
    [Range(0f, 1f)]
    [SerializeField] private float holdGripAmount = 0.8f;

    [Tooltip("Start curling BEFORE the extinguisher lands, so the hand " +
             "looks like it is reaching to take it. 0 = curl only on arrival.")]
    [Range(0f, 1f)]
    [SerializeField] private float curlStartPoint = 0.6f;

    [Header("Right Arm IK")]
    [Tooltip("Drag RightArmIK here. Its weight fades from 0 to 1 during " +
             "the grab, so the arm reaches for the extinguisher rather " +
             "than stretching toward the wall beforehand.")]
    [SerializeField] private TwoBoneIKConstraint rightArmIK;

    [Tooltip("Leave ticked so the arm keeps its natural rest pose until " +
             "the extinguisher is actually within reach.")]
    [SerializeField] private bool forceIKOffBeforeGrab = true;

    // Has the grab already happened? (Prevents grabbing twice.)
    private bool grabbed = false;

    private void Start()
    {
        // Make sure IK is OFF at the start of Phase 2. Without this,
        // the arm reaches for the extinguisher while it is still on
        // the wall, which looks stretched and broken.
        if (forceIKOffBeforeGrab && rightArmIK != null)
            rightArmIK.weight = 0f;
    }

    // -------------------------------------------------------
    // PUBLIC: called when the player correctly grabs (Step 2).
    // SimulationInteractable will call this.
    // -------------------------------------------------------
    public void Grab()
    {
        if (grabbed) return;

        if (holdPoint == null)
        {
            Debug.LogWarning("[ExtinguisherGrab] No holdPoint assigned!");
            return;
        }

        grabbed = true;
        StartCoroutine(GrabRoutine());
    }

    private IEnumerator GrabRoutine()
    {
        // Remember where the extinguisher starts (on the wall).
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        float elapsed = 0f;
        bool curlTriggered = false;

        while (elapsed < grabDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / grabDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t); // ease in/out

            // Fly toward the hold point's CURRENT world pose
            // (the anchor moves with the camera).
            transform.position = Vector3.Lerp(startPos, holdPoint.position, eased);
            transform.rotation = Quaternion.Slerp(startRot, holdPoint.rotation, eased);

            // Fade the IK in over the same curve. The arm lifts toward
            // the extinguisher as it approaches, rather than snapping
            // onto it the instant it lands.
            if (rightArmIK != null)
                rightArmIK.weight = eased;

            // Begin curling the fingers partway through, so the hand is
            // already closing as the extinguisher arrives.
            if (!curlTriggered && t >= curlStartPoint)
            {
                curlTriggered = true;
                if (rightHandGrip != null)
                    rightHandGrip.SetGripAmount(holdGripAmount);
            }

            yield return null;
        }

        // Snap exactly to the hold point, then parent to it.
        transform.position = holdPoint.position;
        transform.rotation = holdPoint.rotation;
        transform.SetParent(holdPoint);

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // Safety net: guarantee the final state regardless of how the
        // loop ended (e.g. a very short grabDuration).
        if (rightArmIK != null)
            rightArmIK.weight = 1f;

        if (rightHandGrip != null)
            rightHandGrip.SetGripAmount(holdGripAmount);

        Debug.Log("[ExtinguisherGrab] Extinguisher is now in hand. Right arm IK active.");
    }
}
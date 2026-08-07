using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// When the player correctly taps the wall extinguisher during
// Step 2 (Grab), this animates it from the wall INTO the player's
// hands: it lerps (smoothly slides) from its current spot to a
// "hold position" in front of the camera, then parents to the
// hold point so it follows the player's view from then on.
//
// NEW: it also tells the RIGHT HAND to curl its fingers at the
// moment the extinguisher arrives. Before that, the hand stays
// open - otherwise the player sees a fist clenched around nothing.
//
// This script goes on the Phase 2 extinguisher (FireExtinguisher_ABC).
// It is triggered by SimulationInteractable on a correct Step 2 tap.
// -------------------------------------------------------

public class ExtinguisherGrab : MonoBehaviour
{
    [Header("Where the extinguisher ends up (in the hands)")]
    // The target transform the extinguisher flies TO and parents UNDER.
    // This is ExtinguisherHoldPoint, which itself follows hand.R
    // via the ExtinguisherHoldFollow script.
    [SerializeField] private Transform holdPoint;

    [Header("Grab Motion")]
    [SerializeField] private float grabDuration = 0.5f;

    [Header("Right Hand Grip")]
    // Drag hand.R here (the object with FingerGripController on it).
    // Its fingers curl the moment the extinguisher lands in the hand.
    [Tooltip("Drag hand.R here. Leave empty to skip finger curling.")]
    [SerializeField] private FingerGripController rightHandGrip;

    [Tooltip("How closed the fingers become when holding the extinguisher. " +
             "0.75 - 0.85 looks like a firm carry grip.")]
    [Range(0f, 1f)]
    [SerializeField] private float holdGripAmount = 0.8f;

    [Tooltip("Start curling slightly BEFORE the extinguisher lands, so the " +
             "hand looks like it is reaching to take it. 0 = curl only on arrival.")]
    [Range(0f, 1f)]
    [SerializeField] private float curlStartPoint = 0.6f;

    // Has the grab already happened? (Prevents grabbing twice.)
    private bool grabbed = false;

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

        // Smoothly move from the wall to the hold point over grabDuration.
        while (elapsed < grabDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / grabDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t); // ease in/out

            // Lerp position and rotation toward the hold point's
            // CURRENT world pose (holdPoint moves with the hand).
            transform.position = Vector3.Lerp(startPos, holdPoint.position, eased);
            transform.rotation = Quaternion.Slerp(startRot, holdPoint.rotation, eased);

            // Begin curling the fingers partway through the travel, so the
            // hand is already closing as the extinguisher arrives rather
            // than snapping shut after it lands.
            if (!curlTriggered && t >= curlStartPoint)
            {
                curlTriggered = true;
                if (rightHandGrip != null)
                    rightHandGrip.SetGripAmount(holdGripAmount);
            }

            yield return null;
        }

        // Snap exactly to the hold point, then parent to it so the
        // extinguisher follows the hands/camera from now on.
        transform.position = holdPoint.position;
        transform.rotation = holdPoint.rotation;
        transform.SetParent(holdPoint);

        // Keep local offset at zero so it sits exactly at the hold point.
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        // Safety net: if curlStartPoint was set to 1 (or the loop was
        // skipped because grabDuration is tiny), make sure the grip is applied.
        if (rightHandGrip != null)
            rightHandGrip.SetGripAmount(holdGripAmount);

        Debug.Log("[ExtinguisherGrab] Extinguisher is now in hand.");
    }
}
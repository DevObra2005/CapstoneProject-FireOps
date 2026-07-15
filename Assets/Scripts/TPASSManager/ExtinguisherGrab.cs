using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// When the player correctly taps the wall extinguisher during
// Step 2 (Grab), this animates it from the wall INTO the player's
// hands: it lerps (smoothly slides) from its current spot to a
// "hold position" in front of the camera, then parents to HandRig
// so it follows the player's view from then on.
//
// This script goes on the Phase 2 extinguisher (Extinguisher_Phase2).
// It is triggered by SimulationInteractable on a correct Step 2 tap.
// -------------------------------------------------------

public class ExtinguisherGrab : MonoBehaviour
{
    [Header("Where the extinguisher ends up (in the hands)")]
    // The target transform the extinguisher flies TO and parents UNDER.
    // Create an empty child of HandRig (or PlayerCamera) placed where
    // you want the extinguisher held, and drag it here.
    [SerializeField] private Transform holdPoint;

    [Header("Grab Motion")]
    [SerializeField] private float grabDuration = 0.5f;

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

        // Smoothly move from the wall to the hold point over grabDuration.
        while (elapsed < grabDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / grabDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t); // ease in/out

            // Lerp position and rotation toward the hold point's
            // CURRENT world pose (holdPoint may move with the camera).
            transform.position = Vector3.Lerp(startPos, holdPoint.position, eased);
            transform.rotation = Quaternion.Slerp(startRot, holdPoint.rotation, eased);

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

        Debug.Log("[ExtinguisherGrab] Extinguisher is now in hand.");
    }
}
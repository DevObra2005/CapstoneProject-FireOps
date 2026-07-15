using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES (v3):
// Like the hose, the pin no longer runs on its own stopwatch.
// LeftHandIKController calls:
//   AttachNow() — the exact frame the hand ARRIVES at the pin
//   HideNow()   — the exact frame the PULL motion finishes
// So the pin always glues with zero offset and disappears exactly
// when the pull completes, no matter what durations you use.
//
// Twist()/Pull() still exist so TPASSButtonManager compiles
// unchanged, but they do nothing — choreography lives in one place.
// -------------------------------------------------------

public class PinController : MonoBehaviour
{
    [Header("Hand Attachment")]
    [Tooltip("The LEFT hand bone (hand.L). On attach, the pin becomes this bone's child and rides every hand movement.")]
    [SerializeField] private Transform handBone;

    // Original "home" of the pin, remembered so ResetPin can restore it.
    private Transform originalParent;
    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;

    private void Start()
    {
        originalParent = transform.parent;
        originalLocalPos = transform.localPosition;
        originalLocalRot = transform.localRotation;
    }

    // -------------------------------------------------------
    // Called by LeftHandIKController at the exact arrival frame.
    // -------------------------------------------------------
    public void AttachNow()
    {
        if (!gameObject.activeInHierarchy)
        {
            Debug.Log("[PinController] AttachNow ignored — pin is hidden (already pulled).");
            return;
        }

        if (handBone == null)
        {
            Debug.LogWarning("[PinController] No handBone assigned — pin cannot attach.");
            return;
        }

        if (transform.parent == handBone) return;   // already attached

        transform.SetParent(handBone, worldPositionStays: true);
        Debug.Log("[PinController] Pin attached to hand (exact-frame).");
    }

    // -------------------------------------------------------
    // Called by LeftHandIKController the moment the pull finishes.
    // -------------------------------------------------------
    public void HideNow()
    {
        if (!gameObject.activeInHierarchy) return;   // already hidden

        transform.SetParent(null);
        gameObject.SetActive(false);
        Debug.Log("[PinController] Pin removed (hidden).");
    }

    // -------------------------------------------------------
    // Kept only so TPASSButtonManager compiles unchanged.
    // -------------------------------------------------------
    public void Twist() { /* handled by LeftHandIKController */ }
    public void Pull() { /* handled by LeftHandIKController */ }

    // -------------------------------------------------------
    // RESET — pin back to its original spot, visible again.
    // Used by the test-key reset (7).
    // -------------------------------------------------------
    public void ResetPin()
    {
        transform.SetParent(originalParent);
        transform.localPosition = originalLocalPos;
        transform.localRotation = originalLocalRot;
        gameObject.SetActive(true);
        Debug.Log("[PinController] Pin reset to original position.");
    }
}
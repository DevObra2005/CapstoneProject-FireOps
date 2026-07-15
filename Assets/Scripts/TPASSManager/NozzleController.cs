using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES (v4 — "the chase"):
// On AIM, StartFollow() makes HoseTarget smoothly CHASE the hand
// every frame (the Chain IK bends the hose along the whole way).
// When it gets within attachDistance of the hand, it parents to
// the hand bone. Because it is already touching at that moment,
// the attachment is invisible — no snap, no teleport, ever.
//
// Release() (used by test-key 7) un-glues everything and returns
// the hose to its natural resting drape.
// -------------------------------------------------------

public class NozzleController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The HoseTarget marker that the Chain IK Constraint chases.")]
    [SerializeField] private Transform hoseTarget;

    [Tooltip("The LEFT hand bone (hand.L). The hose chases and finally attaches to this.")]
    [SerializeField] private Transform handBone;

    [Header("Chase Settings")]
    [Tooltip("How fast the nozzle travels toward the hand, meters/second. Lower = it visibly trails behind the hand (nice 'being carried' feel). 1.5–3 is a good range.")]
    [SerializeField] private float followSpeed = 2f;

    [Tooltip("When the nozzle is closer than this (meters), it attaches to the hand. Small = attaches only when truly touching.")]
    [SerializeField] private float attachDistance = 0.06f;

    // Original "home" of the hose target, remembered for Release().
    private Transform originalParent;
    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;

    private bool following = false;

    private void Start()
    {
        if (hoseTarget != null)
        {
            originalParent = hoseTarget.parent;
            originalLocalPos = hoseTarget.localPosition;
            originalLocalRot = hoseTarget.localRotation;
        }
    }

    private void Update()
    {
        if (!following) return;
        if (hoseTarget == null || handBone == null) { following = false; return; }

        // Already attached? Job done.
        if (hoseTarget.parent == handBone) { following = false; return; }

        // THE CHASE: glide toward the hand at a steady speed. The
        // Chain IK reads this position every frame, so the hose
        // visibly bends along the entire journey.
        hoseTarget.position = Vector3.MoveTowards(
            hoseTarget.position, handBone.position, followSpeed * Time.deltaTime);

        // CONTACT: close enough = attach. worldPositionStays keeps it
        // exactly where it is, and it's already touching the hand —
        // so the parenting is completely invisible.
        if (Vector3.Distance(hoseTarget.position, handBone.position) <= attachDistance)
        {
            hoseTarget.SetParent(handBone, worldPositionStays: true);
            following = false;
            Debug.Log("[NozzleController] Nozzle caught the hand — attached (no snap).");
        }
    }

    // -------------------------------------------------------
    // Called by LeftHandIKController the moment the AIM move begins.
    // Hand moves first; the nozzle starts chasing it.
    // -------------------------------------------------------
    public void StartFollow()
    {
        if (hoseTarget == null || handBone == null)
        {
            Debug.LogWarning("[NozzleController] Missing hoseTarget or handBone reference.");
            return;
        }
        following = true;
        Debug.Log("[NozzleController] Nozzle is now following the hand...");
    }

    // Kept so TPASSButtonManager compiles unchanged (its Aim branch
    // calls this). The real choreography lives in LeftHandIKController.
    public void Grab() { /* handled by LeftHandIKController */ }

    // -------------------------------------------------------
    // RESET — stop chasing, un-glue, hose relaxes back to rest.
    // Used by the editor test-key reset (7).
    // -------------------------------------------------------
    public void Release()
    {
        following = false;

        if (hoseTarget == null || originalParent == null) return;

        hoseTarget.SetParent(originalParent);
        hoseTarget.localPosition = originalLocalPos;
        hoseTarget.localRotation = originalLocalRot;
        Debug.Log("[NozzleController] Hose released back to resting pose.");
    }
}
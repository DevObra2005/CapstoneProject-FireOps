using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Makes ExtinguisherHoldPoint follow the RIGHT HAND BONE
// in WORLD SPACE, every frame, after animation has solved.
//
// WHY NOT JUST PARENT IT?
// The hand rig (HandRig) is at 0.1 scale. Parenting the
// hold point inside it would inherit that scale and make
// the extinguisher render 10x too large.
// Copying world position/rotation sidesteps scale entirely.
//
// WHY LateUpdate?
// Unity frame order is:
//   Update() -> Animation + IK solve -> LateUpdate() -> Render
// Reading the bone in Update() gives LAST frame's position,
// which shows up as jitter. LateUpdate() reads the final,
// already-solved position.
//
// RESULT:
// Hand sways -> hold point sways -> extinguisher sways.
// The hand can no longer clip through the extinguisher,
// because they are locked to the same motion.
// -------------------------------------------------------

[DefaultExecutionOrder(100)] // run after other LateUpdates (e.g. sway scripts)
public class ExtinguisherHoldFollow : MonoBehaviour
{
    [Header("The bone to follow")]
    [Tooltip("Drag the RIGHT HAND bone from fps-hands-v4 here (usually named hand.R)")]
    [SerializeField] private Transform handBone;

    [Header("Offset from the hand bone")]
    [Tooltip("Local offset in the hand bone's own space. Tune this by eye.")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Tooltip("Local rotation offset in degrees. Tune this by eye.")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    [Header("Smoothing (optional)")]
    [Tooltip("0 = snap instantly (recommended). Higher = softer, laggier follow.")]
    [Range(0f, 20f)]
    [SerializeField] private float followSmoothing = 0f;

    void LateUpdate()
    {
        if (handBone == null) return;

        // Where the hold point SHOULD be, in world space:
        // start at the hand bone, then apply our offset in the
        // hand's own local directions (so it rotates correctly with the wrist).
        Vector3 targetPos = handBone.TransformPoint(positionOffset);
        Quaternion targetRot = handBone.rotation * Quaternion.Euler(rotationOffset);

        if (followSmoothing <= 0f)
        {
            // Snap exactly. This is what you want for a held object.
            transform.SetPositionAndRotation(targetPos, targetRot);
        }
        else
        {
            // Optional soft follow, adds a slight "weight" feel.
            float t = 1f - Mathf.Exp(-followSmoothing * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPos, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, t);
        }
    }

    // Draws a small marker in Scene view so you can see where
    // the hold point is landing while you tune the offsets.
    void OnDrawGizmosSelected()
    {
        if (handBone == null) return;

        Gizmos.color = Color.cyan;
        Vector3 p = handBone.TransformPoint(positionOffset);
        Gizmos.DrawWireSphere(p, 0.02f);
        Gizmos.DrawLine(handBone.position, p);
    }
}
using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Smoothly curls finger bones into a grip pose and back.
// No animation clips - just a rotation added on top of each
// finger's natural pose.
//
// HOW IT WORKS: at startup it memorizes each finger's OPEN pose.
// Then every frame it applies (curlAngle * gripAmount) degrees
// of rotation on top of that memorized pose.
//
// WHAT'S NEW IN v2 (all optional - old setups behave the same):
//
//   restGrip    - the grip level the hand SITS AT normally.
//                 Left hand: 0 (open when not holding anything).
//                 Right hand: ~0.8 (always holding the extinguisher).
//
//   startGripped- begin already at restGrip instead of animating
//                 open-to-closed on the first frame.
//
//   boneMultipliers - per-bone curl scaling. The thumb opposes the
//                 fingers, so it should curl LESS (or negative, to
//                 bend the other way). Leave empty for uniform curl.
//
//   SetGripAmount(float) - blend to any value between 0 and 1
//                 instead of just fully open / fully closed.
//
// IMPORTANT: the fingers must be in their natural OPEN pose when
// the game starts. This script does the curling from there.
// -------------------------------------------------------

public class FingerGripController : MonoBehaviour
{
    public enum CurlAxis { X, Y, Z }

    [Header("Finger Bones")]
    [Tooltip("Drag the finger bones here, e.g. palm_index.R, f_index.01.R, f_index.02.R ... " +
             "Add deeper knuckle bones too for a rounder, more natural curl.")]
    [SerializeField] private Transform[] fingerBones;

    [Header("Per-Bone Curl Strength (optional)")]
    [Tooltip("OPTIONAL. One value per bone, matching the order above. " +
             "1 = full curl, 0.5 = half, -1 = curls the opposite way (useful for the thumb). " +
             "Leave this array EMPTY to curl every bone equally.")]
    [SerializeField] private float[] boneMultipliers;

    [Header("Curl Settings")]
    [Tooltip("How many degrees each bone bends at full grip. 40-70 looks natural.")]
    [SerializeField] private float curlAngle = 55f;

    [Tooltip("Which LOCAL axis the fingers bend around. X is correct for most rigs. " +
             "If the curl looks sideways or the fingers splay, try Y or Z.")]
    [SerializeField] private CurlAxis axis = CurlAxis.X;

    [Tooltip("How fast the curl blends in/out. Higher = snappier.")]
    [SerializeField] private float curlSpeed = 8f;

    [Header("Resting Grip")]
    [Tooltip("The grip level the hand sits at when NOT actively gripping. " +
             "0 = fully open (left hand, empty). " +
             "0.7-0.85 = permanently holding something (right hand on the extinguisher).")]
    [Range(0f, 1f)]
    [SerializeField] private float restGrip = 0f;

    [Tooltip("Start already at restGrip instead of animating into it on the first frame. " +
             "Tick this for the right hand so it is holding the extinguisher immediately.")]
    [SerializeField] private bool startGripped = false;

    // Each finger's natural OPEN pose, memorized at startup.
    private Quaternion[] openRotations;

    // 0 = fully open, 1 = fully gripped. Blends smoothly between.
    private float grip = 0f;
    private float gripTarget = 0f;

    private void Start()
    {
        openRotations = new Quaternion[fingerBones.Length];
        for (int i = 0; i < fingerBones.Length; i++)
        {
            if (fingerBones[i] != null)
                openRotations[i] = fingerBones[i].localRotation;
        }

        // Sit at the resting grip level by default.
        gripTarget = restGrip;

        // If requested, snap straight there instead of blending in.
        grip = startGripped ? restGrip : 0f;
    }

    private void LateUpdate()
    {
        if (openRotations == null) return;

        // Slide the grip amount smoothly toward its target...
        grip = Mathf.MoveTowards(grip, gripTarget, curlSpeed * Time.deltaTime);

        // ...and apply that much curl on top of each open pose.
        Vector3 axisVector =
            axis == CurlAxis.X ? Vector3.right :
            axis == CurlAxis.Y ? Vector3.up : Vector3.forward;

        for (int i = 0; i < fingerBones.Length; i++)
        {
            if (fingerBones[i] == null) continue;

            // Per-bone strength. Defaults to 1 when the array is empty
            // or shorter than the bone list.
            float multiplier = 1f;
            if (boneMultipliers != null && i < boneMultipliers.Length)
                multiplier = boneMultipliers[i];

            float angle = curlAngle * grip * multiplier;

            fingerBones[i].localRotation =
                openRotations[i] * Quaternion.AngleAxis(angle, axisVector);
        }
    }

    // -------------------------------------------------------
    // PUBLIC API
    // -------------------------------------------------------

    /// <summary>
    /// true  = curl to FULL grip (1.0) - use for the squeeze.
    /// false = relax back to restGrip (not necessarily open).
    /// </summary>
    public void SetGrip(bool gripped)
    {
        gripTarget = gripped ? 1f : restGrip;
    }

    /// <summary>
    /// Blend to any grip level between 0 (open) and 1 (fully closed).
    /// Useful for partial squeezes or gradual grips.
    /// </summary>
    public void SetGripAmount(float amount)
    {
        gripTarget = Mathf.Clamp01(amount);
    }

    /// <summary>
    /// Return to the resting grip level.
    /// </summary>
    public void ReleaseToRest()
    {
        gripTarget = restGrip;
    }
}
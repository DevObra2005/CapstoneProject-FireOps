using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Shapes the fingers into a grip and curls them, using
// PER-BONE settings instead of one global axis.
//
// WHY PER-BONE:
// Every finger bone in a Blender rig has its own local
// orientation. The thumb does not point the same way as the
// index finger. So a single "curl on X" setting can never
// suit all of them - some bend correctly, others splay
// sideways or twist. Giving each bone its own axis fixes that.
//
// THE TWO LAYERS:
//
//   BASE OFFSET  - a permanent rotation added to a bone, always.
//                  This is how you SHAPE the hand around the
//                  lever: spread the fingers, angle the thumb,
//                  bend a knuckle further than its neighbours.
//                  Set these in the Inspector OUTSIDE Play mode
//                  and they persist.
//
//   CURL         - an extra rotation on top of the base offset,
//                  driven by the grip amount (0 to 1). This is
//                  the squeeze.
//
// So: base offset builds the grip pose, curl tightens it.
// You no longer have to hand-pose bones in Play mode and copy
// numbers back - the base offsets ARE the pose, and they save.
// -------------------------------------------------------

public class FingerGripController : MonoBehaviour
{
    public enum CurlAxis { X, Y, Z }

    [System.Serializable]
    public class FingerBone
    {
        [Tooltip("The bone transform, e.g. f_index.01.R")]
        public Transform bone;

        [Header("Grip Shape (always applied)")]
        [Tooltip("Permanent rotation added to this bone's rest pose. " +
                 "Use this to SHAPE the hand around the lever. " +
                 "Set it outside Play mode - it saves.")]
        public Vector3 baseOffset = Vector3.zero;

        [Header("Curl (added on top, driven by grip amount)")]
        [Tooltip("Which LOCAL axis THIS bone curls around. " +
                 "Different bones often need different axes.")]
        public CurlAxis curlAxis = CurlAxis.X;

        [Tooltip("How many degrees this bone curls at full grip. " +
                 "Negative curls the opposite way (useful for the thumb).")]
        public float curlAngle = 50f;
    }

    [Header("Finger Bones")]
    [Tooltip("One entry per bone. Each has its own shape offset, " +
             "curl axis, and curl angle.")]
    [SerializeField] private FingerBone[] fingerBones;

    [Header("Curl Blending")]
    [Tooltip("How fast the curl blends in and out. Higher = snappier.")]
    [SerializeField] private float curlSpeed = 8f;

    [Header("Resting Grip")]
    [Tooltip("Grip level when not actively squeezing. " +
             "0 = open hand. 0.7-0.85 = holding something.")]
    [Range(0f, 1f)]
    [SerializeField] private float restGrip = 0f;

    [Tooltip("Start already at restGrip instead of blending into it.")]
    [SerializeField] private bool startGripped = false;

    [Header("Live Preview")]
    [Tooltip("Tick to see the grip shape in the Scene view WITHOUT " +
             "entering Play mode. Useful while dialling in baseOffset. " +
             "Untick when done.")]
    [SerializeField] private bool previewInEditor = false;

    [Tooltip("Grip amount used by the editor preview.")]
    [Range(0f, 1f)]
    [SerializeField] private float previewGrip = 0.8f;

    // Each bone's untouched rest pose, memorised once.
    private Quaternion[] restRotations;

    // 0 = open, 1 = fully gripped.
    private float grip = 0f;
    private float gripTarget = 0f;

    private void Start()
    {
        CacheRestPose();
        gripTarget = restGrip;
        grip = startGripped ? restGrip : 0f;
    }

    private void CacheRestPose()
    {
        if (fingerBones == null) return;

        restRotations = new Quaternion[fingerBones.Length];
        for (int i = 0; i < fingerBones.Length; i++)
        {
            if (fingerBones[i] != null && fingerBones[i].bone != null)
                restRotations[i] = fingerBones[i].bone.localRotation;
        }
    }

    private void LateUpdate()
    {
        if (restRotations == null) return;

        grip = Mathf.MoveTowards(grip, gripTarget, curlSpeed * Time.deltaTime);
        ApplyPose(grip);
    }

    // -------------------------------------------------------
    // Applies base offset + curl to every bone.
    // -------------------------------------------------------
    private void ApplyPose(float gripAmount)
    {
        for (int i = 0; i < fingerBones.Length; i++)
        {
            FingerBone fb = fingerBones[i];
            if (fb == null || fb.bone == null) continue;

            // 1. Start from the bone's original rest pose.
            Quaternion result = restRotations[i];

            // 2. Add the permanent shape offset (builds the grip pose).
            result *= Quaternion.Euler(fb.baseOffset);

            // 3. Add the curl on top (the squeeze), on THIS bone's own axis.
            Vector3 axisVector =
                fb.curlAxis == CurlAxis.X ? Vector3.right :
                fb.curlAxis == CurlAxis.Y ? Vector3.up : Vector3.forward;

            result *= Quaternion.AngleAxis(fb.curlAngle * gripAmount, axisVector);

            fb.bone.localRotation = result;
        }
    }

#if UNITY_EDITOR
    // Lets you shape the hand in the Scene view without pressing Play.
    private void OnValidate()
    {
        if (!previewInEditor || !Application.isPlaying == false) return;
        if (fingerBones == null || fingerBones.Length == 0) return;

        // Cache on first preview so we always build from the true rest pose.
        if (restRotations == null || restRotations.Length != fingerBones.Length)
            CacheRestPose();

        ApplyPose(previewGrip);
    }
#endif

    // -------------------------------------------------------
    // PUBLIC API
    // -------------------------------------------------------

    /// <summary>true = full grip (squeeze). false = back to restGrip.</summary>
    public void SetGrip(bool gripped)
    {
        gripTarget = gripped ? 1f : restGrip;
    }

    /// <summary>Blend to any grip level between 0 and 1.</summary>
    public void SetGripAmount(float amount)
    {
        gripTarget = Mathf.Clamp01(amount);
    }

    /// <summary>Return to the resting grip level.</summary>
    public void ReleaseToRest()
    {
        gripTarget = restGrip;
    }
}
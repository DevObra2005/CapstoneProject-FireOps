using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Shapes the fingers and poses them, using PER-BONE settings
// instead of one global axis.
//
// WHY PER-BONE:
// Every finger bone in a Blender rig has its own local
// orientation. The thumb does not point the same way as the
// index finger. So a single "curl on X" setting can never
// suit all of them - some bend correctly, others splay
// sideways or twist. Giving each bone its own axis fixes that.
//
// THE FOUR LAYERS:
//
//   BASE OFFSET  - a permanent rotation added to a bone, always.
//                  This is how you SHAPE the hand: spread the
//                  fingers, angle the thumb, bend a knuckle
//                  further than its neighbours. Set these in the
//                  Inspector OUTSIDE Play mode and they persist.
//
//   CURL         - extra rotation driven by the GRIP amount.
//                  The squeeze around the extinguisher handle.
//
//   POINT        - extra rotation driven by the POINT amount.
//                  Index-finger pointing pose for the alarm press.
//
//   SQUEEZE      - extra rotation driven by the SQUEEZE amount.
//                  The THUMB pressing the lever down during the
//                  Squeeze step. Leave this at 0 on every bone
//                  EXCEPT thumb.01.R and thumb.02.R, so the rest
//                  of the hand keeps its grip on the handle while
//                  only the thumb moves.
//
// ALL THREE POSE DIALS ARE INDEPENDENT.
// They share each bone's curlAxis (the same hinge), because these
// are all the same direction of bend applied to different subsets
// of fingers - not different directions.
//
// In practice grip and point never overlap in time: point rises
// for the alarm press and returns to 0 before the grab. Grip and
// squeeze DO overlap deliberately - the hand is gripping the
// handle while the thumb presses, which is exactly how you hold
// a real extinguisher.
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

        [Header("Curl Axis (shared by all three pose dials)")]
        [Tooltip("Which LOCAL axis THIS bone rotates around. " +
                 "Different bones often need different axes.")]
        public CurlAxis curlAxis = CurlAxis.X;

        [Header("Grip Pose (driven by grip amount)")]
        [Tooltip("How many degrees this bone curls at full grip. " +
                 "Negative curls the opposite way (useful for the thumb).")]
        public float curlAngle = 50f;

        [Header("Point Pose (driven by point amount)")]
        [Tooltip("How many degrees this bone bends at full point. " +
                 "LEAVE AT 0 for the index finger bones so they stay " +
                 "straight. Give the other fingers a curl to fold them " +
                 "into the palm.")]
        public float pointAngle = 0f;

        [Header("Squeeze Pose (driven by squeeze amount)")]
        [Tooltip("How many degrees this bone bends when the thumb presses " +
                 "the lever. LEAVE AT 0 on every bone EXCEPT the thumb - " +
                 "the rest of the hand must keep gripping the handle. " +
                 "Try 20-40 on thumb.01.R and thumb.02.R, then adjust by eye " +
                 "so the thumb tip travels with the lever.")]
        public float squeezeAngle = 0f;
    }

    [Header("Finger Bones")]
    [Tooltip("One entry per bone. Each has its own shape offset, " +
             "rotation axis, and three pose angles.")]
    [SerializeField] private FingerBone[] fingerBones;

    [Header("Blending")]
    [Tooltip("How fast the grip curl blends in and out. Higher = snappier.")]
    [SerializeField] private float curlSpeed = 8f;

    [Tooltip("How fast the point pose blends in and out. " +
             "A press should feel quicker than a grip.")]
    [SerializeField] private float pointSpeed = 10f;

    [Tooltip("How fast the thumb press blends in and out. Match this " +
             "roughly to the Squeeze clip so the thumb and lever move " +
             "together rather than one lagging behind the other.")]
    [SerializeField] private float squeezeSpeed = 6f;

    [Header("Resting Grip")]
    [Tooltip("Grip level when not actively squeezing. " +
             "0 = open hand. 0.7-0.85 = holding something.")]
    [Range(0f, 1f)]
    [SerializeField] private float restGrip = 0f;

    [Tooltip("Start already at restGrip instead of blending into it.")]
    [SerializeField] private bool startGripped = false;

    [Header("Live Preview")]
    [Tooltip("Tick to see the pose in the Scene view WITHOUT " +
             "entering Play mode. Useful while dialling in baseOffset, " +
             "pointAngle and squeezeAngle. Untick when done.")]
    [SerializeField] private bool previewInEditor = false;

    [Tooltip("Grip amount used by the editor preview.")]
    [Range(0f, 1f)]
    [SerializeField] private float previewGrip = 0.8f;

    [Tooltip("Point amount used by the editor preview. " +
             "Set this to 1 while sculpting the pointing pose.")]
    [Range(0f, 1f)]
    [SerializeField] private float previewPoint = 0f;

    [Tooltip("Squeeze amount used by the editor preview. " +
             "Set previewGrip to 0.8 AND previewSqueeze to 1 while dialling " +
             "in the thumb - that is what the hand actually looks like mid-press.")]
    [Range(0f, 1f)]
    [SerializeField] private float previewSqueeze = 0f;

    // Each bone's untouched rest pose, memorised once.
    private Quaternion[] restRotations;

    // 0 = open, 1 = fully gripped.
    private float grip = 0f;
    private float gripTarget = 0f;

    // 0 = neutral, 1 = fully pointing.
    private float point = 0f;
    private float pointTarget = 0f;

    // 0 = thumb up, 1 = thumb pressed down on the lever.
    private float squeeze = 0f;
    private float squeezeTarget = 0f;

    private void Start()
    {
        CacheRestPose();
        gripTarget = restGrip;
        grip = startGripped ? restGrip : 0f;

        pointTarget = 0f;
        point = 0f;

        squeezeTarget = 0f;
        squeeze = 0f;
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
        point = Mathf.MoveTowards(point, pointTarget, pointSpeed * Time.deltaTime);
        squeeze = Mathf.MoveTowards(squeeze, squeezeTarget, squeezeSpeed * Time.deltaTime);

        ApplyPose(grip, point, squeeze);
    }

    // -------------------------------------------------------
    // Applies base offset + all three pose dials to every bone.
    // -------------------------------------------------------
    private void ApplyPose(float gripAmount, float pointAmount, float squeezeAmount)
    {
        for (int i = 0; i < fingerBones.Length; i++)
        {
            FingerBone fb = fingerBones[i];
            if (fb == null || fb.bone == null) continue;

            // 1. Start from the bone's original rest pose.
            Quaternion result = restRotations[i];

            // 2. Add the permanent shape offset (builds the hand shape).
            result *= Quaternion.Euler(fb.baseOffset);

            // 3. Work out THIS bone's own rotation axis.
            Vector3 axisVector =
                fb.curlAxis == CurlAxis.X ? Vector3.right :
                fb.curlAxis == CurlAxis.Y ? Vector3.up : Vector3.forward;

            // 4. Grip curl - the hold on the handle.
            result *= Quaternion.AngleAxis(fb.curlAngle * gripAmount, axisVector);

            // 5. Point pose - index out, for the alarm press.
            result *= Quaternion.AngleAxis(fb.pointAngle * pointAmount, axisVector);

            // 6. Squeeze pose - thumb down on the lever.
            result *= Quaternion.AngleAxis(fb.squeezeAngle * squeezeAmount, axisVector);

            fb.bone.localRotation = result;
        }
    }

#if UNITY_EDITOR
    // Lets you shape the hand in the Scene view without pressing Play.
    private void OnValidate()
    {
        // Preview is an edit-mode tool only. In Play mode the real
        // blend in LateUpdate is in charge, so bail out.
        if (!previewInEditor || Application.isPlaying) return;
        if (fingerBones == null || fingerBones.Length == 0) return;

        // Cache on first preview so we always build from the true rest pose.
        if (restRotations == null || restRotations.Length != fingerBones.Length)
            CacheRestPose();

        ApplyPose(previewGrip, previewPoint, previewSqueeze);
    }
#endif

    // -------------------------------------------------------
    // PUBLIC API - GRIP
    // (unchanged - ExtinguisherGrab.cs still calls these)
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

    // -------------------------------------------------------
    // PUBLIC API - POINT
    // (PressAlarmController.cs calls these)
    // -------------------------------------------------------

    /// <summary>true = index-out pointing pose. false = neutral.</summary>
    public void SetPoint(bool pointing)
    {
        pointTarget = pointing ? 1f : 0f;
    }

    /// <summary>Blend to any point level between 0 and 1.</summary>
    public void SetPointAmount(float amount)
    {
        pointTarget = Mathf.Clamp01(amount);
    }

    // -------------------------------------------------------
    // PUBLIC API - SQUEEZE (thumb on the lever)
    // (SimulationManager.cs calls these on the Squeeze step)
    // -------------------------------------------------------

    /// <summary>true = thumb pressed down on the lever. false = thumb up.</summary>
    public void SetSqueeze(bool pressed)
    {
        squeezeTarget = pressed ? 1f : 0f;
    }

    /// <summary>Blend to any squeeze level between 0 and 1.</summary>
    public void SetSqueezeAmount(float amount)
    {
        squeezeTarget = Mathf.Clamp01(amount);
    }

    /// <summary>Snap all three dials back to their starting values.
    /// Called when a simulation run is reset.</summary>
    public void ResetPose()
    {
        gripTarget = restGrip;
        grip = restGrip;

        pointTarget = 0f;
        point = 0f;

        squeezeTarget = 0f;
        squeeze = 0f;
    }
}
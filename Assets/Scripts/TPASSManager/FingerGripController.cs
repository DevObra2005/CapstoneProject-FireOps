using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES (optional, simple):
// Smoothly curls the finger bones into a "grip pose" and back.
// No animation clips, no keyframes — just a gentle rotation added
// on top of each finger's natural pose.
//
// HOW IT WORKS: at startup it memorizes each finger's OPEN pose.
// SetGrip(true) rotates every listed bone by Curl Angle degrees
// around the chosen local axis (fingers on humanoid rigs usually
// bend on local X). SetGrip(false) relaxes them back.
//
// IMPORTANT: the fingers in your scene must be in their natural
// OPEN pose when the game starts — this script does the curling.
// (If you already posed them curled by hand, either keep that
// static pose and DON'T use this script, or un-curl them first.)
// -------------------------------------------------------

public class FingerGripController : MonoBehaviour
{
    public enum CurlAxis { X, Y, Z }

    [Header("Finger Bones")]
    [Tooltip("Drag the finger bones here: palm_index.L, palm_middle.L, palm_ring.L, palm_pinky.L, thumb.01.L (add deeper knuckle bones too for a rounder curl).")]
    [SerializeField] private Transform[] fingerBones;

    [Header("Curl Settings")]
    [Tooltip("How many degrees each bone bends when gripping. 40–70 looks natural.")]
    [SerializeField] private float curlAngle = 55f;

    [Tooltip("Which LOCAL axis the fingers bend around. X is correct for most rigs — if the curl looks sideways, try Y or Z.")]
    [SerializeField] private CurlAxis axis = CurlAxis.X;

    [Tooltip("How fast the curl blends in/out. Higher = snappier.")]
    [SerializeField] private float curlSpeed = 8f;

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
    }

    private void LateUpdate()
    {
        // Slide the grip amount smoothly toward its target...
        grip = Mathf.MoveTowards(grip, gripTarget, curlSpeed * Time.deltaTime);

        // ...and apply that much curl on top of each open pose.
        Vector3 axisVector =
            axis == CurlAxis.X ? Vector3.right :
            axis == CurlAxis.Y ? Vector3.up : Vector3.forward;

        for (int i = 0; i < fingerBones.Length; i++)
        {
            if (fingerBones[i] == null) continue;
            fingerBones[i].localRotation =
                openRotations[i] * Quaternion.AngleAxis(curlAngle * grip, axisVector);
        }
    }

    /// <summary>true = curl into the grip pose, false = relax open.</summary>
    public void SetGrip(bool gripped)
    {
        gripTarget = gripped ? 1f : 0f;
    }
}
using UnityEngine;

public class PoseCopier : MonoBehaviour
{
    public Transform source;      // drag IK_Target here
    public Transform destination; // drag PinTarget here

    [ContextMenu("Copy Pose")]
    public void CopyPose()
    {
        destination.position = source.position;
        destination.rotation = source.rotation;
        Debug.Log($"Local Pos: {destination.localPosition}  Local Rot: {destination.localEulerAngles}");
    }
}
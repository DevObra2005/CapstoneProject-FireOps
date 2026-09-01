using UnityEngine;

/// <summary>
/// Swinging double door that opens automatically when the player walks
/// into a trigger zone, and closes when they leave.
///
/// Setup:
/// 1. Put this script on an empty parent GameObject (e.g. "DoubleDoor").
/// 2. Drag the LEFT door mesh into leftDoor, RIGHT door mesh into rightDoor.
///    Each door's pivot point (origin) should be at its hinge, not its center.
/// 3. Add a BoxCollider to this same GameObject (or a child), check "Is Trigger".
///    Make it big enough to detect the player walking up to the door.
/// 4. Make sure your Player GameObject is tagged "Player".
/// </summary>
public class DoubleDoor : MonoBehaviour
{
    [Header("Door Parts")]
    public Transform leftDoor;
    public Transform rightDoor;

    [Header("Swing Settings")]
    [Tooltip("How many degrees each door swings open, around its local Y axis.")]
    public float openAngle = 90f;

    [Tooltip("Degrees per second while opening/closing.")]
    public float swingSpeed = 120f;

    [Tooltip("Left door swings the opposite direction of the right door.")]
    public bool mirrorLeftDoor = true;

    [Header("Player Detection")]
    public string playerTag = "Player";

    private Quaternion leftClosedRot;
    private Quaternion rightClosedRot;
    private Quaternion leftOpenRot;
    private Quaternion rightOpenRot;

    private bool isOpen = false;
    private int playersInRange = 0;

    private void Start()
    {
        if (leftDoor != null)
        {
            leftClosedRot = leftDoor.localRotation;
            float leftAngle = mirrorLeftDoor ? -openAngle : openAngle;
            leftOpenRot = leftClosedRot * Quaternion.Euler(0f, leftAngle, 0f);
        }

        if (rightDoor != null)
        {
            rightClosedRot = rightDoor.localRotation;
            rightOpenRot = rightClosedRot * Quaternion.Euler(0f, openAngle, 0f);
        }
    }

    private void Update()
    {
        Quaternion leftTarget = isOpen ? leftOpenRot : leftClosedRot;
        Quaternion rightTarget = isOpen ? rightOpenRot : rightClosedRot;

        if (leftDoor != null)
        {
            leftDoor.localRotation = Quaternion.RotateTowards(
                leftDoor.localRotation, leftTarget, swingSpeed * Time.deltaTime);
        }

        if (rightDoor != null)
        {
            rightDoor.localRotation = Quaternion.RotateTowards(
                rightDoor.localRotation, rightTarget, swingSpeed * Time.deltaTime);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playersInRange++;
        isOpen = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        playersInRange = Mathf.Max(0, playersInRange - 1);

        if (playersInRange == 0)
            isOpen = false;
    }
}
using UnityEngine;

/// <summary>
/// Opens double doors like butterfly wings when the player gets close to a designated trigger box.
/// </summary>
public class ButterflyDoubleDoor : MonoBehaviour
{
    [Header("Door Halves")]
    [Tooltip("The left door panel transform.")]
    public Transform leftDoor;
    
    [Tooltip("The right door panel transform.")]
    public Transform rightDoor;

    [Header("Rotation Settings")]
    [Tooltip("Maximum open angle for each door wing (in degrees).")]
    public float openAngle = 90f;
    
    [Tooltip("How fast the doors open and close.")]
    public float openSpeed = 5f;

    [Header("Trigger Settings")]
    [Tooltip("The tag of the player or object that triggers the door.")]
    public string targetTag = "Player";

    private Quaternion leftClosedRotation;
    private Quaternion rightClosedRotation;
    private Quaternion leftOpenRotation;
    private Quaternion rightOpenRotation;

    private bool isOpen = false;

    private void Start()
    {
        if (leftDoor != null)
        {
            leftClosedRotation = leftDoor.localRotation;
            // Butterfly style: left door rotates outward/inward based on local Y or Z axes. 
            // Using localEulerAngles for precise axis control.
            Vector3 leftOpenEuler = leftDoor.localEulerAngles;
            leftOpenEuler.y += openAngle;
            leftOpenRotation = Quaternion.Euler(leftOpenEuler);
        }

        if (rightDoor != null)
        {
            rightClosedRotation = rightDoor.localRotation;
            // Right door opens in the opposite direction for symmetry
            Vector3 rightOpenEuler = rightDoor.localEulerAngles;
            rightOpenEuler.y -= openAngle;
            rightOpenRotation = Quaternion.Euler(rightOpenEuler);
        }

        // Ensure there is a Collider set as a trigger on this GameObject
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
        else
        {
            Debug.LogWarning($"[ButterflyDoubleDoor] '{gameObject.name}' is missing a Trigger Collider!");
        }
    }

    private void Update()
    {
        // Smoothly interpolate door rotations
        Quaternion targetLeftRot = isOpen ? leftOpenRotation : leftClosedRotation;
        Quaternion targetRightRot = isOpen ? rightOpenRotation : rightClosedRotation;

        if (leftDoor != null)
        {
            leftDoor.localRotation = Quaternion.Slerp(leftDoor.localRotation, targetLeftRot, Time.deltaTime * openSpeed);
        }

        if (rightDoor != null)
        {
            rightDoor.localRotation = Quaternion.Slerp(rightDoor.localRotation, targetRightRot, Time.deltaTime * openSpeed);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            isOpen = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag(targetTag))
        {
            isOpen = false;
        }
    }
}
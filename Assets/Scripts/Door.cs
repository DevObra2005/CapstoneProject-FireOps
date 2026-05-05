using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Door : MonoBehaviour, IInteractable
{
    [Header("Door Settings")]
    public float openAngle = 90f;      // degrees to open
    public float speed = 2f;           // rotation speed
    public bool hingeLeft = true;      // hinge side
    public float autoCloseDelay = 2f;  // seconds before auto-close

    private bool isOpen = false;
    private float currentAngle = 0f;
    private Vector3 hingePosition;
    private Quaternion startRotation;
    private Vector3 doorOriginalPosition;
    private Vector3 hingeAxis = Vector3.up;

    private Collider doorCollider;
    private float closeTimer = 0f;

    void Start()
    {
        doorOriginalPosition = transform.position;
        startRotation = transform.rotation;

        // set hinge position (adjust halfWidth to half of your door width)
        float halfWidth = 0.5f;
        hingePosition = doorOriginalPosition + (hingeLeft ? Vector3.left * halfWidth : Vector3.right * halfWidth);

        // get collider and disable physics blocking
        doorCollider = GetComponent<Collider>();
        if (doorCollider != null)
            doorCollider.isTrigger = true; // make it a trigger so player can pass

        // optionally, add Rigidbody kinematic so physics doesn’t push the player
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
    }

    void Update()
    {
        // smooth rotation
        float targetAngle = isOpen ? openAngle : 0f;
        currentAngle = Mathf.Lerp(currentAngle, targetAngle, Time.deltaTime * speed);

        transform.position = doorOriginalPosition;
        transform.rotation = startRotation;
        transform.RotateAround(hingePosition, hingeAxis, currentAngle * (hingeLeft ? 1f : -1f));

        // auto-close logic
        if (isOpen)
        {
            closeTimer += Time.deltaTime;
            if (closeTimer >= autoCloseDelay)
                CloseDoor();
        }
    }

    public void Interact()
    {
        if (!isOpen)
            OpenDoor();
        else
            CloseDoor();
    }

    private void OpenDoor()
    {
        isOpen = true;
        closeTimer = 0f;
    }

    private void CloseDoor()
    {
        isOpen = false;
        closeTimer = 0f;
    }
}
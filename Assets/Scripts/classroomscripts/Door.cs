using UnityEngine;

/// <summary>
/// Attach this to your Door GameObject (the one with the Box Collider).
/// Click the door to open it; click again to close it.
///
/// IMPORTANT: This rotates the door around its own pivot point. If the
/// door swings from its center instead of from the hinge edge, the
/// door's pivot isn't at the hinge. Fix: create an empty GameObject at
/// the hinge edge, make it the door's parent, and put this script on
/// the door itself (rotation will still look wrong) OR re-parent the
/// door mesh under a hinge-positioned empty and put this script on
/// that empty instead. Ask if you're not sure which case you're in.
/// </summary>
public class Door : MonoBehaviour
{
    [Header("Swing settings")]
    [SerializeField] private float openAngle = 90f;   // degrees to swing open
    [SerializeField] private float openSpeed = 2f;    // higher = faster swing

    [Header("Optional sound")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;

    private bool isOpen = false;
    private Quaternion closedRotation;
    private Quaternion openRotation;

    private void Awake()
    {
        closedRotation = transform.rotation;
        openRotation = closedRotation * Quaternion.Euler(0f, openAngle, 0f);
    }

    private void Update()
    {
        Quaternion target = isOpen ? openRotation : closedRotation;
        transform.rotation = Quaternion.Lerp(transform.rotation, target, Time.deltaTime * openSpeed);
    }

    /// <summary>
    /// Fires automatically when the player clicks this object's Collider,
    /// as long as there's a Camera in the scene (no extra setup needed).
    /// </summary>
    private void OnMouseDown()
    {
        Toggle();
    }

    public void Toggle()
    {
        isOpen = !isOpen;

        if (audioSource != null)
        {
            AudioClip clip = isOpen ? openSound : closeSound;
            if (clip != null) audioSource.PlayOneShot(clip);
        }
    }

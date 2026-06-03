using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : MonoBehaviour
{
    public Camera playerCamera;
    public float interactDistance = 3f;

    // Tap detection values
    private Vector2 touchStartPos;
    private float touchStartTime;
    private const float TAP_MAX_DURATION = 0.3f;  // max seconds = tap
    private const float TAP_MAX_MOVEMENT = 20f;   // max pixels moved = tap

    void Update()
    {
        if (Touchscreen.current == null) return;

        var touch = Touchscreen.current.primaryTouch;

        // Record where and when the finger touched down
        if (touch.press.wasPressedThisFrame)
        {
            touchStartPos = touch.position.ReadValue();
            touchStartTime = Time.time;
        }

        // On finger release, check if it was a tap (not a swipe)
        if (touch.press.wasReleasedThisFrame)
        {
            float duration = Time.time - touchStartTime;
            float movement = Vector2.Distance(touchStartPos,
                             touch.position.ReadValue());

            // Only interact if finger barely moved and released quickly
            if (duration < TAP_MAX_DURATION && movement < TAP_MAX_MOVEMENT)
            {
                TryInteract();
            }
        }
    }

    void TryInteract()
    {
        if (playerCamera == null) return;

        Ray ray = playerCamera.ScreenPointToRay(
                  Touchscreen.current.primaryTouch.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            IInteractable interactable =
                hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                interactable.Interact();
            }
        }
    }
}
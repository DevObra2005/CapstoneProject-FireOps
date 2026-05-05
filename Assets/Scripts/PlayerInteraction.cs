using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteraction : MonoBehaviour
{
    public Camera playerCamera;
    public float interactDistance = 3f;

    void Update()
    {
        if (Touchscreen.current == null) return;

        var touch = Touchscreen.current.primaryTouch;

        // tap detection
        if (touch.press.wasPressedThisFrame)
        {
            TryInteract();
        }
    }

    void TryInteract()
    {
        Ray ray = playerCamera.ScreenPointToRay(Touchscreen.current.primaryTouch.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
        {
            // check if object implements IInteractable
            IInteractable interactable = hit.collider.GetComponent<IInteractable>();
            if (interactable != null)
            {
                interactable.Interact();
            }
        }
    }
}
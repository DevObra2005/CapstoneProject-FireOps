using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems; // ✅ IMPORTANT

public class HazardInteractionManager : MonoBehaviour
{
    public float maxRayDistance = 8f;
    public GameObject interactPrompt;

    private ClickableHazard currentHovered;
    private Camera playerCamera;

    private void Start()
    {
        playerCamera = GetComponent<Camera>();
        if (playerCamera == null)
            playerCamera = Camera.main;

        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    private void Update()
    {
        if (HazardPopupManager.Instance != null && HazardPopupManager.Instance.IsOpen)
            return;

        // ✅ BLOCK UI TOUCH / CLICK
        if (IsPointerOverUI())
            return;

        HandleHover();

        bool clicked = false;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            clicked = true;

        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    clicked = true;
                    break;
                }
            }
        }

        if (clicked)
            HandleClick();
    }

    // ✅ WORKS FOR PC + MOBILE
    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null)
            return false;

        // Mouse
        if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;

        // Touch
        if (Touchscreen.current != null)
        {
            foreach (var touch in Touchscreen.current.touches)
            {
                if (touch.press.isPressed)
                {
                    if (EventSystem.current.IsPointerOverGameObject(touch.touchId.ReadValue()))
                        return true;
                }
            }
        }

        return false;
    }

    private void HandleHover()
    {
        Ray ray = playerCamera.ScreenPointToRay(
            new Vector2(Screen.width / 2f, Screen.height / 2f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            ClickableHazard hazard = hit.collider.GetComponentInParent<ClickableHazard>();

            if (hazard != null)
            {
                if (currentHovered != hazard)
                {
                    currentHovered?.OnHoverExit();
                    currentHovered = hazard;
                    currentHovered.OnHoverEnter();
                }

                if (interactPrompt != null)
                    interactPrompt.SetActive(true);

                return;
            }
        }

        if (currentHovered != null)
        {
            currentHovered.OnHoverExit();
            currentHovered = null;
        }

        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    private void HandleClick()
    {
        Ray ray = playerCamera.ScreenPointToRay(
            new Vector2(Screen.width / 2f, Screen.height / 2f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance))
        {
            ClickableHazard hazard = hit.collider.GetComponentInParent<ClickableHazard>();

            if (hazard != null)
            {
                Debug.Log("[HazardInteractionManager] Clicked: " + hit.collider.name);
                hazard.OnClicked();
            }
        }
    }
}
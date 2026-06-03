using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class HazardInteractionManager : MonoBehaviour
{
    public float maxRayDistance = 8f;
    public GameObject interactPrompt;

    private ClickableHazard currentHovered;
    private Camera playerCamera;

    private float interactCooldown = 0f;
    private const float INTERACT_COOLDOWN = 0.8f;

    // Tap detection
    private Vector2 tapStartPos;
    private float tapStartTime;
    private bool tapStarted = false;
    private const float TAP_MAX_DURATION = 0.35f; // slightly longer for real device
    private const float TAP_MAX_MOVEMENT = 40f;   // slightly more lenient for real device

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable(); // ✅ Required for real device
    }

    private void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }

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

        if (interactCooldown > 0f)
        {
            interactCooldown -= Time.deltaTime;
            HandleHover();
            return;
        }

        HandleHover();

        bool clicked = false;

        // ✅ Editor only — mouse click for testing
#if UNITY_EDITOR
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            clicked = true;
#endif

        // ✅ EnhancedTouch — reliable tap detection on real device
        foreach (var touch in Touch.activeTouches)
        {
            if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                tapStartPos = touch.screenPosition;
                tapStartTime = Time.time;
                tapStarted = true;
            }

            if (tapStarted && touch.phase == UnityEngine.InputSystem.TouchPhase.Ended)
            {
                float duration = Time.time - tapStartTime;
                float moved = Vector2.Distance(tapStartPos, touch.screenPosition);

                if (duration < TAP_MAX_DURATION && moved < TAP_MAX_MOVEMENT)
                    clicked = true;

                tapStarted = false;
            }
        }

        if (clicked && !IsPointerOverUI())
        {
            HandleClick();
            interactCooldown = INTERACT_COOLDOWN;
        }
    }

    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;

#if UNITY_EDITOR
        if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;
#endif

        foreach (var touch in Touch.activeTouches)
        {
            if (EventSystem.current.IsPointerOverGameObject(touch.touchId))
                return true;
        }
        return false;
    }

    private void HandleHover()
    {
        Ray ray = playerCamera.ScreenPointToRay(
            new Vector2(Screen.width / 2f, Screen.height / 2f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
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

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
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
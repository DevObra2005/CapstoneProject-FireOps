using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class HazardInteractionManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // Handles hover highlight and click detection for both
    // Phase 1 (ClickableHazard) and Phase 2 (SimulationInteractable).
    // Shoots a center-screen raycast every frame for hover,
    // and on tap for click.
    // -------------------------------------------------------

    public float maxRayDistance = 8f;
    public GameObject interactPrompt;

    // Phase 1 — currently highlighted hazard
    private ClickableHazard currentHovered;

    // Phase 2 — currently highlighted simulation object
    private SimulationInteractable currentHoveredSim;

    private Camera playerCamera;

    private float interactCooldown = 0f;
    private const float INTERACT_COOLDOWN = 0.8f;

    private Vector2 tapStartPos;
    private float tapStartTime;
    private bool tapStarted = false;
    private const float TAP_MAX_DURATION = 0.35f;
    private const float TAP_MAX_MOVEMENT = 40f;

    private void OnEnable()
    {
        EnhancedTouchSupport.Enable();
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

#if UNITY_EDITOR
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            clicked = true;
#endif

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
            // Phase 1 — highlight ClickableHazard
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

                ClearSimHover();
                return;
            }

            // Phase 2 — highlight SimulationInteractable
            if (PlayerPrefs.GetInt("SimulationMode", 0) == 1)
            {
                SimulationInteractable sim =
                    hit.collider.GetComponentInParent<SimulationInteractable>();

                if (sim != null)
                {
                    if (currentHoveredSim != sim)
                    {
                        ClearSimHover();
                        currentHoveredSim = sim;
                        currentHoveredSim.OnHoverEnter();
                    }
                    if (interactPrompt != null)
                        interactPrompt.SetActive(true);

                    ClearHazardHover();
                    return;
                }
            }
        }

        // Nothing hit — clear both highlights
        ClearHazardHover();
        ClearSimHover();

        if (interactPrompt != null)
            interactPrompt.SetActive(false);
    }

    private void ClearHazardHover()
    {
        if (currentHovered != null)
        {
            currentHovered.OnHoverExit();
            currentHovered = null;
        }
    }

    private void ClearSimHover()
    {
        if (currentHoveredSim != null)
        {
            currentHoveredSim.OnHoverExit();
            currentHoveredSim = null;
        }
    }

    private void HandleClick()
    {
        Ray ray = playerCamera.ScreenPointToRay(
            new Vector2(Screen.width / 2f, Screen.height / 2f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            // Phase 1 — click ClickableHazard
            ClickableHazard hazard = hit.collider.GetComponentInParent<ClickableHazard>();
            if (hazard != null)
            {
                Debug.Log("[HazardInteractionManager] Clicked: " + hit.collider.name);
                hazard.OnClicked();
                return;
            }

            // Phase 2 — click SimulationInteractable
            if (PlayerPrefs.GetInt("SimulationMode", 0) == 1)
            {
                SimulationInteractable sim =
                    hit.collider.GetComponentInParent<SimulationInteractable>();
                if (sim != null)
                {
                    Debug.Log("[HazardInteractionManager] Phase 2 clicked: " + hit.collider.name);
                    sim.Interact();
                }
            }
        }
    }
}
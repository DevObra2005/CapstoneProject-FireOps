using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.EventSystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class HazardInteractionManager : MonoBehaviour
{
    // -------------------------------------------------------
    // WHAT THIS DOES:
    // Handles hover highlight and click detection for:
    //   Phase 1 — ClickableHazard (identify the hazard)
    //   Phase 1 — HazardActionTarget (perform the correct action)
    //   Transition — AlarmTarget (learn the fire alarm)
    //   Transition — ExtinguisherTarget (bridge to Phase 2)
    //   Phase 2 — SimulationInteractable
    // -------------------------------------------------------

    [Header("Raycast")]
    public float maxRayDistance = 8f;
    public GameObject interactPrompt;

    [Header("Reticle Feedback (optional)")]
    [Tooltip("Reticle image that changes colour when something is targetable")]
    public UnityEngine.UI.Image reticle;
    public Color reticleIdleColor = new Color(1f, 1f, 1f, 0.55f);
    public Color reticleHitColor = new Color(0.98f, 0.76f, 0.18f, 1f);
    public float reticleScaleOnHit = 1.35f;

    private ClickableHazard currentHovered;
    private SimulationInteractable currentHoveredSim;

    private Camera playerCamera;
    private Vector3 reticleBaseScale;

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

        if (reticle != null)
        {
            reticleBaseScale = reticle.transform.localScale;
            reticle.color = reticleIdleColor;
        }
    }

    private void Update()
    {
        // Block input while the BFP dialogue is open
        if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive())
        {
            ClearHazardHover();
            ClearSimHover();
            SetReticleState(false);
            if (interactPrompt != null) interactPrompt.SetActive(false);
            return;
        }

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

    // Smoothly animates the reticle between idle and "you can tap this" states
    private void SetReticleState(bool targetable)
    {
        if (reticle == null) return;

        Color target = targetable ? reticleHitColor : reticleIdleColor;
        reticle.color = Color.Lerp(reticle.color, target, Time.deltaTime * 12f);

        Vector3 wanted = targetable
            ? reticleBaseScale * reticleScaleOnHit
            : reticleBaseScale;
        reticle.transform.localScale = Vector3.Lerp(
            reticle.transform.localScale, wanted, Time.deltaTime * 12f);
    }

    private void HandleHover()
    {
        Ray ray = playerCamera.ScreenPointToRay(
            new Vector2(Screen.width / 2f, Screen.height / 2f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            // Alarm transition target — highest priority
            AlarmTarget alarmTarget =
                hit.collider.GetComponentInParent<AlarmTarget>();
            if (alarmTarget != null)
            {
                if (interactPrompt != null) interactPrompt.SetActive(true);
                SetReticleState(true);
                ClearHazardHover();
                ClearSimHover();
                return;
            }

            // Extinguisher transition target
            ExtinguisherTarget extTarget =
                hit.collider.GetComponentInParent<ExtinguisherTarget>();
            if (extTarget != null)
            {
                if (interactPrompt != null) interactPrompt.SetActive(true);
                SetReticleState(true);
                ClearHazardHover();
                ClearSimHover();
                return;
            }

            // Action target (phone, tower, etc.)
            HazardActionTarget actionTarget =
                hit.collider.GetComponentInParent<HazardActionTarget>();
            if (actionTarget != null)
            {
                if (interactPrompt != null) interactPrompt.SetActive(true);
                SetReticleState(true);
                ClearHazardHover();
                ClearSimHover();
                return;
            }

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
                if (interactPrompt != null) interactPrompt.SetActive(true);
                SetReticleState(true);
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
                    if (interactPrompt != null) interactPrompt.SetActive(true);
                    SetReticleState(true);
                    ClearHazardHover();
                    return;
                }
            }
        }

        // Nothing targetable
        ClearHazardHover();
        ClearSimHover();
        SetReticleState(false);

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
            // Alarm transition target — checked FIRST
            AlarmTarget alarmTarget =
                hit.collider.GetComponentInParent<AlarmTarget>();
            if (alarmTarget != null)
            {
                Debug.Log("[HazardInteractionManager] Alarm clicked: "
                    + hit.collider.name);
                alarmTarget.OnClicked();
                return;
            }

            // Extinguisher transition target
            ExtinguisherTarget extTarget =
                hit.collider.GetComponentInParent<ExtinguisherTarget>();
            if (extTarget != null)
            {
                Debug.Log("[HazardInteractionManager] Extinguisher clicked: "
                    + hit.collider.name);
                extTarget.OnClicked();
                return;
            }

            // Action target (phone, report point, etc.)
            HazardActionTarget actionTarget =
                hit.collider.GetComponentInParent<HazardActionTarget>();
            if (actionTarget != null)
            {
                Debug.Log("[HazardInteractionManager] Action target clicked: "
                    + hit.collider.name);
                actionTarget.OnClicked();
                return;
            }

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
                    Debug.Log("[HazardInteractionManager] Phase 2 clicked: "
                        + hit.collider.name);
                    sim.Interact();
                }
            }
        }
    }
}
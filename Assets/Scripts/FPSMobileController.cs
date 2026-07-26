using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using MobileJoystick = PinePie.SimpleJoystick.Joystick;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

[RequireComponent(typeof(CharacterController))]
public class FPSMobileController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 3f;
    public float gravity = -9.81f;

    [Header("Look Settings")]
    public Camera playerCamera;
    public float lookSensitivityX = 3f;
    public float lookSensitivityY = 3f;

    [Tooltip("Higher = smoother but slower. Try 0.05 to 0.15")]
    public float smoothTime = 0.08f;

    [Tooltip("Ignore tiny movements to reduce finger jitter")]
    public float deadZone = 0.5f;

    [Tooltip("Max delta per frame to prevent camera jumps")]
    public float maxDelta = 25f;

    [Header("Joystick Settings")]
    public MobileJoystick moveJoystick;

    private CharacterController controller;
    private Vector3 moveDirection;
    private float verticalVelocity = 0f;
    private float rotationX = 0f;
    private Vector2 currentLookDelta;
    private Vector2 lookVelocity;
    private bool wasDialogueOpen = false;
    private float lookCooldown = 0f;
    private const float LOOK_COOLDOWN_AFTER_POPUP = 0.4f;

    void OnEnable()
    {
        EnhancedTouchSupport.Enable(); // Required for real device
    }

    void OnDisable()
    {
        EnhancedTouchSupport.Disable();
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (playerCamera == null)
            Debug.LogError("[FPSMobileController] PlayerCamera is not assigned!");
        if (moveJoystick == null)
            Debug.LogError("[FPSMobileController] MoveJoystick is not assigned!");
    }

    void Update()
    {
        if (playerCamera == null || moveJoystick == null) return;

        // Freeze camera look while the BFP dialogue is open
        bool isDialogueOpen = DialogueManager.Instance != null
                              && DialogueManager.Instance.IsDialogueActive();

        if (isDialogueOpen)
        {
            currentLookDelta = Vector2.zero;
            wasDialogueOpen = true;
            HandleMovement();
            return;
        }

        if (wasDialogueOpen)
        {
            wasDialogueOpen = false;
            lookCooldown = LOOK_COOLDOWN_AFTER_POPUP;
        }

        if (lookCooldown > 0f)
        {
            lookCooldown -= Time.deltaTime;
            currentLookDelta = Vector2.zero;
            HandleMovement();
            return;
        }

        HandleMovement();
        HandleLook();
    }

    void HandleMovement()
    {
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        float moveX = moveJoystick.InputDirection.x;
        float moveZ = moveJoystick.InputDirection.y;
        moveDirection = forward * moveZ + right * moveX;

        if (controller.isGrounded)
        {
            if (verticalVelocity < 0)
                verticalVelocity = -0.5f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        moveDirection.y = verticalVelocity;
        controller.Move(moveDirection * walkSpeed * Time.deltaTime);
    }

    void HandleLook()
    {
        Vector2 rawInput = Vector2.zero;

        // EnhancedTouch — reliable on real devices
        foreach (var touch in Touch.activeTouches)
        {
            Vector2 touchPos = touch.screenPosition;

            // Right side of screen only
            if (touchPos.x > Screen.width * 0.4f)
            {
                Vector2 delta = touch.delta;

                delta.x = Mathf.Clamp(delta.x, -maxDelta, maxDelta);
                delta.y = Mathf.Clamp(delta.y, -maxDelta, maxDelta);

                if (delta.magnitude < deadZone)
                    delta = Vector2.zero;

                rawInput = delta * 0.1f;
                break;
            }
        }

        // Mouse fallback for Unity Editor
#if UNITY_EDITOR
        if (Mouse.current != null && Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            mouseDelta.x = Mathf.Clamp(mouseDelta.x, -maxDelta, maxDelta);
            mouseDelta.y = Mathf.Clamp(mouseDelta.y, -maxDelta, maxDelta);
            rawInput = mouseDelta * 0.05f;
        }
#endif

        currentLookDelta = Vector2.SmoothDamp(
            currentLookDelta, rawInput, ref lookVelocity, smoothTime);

        rotationX -= currentLookDelta.y * lookSensitivityY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        transform.Rotate(Vector3.up * currentLookDelta.x * lookSensitivityX);
    }
}
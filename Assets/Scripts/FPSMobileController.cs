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

    // -------------------------------------------------------
    // THESE TWO ARE THE BASELINE FEEL, NOT THE PLAYER'S SETTING.
    //
    // They are yours to tune. The settings slider multiplies them via
    // SettingsManager.Sensitivity, so the final turn rate is:
    //
    //     lookSensitivityX  x  slider (0.25 to 3.00)
    //
    // The player scales your tuning rather than replacing it, which is why
    // 1.00x on the slider feels exactly like the game did before the
    // setting existed.
    // -------------------------------------------------------
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

        // -------------------------------------------------------
        // PAUSE GUARD.
        //
        // Time.timeScale = 0 freezes physics and Time.deltaTime, but Update
        // KEEPS RUNNING. Touch deltas are not time-scaled either, so without
        // this the camera would keep rotating behind the pause panel while a
        // trainee drags across it.
        //
        // Movement stops on its own at timeScale 0 because controller.Move
        // is multiplied by Time.deltaTime, but returning here makes both
        // behave the same way for the same visible reason.
        // -------------------------------------------------------
        if (PauseMenuUI.IsPaused)
        {
            currentLookDelta = Vector2.zero;
            return;
        }

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

        // -------------------------------------------------------
        // Read fresh every frame rather than cached in Start(). A trainee
        // who changes sensitivity in the pause menu should feel it the
        // moment they resume, not on the next scene load.
        //
        // Applied AFTER SmoothDamp on purpose. Scaling the raw input first
        // would push the smoothing filter harder at 3x and change the feel
        // of the damping, not just the turn rate.
        //
        // SettingsManager.Sensitivity returns 1.0 when no SettingsManager
        // exists, so opening a scene directly in the Editor behaves exactly
        // as it did before this setting was added.
        // -------------------------------------------------------
        float sens = SettingsManager.Sensitivity;

        rotationX -= currentLookDelta.y * lookSensitivityY * sens;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);
        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        transform.Rotate(Vector3.up * currentLookDelta.x * lookSensitivityX * sens);
    }
}
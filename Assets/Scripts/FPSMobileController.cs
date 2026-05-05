using UnityEngine;
using UnityEngine.InputSystem;
using MobileJoystick = PinePie.SimpleJoystick.Joystick; // alias to avoid ambiguity

[RequireComponent(typeof(CharacterController))]
public class FPSMobileController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float walkSpeed = 3f;
    public float gravity = -9.81f;

    [Header("Look Settings")]
    public Camera playerCamera;
    public float lookSensitivityX = 2.5f; // normal horizontal swipe
    public float lookSensitivityY = 2.5f; // normal vertical swipe
    public float smoothTime = 0.05f;

    [Header("Joystick Settings")]
    public MobileJoystick moveJoystick; // fixed ambiguity

    private CharacterController controller;
    private Vector3 moveDirection;
    private float verticalVelocity = 0f;

    private float rotationX = 0f;
    private Vector2 currentLookDelta;
    private Vector2 lookVelocity;

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

        // Gravity
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0)
                verticalVelocity = -0.5f; // small stick to ground
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
        Vector2 targetDelta = Vector2.zero;

        if (Touchscreen.current != null)
        {
            var touch = Touchscreen.current.primaryTouch;

            if (touch.press.isPressed)
            {
                Vector2 touchPos = touch.position.ReadValue();

                // Only allow looking on the RIGHT side of the screen
                if (touchPos.x > Screen.width * 0.4f)
                {
                    Vector2 rawDelta = touch.delta.ReadValue();

                    // Normalize delta by screen size for consistent feel
                    targetDelta = new Vector2(
                        rawDelta.x / Screen.width * 100f,
                        rawDelta.y / Screen.height * 100f
                    );
                }
            }
        }

        // Smooth the look movement
        currentLookDelta = Vector2.SmoothDamp(
            currentLookDelta,
            targetDelta,
            ref lookVelocity,
            smoothTime
        );

        // Apply rotation
        rotationX -= currentLookDelta.y * lookSensitivityY;
        rotationX = Mathf.Clamp(rotationX, -90f, 90f);

        playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0f, 0f);
        transform.Rotate(Vector3.up * currentLookDelta.x * lookSensitivityX);
    }
}
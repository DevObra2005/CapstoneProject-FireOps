using UnityEngine;

// -------------------------------------------------------
// WHAT THIS DOES:
// Adds subtle life to the FPS hands so they don't look frozen.
//   - IDLE BOB: a slow up/down float, like gentle breathing.
//   - WALK BOB: a faster bob when the player is moving.
//   - LOOK SWAY: hands lag slightly behind camera turns, then
//     catch up — a very natural FPS feel.
//
// This does NOT replace your TPASS animations. It only nudges
// the HandRig's LOCAL position/rotation by tiny amounts around
// its resting pose. Put this script on HandRig.
//
// It reads the player's movement from the joystick / character
// controller if available, and falls back to idle bob otherwise.
// -------------------------------------------------------

public class HandSway : MonoBehaviour
{
    [Header("References")]
    // The player root that actually moves (has CharacterController or
    // moves in world space). Used to detect walking. Optional — if left
    // empty, we only do idle bob + look sway.
    [SerializeField] private Transform playerBody;
    // The camera the hands follow. Used for look-sway. Usually PlayerCamera.
    [SerializeField] private Transform playerCamera;

    [Header("Idle Bob (when standing still)")]
    [SerializeField] private float idleBobSpeed = 1.5f;
    [SerializeField] private float idleBobAmount = 0.005f;

    [Header("Walk Bob (when moving)")]
    [SerializeField] private float walkBobSpeed = 8f;
    [SerializeField] private float walkBobAmount = 0.015f;
    [SerializeField] private float walkSideAmount = 0.01f;

    [Header("Look Sway (hands lag behind camera turns)")]
    [SerializeField] private float swayAmount = 0.02f;
    [SerializeField] private float swaySmooth = 6f;
    [SerializeField] private float maxSway = 0.04f;

    // -------------------------------------------------------
    // Resting pose — captured at Start. All sway is added on
    // top of this, then we return to it. Just like your
    // HandAnimationController captures rest pose.
    // -------------------------------------------------------
    private Vector3 restPosition;

    private float bobTimer = 0f;
    private Vector3 lastPlayerPos;
    private Vector2 currentSway;

    // For camera-rotation-based look detection (works on mobile touch-look).
    private Quaternion lastCameraRotation;

    private void Start()
    {
        // Remember where the hands sit by default.
        restPosition = transform.localPosition;

        if (playerBody != null)
            lastPlayerPos = playerBody.position;

        if (playerCamera != null)
            lastCameraRotation = playerCamera.rotation;
    }

    private void Update()
    {
        // How fast is the player moving right now?
        float moveSpeed = 0f;
        if (playerBody != null)
        {
            Vector3 delta = playerBody.position - lastPlayerPos;
            // Ignore vertical (falling) — only care about walking on the ground.
            delta.y = 0f;
            moveSpeed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            lastPlayerPos = playerBody.position;
        }

        bool isMoving = moveSpeed > 0.1f;

        // ---- BOB ----
        // Pick speed/amount based on moving vs idle.
        float bobSpeed = isMoving ? walkBobSpeed : idleBobSpeed;
        float bobAmount = isMoving ? walkBobAmount : idleBobAmount;
        float sideAmount = isMoving ? walkSideAmount : 0f;

        bobTimer += Time.deltaTime * bobSpeed;

        // Vertical bob = sine wave. Side bob = slower sine for a figure-8 feel.
        float bobY = Mathf.Sin(bobTimer) * bobAmount;
        float bobX = Mathf.Cos(bobTimer * 0.5f) * sideAmount;

        Vector3 bobOffset = new Vector3(bobX, bobY, 0f);

        // ---- LOOK SWAY ----
        // Read how much the player is turning the camera this frame.
        // We use mouse/look delta approximated from camera rotation change.
        Vector2 lookInput = GetLookInput();

        // Target sway is opposite the look direction (hands lag behind).
        Vector2 targetSway = new Vector2(
            Mathf.Clamp(-lookInput.x * swayAmount, -maxSway, maxSway),
            Mathf.Clamp(-lookInput.y * swayAmount, -maxSway, maxSway)
        );

        // Smoothly move current sway toward target (then it eases back to 0).
        currentSway = Vector2.Lerp(currentSway, targetSway, Time.deltaTime * swaySmooth);

        Vector3 swayOffset = new Vector3(currentSway.x, currentSway.y, 0f);

        // ---- APPLY ----
        // Final local position = rest + bob + sway.
        transform.localPosition = restPosition + bobOffset + swayOffset;
    }

    // -------------------------------------------------------
    // Detects how much the player is turning the CAMERA this frame
    // by comparing the camera's rotation to last frame. This works
    // with ANY look system — mouse, touch-drag, gamepad — because it
    // reads the actual camera movement, not a specific input axis.
    // -------------------------------------------------------
    private Vector2 GetLookInput()
    {
        if (playerCamera == null) return Vector2.zero;

        // How much did the camera rotate since last frame?
        Quaternion current = playerCamera.rotation;
        Quaternion delta = current * Quaternion.Inverse(lastCameraRotation);
        lastCameraRotation = current;

        // Convert the rotation delta to euler angles.
        Vector3 euler = delta.eulerAngles;

        // Euler angles wrap 0..360; convert to -180..180 so turns
        // read as small +/- values instead of huge numbers.
        float yaw = Mathf.DeltaAngle(0f, euler.y);   // turning left/right
        float pitch = Mathf.DeltaAngle(0f, euler.x); // looking up/down

        // Scale down and divide by deltaTime so it's frame-rate independent.
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float x = yaw / dt * 0.01f;
        float y = pitch / dt * 0.01f;

        return new Vector2(x, y);
    }
}
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class Door : MonoBehaviour, IInteractable
{
    [Header("Door Settings")]
    public float openAngle = 90f;      // degrees to open
    public float speed = 2f;           // rotation speed (degrees/sec via MoveTowards)
    public bool hingeLeft = true;      // hinge side
    public float autoCloseDelay = 2f;  // seconds before auto-close

    [Header("Phase 2 — Evacuate")]
    [Tooltip("Label sent to Laravel for this exit, e.g. 'ExitDoor'.")]
    [SerializeField] private string actionName = "ExitDoor";

    [Tooltip("Penalty applied if the player tries to exit before finishing the fire safety steps.")]
    [SerializeField] private float wrongExitPenalty = 20f;

    [TextArea]
    [SerializeField] private string wrongExitTip = "You must finish putting out the fire before evacuating!";

    private bool isOpen = false;
    private float currentAngle = 0f;
    private Vector3 hingePosition;
    private Quaternion startRotation;
    private Vector3 doorOriginalPosition;
    private Vector3 hingeAxis = Vector3.up;
    private Collider doorCollider;
    private float closeTimer = 0f;

    public bool IsOpen => isOpen;

    void Start()
    {
        doorOriginalPosition = transform.position;
        startRotation = transform.rotation;

        // get collider and disable physics blocking
        doorCollider = GetComponent<Collider>();
        if (doorCollider != null)
            doorCollider.isTrigger = true; // make it a trigger so player can pass

        // Compute hinge position from the door's actual width instead of a
        // hardcoded guess, so the pivot lines up with the real edge of the door.
        float halfWidth = 0.5f;
        if (doorCollider != null)
        {
            // bounds.extents.x already accounts for the object's scale
            halfWidth = doorCollider.bounds.extents.x;
        }
        hingePosition = doorOriginalPosition + (hingeLeft ? Vector3.left * halfWidth : Vector3.right * halfWidth);

        // optionally, add Rigidbody kinematic so physics doesn't push the player
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }

        Debug.Log($"[Door:{name}] Start — SimulationMode PlayerPref = {PlayerPrefs.GetInt("SimulationMode", 0)}");
    }

    void Update()
    {
        // smooth rotation — MoveTowards guarantees the door actually reaches
        // the target angle (Lerp asymptotically approaches it and never quite arrives).
        float targetAngle = isOpen ? openAngle : 0f;
        currentAngle = Mathf.MoveTowards(currentAngle, targetAngle, speed * 60f * Time.deltaTime);

        transform.position = doorOriginalPosition;
        transform.rotation = startRotation;
        transform.RotateAround(hingePosition, hingeAxis, currentAngle * (hingeLeft ? 1f : -1f));

        // auto-close logic
        if (isOpen)
        {
            closeTimer += Time.deltaTime;
            if (closeTimer >= autoCloseDelay)
                CloseDoor();
        }
    }

    public void Interact()
    {
        // Phase 2 has its own special handling — evacuating is only
        // valid once TPASS is actually finished. Phase 1 (or no
        // simulation active) falls through to the normal toggle below.
        if (PlayerPrefs.GetInt("SimulationMode", 0) == 1)
        {
            HandlePhase2Tap();
            return;
        }

        if (!isOpen)
            OpenDoor();
        else
            CloseDoor();
    }

    // -------------------------------------------------------
    // Handles a door tap during Phase 2 specifically.
    //
    // If the player has actually finished TPASS (currentStep >= 8,
    // meaning Sweep already completed), this IS the correct
    // Evacuate action — door opens, Step 8 logs correct, simulation
    // ends as a WIN.
    //
    // If they tap the door too early (still mid-TPASS or earlier),
    // this is treated as a WRONG action — same as tapping any other
    // wrong object: a time penalty and an educational tip, door
    // stays shut, simulation keeps running.
    // -------------------------------------------------------
    private void HandlePhase2Tap()
    {
        if (SimulationManager.Instance == null)
        {
            Debug.LogWarning($"[Door:{name}] SimulationManager.Instance is null — door tap ignored. " +
                              "Make sure SimulationManager exists in the scene and initializes before the player can interact.");
            return;
        }

        int currentStep = SimulationManager.Instance.CurrentStep;

        if (currentStep >= 8)
        {
            // Correct: TPASS is done, this really is the exit.
            OpenDoor();

            Debug.Log("[Door] Door opened during Phase 2 — evacuating!");

            SimulationManager.Instance.RegisterCorrectAction(
                SimulationInteractable.SimStep.Evacuate,
                actionName
            );

            SimulationManager.Instance.EndSimulation(won: true);
        }
        else
        {
            // Wrong: tried to leave before finishing the steps.
            Debug.Log($"[Door] WRONG — tried to evacuate early at step {currentStep}. Penalty -{wrongExitPenalty}s");

            // currentStep (int) lines up 1-to-1 with SimStep's numeric
            // values, so casting tells us which step they SHOULD be on.
            SimulationInteractable.SimStep expectedStep = (SimulationInteractable.SimStep)currentStep;

            SimulationManager.Instance.RegisterWrongAction(
                expectedStep,
                actionName,
                wrongExitPenalty,
                wrongExitTip
            );
        }
    }

    private void OpenDoor()
    {
        isOpen = true;
        closeTimer = 0f;
    }

    private void CloseDoor()
    {
        isOpen = false;
        closeTimer = 0f;
    }
}
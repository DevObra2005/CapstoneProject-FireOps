using UnityEngine;

// -------------------------------------------------------
// Door — opens on tap, and in Phase 2 doubles as the EVACUATE step.
//
// Evacuate is completed by tapping this door, not by a button. Office does
// the same thing: WCTL and TPASS both end with "leave", and leaving is a
// physical act rather than a menu choice.
//
// -------------------------------------------------------
// WHAT WAS BROKEN FOR KITCHEN, AND WHY IT MATTERED
//
// 1. THE STEP CHECK WAS HARDCODED TO 8.
//
//    Office runs 8 steps and Evacuate is number 8, so `currentStep >= 8`
//    happened to be correct there. Kitchen runs 5, and Evacuate is number 5.
//
//    A Kitchen player would reach the final step, tap the door, and be told
//    they had evacuated too early — plus a 20-second penalty for doing the
//    right thing. The run could never be won.
//
//    Now it reads SimulationManager.MaxStep, which each scene already sets to
//    match its own enum. That property exists for exactly this.
//
// 2. THE PENALTY PATH REPORTED THE WRONG ENUM.
//
//    The old code cast currentStep to SimulationInteractable.SimStep — the
//    OFFICE enum — regardless of scene. In Kitchen, an early door tap at step
//    3 would send "TPASS_Twist" to Laravel, from a scene with no extinguisher
//    and no TPASS.
//
//    No error, no warning, just a step name in the attempt record that does
//    not exist in that environment. useKitchenSteps below picks the right
//    table, same pattern as useKitchenHints on SimulationManager.
//
// -------------------------------------------------------
// SET useKitchenSteps PER SCENE. Tick in Kitchen, leave unticked in Office
// and Classroom. Getting it wrong does not break the run — it corrupts the
// step names in the results, which is worse, because nothing tells you.
// -------------------------------------------------------

[RequireComponent(typeof(Collider))]
public class Door : MonoBehaviour, IInteractable
{
    [Header("Door Settings")]
    public float openAngle = 90f;      // degrees to open
    public float speed = 2f;           // rotation speed
    public bool hingeLeft = true;      // hinge side
    public float autoCloseDelay = 2f;  // seconds before auto-close

    [Tooltip("Half the door's width, used to place the hinge. Measure the " +
             "mesh if the door swings around the wrong point.")]
    [SerializeField] private float halfWidth = 0.5f;

    [Header("Phase 2 — Evacuate")]
    [Tooltip("TICK IN THE KITCHEN SCENE ONLY.\n\n" +
             "Picks which enum this door reports step names from. Office and " +
             "Classroom use SimulationInteractable.SimStep; Kitchen uses " +
             "KitchenStep.\n\n" +
             "Wrong value does not break the run — it silently writes step " +
             "names from the other environment into the attempt record.")]
    [SerializeField] private bool useKitchenSteps = false;

    [Tooltip("Label sent to Laravel for this exit, e.g. 'ExitDoor'.")]
    [SerializeField] private string actionName = "ExitDoor";

    [Tooltip("Penalty applied if the player tries to exit before finishing " +
             "the fire safety steps.")]
    [SerializeField] private float wrongExitPenalty = 20f;

    [TextArea]
    [Tooltip("Shown when the player tries to leave too early. WORD THIS PER " +
             "SCENE — Office is about the extinguisher, Kitchen is about the " +
             "gas still being on.")]
    [SerializeField]
    private string wrongExitTip =
        "You must finish putting out the fire before evacuating!";

    private bool isOpen = false;
    private float currentAngle = 0f;
    private Vector3 hingePosition;
    private Quaternion startRotation;
    private Vector3 doorOriginalPosition;
    private Vector3 hingeAxis = Vector3.up;
    private Collider doorCollider;
    private float closeTimer = 0f;

    void Start()
    {
        doorOriginalPosition = transform.position;
        startRotation = transform.rotation;

        hingePosition = doorOriginalPosition
                      + (hingeLeft ? Vector3.left * halfWidth : Vector3.right * halfWidth);

        // Trigger rather than solid, so the player can walk through.
        doorCollider = GetComponent<Collider>();
        if (doorCollider != null)
            doorCollider.isTrigger = true;

        // Kinematic body so physics never shoves the player.
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
        }
    }

    void Update()
    {
        float targetAngle = isOpen ? openAngle : 0f;
        currentAngle = Mathf.Lerp(currentAngle, targetAngle, Time.deltaTime * speed);

        transform.position = doorOriginalPosition;
        transform.rotation = startRotation;
        transform.RotateAround(hingePosition, hingeAxis, currentAngle * (hingeLeft ? 1f : -1f));

        if (isOpen)
        {
            closeTimer += Time.deltaTime;
            if (closeTimer >= autoCloseDelay)
                CloseDoor();
        }
    }

    public void Interact()
    {
        // Phase 2 has its own handling — evacuating is only valid once the
        // sequence is actually finished. Phase 1 falls through to the normal
        // open/close toggle.
        if (PlayerPrefs.GetInt("SimulationMode", 0) == 1)
        {
            HandlePhase2Tap();
            return;
        }

        if (!isOpen) OpenDoor();
        else CloseDoor();
    }

    // -------------------------------------------------------
    // A DOOR TAP DURING PHASE 2
    //
    // At the FINAL step this is the correct Evacuate action: the door opens,
    // the step logs correct, and the run ends as a win.
    //
    // Earlier than that, it is a wrong action — a penalty and a tip, door
    // stays shut, simulation keeps running.
    //
    // MaxStep rather than a hardcoded number, because the final step is 8 in
    // Office and 5 in Kitchen. Each scene sets MaxStep to match its own enum,
    // so this one comparison is correct in both without knowing which is which.
    // -------------------------------------------------------
    private void HandlePhase2Tap()
    {
        if (SimulationManager.Instance == null) return;

        int currentStep = SimulationManager.Instance.CurrentStep;
        int finalStep = SimulationManager.Instance.MaxStep;

        if (currentStep >= finalStep)
        {
            // Correct — the sequence is done, this really is the exit.
            OpenDoor();

            Debug.Log($"[Door] Evacuating at step {currentStep} of {finalStep}.");

            // The STRING overload, so one call serves both enums. Both happen
            // to name their last step "Evacuate", but reading it from the
            // right enum keeps this honest if either is ever renamed.
            string evacuateName = useKitchenSteps
                ? KitchenStep.Evacuate.ToString()
                : SimulationInteractable.SimStep.Evacuate.ToString();

            SimulationManager.Instance.RegisterCorrectAction(evacuateName, actionName);
            SimulationManager.Instance.EndSimulation(won: true);
        }
        else
        {
            // Wrong — tried to leave before finishing.
            Debug.Log($"[Door] WRONG — tried to evacuate at step {currentStep}, " +
                      $"final step is {finalStep}. Penalty -{wrongExitPenalty}s");

            SimulationManager.Instance.RegisterWrongAction(
                DescribeStep(currentStep),
                actionName,
                wrongExitPenalty,
                wrongExitTip
            );
        }
    }

    // -------------------------------------------------------
    // Names the step the player SHOULD be on, from whichever enum this scene
    // uses.
    //
    // Range-checked because casting a raw int to an enum produces a garbage
    // value when it is out of range, and ToString() on a garbage value returns
    // the NUMBER as text. That would put "7" in the attempt record as a step
    // name and quietly corrupt it.
    // -------------------------------------------------------
    private string DescribeStep(int step)
    {
        if (useKitchenSteps)
        {
            return System.Enum.IsDefined(typeof(KitchenStep), step)
                ? ((KitchenStep)step).ToString()
                : $"Step{step}";
        }

        return System.Enum.IsDefined(typeof(SimulationInteractable.SimStep), step)
            ? ((SimulationInteractable.SimStep)step).ToString()
            : $"Step{step}";
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
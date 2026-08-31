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
//
// -------------------------------------------------------
// THE FIRE-BLOCKED EXIT (OFFICE ONLY)
//
// One addition: a fire still burning in the doorway blocks the WIN.
//
// WHAT IT GUARDS. In the Office decision scenario the player chooses which
// of two fires to attack. Choose the far one and TwoFireDecision lets it
// die, grows the doorway fire, and ends the run in a loss about six seconds
// later.
//
// currentStep is already 8 during those six seconds. Without this check a
// player who sprints to the door taps it, passes the step test, and the run
// ends as a WIN — through a doorway that is visibly on fire, seconds before
// the loss was going to land.
//
// WHERE IT SITS, AND WHY THAT IS DELIBERATE. Inside the final-step branch,
// NOT above it. The early-exit penalty below is untouched: tapping the door
// at step 1 still costs 20 seconds and still says "sound the alarm first",
// exactly as before. That path already worked and teaches the right thing —
// leaving without raising the alarm is a real mistake, not a blocked one.
//
// So this check only ever answers one question: may this run be WON right
// now? A doorway on fire says no.
//
// NO PENALTY, AND AMBER RATHER THAN RED. The player finished the sequence
// and walked to the exit — nothing about that is wrong. The room is refusing
// them, not marking them down. Same treatment TPASSButtonManager gives a
// positioning mistake.
//
// A PHYSICAL BLOCKER IS STILL WORTH ADDING. A Box Collider on a CHILD of the
// fire object stops the player reaching the door at all, and disappears on
// its own when the fire deactivates. This check is the safety net behind it:
// geometry can be mis-sized, and a gap in a collider is silent.
//
// LEAVE Exit Blocking Fire EMPTY in Kitchen and Classroom. Null means the
// check is skipped and this file behaves exactly as it always has.
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

    [Header("Phase 2 — Fire Blocking the Exit (Office only)")]
    [Tooltip("OFFICE ONLY. The fire burning IN THE DOORWAY. Leave EMPTY in " +
             "Kitchen and Classroom.\n\n" +
             "While this fire is still burning the run cannot be WON here — " +
             "so a player who cleared the wrong fire cannot sprint to the " +
             "door and steal a win in the seconds before the loss lands.\n\n" +
             "It does NOT affect the early-exit penalty. Tapping the door at " +
             "step 1 still costs 20 seconds and still says to sound the alarm " +
             "first, exactly as before.\n\n" +
             "Assign the same FireController that is set as Exit Fire on " +
             "TwoFireDecision. If those two ever point at different fires, the " +
             "decision and the door would disagree about which one matters.")]
    [SerializeField] private FireController exitBlockingFire;

    [TextArea]
    [Tooltip("Amber row shown when the doorway is on fire. Keep it SHORT — " +
             "the feedback row truncates, and a warning cut off mid-sentence " +
             "is worse than a blunt one.")]
    [SerializeField]
    private string fireBlockingTip = "Fire is blocking the exit.";

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
    // the step logs correct, and the run ends as a win — UNLESS the doorway
    // itself is still on fire, in which case the tap is refused and nothing
    // is taken.
    //
    // Earlier than that, it is a wrong action — a penalty and a tip, door
    // stays shut, simulation keeps running. UNCHANGED, including in Office
    // with a fire in the doorway: leaving without finishing the sequence is a
    // real mistake and should still be marked as one.
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
            // THE WAY OUT IS ON FIRE. The sequence is finished, but this run
            // cannot be won through a burning doorway — see the header note
            // for the stolen win this prevents.
            //
            // Null in Kitchen and Classroom, so those scenes never reach the
            // body of this if.
            if (IsExitBlockedByFire())
            {
                Debug.Log("[Door] Win refused — the exit fire is still burning.");

                // Amber, not red. No time taken: they did the sequence and
                // walked to the exit. The room is refusing them, not marking
                // them down.
                if (ActionFeedbackManager.Instance != null)
                    ActionFeedbackManager.Instance.ShowHint(fireBlockingTip);

                return;
            }

            // Correct — the sequence is done and the way is clear.
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
            // Wrong — tried to leave before finishing. UNTOUCHED.
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
    // IS A FIRE STILL BURNING IN THE DOORWAY?
    //
    // Three things have to be true for the answer to be yes:
    //
    //   a fire is assigned      — null in Kitchen and Classroom, so those
    //                             scenes skip this entirely
    //
    //   its object is active    — a fire that has already been hidden is
    //                             gone. Also covers a scene where the fire
    //                             starts disabled.
    //
    //   IsOut is false          — the flames have not finished dying.
    //
    // THE IsOut CHECK IS THE ONE THAT MATTERS. FireController keeps a dead
    // fire's object alive for hideDelay seconds — about two — so drifting
    // particles can fade rather than pop out. Testing only activeInHierarchy
    // would refuse the win for those two seconds with the fire visibly out,
    // on the run where the player did everything right.
    // -------------------------------------------------------
    private bool IsExitBlockedByFire()
    {
        if (exitBlockingFire == null) return false;
        if (!exitBlockingFire.gameObject.activeInHierarchy) return false;

        return !exitBlockingFire.IsOut;
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
using System;
using System.Collections;
using UnityEngine;

// -------------------------------------------------------
// TwoFireDecision — the prioritisation decision for Phase 2 (OFFICE ONLY).
//
// WHAT IT DOES
// Two fires burn at once. The player picks one by STANDING NEAR IT when they
// tap Squeeze.
//
//   EXIT FIRE (near the door)  -> the ONLY correct target. Extinguish it and
//                                 the run continues to Evacuate as normal.
//
//   FAR FIRE                   -> the wrong priority. The fire still dies —
//                                 nothing is artificially protected — but the
//                                 fire by the DOOR then spreads across the
//                                 only way out, and the run ends in a LOSS.
//
// THE LOSS IS NOT INSTANT, AND THAT IS THE POINT. The player watches their
// chosen fire go out, THEN watches the door fire take hold, THEN loses. A
// loss the moment they aim would teach them they picked wrong; this teaches
// them WHY. The consequence is on screen before the run ends.
//
// -------------------------------------------------------
// THIS FILE CANNOT AFFECT KITCHEN OR CLASSROOM.
//
// It is called from exactly four places in SimulationManager: the "Squeeze"
// and "Sweep" branches of PlayClipForStep, a null-guarded reset, and a
// null-guarded check in ShowEvacuateArrow.
//
//   KITCHEN never reaches the Squeeze or Sweep branches at all. Its step
//   names are GrabTowel, WCTL_Wet, WCTL_Cover, WCTL_TurnOff and Evacuate —
//   none contains "Squeeze" or "Sweep". Same reason the Alarm branch is
//   already dead there. Stronger than a null check: the code path does not
//   exist for Kitchen.
//
//   CLASSROOM does use TPASS and does reach those branches, so it is
//   protected by the null field instead. Leave twoFireDecision EMPTY in the
//   Classroom scene and every path falls through to the original
//   fireController code, byte for byte.
//
// -------------------------------------------------------
// WHY THE TARGET IS LOCKED AT SQUEEZE
//
// selectedFire is resolved ONCE, on Squeeze, and reused for Sweep. The
// player can walk between the two fires mid-discharge and it changes
// nothing.
//
// That matches the doctrine already written into SimulationManager's header:
// "you do not re-aim while discharging." It also removes a whole class of
// bug — a target that could change between Squeeze and Sweep means the fire
// you weakened is not the fire you kill, and the weaken would be stranded
// halfway with no way back.
//
// -------------------------------------------------------
// WHY THE PENALTY STILL GOES THROUGH RegisterWrongAction
//
// The run is lost either way, so the 15 or 25 seconds do not decide pass or
// fail — Laravel fails the attempt on phase2_passed = false regardless.
//
// It is kept for three things the loss alone would not give you:
//
//   1. The RED ROW at the moment of choice. "Clear the fire near the exit
//      first" appears while they are still spraying, so the ending feels
//      earned rather than arbitrary.
//
//   2. missedTips. The lose screen reads that list — without this entry the
//      player would be told they lost with no explanation.
//
//   3. stepResults. chosen_action names which fire they attacked, so the
//      decision appears in the Simulation Analysis report with NO backend
//      change at all.
// -------------------------------------------------------

public class TwoFireDecision : MonoBehaviour
{
    [Header("The Two Fires")]
    [Tooltip("The fire NEAR THE DOOR. This is the ONLY correct target — it is " +
             "blocking the escape route, so it has to go first.\n\n" +
             "SWAPPING THESE TWO FIELDS INVERTS THE WHOLE DECISION and nothing " +
             "will warn you. Check which fire is physically nearer the exit in " +
             "the Scene view before assigning.")]
    [SerializeField] private FireController exitFire;

    [Tooltip("The fire on the FAR SIDE of the room. Attacking this one first " +
             "loses the run — the door fire spreads while the player is away.")]
    [SerializeField] private FireController farFire;

    [Header("Player")]
    [Tooltip("Used to work out which fire the player is standing nearest to " +
             "when they tap Squeeze. Leave EMPTY to fall back to Camera.main.")]
    [SerializeField] private Transform player;

    [Header("Danger Zone (optional)")]
    [Tooltip("A trigger Collider covering the BACK of the room. Attacking the " +
             "far fire from INSIDE it records the heavier penalty — the player " +
             "committed to the far side with fire between them and the door.\n\n" +
             "A Box Collider with Is Trigger ticked is all this needs. No " +
             "Rigidbody, no OnTriggerEnter — the check is direct geometry, so " +
             "nothing can be missed by a physics frame.\n\n" +
             "Leave EMPTY to use one flat penalty. The run is lost either way, " +
             "so this only changes the recorded number.")]
    [SerializeField] private Collider dangerZone;

    [Header("Penalties (seconds)")]
    [Tooltip("Recorded when the far fire is attacked from a safe position.")]
    [SerializeField] private int wrongFirePenalty = 15;

    [Tooltip("Recorded when the far fire is attacked from inside the danger " +
             "zone. Ignored if no danger zone is assigned.")]
    [SerializeField] private int dangerZonePenalty = 25;

    [Header("Reported Action Name")]
    [Tooltip("Written to chosen_action in the results, and what staff will read " +
             "in the Simulation Analysis report. Plain language — a staff " +
             "member reads this, not a developer.")]
    [SerializeField] private string farFireActionName = "Attacked the far fire instead of the one blocking the exit";

    [Header("Feedback Lines")]
    [Tooltip("Red WRONG ACTION row, shown at the moment of choice — while they " +
             "are still spraying, before anything bad has happened.")]
    [TextArea(2, 3)]
    [SerializeField]
    private string wrongFireTip =
        "Clear the fire near the exit first — it is blocking your way out.";

    [Tooltip("Shown instead when the far fire is chosen from inside the danger " +
             "zone.")]
    [TextArea(2, 3)]
    [SerializeField]
    private string dangerZoneTip =
        "Never let fire get between you and the exit. Clear the doorway first.";

    [Tooltip("Amber row as the door fire spreads, just before the run ends.")]
    [TextArea(2, 3)]
    [SerializeField]
    private string blockedExitTip =
        "The fire by the door is spreading. Your way out is closing.";

    [Tooltip("Amber row after the CORRECT fire dies. The far fire grows, but " +
             "the exit is clear and the run continues.")]
    [TextArea(2, 3)]
    [SerializeField]
    private string secondFireTip =
        "The exit is clear. The remaining fire is beyond your extinguisher — evacuate.";

    [Header("Second Fire Growth")]
    [Tooltip("Seconds after the first fire is out before the other one starts " +
             "growing. A short beat reads as the fire taking hold rather than " +
             "reacting instantly to the other one dying.")]
    [SerializeField] private float growDelay = 0.8f;

    [Tooltip("How large the remaining fire becomes, as a multiple of its " +
             "ORIGINAL size. 1.6 is a clear, readable jump on a phone screen.\n\n" +
             "Values at or below 1 would SHRINK it, teaching the opposite " +
             "lesson — FireController clamps those rather than trust this field.")]
    [SerializeField] private float growTo = 1.6f;

    [Tooltip("Seconds the growth takes. Fast enough to read as spread, slow " +
             "enough to be seen.")]
    [SerializeField] private float growDuration = 1.5f;

    [Header("Wrong Choice — Losing the Run")]
    [Tooltip("End the run as a LOSS when the far fire is attacked first.\n\n" +
             "UNTICK TO TEST THE REST OF THE FLOW without losing every run — " +
             "the penalty, the red row and the growth all still happen, the " +
             "run just continues to Evacuate.\n\n" +
             "Leave TICKED for the real simulation.")]
    [SerializeField] private bool endRunOnWrongFire = true;

    [Tooltip("Seconds to hold AFTER the door fire finishes growing, before the " +
             "run ends.\n\n" +
             "This is the whole point of the delayed loss: the player has to " +
             "SEE the exit close. Too short and the lose screen cuts in before " +
             "they register why. Around 1.5 to 2 reads well.")]
    [SerializeField] private float loseHoldAfterGrowth = 1.5f;

    [Header("Debug")]
    [Tooltip("Log which fire was resolved and why. Turn OFF before the APK build.")]
    [SerializeField] private bool verboseLogging = true;

    // --- RUNTIME STATE ---

    // The fire chosen on Squeeze. Locked for the rest of the run.
    private FireController selectedFire;

    // TRUE once a decision has been graded, so a second Squeeze cannot record
    // a second penalty.
    private bool decisionRecorded = false;

    // TRUE if that decision was the WRONG one. Read by SimulationManager to
    // suppress the evacuate arrow — see the property below.
    private bool wrongFireChosen = false;

    private Coroutine growRoutine;

    /// <summary>
    /// TRUE once the player has committed to the far fire.
    ///
    /// SimulationManager reads this in ShowEvacuateArrow. Without it, the green
    /// arrow would appear on the door about 1.5s after Sweep — pointing the
    /// player at an exit they are seconds away from losing to. That reads as a
    /// bug rather than a lesson.
    /// </summary>
    public bool WrongFireChosen => wrongFireChosen;

    // -------------------------------------------------------
    // SQUEEZE — the decision happens here.
    //
    // stepName is passed in rather than hardcoded, so the penalty is recorded
    // against whatever the Squeeze step is actually called in the enum.
    //
    // onWeakened is SimulationManager's EndStepLockout. It MUST fire on every
    // path, or the TPASS buttons stay dead until the safety valve trips.
    // -------------------------------------------------------
    public void HandleSqueeze(string stepName, Action onWeakened)
    {
        if (selectedFire == null)
            selectedFire = ResolveNearestFire();

        // No fires assigned — behave like the original single-fire path did
        // with a null fireController: unlock and move on, so the run can still
        // be completed.
        if (selectedFire == null)
        {
            Debug.LogWarning("[TwoFireDecision] Squeeze reached but neither fire is " +
                             "assigned. Assign Exit Fire and Far Fire, or clear the " +
                             "Two Fire Decision field on SimulationManager to use the " +
                             "original single-fire behaviour.");
            onWeakened?.Invoke();
            return;
        }

        if (!decisionRecorded)
        {
            decisionRecorded = true;
            GradeChoice(stepName);
        }

        // A penalty can drive the clock to zero, which ends the run inside
        // RegisterWrongAction. Starting a weaken after that would animate a
        // fire over the results screen.
        if (SimulationManager.Instance != null && !SimulationManager.Instance.IsSimActive)
        {
            onWeakened?.Invoke();
            return;
        }

        selectedFire.WeakenFire(onWeakened);
    }

    // -------------------------------------------------------
    // SWEEP — kill the chosen fire, then let the other one take hold.
    //
    // onFireOut is SimulationManager's OnFireIsOut: stops the spray, lifts the
    // thumb, relaxes the hose. It runs FIRST — the player finishes the fire
    // they were fighting before the room turns against them.
    // -------------------------------------------------------
    public void HandleSweep(Action onFireOut)
    {
        if (selectedFire == null)
            selectedFire = ResolveNearestFire();

        if (selectedFire == null)
        {
            Debug.LogWarning("[TwoFireDecision] Sweep reached with no fire resolved — " +
                             "running the fire-out teardown immediately.");
            onFireOut?.Invoke();
            return;
        }

        FireController other = (selectedFire == exitFire) ? farFire : exitFire;

        selectedFire.ExtinguishFire(() =>
        {
            // The extinguisher teardown always runs, exactly as in the
            // single-fire path.
            onFireOut?.Invoke();

            if (other != null)
            {
                if (growRoutine != null) StopCoroutine(growRoutine);
                growRoutine = StartCoroutine(AftermathRoutine(other));
            }
            else if (wrongFireChosen && endRunOnWrongFire)
            {
                // No second fire assigned but the choice was still wrong. End
                // the run rather than letting a misconfigured scene turn a
                // losing decision into a win.
                EndRunAsLoss();
            }
        });
    }

    // -------------------------------------------------------
    // THE AFTERMATH
    //
    // One routine, two endings, because the shape is the same either way: the
    // other fire grows, a line explains what that means, and then either the
    // run continues or it does not.
    //
    //   CORRECT CHOICE -> the far fire grows. The exit is clear. The player
    //                     evacuates and wins. The growth teaches when to STOP
    //                     fighting: that fire is beyond one extinguisher.
    //
    //   WRONG CHOICE   -> the DOOR fire grows. The player watches their only
    //                     way out close, and the run ends. The growth is the
    //                     explanation for the loss, which is why the loss
    //                     waits for it to finish.
    // -------------------------------------------------------
    private IEnumerator AftermathRoutine(FireController other)
    {
        if (growDelay > 0f)
            yield return new WaitForSeconds(growDelay);

        // The run can end during the delay — the timer expiring, or a penalty
        // driving the clock to zero.
        if (SimulationManager.Instance != null && !SimulationManager.Instance.IsSimActive)
        {
            growRoutine = null;
            yield break;
        }

        if (other == null || !other.gameObject.activeSelf)
        {
            growRoutine = null;
            yield break;
        }

        other.GrowFire(growTo, growDuration);

        // Amber, not red. No time is being taken here — this is information
        // about the room, not a mark against the player.
        if (ActionFeedbackManager.Instance != null)
        {
            ActionFeedbackManager.Instance.ShowHint(
                wrongFireChosen ? blockedExitTip : secondFireTip);
        }

        if (verboseLogging)
            Debug.Log($"[TwoFireDecision] {other.name} grew to {growTo}x.");

        // CORRECT CHOICE — nothing more to do. The exit is clear and the
        // Evacuate step proceeds exactly as it always has.
        if (!wrongFireChosen || !endRunOnWrongFire)
        {
            growRoutine = null;
            yield break;
        }

        // WRONG CHOICE — let the spread finish and land, THEN end the run.
        // The hold is what turns "you lost" into "you lost because that is
        // your only door and it is now on fire".
        yield return new WaitForSeconds(growDuration + Mathf.Max(0f, loseHoldAfterGrowth));

        growRoutine = null;
        EndRunAsLoss();
    }

    // -------------------------------------------------------
    // END THE RUN AS A LOSS
    //
    // won: false is the SAME path a timeout already takes. It sends
    // phase2_passed = false, and Laravel fails the attempt regardless of the
    // score — so there is no penalty arithmetic here and no new failure state
    // to design.
    //
    // Deliberately NOT "remaining time becomes the penalty". That inverts the
    // score: a player who dawdles would finish with less time left, take a
    // smaller penalty, and outscore someone who moved quickly. The flag alone
    // is correct and needs no maths.
    //
    // EndSimulation's own `if (!simActive) return;` makes a second call
    // harmless, so a timeout landing in the same frame costs nothing.
    // -------------------------------------------------------
    private void EndRunAsLoss()
    {
        if (SimulationManager.Instance == null) return;
        if (!SimulationManager.Instance.IsSimActive) return;

        Debug.Log("[TwoFireDecision] LOSS — the far fire was cleared first and the " +
                  "door fire spread across the only exit.");

        SimulationManager.Instance.EndSimulation(won: false);
    }

    // -------------------------------------------------------
    // RESET — called from SimulationManager.ResetRuntimeState, null-guarded.
    // -------------------------------------------------------
    public void ResetForReplay()
    {
        if (growRoutine != null)
        {
            StopCoroutine(growRoutine);
            growRoutine = null;
        }

        selectedFire = null;
        decisionRecorded = false;
        wrongFireChosen = false;

        // PHASE 1 GUARD. ResetRuntimeState runs from Start(), which fires when
        // the scene loads in PHASE 1 too — and RestoreFire below calls
        // SetActive(true). Without this, loading into the hazard-identification
        // phase would switch both fires ON.
        //
        // Same PlayerPrefs check BeginSimulation already uses, so the two stay
        // consistent.
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1) return;

        RestoreFire(exitFire);
        RestoreFire(farFire);
    }

    private void RestoreFire(FireController fire)
    {
        if (fire == null) return;

        // A dead fire deactivates itself. Bring it back BEFORE resetting, or
        // the coroutine inside would never run.
        if (!fire.gameObject.activeSelf)
            fire.gameObject.SetActive(true);

        fire.ResetToFullStrength();
    }

    // -------------------------------------------------------
    // WHICH FIRE IS THE PLAYER ATTACKING?
    //
    // Nearest wins. There is deliberately NO maximum distance: a radius the
    // player can stand outside of creates a state where no fire resolves and
    // the Squeeze step does nothing at all — a stalled run with no error to
    // follow. Nearest is always defined, so the sequence can always continue.
    // -------------------------------------------------------
    private FireController ResolveNearestFire()
    {
        Transform p = GetPlayerTransform();

        if (exitFire == null) return farFire;
        if (farFire == null) return exitFire;

        // No player reference and no camera. Default to the CORRECT fire — a
        // missing Inspector field should not lose someone's run.
        if (p == null)
        {
            Debug.LogWarning("[TwoFireDecision] No Player transform and no Camera.main — " +
                             "defaulting to the exit fire. Assign Player in the Inspector.");
            return exitFire;
        }

        float toExit = (exitFire.transform.position - p.position).sqrMagnitude;
        float toFar = (farFire.transform.position - p.position).sqrMagnitude;

        FireController chosen = (toExit <= toFar) ? exitFire : farFire;

        if (verboseLogging)
        {
            Debug.Log($"[TwoFireDecision] Target resolved: {chosen.name} " +
                      $"(exit {Mathf.Sqrt(toExit):F1}m, far {Mathf.Sqrt(toFar):F1}m)");
        }

        return chosen;
    }

    private Transform GetPlayerTransform()
    {
        if (player != null) return player;
        return Camera.main != null ? Camera.main.transform : null;
    }

    // -------------------------------------------------------
    // GRADE THE CHOICE
    //
    // Correct choice records nothing extra — the green CORRECT row for the
    // Squeeze step has already appeared, and a second row saying the same
    // thing would dilute it.
    // -------------------------------------------------------
    private void GradeChoice(string stepName)
    {
        if (SimulationManager.Instance == null) return;

        if (selectedFire == exitFire)
        {
            if (verboseLogging)
                Debug.Log("[TwoFireDecision] CORRECT — cleared the exit fire first.");
            return;
        }

        wrongFireChosen = true;

        bool inDangerZone = IsPlayerInDangerZone();
        int penalty = inDangerZone ? dangerZonePenalty : wrongFirePenalty;
        string tip = inDangerZone ? dangerZoneTip : wrongFireTip;

        if (verboseLogging)
        {
            Debug.Log($"[TwoFireDecision] WRONG — attacked the far fire first " +
                      $"({(inDangerZone ? "inside" : "outside")} the danger zone). " +
                      $"Penalty {penalty}s. The run will end in a loss.");
        }

        SimulationManager.Instance.RegisterWrongAction(
            stepName,
            farFireActionName,
            penalty,
            tip,
            showTipDirectly: true);
    }

    // -------------------------------------------------------
    // DANGER ZONE TEST
    //
    // ClosestPoint returns the point on the collider nearest the given
    // position — and for a point INSIDE the collider, it returns that same
    // point back. So "the closest point is where I already am" means inside.
    //
    // Works for rotated and non-box colliders, needs no Rigidbody, and cannot
    // be missed by a physics frame the way OnTriggerEnter can.
    // -------------------------------------------------------
    private bool IsPlayerInDangerZone()
    {
        if (dangerZone == null) return false;

        Transform p = GetPlayerTransform();
        if (p == null) return false;

        Vector3 pos = p.position;
        return (dangerZone.ClosestPoint(pos) - pos).sqrMagnitude < 0.0001f;
    }
}
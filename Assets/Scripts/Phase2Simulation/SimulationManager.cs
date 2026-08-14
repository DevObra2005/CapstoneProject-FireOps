using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// -------------------------------------------------------
// SimulationManager — the brain of Phase 2.
//
// Owns the 90-second timer, tracks the current step (1-8), applies
// penalties for wrong actions, collects step results, and ends the
// run as a Win or a Lose.
//
// STEP BREAKDOWN (Office / Classroom — TPASS):
//   1 Sound Alarm        -> right-hand press (PressAlarmController)
//   2 Grab Extinguisher  -> handled by ExtinguisherGrab
//   3 TPASS Twist        -> PinLayer, after the hand arrives
//   4 TPASS Pull         -> PinLayer, then PullSequence
//   5 TPASS Aim          -> HoseLayer, after the hand arrives
//   6 TPASS Squeeze      -> LeverLayer only
//   7 TPASS Sweep        -> HoseLayer, then everything relaxes
//   8 Evacuate           -> completed by Door, not RegisterCorrectAction
//
// THINGS THAT COST DAYS TO LEARN — do not undo these:
//
// * MASKED LAYERS. The six clips are frame ranges carved out of ONE
//   Blender timeline, so jumping to Twist's range also snapped the
//   hose to whatever pose was baked there. Three masked layers fix
//   it: a clip physically cannot write to a part outside its mask.
//
// * SQUEEZE IS LEVER-ONLY. Squeeze sits at frames 1-23, BEFORE Aim
//   at 24-66, so playing it on HoseLayer threw the hose back to its
//   pre-aim pose. The hose should not move during Squeeze anyway —
//   you do not re-aim while discharging.
//
// * THE DISCHARGE OUTLIVES THE BUTTON PRESS. Spray and thumb stop in
//   OnFireIsOut(), a FireController callback, not when Sweep is
//   tapped. Otherwise the player watches the fire die with nothing
//   hitting it — backwards for a training tool.
//
// * RESTORE LAYER WEIGHTS ON RESET. A run ends by fading HoseLayer
//   and LeverLayer to 0. A layer at weight 0 writes NOTHING, so a
//   replay would play Aim and Sweep correctly, log correctly, and
//   move absolutely nothing on screen. No error to follow.
//
// * INPUT LOCKOUT. Tapping Pull then Aim immediately left the hand
//   frozen and the Aim clip never playing. PullSequence ends with
//   ReleaseAll(), which runs through LeftHandIKController's
//   StartExclusive — and that STOPS whatever coroutine is running,
//   including the nozzle travel Aim had just started, and with it
//   the callback that plays the Aim clip. Silent, no error.
//   stepBusy blocks taps while a step is still animating. EVERY
//   branch of PlayClipForStep must clear it, or the buttons stay
//   dead until the Update() safety valve fires.
//
// * HANDS NEED NO CLIPS. Grip_Pin is parented to Pin, Grip_Nozzle to
//   a hose bone. Those are the IK targets — move the part and the
//   hand follows for free. One system animates the prop; IK keeps
//   the hands attached.
// -------------------------------------------------------

public class SimulationManager : MonoBehaviour
{
    // --- SINGLETON ---
    public static SimulationManager Instance { get; private set; }

    // --- INSPECTOR SETTINGS ---
    [SerializeField] private float totalTime = 90f;
    [SerializeField] private UnityEvent onWin;
    [SerializeField] private UnityEvent onLose;

    [Header("Results Submission")]
    [SerializeField] private ResultsSubmitter resultsSubmitter;

    [Header("Alarm Press Animation")]
    [Tooltip("Plays the right-hand reach and press on Step 1. Leave empty " +
             "to skip the animation — the step still registers.")]
    [SerializeField] private PressAlarmController pressAlarm;

    [Header("Extinguisher Animation")]
    [Tooltip("The Animator on FireExtinguisher_ABC. Plays the six Blender clips.")]
    [SerializeField] private Animator extinguisherAnimator;

    [Tooltip("Clip state names as they appear in the Animator Controller.")]
    [SerializeField] private string clipTwist = "Twist";
    [SerializeField] private string clipPull = "Pull";
    [SerializeField] private string clipPinDrop = "PinDrop";
    [SerializeField] private string clipAim = "Aim";
    [SerializeField] private string clipSqueeze = "Squeeze";
    [SerializeField] private string clipSweep = "Sweep";

    [Tooltip("Seconds to wait after Pull before the pin drops to the floor.")]
    [SerializeField] private float pinDropDelay = 0.4f;

    [Header("Animator Layers (masked)")]
    [Tooltip("0 = Base (empty)  1 = HoseLayer  2 = PinLayer  3 = LeverLayer\n\n" +
             "These MUST match the layer order in the Animator window, " +
             "counting from the top starting at 0. If nothing animates, " +
             "these values are probably out of order.")]
    [SerializeField] private int hoseLayerIndex = 1;

    [Tooltip("Layer holding Twist, Pull and PinDrop.")]
    [SerializeField] private int pinLayerIndex = 2;

    [Tooltip("Layer holding Squeeze. LeverMask covers only the lever chains, " +
             "so the hose keeps whatever pose Aim left it in.")]
    [SerializeField] private int leverLayerIndex = 3;

    [Tooltip("Seconds to blend between HOSE clips. Aim and Sweep sit next to " +
             "each other on the timeline, so a cut already lands on a sensible " +
             "pose — this just softens it. Set 0 for an instant cut.")]
    [SerializeField] private float hoseBlendDuration = 0.25f;

    [Header("After the Pin Drops")]
    [Tooltip("The Pin object. Hidden once PinDrop finishes — it has left the " +
             "extinguisher, so it should not float in the scene.")]
    [SerializeField] private GameObject pinObject;

    [Tooltip("Drives the reach-then-play timing for Twist and Aim, and releases " +
             "the left hand after PinDrop and when the fire goes out.")]
    [SerializeField] private LeftHandIKController leftHandIK;

    [Tooltip("How long the PinDrop clip runs. Check the clip length in the Inspector.\n\n" +
             "This plus Pin Drop Delay is also how long the TPASS buttons stay " +
             "locked after Pull — PullSequence clears the lockout itself rather " +
             "than a separate timer duplicating these numbers.")]
    [SerializeField] private float pinDropClipLength = 0.5f;

    [Header("Thumb Press")]
    [Tooltip("Presses the THUMB down on Squeeze while the rest of the hand keeps " +
             "gripping the handle. The lever itself moves via the Squeeze clip — " +
             "this is only the thumb. Released when the FIRE goes out, not when " +
             "Sweep is tapped. Leave empty to skip the thumb.")]
    [SerializeField] private FingerGripController rightHandGrip;

    [Header("Nozzle Spray")]
    [Tooltip("Play() on Squeeze, Stop() when the FIRE finishes dying. The particle " +
             "system is parented to the hose tip bone, so it follows the nozzle " +
             "through Aim and Sweep with no code — same trick as the grip markers.")]
    [SerializeField] private ExtinguisherSprayVFX sprayVFX;

    [Header("Fire Reaction")]
    [Tooltip("WeakenFire() on Squeeze, ExtinguishFire() on Sweep — the fire shrinks " +
             "partway, then dies. Both take a callback:\n\n" +
             "WeakenFire's unlocks the buttons once the shrink is VISIBLE, so Sweep " +
             "cannot cancel the weaken mid-fade and skip the lesson that one burst " +
             "is not enough.\n\n" +
             "ExtinguishFire's stops the spray and relaxes everything once the " +
             "flames are actually out.")]
    [SerializeField] private FireController fireController;

    [Header("Return to Rest (after the fire is out)")]
    [Tooltip("Seconds for the hose and lever to settle back to rest. This is a FADE " +
             "of the Animator layer weight to 0, not a clip — at weight 0 the masked " +
             "layer stops writing and the bones fall back to the bind pose. Around " +
             "0.8 reads as lowering the hose; much faster reads as a jump.")]
    [SerializeField] private float relaxDuration = 0.8f;

    [Tooltip("Return the LEFT HAND to rest when the fire goes out. Untick to isolate " +
             "a problem during the relax.")]
    [SerializeField] private bool releaseLeftHandOnFireOut = true;

    [Tooltip("Relax the HOSE and LEVER back to rest. The lever matters as much as " +
             "the hose: SetSqueeze(false) lifts the THUMB, but nothing else releases " +
             "the lever bone — without this the thumb lifts off a pressed lever.")]
    [SerializeField] private bool relaxExtinguisherOnFireOut = true;

    [Header("Input Lockout (anti spam-click)")]
    [Tooltip("How long the TPASS buttons stay locked after a WRONG tap.\n\n" +
             "The only real timer in this file. A wrong tap has no animation to " +
             "hang the unlock off, so it needs a genuine cooldown.\n\n" +
             "It complements the penalty clamp in RegisterWrongAction rather than " +
             "duplicating it: the clamp stops the RECORDED penalty exceeding the " +
             "time budget, this stops five panicked taps draining the whole clock " +
             "and losing the run outright. 0.6 breaks a double-tap without making " +
             "a deliberate correction feel sluggish.")]
    [SerializeField] private float wrongActionCooldown = 0.6f;

    [Tooltip("SAFETY VALVE, not a timing setting.\n\n" +
             "The buttons unlock when a step's animation reports it is finished — " +
             "never on a timer. But if a branch ever forgets to unlock, every " +
             "button would stay dead for the rest of the run.\n\n" +
             "So the lockout is force-cleared after this many seconds and a warning " +
             "is logged naming the step. A missed unlock becomes a delay you can " +
             "find in the Console rather than a frozen simulation you cannot.\n\n" +
             "The longest real step is Pull at about 0.9s. If you ever see the " +
             "warning, do NOT raise this — find the missing unlock.")]
    [SerializeField] private float maxStepLockout = 3f;

    // --- RUNTIME STATE ---
    private float timeRemaining;
    [SerializeField] private int currentStep = 1;
    [SerializeField] private bool simActive = false;
    private List<string> missedTips = new List<string>();
    private List<StepResult> stepResults = new List<StepResult>();
    private int totalPenaltySeconds = 0;

    // Held so it can be cancelled. OnFireIsOut can legitimately run twice —
    // once from FireController's callback, once from EndSimulation's safety
    // net — and two coroutines lerping the same weight would visibly fight.
    private Coroutine relaxRoutine;

    // TRUE while a step is still animating, or during a wrong-tap cooldown.
    // TPASSButtonManager reads IsStepBusy and ignores taps while it is up.
    private bool stepBusy = false;
    private float stepBusyStartedAt = 0f;

    // The timed unlock used by wrong taps, held so it can be cancelled.
    private Coroutine lockoutRoutine;

    // TRUE from the moment Pull starts until PullSequence finishes — UNLESS a
    // later step claims the left hand first, which clears it early and makes
    // PullSequence skip its release. Second guard on the Pull -> Aim bug.
    private bool pullSequenceOwnsHand = false;

    // --- PUBLIC READ-ONLY PROPERTIES ---
    public float TimeRemaining => timeRemaining;
    public int CurrentStep => currentStep;
    public List<string> MissedTips => missedTips;
    public bool IsSimActive => simActive;
    public int TotalPenaltySeconds => totalPenaltySeconds;

    /// <summary>
    /// TRUE while the previous step is still animating, or during a wrong-tap
    /// cooldown. Taps are IGNORED while this is up — not penalised. The player
    /// is early or flustered, not wrong.
    /// </summary>
    public bool IsStepBusy => stepBusy;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        // Bring the pin back — it was hidden last run.
        if (pinObject != null) pinObject.SetActive(true);

        // Withdraw the right arm, in case a run was abandoned mid-press.
        if (pressAlarm != null) pressAlarm.ResetState();

        // Lift the thumb, in case a run ended mid-squeeze.
        if (rightHandGrip != null) rightHandGrip.SetSqueeze(false);

        // Kill any spray still running.
        if (sprayVFX != null) sprayVFX.Stop();

        // CRITICAL — see the header note. Without this a replay inherits
        // HoseLayer and LeverLayer at weight 0 and moves nothing, silently.
        RestoreLayerWeights();

        // Buttons live again for the new run.
        EndStepLockout();
        pullSequenceOwnsHand = false;

        timeRemaining = totalTime;
        currentStep = 1;
        simActive = false;
        totalPenaltySeconds = 0;
        missedTips.Clear();
        stepResults.Clear();
    }

    // -------------------------------------------------------
    // BEGIN SIMULATION
    // -------------------------------------------------------
    public void BeginSimulation()
    {
        if (PlayerPrefs.GetInt("SimulationMode", 0) != 1)
        {
            Debug.LogWarning("[SimulationManager] BeginSimulation called outside Phase 2 — ignored.");
            return;
        }

        if (simActive) return;

        simActive = true;
        Debug.Log("[SimulationManager] Simulation started. Timer running.");
    }

    private void Update()
    {
        if (!simActive) return;

        // SAFETY VALVE. The lockout is meant to be cleared by the animation
        // that raised it; a missed clear would leave every button dead for the
        // rest of the run. This turns that into a delay plus a warning naming
        // the step, so it is findable. Seeing this means a branch is missing
        // its unlock — raising maxStepLockout would only hide it.
        if (stepBusy && Time.time - stepBusyStartedAt > maxStepLockout)
        {
            Debug.LogWarning($"[SimulationManager] Step lockout exceeded {maxStepLockout}s " +
                             $"on step {currentStep} — forcing buttons back on. " +
                             "Something did not clear the lockout.");
            EndStepLockout();
        }

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    // -------------------------------------------------------
    // INPUT LOCKOUT
    //
    // Raised as a step begins, cleared when that step reports it is done.
    // Correct steps clear it from a callback owned by whichever routine holds
    // the real duration — never from a copy of that duration here.
    //
    // Wrong taps have no routine to wait for, so they use BeginTimedLockout
    // with wrongActionCooldown. That is the only genuine timer.
    // -------------------------------------------------------
    private void BeginStepLockout()
    {
        stepBusy = true;
        stepBusyStartedAt = Time.time;
    }

    private void EndStepLockout()
    {
        stepBusy = false;

        if (lockoutRoutine != null)
        {
            StopCoroutine(lockoutRoutine);
            lockoutRoutine = null;
        }
    }

    private void BeginTimedLockout(float seconds)
    {
        BeginStepLockout();

        if (lockoutRoutine != null) StopCoroutine(lockoutRoutine);
        lockoutRoutine = StartCoroutine(ClearLockoutAfter(seconds));
    }

    private IEnumerator ClearLockoutAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // Null the handle BEFORE unlocking, so EndStepLockout does not try to
        // stop the coroutine it is currently being called from.
        lockoutRoutine = null;
        EndStepLockout();
    }

    // -------------------------------------------------------
    // REGISTER CORRECT ACTION
    // -------------------------------------------------------
    public void RegisterCorrectAction(SimulationInteractable.SimStep step, string chosenAction)
    {
        if (!simActive) return;

        stepResults.Add(new StepResult
        {
            step_name = step.ToString(),
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = true,
            penalty_seconds = 0
        });

        // FEEDBACK LOG: green row naming the completed step.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowCorrect(StepNames.Friendly(step.ToString()));

        // Lock BEFORE dispatching — some branches finish synchronously and
        // clear it again on the very same line.
        BeginStepLockout();

        PlayClipForStep(step);

        if (currentStep < 8)
            currentStep++;
    }

    // -------------------------------------------------------
    // REGISTER WRONG ACTION
    // -------------------------------------------------------
    public void RegisterWrongAction(SimulationInteractable.SimStep step, string chosenAction, float timePenalty, string tip)
    {
        if (!simActive) return;

        // Only take the time that ACTUALLY existed to lose. timeRemaining
        // floors at 0, but totalPenaltySeconds used to add the full nominal
        // penalty regardless — so a 20s hit with 10s left recorded 20, and
        // five of those reported 100 seconds of penalties out of a 90-second
        // budget. Impossible, and it reads as a bug in the admin panel.
        float appliedPenalty = Mathf.Min(timePenalty, timeRemaining);

        timeRemaining -= appliedPenalty;
        missedTips.Add(tip);

        int penaltyInt = Mathf.RoundToInt(appliedPenalty);
        totalPenaltySeconds += penaltyInt;

        stepResults.Add(new StepResult
        {
            step_name = step.ToString(),
            sub_step = null,
            chosen_action = chosenAction,
            was_correct = false,
            penalty_seconds = penaltyInt
        });

        // FEEDBACK LOG: red row. The hint points at the CURRENT step the
        // player should be doing, NOT the button they mis-tapped — it
        // guides them to the right next move rather than scolding.
        //
        // The DISPLAYED penalty is the full nominal one, not the clamped
        // figure. With 5s left, a 20s mistake still cost them 20 seconds'
        // worth of trouble; showing "-5s" understates it, and at 0 seconds
        // "-0s" would read as if wrong actions were free.
        if (ActionFeedbackManager.Instance != null)
            ActionFeedbackManager.Instance.ShowWrong(
                StepNames.Hint(currentStep),
                Mathf.RoundToInt(timePenalty));

        // COOLDOWN. The clamp above stops the RECORDED penalty exceeding the
        // budget, but it does not stop the run being lost — five taps at 20s
        // still drains 90 seconds, clamped to 20/20/20/20/10. This gives the
        // player a beat to read the hint instead of losing to their own thumb.
        BeginTimedLockout(wrongActionCooldown);

        // A penalty can drive the clock to zero, which ends the run just as
        // surely as the timer expiring in Update(). Without this the
        // simulation kept running at 0 seconds with no way to finish.
        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            EndSimulation(won: false);
        }
    }

    // -------------------------------------------------------
    // STEP ANIMATION
    //
    // Matched on the step's NAME rather than the enum member, so this
    // survives a rename or reorder — it only cares that the name contains
    // "Alarm", "Twist", "Pull", and so on.
    //
    // ORDER MATTERS. The alarm press is a HAND animation and does not use
    // extinguisherAnimator at all, so it must be dispatched BEFORE the
    // null guard below — otherwise a scene with no Animator assigned would
    // silently swallow the press.
    //
    // EVERY branch must end the lockout, either immediately or by handing
    // EndStepLockout into a callback. A branch that forgets leaves the
    // buttons dead until the Update() safety valve fires.
    // -------------------------------------------------------
    private void PlayClipForStep(SimulationInteractable.SimStep step)
    {
        string name = step.ToString();

        // --- HAND ANIMATION (no extinguisher Animator involved) ---
        if (name.Contains("Alarm"))
        {
            if (pressAlarm != null)
            {
                pressAlarm.PlayAlarmPress();
                Debug.Log("[SimulationManager] Alarm press animation started.");
            }

            // No lockout needed: the TPASS buttons are not visible on step 1,
            // and the next action is a world tap on the extinguisher, which
            // pressAlarm.ResetState() below already handles cleanly.
            EndStepLockout();
            return;
        }

        if (name.Contains("Grab"))
        {
            // Cancel any press still in flight. Nothing stops the player
            // tapping Grab while the arm is still withdrawing — if that
            // happened, PressAlarmController would fade rightArmIK.weight
            // 1 -> 0 while ExtinguisherGrab fades the SAME value 0 -> 1.
            // Two writers on one value, which stutters or snaps the arm.
            if (pressAlarm != null)
            {
                pressAlarm.ResetState();
                Debug.Log("[SimulationManager] Alarm press cancelled - grab takes the arm.");
            }

            EndStepLockout();
            return;
        }

        // --- EXTINGUISHER CLIPS from here down ---
        if (extinguisherAnimator == null)
        {
            // No animator means no choreography to wait for. Without this
            // clear, a scene with an unassigned Animator would lock every
            // button until the safety valve fired.
            EndStepLockout();
            return;
        }

        if (name.Contains("Twist"))
        {
            // WAIT FOR THE HAND. Playing straight away twisted the pin
            // while the hand was still travelling toward it. The callback
            // fires the moment the hand is holding Grip_Pin.
            //
            // Deliberately NOT a delay field: that would duplicate
            // moveDuration in a second file, and the two would drift apart
            // the first time one was retuned, with nothing to warn you.
            if (leftHandIK != null)
            {
                leftHandIK.ReachPinAndTwist(() =>
                {
                    PlayClip(clipTwist, pinLayerIndex);
                    EndStepLockout();
                });
            }
            else
            {
                PlayClip(clipTwist, pinLayerIndex);
                EndStepLockout();
            }
        }
        else if (name.Contains("Pull"))
        {
            // The left hand belongs to PullSequence until it finishes —
            // unless a later step claims it, which clears this flag.
            pullSequenceOwnsHand = true;

            PlayClip(clipPull, pinLayerIndex);

            // PullSequence clears the lockout ~0.9s later. That window is
            // where the Pull -> Aim bug used to slip through.
            StartCoroutine(PullSequence());
        }
        else if (name.Contains("Aim"))
        {
            // The left hand is ours now. If PullSequence is somehow still
            // running it will see this cleared and skip its ReleaseAll()
            // instead of cancelling the travel we are about to start.
            //
            // The lockout should make this unreachable — but it is one line,
            // and the bug it guards against produces no error at all.
            pullSequenceOwnsHand = false;

            // Same reasoning as Twist: let the hand arrive at the nozzle
            // before the hose starts bending.
            if (leftHandIK != null)
            {
                leftHandIK.GrabNozzle(() =>
                {
                    PlayHoseClip(clipAim);
                    EndStepLockout();
                });
            }
            else
            {
                PlayHoseClip(clipAim);
                EndStepLockout();
            }
        }
        else if (name.Contains("Squeeze"))
        {
            pullSequenceOwnsHand = false;

            // LEVER ONLY — see the header note on why this must not play
            // on HoseLayer.
            PlayClip(clipSqueeze, leverLayerIndex);

            // THUMB: presses down with the lever, stays until the FIRE
            // goes out — not until Sweep is tapped.
            if (rightHandGrip != null)
                rightHandGrip.SetSqueeze(true);

            // SPRAY: starts here and runs through Sweep until the flames
            // are actually out.
            if (sprayVFX != null)
            {
                sprayVFX.Play();
                Debug.Log("[SimulationManager] Nozzle spray started.");
            }

            // FIRE: shrinks partway, and the buttons stay locked until the
            // shrink is VISIBLE. Without the wait, tapping Sweep straight
            // after Squeeze meant ExtinguishFire's StopAllCoroutines killed
            // the weaken mid-fade — the fire simply died, and the point that
            // one burst is not enough never reached the screen.
            //
            // The unlock comes from FireController's callback, so the
            // duration (squeezeDuration) stays in one place.
            if (fireController != null)
                fireController.WeakenFire(EndStepLockout);
            else
                EndStepLockout();
        }
        else if (name.Contains("Sweep"))
        {
            pullSequenceOwnsHand = false;

            // Only the hose should move — HoseMask limits this clip to
            // Bone.012 and below. If the right hand or the body ever
            // swings on Sweep, it is NOT this line: check that
            // HandAnimationController.sweepControlledByAnimation is
            // ticked, and that HoseMask has no lever bones in it.
            PlayHoseClip(clipSweep);

            if (fireController != null)
            {
                fireController.ExtinguishFire(OnFireIsOut);
            }
            else
            {
                // No fire assigned (test scene). Nothing will call back, so
                // end the discharge now rather than leaving it running.
                Debug.LogWarning("[SimulationManager] No FireController assigned - " +
                                 "stopping spray immediately instead of waiting for " +
                                 "the fire to go out.");
                OnFireIsOut();
            }

            // The buttons are hidden after step 7 anyway, but clearing keeps
            // the flag honest and stops the safety valve firing a warning.
            EndStepLockout();
        }
        else
        {
            // Evacuate, or a step with no animation. Nothing to wait for.
            EndStepLockout();
        }
    }

    // -------------------------------------------------------
    // THE FIRE IS OUT
    //
    // Handed to FireController.ExtinguishFire() as a callback. Runs when
    // the flames have finished fading, NOT when Sweep was tapped.
    //
    // A named method rather than a lambda because EndSimulation calls it
    // directly as a safety net.
    // -------------------------------------------------------
    private void OnFireIsOut()
    {
        // 1. Thumb off the lever — nothing left to discharge.
        if (rightHandGrip != null)
            rightHandGrip.SetSqueeze(false);

        // 2. Stop() lets airborne particles finish their lifetime instead
        // of vanishing mid-flight, so the spray tails off naturally.
        if (sprayVFX != null)
        {
            sprayVFX.Stop();
            Debug.Log("[SimulationManager] Fire is out - nozzle spray stopped.");
        }

        // 3. The left hand has nothing left to hold.
        if (releaseLeftHandOnFireOut && leftHandIK != null)
        {
            leftHandIK.ReleaseAll();
            Debug.Log("[SimulationManager] Left hand released to rest.");
        }

        // 4. Hose and lever settle back to rest.
        if (relaxExtinguisherOnFireOut)
            RelaxExtinguisherToRest();
    }

    // -------------------------------------------------------
    // RELAX THE EXTINGUISHER
    //
    // There is no "return to rest" clip, so we cannot play our way back.
    // Instead we fade the masked layers' WEIGHT to 0 — at which point they
    // write nothing and the bones fall back to the model's bind pose.
    // -------------------------------------------------------
    private void RelaxExtinguisherToRest()
    {
        if (extinguisherAnimator == null) return;

        if (relaxRoutine != null)
        {
            StopCoroutine(relaxRoutine);
            relaxRoutine = null;
        }

        // Instant snap at 0 — useful for checking WHERE the rest pose
        // lands before tuning how long it takes to get there.
        if (relaxDuration <= 0f)
        {
            SetLayerWeightSafe(hoseLayerIndex, 0f);
            SetLayerWeightSafe(leverLayerIndex, 0f);
            Debug.Log("[SimulationManager] Extinguisher snapped to rest.");
            return;
        }

        relaxRoutine = StartCoroutine(RelaxRoutine());
    }

    private IEnumerator RelaxRoutine()
    {
        float hoseStart = GetLayerWeightSafe(hoseLayerIndex);
        float leverStart = GetLayerWeightSafe(leverLayerIndex);

        float elapsed = 0f;
        while (elapsed < relaxDuration)
        {
            elapsed += Time.deltaTime;

            // SmoothStep rather than a straight lerp, so the hose eases out
            // of the swept pose instead of moving at constant speed and
            // stopping dead.
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / relaxDuration));

            SetLayerWeightSafe(hoseLayerIndex, Mathf.Lerp(hoseStart, 0f, t));
            SetLayerWeightSafe(leverLayerIndex, Mathf.Lerp(leverStart, 0f, t));

            yield return null;
        }

        // Land exactly on 0 — a lerp can finish a hair short.
        SetLayerWeightSafe(hoseLayerIndex, 0f);
        SetLayerWeightSafe(leverLayerIndex, 0f);

        relaxRoutine = null;
        Debug.Log("[SimulationManager] Extinguisher relaxed to rest.");
    }

    // Puts every masked layer back to full strength for a fresh run.
    private void RestoreLayerWeights()
    {
        if (extinguisherAnimator == null) return;

        if (relaxRoutine != null)
        {
            StopCoroutine(relaxRoutine);
            relaxRoutine = null;
        }

        SetLayerWeightSafe(hoseLayerIndex, 1f);
        SetLayerWeightSafe(pinLayerIndex, 1f);
        SetLayerWeightSafe(leverLayerIndex, 1f);
    }

    // Guarded layer access. A layer index past the end of the Animator
    // throws, and these indices are hand-typed in the Inspector — so a
    // mismatch would otherwise take down whatever coroutine is running.
    private void SetLayerWeightSafe(int layer, float weight)
    {
        if (extinguisherAnimator == null) return;
        if (layer < 0 || layer >= extinguisherAnimator.layerCount) return;

        extinguisherAnimator.SetLayerWeight(layer, weight);
    }

    private float GetLayerWeightSafe(int layer)
    {
        if (extinguisherAnimator == null) return 0f;
        if (layer < 0 || layer >= extinguisherAnimator.layerCount) return 0f;

        return extinguisherAnimator.GetLayerWeight(layer);
    }

    // Play a state from frame 0 on a SPECIFIC layer. Play() rather than
    // CrossFade() because these clips animate different parts — there is
    // nothing to blend between.
    //
    // The layer argument matters: passing 0 targets the empty Base Layer,
    // which holds no states and no mask, so nothing would play at all.
    private void PlayClip(string stateName, int layer)
    {
        if (extinguisherAnimator == null || string.IsNullOrEmpty(stateName)) return;

        extinguisherAnimator.Play(stateName, layer, 0f);
        Debug.Log($"[SimulationManager] Extinguisher clip: {stateName} (layer {layer})");
    }

    // Play a HOSE clip with a short blend, so the change between Aim and
    // Sweep reads as a movement rather than a jump.
    private void PlayHoseClip(string stateName)
    {
        if (extinguisherAnimator == null || string.IsNullOrEmpty(stateName)) return;

        if (hoseBlendDuration <= 0f)
        {
            PlayClip(stateName, hoseLayerIndex);
            return;
        }

        extinguisherAnimator.CrossFade(stateName, hoseBlendDuration, hoseLayerIndex, 0f);
        Debug.Log($"[SimulationManager] Extinguisher clip: {stateName} " +
                  $"(layer {hoseLayerIndex}, blended {hoseBlendDuration}s)");
    }

    // -------------------------------------------------------
    // PULL SEQUENCE
    //
    // The whole post-Pull choreography in one place:
    //   1. wait    -> the Pull clip carries the pin out
    //   2. drop    -> PinDrop plays, pin falls
    //   3. wait    -> the drop finishes
    //   4. hide    -> pin disappears
    //   5. release -> left hand lets go, IF it is still ours
    //   6. unlock  -> TPASS buttons live again
    //
    // Step 5 is what used to break Aim. A tap on Aim during this sequence
    // started a nozzle travel, and this release then went through
    // StartExclusive and STOPPED it — killing the callback that plays the
    // Aim clip. The lockout should make that impossible now, but
    // pullSequenceOwnsHand is checked anyway: two independent guards on a
    // bug that produces no error message is proportionate.
    // -------------------------------------------------------
    private IEnumerator PullSequence()
    {
        yield return new WaitForSeconds(pinDropDelay);
        PlayClip(clipPinDrop, pinLayerIndex);

        yield return new WaitForSeconds(pinDropClipLength);

        if (pinObject != null)
        {
            pinObject.SetActive(false);
            Debug.Log("[SimulationManager] Pin hidden after drop.");
        }

        if (pullSequenceOwnsHand)
        {
            if (leftHandIK != null)
            {
                leftHandIK.ReleaseAll();
                Debug.Log("[SimulationManager] Left hand released to rest.");
            }
        }
        else
        {
            Debug.Log("[SimulationManager] Pin drop release SKIPPED — a later step " +
                      "already claimed the left hand.");
        }

        pullSequenceOwnsHand = false;
        EndStepLockout();
    }

    // -------------------------------------------------------
    // END SIMULATION
    // -------------------------------------------------------
    public void EndSimulation(bool won)
    {
        if (!simActive) return;
        simActive = false;

        // SAFETY NET: a run can end at ANY moment — the timer can expire
        // mid-discharge, or a penalty can drive the clock to zero between
        // Squeeze and the fire going out. The FireController callback may
        // then never arrive, and the spray would keep firing over the
        // results screen with the hand clamped to the nozzle.
        //
        // Harmless when the fire already went out: Stop() on a stopped
        // system and a relax fade restarting from 0 both do nothing.
        OnFireIsOut();

        // Clear the lockout so a run that ended mid-choreography does not
        // leave the flag set for whatever comes next.
        EndStepLockout();
        pullSequenceOwnsHand = false;

        GameModeManager modeManager = FindFirstObjectByType<GameModeManager>();
        if (modeManager != null)
            modeManager.ResetToPhase1();

        int finalScore = Mathf.RoundToInt(timeRemaining);

        // ── SUBMIT EVERY ATTEMPT ─────────────────────────────────────
        // This used to run only inside the `won` branch, so timeouts were
        // never sent anywhere — failed runs did not exist as far as the
        // system was concerned, and "attempt counts" were really
        // successful-attempt counts.
        //
        // `won` becomes phase2_passed:
        //   won = true   -> finished all steps with time left
        //   won = false  -> the timer hit zero (the only loss Unity detects)
        //
        // A low-score failure arrives here as won = true — the player DID
        // beat the clock. Laravel scores the penalties and decides they
        // did not pass. The server owns pass/fail, which is why
        // ResultsSubmitter branches on the RESPONSE, not on this flag.
        if (won)
        {
            Debug.Log($"[SimulationManager] Completed. Submitting for validation. Local score: {finalScore}");

            ResultsUIManager resultsUI = FindFirstObjectByType<ResultsUIManager>();
            if (resultsUI != null)
                resultsUI.ShowSubmitting();

            onWin.Invoke();
        }
        else
        {
            Debug.Log("[SimulationManager] LOSE — timer expired. Recording the attempt.");
            onLose.Invoke();
        }

        if (resultsSubmitter != null)
        {
            resultsSubmitter.Submit(finalScore, totalPenaltySeconds, stepResults, won);
        }
        else
        {
            Debug.LogWarning("[SimulationManager] No ResultsSubmitter assigned — results were NOT sent to Laravel.");
        }
    }
}